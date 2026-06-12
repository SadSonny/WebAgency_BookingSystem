// [INTENT]: Esegue il provisioning di un tenant (step 7.3-7.6) in un'unica transazione (un solo SaveChanges):
// crea tenant, orari, chiusure, servizi, staff e relative associazioni, genera l'API key (formato bk_live_,
// salvata come hash SHA-256), crea l'utente admin Owner con password bcrypt, e registra l'audit log. Restituisce
// i segreti generati (API key e password) da mostrare UNA SOLA VOLTA. Solo modalità CREATE (no --update in V1).

using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Core.Security;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.TenantProvisioning;

internal sealed class TenantProvisioner
{
    private readonly BookingSystemDbContext _db;

    public TenantProvisioner(BookingSystemDbContext db) => _db = db;

    public async Task<ProvisioningResult> CreateAsync(ProvisioningInput input, CancellationToken ct)
    {
        if (await _db.Tenants.AnyAsync(t => t.Slug == input.Slug, ct))
        {
            throw new ProvisioningException(
                $"Esiste già un tenant con slug '{input.Slug}'. La modalità --update non è ancora supportata in V1.");
        }

        BookingRulesInput rules = input.BookingRules ?? new BookingRulesInput(null, null, null, null, null);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = input.Slug!,
            Name = input.Name!,
            SiteUrl = input.SiteUrl!,
            OwnerEmail = input.OwnerEmail!,
            Timezone = string.IsNullOrWhiteSpace(input.Timezone) ? "Europe/Rome" : input.Timezone,
            MinAdvanceHours = rules.MinAdvanceHours ?? 1,
            MinCancellationHours = rules.MinCancellationHours ?? 24,
            VisibleDaysAhead = rules.VisibleDaysAhead ?? 30,
            StaffChoiceEnabled = rules.StaffChoiceEnabled ?? true,
            NotificationMethod = string.IsNullOrWhiteSpace(rules.NotificationMethod) ? "email" : rules.NotificationMethod,
            Active = true,
            // CreatedAt/UpdatedAt valorizzati dal TimestampInterceptor.
        };
        _db.Tenants.Add(tenant);

        AddBusinessHours(tenant.Id, input.BusinessHours);
        int closures = AddClosures(tenant.Id, input.SpecialClosures);
        IReadOnlyDictionary<string, Guid> serviceMap = AddServices(tenant.Id, input.Services!);
        int staffCount = AddStaff(tenant.Id, input.Staff, serviceMap);

