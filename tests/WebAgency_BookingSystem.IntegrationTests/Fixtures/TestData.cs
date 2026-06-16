// [INTENT]: Costanti e metodo di seed per i test di integrazione. Fissa GUIDs e chiave API per rendere
// i test deterministici e indipendenti dall'ordine. SeedAsync inserisce un tenant completo (orari, servizi,
// staff, chiave API) usato da tutta la suite senza essere ricreato tra i test.

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Core.Security;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.IntegrationTests.Fixtures;

public static class TestData
{
    // Chiave API raw — usata come header X-Api-Key nelle richieste HTTP di test.
    public const string RawApiKey = "bk_integration_test_key_abc12345";
    public static readonly string ApiKeyHash = ApiKeyHasher.Hash(RawApiKey);

    // GUIDs fissi — immutabili per tutta la suite, così i test sono deterministici.
    public static readonly Guid TenantId       = new("10000000-0000-0000-0000-000000000001");
    public static readonly Guid ApiKeyId        = new("10000000-0000-0000-0000-000000000002");
    // parallelSlots=2: per testare slot multi-posto e disponibilità
    public static readonly Guid ServiceMultiId  = new("20000000-0000-0000-0000-000000000001");
    // parallelSlots=1: per testare il lock advisory (solo una prenotazione per slot)
    public static readonly Guid ServiceSingleId = new("20000000-0000-0000-0000-000000000002");
    // parallelSlots=1, BufferEnabled=true, BufferMinutes=15, BufferPosition=After: per testare il buffer (D-10)
    public static readonly Guid ServiceBufferId = new("20000000-0000-0000-0000-000000000003");
    // parallelSlots=2, SENZA operatori: testa il fallback a parallelSlots (T1.2) — capacità del servizio, niente staff.
    public static readonly Guid ServiceParallelId = new("20000000-0000-0000-0000-000000000004");
    public static readonly Guid StaffId         = new("30000000-0000-0000-0000-000000000001");

    // Owner admin attivato — usato dai test account (login read-only sul seeded Owner; i test che mutano la
    // password usano invece utenti usa-e-getta per non contaminare la cache stamp condivisa del factory).
    public static readonly Guid OwnerUserId = new("40000000-0000-0000-0000-000000000001");
    public const string OwnerEmail = "owner@test.example.it";
    public const string OwnerPassword = "TestPassword123!";

    // Lunedì ≥ 7 giorni da oggi: giorno lavorativo, dentro i 30 visibili, ben oltre MinAdvanceHours=1h.
    public static DateOnly FutureMonday =>
        Enumerable.Range(7, 14)
                  .Select(d => DateOnly.FromDateTime(DateTime.Today.AddDays(d)))
                  .First(d => d.DayOfWeek == DayOfWeek.Monday);

    // Domenica prossima: giorno chiuso — usato per testare slot non disponibili.
    public static DateOnly NextSunday =>
        Enumerable.Range(1, 8)
                  .Select(d => DateOnly.FromDateTime(DateTime.Today.AddDays(d)))
                  .First(d => d.DayOfWeek == DayOfWeek.Sunday);

