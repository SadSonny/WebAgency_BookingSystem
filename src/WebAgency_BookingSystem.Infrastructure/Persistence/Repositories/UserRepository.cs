// [INTENT]: Implementazione EF Core di IUserRepository. La ricerca per (tenant, email) avviene al login,
// prima che il tenant corrente sia risolto: IgnoreQueryFilters + filtro esplicito su TenantId. AsNoTracking
// perché l'esito è di sola lettura (verifica password).

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository : IUserRepository
{
    private readonly BookingSystemDbContext _db;

    public UserRepository(BookingSystemDbContext db) => _db = db;

    public Task<User?> GetByTenantAndEmailAsync(Guid tenantId, string email, CancellationToken ct = default) =>
        _db.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Email == email, ct);

    public async Task RegisterFailedAttemptAsync(Guid userId, int lockoutThreshold, TimeSpan lockoutDuration, CancellationToken ct = default)
    {
        // Carica tracked (no AsNoTracking) per persistere la mutazione; IgnoreQueryFilters perché il login è
        // pre-risoluzione tenant.
        User? user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return;
        }

        user.FailedAccessCount++;
        if (user.FailedAccessCount >= lockoutThreshold)
        {
            user.LockoutEnd = DateTimeOffset.UtcNow.Add(lockoutDuration);
            user.FailedAccessCount = 0; // riparte da zero dopo il blocco
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task RegisterSuccessfulLoginAsync(Guid userId, CancellationToken ct = default)
    {
        User? user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return;
        }

        user.FailedAccessCount = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
