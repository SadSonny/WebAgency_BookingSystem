// [INTENT]: Interceptor di SaveChanges che valorizza CreatedAt/UpdatedAt sulle entità IAuditableEntity (R-27):
// CreatedAt+UpdatedAt sugli inserimenti, UpdatedAt sulle modifiche. Centralizza i timestamp così nessun punto
// di creazione/modifica (anche futuri: Admin CRUD, CLI) può dimenticarli o impostarli in modo incoerente.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Interceptors;

internal sealed class TimestampInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyTimestamps(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ApplyTimestamps(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ApplyTimestamps(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