    /// <summary>Inserisce tenant, orari, chiave API, servizi e staff nel DB di test.</summary>
    public static async Task SeedAsync(BookingSystemDbContext db)
    {
        // WHY: il container Testcontainers può essere riusato tra run consecutive (Ryuk non ancora attivo).
        // Rileviamo il tenant già esistente per rendere il seed idempotente. Se il tenant esiste ma mancano
        // entità aggiunte in sessioni successive (es. ServiceBufferId), le inseriamo selettivamente.
        if (await db.Tenants.AnyAsync(t => t.Id == TenantId))
        {
            await EnsureLaterSeedAsync(db);
            return;
        }

        var now = DateTimeOffset.UtcNow;

        db.Tenants.Add(new Tenant
        {
            Id = TenantId, Slug = "test-barbershop", Name = "Test Barbershop",
            SiteUrl = "https://test.example.it", OwnerEmail = "owner@test.example.it",
            Timezone = "Europe/Rome", MinAdvanceHours = 1, MinCancellationHours = 24,
            VisibleDaysAhead = 30, StaffChoiceEnabled = true, NotificationMethod = "email",
            Active = true, CreatedAt = now, UpdatedAt = now,
        });

        // Orari: Dom chiuso, Lun-Ven 9-19 con pausa 13-14, Sab 9-13.
        db.TenantBusinessHours.AddRange(
            ClosedDay(TenantId, DayOfWeekIndex.Domenica),
            OpenDay(TenantId, DayOfWeekIndex.Lunedi,    "09:00", "19:00", "13:00", "14:00"),
            OpenDay(TenantId, DayOfWeekIndex.Martedi,   "09:00", "19:00", "13:00", "14:00"),
            OpenDay(TenantId, DayOfWeekIndex.Mercoledi, "09:00", "19:00", "13:00", "14:00"),
            OpenDay(TenantId, DayOfWeekIndex.Giovedi,   "09:00", "19:00", "13:00", "14:00"),
            OpenDay(TenantId, DayOfWeekIndex.Venerdi,   "09:00", "19:00", "13:00", "14:00"),
            OpenDay(TenantId, DayOfWeekIndex.Sabato,    "09:00", "13:00")
        );

        db.TenantApiKeys.Add(new TenantApiKey
        {
            Id = ApiKeyId, TenantId = TenantId, KeyHash = ApiKeyHash,
            KeyPrefix = RawApiKey[..8], Description = "Integration test key",
            Active = true, CreatedAt = now,
        });

        db.Users.Add(new User
        {
            Id = OwnerUserId, TenantId = TenantId, Email = OwnerEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(OwnerPassword),
            ActivatedAt = now, SecurityStamp = Guid.NewGuid(),
            Role = UserRole.Owner, Active = true, CreatedAt = now, UpdatedAt = now,
        });

        db.Services.AddRange(
            new Service
            {
                Id = ServiceMultiId, TenantId = TenantId, Name = "Taglio Multi",
                DurationMinutes = 30, BasePrice = 18m, ParallelSlots = 2,
                Active = true, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now,
            },
            new Service
            {
                Id = ServiceSingleId, TenantId = TenantId, Name = "Taglio Singolo",
                DurationMinutes = 30, BasePrice = 15m, ParallelSlots = 1,
                Active = true, DisplayOrder = 2, CreatedAt = now, UpdatedAt = now,
            },
            new Service
            {
                // WHY: servizio dedicato al test D-10 — buffer After 15min: finestra occupata = DurationMinutes + bufferAfter.
                Id = ServiceBufferId, TenantId = TenantId, Name = "Taglio Buffer",
                DurationMinutes = 30, BasePrice = 20m, ParallelSlots = 1,
                BufferEnabled = true, BufferMinutes = 15, BufferPosition = BufferPosition.After,
                Active = true, DisplayOrder = 3, CreatedAt = now, UpdatedAt = now,
            },
            new Service
            {
                // WHY (T1.2): servizio SENZA operatori → capacità a parallelSlots. Nessun StaffService collegato.
                Id = ServiceParallelId, TenantId = TenantId, Name = "Servizio Parallelo",
                DurationMinutes = 30, BasePrice = 10m, ParallelSlots = 2,
                Active = true, DisplayOrder = 4, CreatedAt = now, UpdatedAt = now,
            }
        );

        db.Staff.Add(new Staff
        {
            Id = StaffId, TenantId = TenantId, Name = "Marco Test", Role = "Barbiere",
            Active = true, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now,
        });

        // L'operatore esegue Multi/Single/Buffer (percorso aggregato/auto-assegnazione, T1.2). ServiceParallel
        // resta SENZA operatori (fallback a parallelSlots).
        db.StaffServices.AddRange(
            new StaffService { Id = Guid.NewGuid(), TenantId = TenantId, StaffId = StaffId, ServiceId = ServiceMultiId },
            new StaffService { Id = Guid.NewGuid(), TenantId = TenantId, StaffId = StaffId, ServiceId = ServiceSingleId },
            new StaffService { Id = Guid.NewGuid(), TenantId = TenantId, StaffId = StaffId, ServiceId = ServiceBufferId }
        );

        // Staff disponibile Lun-Ven 9-19 (coincide con gli orari del tenant per semplicità).
        foreach (var day in new[] { DayOfWeekIndex.Lunedi, DayOfWeekIndex.Martedi, DayOfWeekIndex.Mercoledi, DayOfWeekIndex.Giovedi, DayOfWeekIndex.Venerdi })
        {
            db.StaffBusinessHours.Add(new StaffBusinessHours
            {
                Id = Guid.NewGuid(), TenantId = TenantId, StaffId = StaffId,
                DayOfWeek = day, IsAvailable = true,
                StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(19, 0),
            });
        }

        await db.SaveChangesAsync();
    }

