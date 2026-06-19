// [INTENT]: Implementazione di IDbHealthProbe: verifica la connettività al database con CanConnectAsync. Singleton
// che apre uno scope per chiamata (il DbContext è scoped). Non lancia: in caso di errore restituisce false.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAgency_BookingSystem.Core.Observability;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Observability;

internal sealed class DbHealthProbe : IDbHealthProbe
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DbHealthProbe(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<bool> CanConnectAsync(CancellationToken ct = default)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            BookingSystemDbContext db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
            return await db.Database.CanConnectAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // WHY: un'eccezione di connessione significa "DB non raggiungibile", non un errore da propagare.
            return false;
        }
    }
}
