// [INTENT]: DbContext applicativo. Espone i DbSet, applica le configurazioni Fluent API (una per entità)
// e installa i GLOBAL QUERY FILTER su tenant_id per tutte le entità tenant-scoped, prevenendo data leak
// cross-tenant per dimenticanza. Il tenant corrente è fornito da ITenantContext (popolato dal middleware);
// EF rivaluta il suo TenantId a ogni query, così il filtro segue la richiesta in corso.

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence;

/// <summary>
/// Contesto EF Core del sistema di prenotazioni. Tutte le entità tranne <see cref="Tenant"/> sono
/// filtrate automaticamente sul tenant corrente.
/// </summary>
public sealed class BookingSystemDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public BookingSystemDbContext(DbContextOptions<BookingSystemDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantApiKey> TenantApiKeys => Set<TenantApiKey>();
    public DbSet<TenantBusinessHours> TenantBusinessHours => Set<TenantBusinessHours>();
    public DbSet<TenantSpecialClosure> TenantSpecialClosures => Set<TenantSpecialClosure>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<StaffService> StaffServices => Set<StaffService>();
    public DbSet<StaffBusinessHours> StaffBusinessHours => Set<StaffBusinessHours>();
    public DbSet<StaffTimeOff> StaffTimeOff => Set<StaffTimeOff>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingItem> BookingItems => Set<BookingItem>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<User> Users => Set<User>();
    public DbSet<OutboxEmail> OutboxEmails => Set<OutboxEmail>();
    public DbSet<UserSecurityToken> UserSecurityTokens => Set<UserSecurityToken>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configurazioni Fluent API: una classe IEntityTypeConfiguration per entità (cartella Configurations).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingSystemDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    // WHY: i filtri sono impostati qui (non nelle singole config) perché devono catturare _tenantContext,
    // che è un servizio scoped iniettato nel DbContext. EF tratta l'accesso a _tenantContext.TenantId come
    // un parametro rivalutato a ogni query, quindi il filtro segue il tenant della richiesta corrente.
    // Se il tenant non è risolto (TenantId == null), il confronto con TenantId (non-null) non restituisce
    // alcuna riga: comportamento sicuro by default (nessuna fuga di dati).
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantApiKey>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<TenantBusinessHours>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<TenantSpecialClosure>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<StaffService>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<StaffBusinessHours>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<StaffTimeOff>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Booking>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<BookingItem>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<User>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
        // WHY: l'accodamento avviene in scope tenant (filtro coerente), ma il dispatcher processa la outbox
        // CROSS-tenant → userà IgnoreQueryFilters(), come il cleanup job delle prenotazioni scadute.
        modelBuilder.Entity<OutboxEmail>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);

        // Service e Staff combinano il filtro tenant con il soft delete (DeletedAt == null).
        modelBuilder.Entity<Service>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId && e.DeletedAt == null);
        modelBuilder.Entity<Staff>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId && e.DeletedAt == null);
    }
}
