using Moq;
using System.Net;
using System.Text;

namespace BookshelfWallpaper.Api.Tests.Helpers;

/// <summary>Controllable <see cref="HttpMessageHandler"/> for unit tests.</summary>
internal sealed class HttpHandlerStub : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    internal HttpHandlerStub(string responseBody, HttpStatusCode status = HttpStatusCode.OK)
        : this(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        })
    { }

    internal HttpHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_respond(request));

    /// <summary>
    /// Creates an <see cref="IHttpClientFactory"/> that always returns a client
    /// backed by this handler.
    /// </summary>
    internal IHttpClientFactory ToFactory()
    {
        var mock = new Mock<IHttpClientFactory>();
        mock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(this));
        return mock.Object;
    }

    /// <summary>Creates a factory where each <c>CreateClient</c> call uses the next handler in order.</summary>
    internal static IHttpClientFactory SequentialFactory(params HttpHandlerStub[] handlers)
    {
        var queue = new Queue<HttpHandlerStub>(handlers);
        var mock = new Mock<IHttpClientFactory>();
        mock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() =>
            {
                var h = queue.Count > 0 ? queue.Dequeue() : handlers[^1];
                return new HttpClient(h);
            });
        return mock.Object;
    }

    /// <summary>Empty 200 OK factory – for tests where HTTP is not expected to be called.</summary>
    internal static IHttpClientFactory Silent() =>
        new HttpHandlerStub("{}").ToFactory();
}
