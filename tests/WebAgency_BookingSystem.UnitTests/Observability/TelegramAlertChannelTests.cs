// [INTENT]: Unit test di TelegramAlertChannel con un HttpMessageHandler stub: verifica che la richiesta vada su
// "sendMessage" col chat_id corretto, e che un errore HTTP o un'eccezione di rete NON propaghino (swallowed).

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WebAgency_BookingSystem.Core.Observability;
using WebAgency_BookingSystem.Infrastructure.Observability;

namespace WebAgency_BookingSystem.UnitTests.Observability;

public class TelegramAlertChannelTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private static IHttpClientFactory FactoryFor(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/bot123:abc/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(TelegramAlertChannel.HttpClientName).Returns(http);
        return factory;
    }

    private static OpsAlert SampleAlert() =>
        new(OpsAlertKind.ErrorDigest, "2 errori", "dettaglio", DateTimeOffset.UtcNow);

    [Fact]
    public async Task invia_su_sendMessage_con_chat_id()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new TelegramAlertChannel(FactoryFor(handler), "999", NullLogger<TelegramAlertChannel>.Instance);

        await sut.SendAsync(SampleAlert());

        Assert.EndsWith("sendMessage", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"chat_id\":\"999\"", handler.LastBody);
        Assert.Contains("2 errori", handler.LastBody);
    }

    [Fact]
    public async Task errore_http_non_propaga()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = new TelegramAlertChannel(FactoryFor(handler), "999", NullLogger<TelegramAlertChannel>.Instance);

        // Non deve lanciare.
        await sut.SendAsync(SampleAlert());
    }

    [Fact]
    public async Task eccezione_di_rete_non_propaga()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("network down"));
        var sut = new TelegramAlertChannel(FactoryFor(handler), "999", NullLogger<TelegramAlertChannel>.Instance);

        await sut.SendAsync(SampleAlert());
    }
}
