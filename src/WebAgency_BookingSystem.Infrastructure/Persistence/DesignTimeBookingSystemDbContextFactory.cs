// [INTENT]: Factory usata SOLO a design-time da `dotnet ef` (migrations add / script). Permette agli
// strumenti EF di istanziare il DbContext senza il container DI dell'applicazione. NON apre connessioni
// per `migrations add` (genera solo codice); la connection string serve solo a configurare il provider.
// A runtime questa classe non viene mai usata: il DbContext è creato dal DI con il vero ITenantContext.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WebAgency_BookingSystem.Infrastructure.Tenancy;

namespace WebAgency_BookingSystem.Infrastructure.Persistence;

internal sealed class DesignTimeBookingSystemDbContextFactory : IDesignTimeDbContextFactory<BookingSystemDbContext>
{
    public BookingSystemDbContext CreateDbContext(string[] args)
    {
        // WHY: a design-time il DI non è disponibile; leggiamo DATABASE_URL se presente, altrimenti una
        // connection string locale di default. Il valore non viene usato per connettersi durante l'add.
        string connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5432;Database=bookingsystem;Username=postgres;Password=postgres";

        DbContextOptions<BookingSystemDbContext> options =
            new DbContextOptionsBuilder<BookingSystemDbContext>()
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention()
                .Options;

        // Tenant non risolto a design-time: i query filter non vengono comunque eseguiti durante la generazione.
        return new BookingSystemDbContext(options, new TenantContext());
    }
}