        (string apiKey, string keyPrefix, string keyHash) = GenerateApiKey();
        _db.TenantApiKeys.Add(new TenantApiKey
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            Description = "Chiave generata dal provisioning",
            Active = true,
            CreatedAt = nowUtc,
        });

        string adminPassword = GeneratePassword();
        _db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = input.OwnerEmail!,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
            Role = UserRole.Owner,
            Active = true,
            // CreatedAt/UpdatedAt valorizzati dal TimestampInterceptor.
        });

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Action = "tenant_created",
            Actor = "provisioning",
            CreatedAt = nowUtc,
        });

        // Un solo SaveChanges = una sola transazione atomica (con EnableRetryOnFailure la riprova è gestita da EF).
        await _db.SaveChangesAsync(ct);

        return new ProvisioningResult(
            tenant.Id, tenant.Slug, apiKey, keyPrefix, input.OwnerEmail!, adminPassword,
            input.Services!.Count, staffCount, closures);
    }

    private void AddBusinessHours(Guid tenantId, IReadOnlyList<BusinessHoursInput>? hours)
    {
        foreach (BusinessHoursInput h in hours ?? [])
        {
            _db.TenantBusinessHours.Add(new TenantBusinessHours
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DayOfWeek = (DayOfWeekIndex)h.DayOfWeek,
                IsOpen = h.IsOpen,
                OpenTime = ToTime(h.OpenTime),
                CloseTime = ToTime(h.CloseTime),
                BreakStart = ToTime(h.BreakStart),
                BreakEnd = ToTime(h.BreakEnd),
            });
        }
    }

    private int AddClosures(Guid tenantId, IReadOnlyList<SpecialClosureInput>? closures)
    {
        int count = 0;
        foreach (SpecialClosureInput c in closures ?? [])
        {
            _db.TenantSpecialClosures.Add(new TenantSpecialClosure
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DateFrom = ToDate(c.DateFrom!),
                DateTo = ToDate(c.DateTo!),
                Reason = c.Reason,
            });
            count++;
        }

        return count;
    }

    private IReadOnlyDictionary<string, Guid> AddServices(Guid tenantId, IReadOnlyList<ServiceInput> services)
    {
        var map = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (ServiceInput s in services)
        {
            var id = Guid.NewGuid();
            map[s.LocalId!] = id;

            _db.Services.Add(new Service
            {
                Id = id,
                TenantId = tenantId,
                Name = s.Name!,
                Category = s.Category,
                Description = s.Description,
                DurationMinutes = s.DurationMinutes!.Value,
                BasePrice = s.BasePrice,
                ParallelSlots = s.ParallelSlots ?? 1,
                BufferEnabled = s.BufferEnabled ?? false,
                BufferMinutes = s.BufferMinutes ?? 0,
                BufferPosition = Enum.TryParse(s.BufferPosition, out BufferPosition bp) ? bp : BufferPosition.After,
                Active = true,
                DisplayOrder = s.DisplayOrder ?? 0,
            });
        }

        return map;
    }

    private int AddStaff(Guid tenantId, IReadOnlyList<StaffInput>? staff, IReadOnlyDictionary<string, Guid> serviceMap)
    {
        int count = 0;
        foreach (StaffInput st in staff ?? [])
        {
            var staffId = Guid.NewGuid();
            _db.Staff.Add(new Staff
            {
                Id = staffId,
                TenantId = tenantId,
                Name = st.Name!,
                Role = st.Role,
                Specialization = st.Specialization,
                PhotoUrl = st.PhotoUrl,
                Active = true,
                DisplayOrder = st.DisplayOrder ?? 0,
            });

            foreach (StaffBusinessHoursInput h in st.BusinessHours ?? [])
            {
                _db.StaffBusinessHours.Add(new StaffBusinessHours
                {
                    Id = Guid.NewGuid(),
                    StaffId = staffId,
                    TenantId = tenantId,
                    DayOfWeek = (DayOfWeekIndex)h.DayOfWeek,
                    IsAvailable = h.IsAvailable,
                    StartTime = ToTime(h.StartTime),
                    EndTime = ToTime(h.EndTime),
                    BreakStart = ToTime(h.BreakStart),
                    BreakEnd = ToTime(h.BreakEnd),
                });
            }

            foreach (StaffServiceInput link in st.Services ?? [])
            {
                _db.StaffServices.Add(new StaffService
                {
                    Id = Guid.NewGuid(),
                    StaffId = staffId,
                    ServiceId = serviceMap[link.ServiceLocalId!],
                    TenantId = tenantId,
                    PriceOverride = link.PriceOverride,
                });
            }

            count++;
        }

        return count;
    }

    private static (string ApiKey, string KeyPrefix, string KeyHash) GenerateApiKey()
    {
        string secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        string apiKey = $"bk_live_{secret}";
        string keyPrefix = secret[..8];
        return (apiKey, keyPrefix, ApiKeyHasher.Hash(apiKey));
    }

    private static string GeneratePassword() =>
        // 18 caratteri esadecimali: random sicuro, leggibile e comunicabile.
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(9));

    private static TimeOnly? ToTime(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    private static DateOnly ToDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Risultato del provisioning: segreti generati (da mostrare una sola volta) e conteggi.</summary>
internal sealed record ProvisioningResult(
    Guid TenantId,
    string Slug,
    string ApiKey,
    string KeyPrefix,
    string AdminEmail,
    string AdminPassword,
    int ServiceCount,
    int StaffCount,
    int ClosureCount);

/// <summary>Errore di provisioning con messaggio destinato all'operatore.</summary>
internal sealed class ProvisioningException : Exception
{
    public ProvisioningException(string message) : base(message)
    {
    }

    public ProvisioningException() : base()
    {
    }

    public ProvisioningException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
