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
}
