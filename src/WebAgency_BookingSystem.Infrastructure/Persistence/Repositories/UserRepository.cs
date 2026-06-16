// [INTENT]: Implementazione EF Core di IUserRepository. La ricerca per email è GLOBALE (un'email = un account)
// e avviene al login, prima che il tenant corrente sia risolto: IgnoreQueryFilters per bypassare il global query
// filter. Gestisce anche i token di sicurezza (attivazione/reset) e la lettura della SecurityStamp.

using Microsoft.EntityFrameworkCore;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository : IUserRepository
{
    private readonly BookingSystemDbContext _db;

    public UserRepository(BookingSystemDbContext db) => _db = db;

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> GetTrackedByIdAsync(Guid userId, CancellationToken ct = default) =>
        _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);

    public async Task<Guid?> GetSecurityStampAsync(Guid userId, CancellationToken ct = default)
    {
        var row = await _db.Users
            .AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.SecurityStamp })
            .FirstOrDefaultAsync(ct);
        return row?.SecurityStamp;
    }

    public async Task AddTokenInvalidatingPreviousAsync(UserSecurityToken token, CancellationToken ct = default)
    {
        // WHY: un solo token attivo per scopo: marchiamo "usati" i precedenti ancora validi prima di aggiungere.
        // NON salviamo qui: il chiamante chiama SaveChangesAsync così l'email può entrare nella stessa transazione.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<UserSecurityToken> previous = await _db.UserSecurityTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == token.UserId && t.Purpose == token.Purpose && t.UsedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);
        foreach (UserSecurityToken t in previous)
        {
            t.UsedAt = now;
        }

        _db.UserSecurityTokens.Add(token);
    }

    public Task<UserSecurityToken?> GetValidTokenAsync(string tokenHash, SecurityTokenPurpose purpose, CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return _db.UserSecurityTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.Purpose == purpose
                                      && t.UsedAt == null && t.ExpiresAt > now, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

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
