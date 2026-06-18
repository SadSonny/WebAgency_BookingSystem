// [INTENT]: Implementazione condivisa del provisioning tenant (CLI + API platform). Crea tenant,
// orari, chiusure, servizi, staff e relative associazioni, genera l'API key (formato bk_live_,
// salvata come hash SHA-256), crea l'utente Owner SENZA password (account non ancora attivato),
// genera un token di attivazione (hash in DB) e accoda l'email di attivazione tramite IEmailOutbox,
// il tutto in un'unica transazione (un solo SaveChanges). L'Owner imposta la password al primo
// accesso tramite il link. Fonte di verità unica; sostituisce il precedente TenantProvisioner interno alla CLI.

using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Provisioning;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Core.Provisioning;
using WebAgency_BookingSystem.Core.Security;
using WebAgency_BookingSystem.Infrastructure.Auth;
using WebAgency_BookingSystem.Infrastructure.Email;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services.Provisioning;

/// <summary>Crea un tenant completo in transazione a partire da un input di provisioning.</summary>
internal sealed class TenantProvisioningService : ITenantProvisioningService
{
    private readonly BookingSystemDbContext _db;
    private readonly IEmailOutbox _outbox;
    private readonly AccountSettings _account;

    public TenantProvisioningService(BookingSystemDbContext db, IEmailOutbox outbox, AccountSettings account)
    {
        _db = db;
        _outbox = outbox;
        _account = account;
    }

    /// <inheritdoc/>
    public async Task<Result<ProvisioningOutput>> CreateAsync(ProvisioningInput input, CancellationToken ct = default)
    {
        // WHY: IgnoreQueryFilters è necessario per verificare slug tra tutti i tenant (il global query filter
        // filtra per tenant_id corrente, ma qui non c'è un tenant corrente nel context).
        if (await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == input.Slug, ct))
        {
            return Error.Conflict("slug_esistente", $"Esiste già un tenant con slug '{input.Slug}'.");
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

        var ownerUser = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = input.OwnerEmail!,
            PasswordHash = null,        // account non ancora attivato
            ActivatedAt = null,
            SecurityStamp = Guid.NewGuid(),
            Role = UserRole.Owner,
            Active = true,
            // CreatedAt/UpdatedAt valorizzati dal TimestampInterceptor.
        };
        _db.Users.Add(ownerUser);

        // Token di attivazione (hash in DB) + email con il link. Tutto nella stessa transazione del tenant.
        GeneratedSecurityToken activation = SecurityTokenGenerator.Generate();
        _db.UserSecurityTokens.Add(new UserSecurityToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = ownerUser.Id,
            TokenHash = activation.TokenHash,
            Purpose = SecurityTokenPurpose.Activation,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(_account.ActivationTokenHours),
            CreatedAt = nowUtc,
        });

        string activationUrl = $"{_account.PublicBaseUrl}/api/v1/admin/account/activate?token={activation.Token}";
        _outbox.EnqueueAccountActivation(tenant.Id, tenant.Name, input.OwnerEmail!, activationUrl);

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Action = "tenant_created",
            Actor = "provisioning",
            CreatedAt = nowUtc,
        });

        // WHY: Un solo SaveChanges = un'unica transazione atomica. Con EnableRetryOnFailure la riprova
        // è gestita automaticamente da EF in caso di errori transitori del database.
        await _db.SaveChangesAsync(ct);

        return new ProvisioningOutput(
            tenant.Id, tenant.Slug, apiKey, keyPrefix, input.OwnerEmail!,
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

    private static TimeOnly? ToTime(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    private static DateOnly ToDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
