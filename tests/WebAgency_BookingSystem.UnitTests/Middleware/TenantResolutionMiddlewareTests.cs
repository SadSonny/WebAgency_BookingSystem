// [INTENT]: Unit test per TenantResolutionMiddleware — verifica l'esclusione dei path non tenant-scoped,
// la restituzione di 401 se manca l'API key, di 403 se la chiave non è valida, e la risoluzione corretta
// del tenant quando la chiave è valida. Usa NSubstitute per i collaboratori (ITenantContext, ITenantRepository).

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WebAgency_BookingSystem.Api.Middleware;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.UnitTests.Middleware;

public sealed class TenantResolutionMiddlewareTests
{
    private static TenantResolutionMiddleware BuildSut(RequestDelegate next) =>
        new(next, Substitute.For<ILogger<TenantResolutionMiddleware>>());

    private static DefaultHttpContext BuildContext(string path)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        // WHY: il body di default è Stream.Null e HttpErrorWriter usa WriteAsync — serve uno stream scrivibile.
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Theory]
    [InlineData("/api/v1/health")]
    [InlineData("/api/v1/admin/auth/token")]
    [InlineData("/scalar")]
    [InlineData("/openapi/v1.json")]
    public async Task path_escluso_passa_direttamente_senza_leggere_api_key(string path)
    {
        bool nextCalled = false;
        var sut = BuildSut(_ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = BuildContext(path);
        var tenantContext = Substitute.For<ITenantContext>();
        var tenantRepo    = Substitute.For<ITenantRepository>();

        await sut.InvokeAsync(ctx, tenantContext, tenantRepo);

        Assert.True(nextCalled);
        tenantContext.DidNotReceive().SetTenant(Arg.Any<Tenant>());
    }

    [Fact]
    public async Task api_key_mancante_restituisce_401()
    {
        var sut = BuildSut(_ => Task.CompletedTask);
        var ctx = BuildContext("/api/v1/services");

        await sut.InvokeAsync(ctx, Substitute.For<ITenantContext>(), Substitute.For<ITenantRepository>());

        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task api_key_non_valida_restituisce_403()
    {
        var sut = BuildSut(_ => Task.CompletedTask);
        var ctx = BuildContext("/api/v1/services");
        ctx.Request.Headers["X-Api-Key"] = "chiave-inesistente";
        var tenantRepo = Substitute.For<ITenantRepository>();
        tenantRepo.ResolveActiveByApiKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                  .Returns((Tenant?)null);

        await sut.InvokeAsync(ctx, Substitute.For<ITenantContext>(), tenantRepo);

        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task api_key_valida_risolve_tenant_e_prosegue()
    {
        bool nextCalled = false;
        var sut = BuildSut(_ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = BuildContext("/api/v1/services");
        ctx.Request.Headers["X-Api-Key"] = "chiave-valida";
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(), Slug = "test", Name = "Test", Active = true,
            Timezone = "Europe/Rome", MinAdvanceHours = 1, MinCancellationHours = 24, VisibleDaysAhead = 30,
        };
        var tenantRepo = Substitute.For<ITenantRepository>();
        tenantRepo.ResolveActiveByApiKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                  .Returns(tenant);
        var tenantContext = Substitute.For<ITenantContext>();

        await sut.InvokeAsync(ctx, tenantContext, tenantRepo);

        Assert.True(nextCalled);
        tenantContext.Received(1).SetTenant(tenant);
    }
}
