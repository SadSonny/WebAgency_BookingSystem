// [INTENT]: Accesso ai PlatformAdmin. Le ricerche sono pre-auth (login/setup) e cross-tenant → IgnoreQueryFilters
// non serve (l'entità non ha filtro), ma manteniamo AsNoTracking sulle letture pure.

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;

internal sealed class PlatformAdminRepository : IPlatformAdminRepository
{
    private readonly BookingSystemDbContext _db;
    public PlatformAdminRepository(BookingSystemDbContext db) => _db = db;

    public Task<PlatformAdmin?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.PlatformAdmins.AsNoTracking().FirstOrDefaultAsync(p => p.Email == email, ct);

    public Task<PlatformAdmin?> GetTrackedByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PlatformAdmins.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Guid?> GetSecurityStampAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.PlatformAdmins.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new { p.SecurityStamp }).FirstOrDefaultAsync(ct);
        return row?.SecurityStamp;
    }

    public async Task RegisterFailedAttemptAsync(Guid id, int threshold, TimeSpan duration, CancellationToken ct = default)
    {
        PlatformAdmin? a = await _db.PlatformAdmins.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (a is null) return;
        a.FailedAccessCount++;
        if (a.FailedAccessCount >= threshold)
        {
            a.LockoutEnd = DateTimeOffset.UtcNow.Add(duration);
            a.FailedAccessCount = 0;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task RegisterSuccessfulLoginAsync(Guid id, CancellationToken ct = default)
    {
        PlatformAdmin? a = await _db.PlatformAdmins.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (a is null) return;
        a.FailedAccessCount = 0;
        a.LockoutEnd = null;
        a.LastLoginAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpsertPasswordByEmailAsync(string email, string passwordHash, CancellationToken ct = default)
    {
        // WHY: setup/break-glass — crea l'admin se non esiste, altrimenti ne reimposta la password. L'unique su
        // Email rende l'operazione idempotente per email anche con chiamate concorrenti.
        PlatformAdmin? a = await _db.PlatformAdmins.FirstOrDefaultAsync(p => p.Email == email, ct);
        bool created = a is null;
        if (a is null)
        {
            a = new PlatformAdmin { Id = Guid.NewGuid(), Email = email, ActivatedAt = DateTimeOffset.UtcNow };
            _db.PlatformAdmins.Add(a);
        }
        a.PasswordHash = passwordHash;
        a.SecurityStamp = Guid.NewGuid();
        a.Active = true;
        a.FailedAccessCount = 0;
        a.LockoutEnd = null;
        await _db.SaveChangesAsync(ct);
        return created;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