    // WHY: quando il container viene riusato, il tenant esiste già ma entità aggiunte in run successive
    // (es. ServiceBufferId, ServiceParallelId) possono mancare. Le inseriamo singolarmente (ognuna con il
    // proprio guard) senza toccare il resto. IgnoreQueryFilters() perché nel fixture non c'è ITenantContext.
    private static async Task EnsureLaterSeedAsync(BookingSystemDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        bool changed = false;

        if (!await db.Services.IgnoreQueryFilters().AnyAsync(s => s.Id == ServiceBufferId))
        {
            db.Services.Add(new Service
            {
                Id = ServiceBufferId, TenantId = TenantId, Name = "Taglio Buffer",
                DurationMinutes = 30, BasePrice = 20m, ParallelSlots = 1,
                BufferEnabled = true, BufferMinutes = 15, BufferPosition = BufferPosition.After,
                Active = true, DisplayOrder = 3, CreatedAt = now, UpdatedAt = now,
            });
            db.StaffServices.Add(new StaffService
            {
                Id = Guid.NewGuid(), TenantId = TenantId, StaffId = StaffId, ServiceId = ServiceBufferId,
            });
            changed = true;
        }

        if (!await db.Services.IgnoreQueryFilters().AnyAsync(s => s.Id == ServiceParallelId))
        {
            // SENZA StaffService: testa il fallback a parallelSlots (T1.2).
            db.Services.Add(new Service
            {
                Id = ServiceParallelId, TenantId = TenantId, Name = "Servizio Parallelo",
                DurationMinutes = 30, BasePrice = 10m, ParallelSlots = 2,
                Active = true, DisplayOrder = 4, CreatedAt = now, UpdatedAt = now,
            });
            changed = true;
        }

        if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == OwnerUserId))
        {
            db.Users.Add(new User
            {
                Id = OwnerUserId, TenantId = TenantId, Email = OwnerEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(OwnerPassword),
                ActivatedAt = now, SecurityStamp = Guid.NewGuid(),
                Role = UserRole.Owner, Active = true, CreatedAt = now, UpdatedAt = now,
            });
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static TenantBusinessHours OpenDay(Guid tenantId, DayOfWeekIndex day,
        string open, string close, string? bStart = null, string? bEnd = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, DayOfWeek = day, IsOpen = true,
        OpenTime = TimeOnly.Parse(open), CloseTime = TimeOnly.Parse(close),
        BreakStart = bStart is null ? null : TimeOnly.Parse(bStart),
        BreakEnd   = bEnd   is null ? null : TimeOnly.Parse(bEnd),
    };

    private static TenantBusinessHours ClosedDay(Guid tenantId, DayOfWeekIndex day) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, DayOfWeek = day, IsOpen = false,
    };
}
