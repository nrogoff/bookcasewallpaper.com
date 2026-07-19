using BookshelfWallpaper.Api.Functions;
using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using BookshelfWallpaper.Api.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BookshelfWallpaper.Api.Tests.Functions;

public sealed class AudibleFunctionsTests
{
    private static AudibleFunctions CreateSut(
        Mock<ICosmosDbService>? cosmos = null,
        Mock<IAudibleService>? audible = null,
        IHttpClientFactory? http = null) =>
        new(cosmos?.Object ?? CosmosFactory.CosmosDbService().Object,
            audible?.Object ?? new Mock<IAudibleService>().Object,
            http ?? HttpHandlerStub.Silent(),
            NullLogger<AudibleFunctions>.Instance);

    // ── GetAudibleAuthUrl ─────────────────────────────────────────────────────

    [Fact]
    public void GetAudibleAuthUrl_WhenClientIdNotConfigured_Returns503()
    {
        Environment.SetEnvironmentVariable("AMAZON_CLIENT_ID", null);
        try
        {
            var result = CreateSut().GetAudibleAuthUrl(RequestFactory.Create());

            Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        }
        finally { Environment.SetEnvironmentVariable("AMAZON_CLIENT_ID", null); }
    }

    [Fact]
    public void GetAudibleAuthUrl_WithClientIdSet_ReturnsAuthUrl()
    {
        Environment.SetEnvironmentVariable("AMAZON_CLIENT_ID", "test-client-id");
        try
        {
            var req = RequestFactory.Create(
                userId: "user-1",
                query: new Dictionary<string, string> { ["marketplace"] = "UK" });

            var result = CreateSut().GetAudibleAuthUrl(req);

            var ok = Assert.IsType<OkObjectResult>(result);
            var authUrl = ok.Value!.GetType().GetProperty("authUrl")!.GetValue(ok.Value)!.ToString();
            Assert.NotNull(authUrl);
            Assert.Contains("amazon.co.uk", authUrl);
            Assert.Contains("test-client-id", authUrl);
        }
        finally { Environment.SetEnvironmentVariable("AMAZON_CLIENT_ID", null); }
    }

    [Fact]
    public void GetAudibleAuthUrl_WithUsMarketplace_UsesAmazonCom()
    {
        Environment.SetEnvironmentVariable("AMAZON_CLIENT_ID", "client-id");
        try
        {
            var req = RequestFactory.Create(
                query: new Dictionary<string, string> { ["marketplace"] = "US" });

            var result = CreateSut().GetAudibleAuthUrl(req);

            var ok = Assert.IsType<OkObjectResult>(result);
            var authUrl = ok.Value!.GetType().GetProperty("authUrl")!.GetValue(ok.Value)!.ToString()!;
            Assert.Contains("amazon.com/ap/oa", authUrl);
        }
        finally { Environment.SetEnvironmentVariable("AMAZON_CLIENT_ID", null); }
    }

    // ── GetAudibleConnectionStatus ────────────────────────────────────────────

    [Fact]
    public async Task GetAudibleConnectionStatus_WhenNoConnection_ReturnsDisconnected()
    {
        var connectionsMock = CosmosFactory.Container();
        connectionsMock
            .Setup(c => c.ReadItemAsync<AudibleConnection>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosFactory.NotFound());

        var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(
            audibleConnectionsContainer: connectionsMock));

        var result = await sut.GetAudibleConnectionStatus(
            RequestFactory.Create(userId: "user-1"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var connected = (bool)ok.Value!.GetType().GetProperty("connected")!.GetValue(ok.Value)!;
        Assert.False(connected);
    }

    [Fact]
    public async Task GetAudibleConnectionStatus_WithValidToken_ReturnsConnected()
    {
        var conn = new AudibleConnection
        {
            Id = "user-1",
            UserId = "user-1",
            AccessToken = "valid-token",
            Marketplace = "UK",
            ExpiresAt = DateTime.UtcNow.AddHours(1).ToString("O"), // not expired
        };

        var connectionsMock = CosmosFactory.Container();
        connectionsMock
            .Setup(c => c.ReadItemAsync<AudibleConnection>("user-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(conn).Object);

        var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(
            audibleConnectionsContainer: connectionsMock));

        var result = await sut.GetAudibleConnectionStatus(
            RequestFactory.Create(userId: "user-1"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var val = ok.Value!;
        var connected = (bool)val.GetType().GetProperty("connected")!.GetValue(val)!;
        Assert.True(connected);
    }

    [Fact]
    public async Task GetAudibleConnectionStatus_WhenTokenExpired_ReturnsDisconnected()
    {
        var conn = new AudibleConnection
        {
            Id = "user-1",
            UserId = "user-1",
            AccessToken = "expired-token",
            ExpiresAt = DateTime.UtcNow.AddHours(-1).ToString("O"), // expired
        };

        var connectionsMock = CosmosFactory.Container();
        connectionsMock
            .Setup(c => c.ReadItemAsync<AudibleConnection>("user-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(conn).Object);

        var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(
            audibleConnectionsContainer: connectionsMock));

        var result = await sut.GetAudibleConnectionStatus(
            RequestFactory.Create(userId: "user-1"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var connected = (bool)ok.Value!.GetType().GetProperty("connected")!.GetValue(ok.Value)!;
        Assert.False(connected);
    }

    // ── DisconnectAudible ─────────────────────────────────────────────────────

    [Fact]
    public async Task DisconnectAudible_WhenConnected_DeletesAndReturnsDisconnected()
    {
        var connectionsMock = CosmosFactory.Container();
        connectionsMock
            .Setup(c => c.DeleteItemAsync<AudibleConnection>(
                "user-1", It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(new AudibleConnection()).Object);

        var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(
            audibleConnectionsContainer: connectionsMock));

        var result = await sut.DisconnectAudible(
            RequestFactory.Create(userId: "user-1"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.False((bool)ok.Value!.GetType().GetProperty("connected")!.GetValue(ok.Value)!);
    }

    [Fact]
    public async Task DisconnectAudible_WhenAlreadyDisconnected_ReturnsDisconnected()
    {
        var connectionsMock = CosmosFactory.Container();
        connectionsMock
            .Setup(c => c.DeleteItemAsync<AudibleConnection>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosFactory.NotFound());

        var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(
            audibleConnectionsContainer: connectionsMock));

        var result = await sut.DisconnectAudible(
            RequestFactory.Create(userId: "user-1"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result); // idempotent
    }

    // ── SyncAudible ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncAudible_WithMissingShelfId_ReturnsBadRequest()
    {
        var result = await CreateSut().SyncAudible(
            RequestFactory.Create(body: new { shelfId = "" }, method: "POST"),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SyncAudible_WithNullBody_ReturnsBadRequest()
    {
        var result = await CreateSut().SyncAudible(
            RequestFactory.Create(method: "POST"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SyncAudible_WhenAudibleSyncExceptionThrown_ReturnsStatusFromException()
    {
        var audibleMock = new Mock<IAudibleService>();
        audibleMock
            .Setup(a => a.SyncAudibleIntoShelfAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<Microsoft.Extensions.Logging.ILogger>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AudibleSyncException(428, "Audible account not connected"));

        var sut = CreateSut(audible: audibleMock);

        var result = await sut.SyncAudible(
            RequestFactory.Create(body: new { shelfId = "shelf-1" }, method: "POST"),
            CancellationToken.None);

        Assert.Equal(428, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task SyncAudible_OnSuccess_ReturnsOkWithSyncResult()
    {
        var syncResult = new AudibleSyncResult { BooksFound = 3, BooksAdded = 2 };
        var audibleMock = new Mock<IAudibleService>();
        audibleMock
            .Setup(a => a.SyncAudibleIntoShelfAsync(
                It.IsAny<string>(), "shelf-1", It.IsAny<string?>(),
                It.IsAny<Microsoft.Extensions.Logging.ILogger>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncResult);

        var sut = CreateSut(audible: audibleMock);

        var result = await sut.SyncAudible(
            RequestFactory.Create(body: new { shelfId = "shelf-1" }, method: "POST"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(syncResult, ok.Value);
    }

    // ── AudibleCallback ───────────────────────────────────────────────────────

    [Fact]
    public async Task AudibleCallback_WithErrorQueryParam_ReturnsRedirect()
    {
        var result = await CreateSut().AudibleCallback(
            RequestFactory.Create(query: new Dictionary<string, string>
            {
                ["error"] = "access_denied",
            }),
            CancellationToken.None);

        Assert.IsType<RedirectResult>(result);
    }

    [Fact]
    public async Task AudibleCallback_WithMissingCode_ReturnsBadRequest()
    {
        var result = await CreateSut().AudibleCallback(
            RequestFactory.Create(query: new Dictionary<string, string>
            {
                // no "code" param
            }),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AudibleCallback_WithMissingClientCredentials_Returns503()
    {
        Environment.SetEnvironmentVariable("AMAZON_CLIENT_ID", null);
        Environment.SetEnvironmentVariable("AMAZON_CLIENT_SECRET", null);
        try
        {
            var result = await CreateSut().AudibleCallback(
                RequestFactory.Create(query: new Dictionary<string, string> { ["code"] = "auth-code" }),
                CancellationToken.None);

            Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AMAZON_CLIENT_ID", null);
            Environment.SetEnvironmentVariable("AMAZON_CLIENT_SECRET", null);
        }
    }

    // ── ParseCallbackState ────────────────────────────────────────────────────

    [Fact]
    public void ParseCallbackState_WithValidBase64Json_ReturnsState()
    {
        var json = """{"userId":"user-1","marketplace":"UK","shelfId":"shelf-1"}""";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        var state = AudibleFunctions.ParseCallbackState(encoded);

        Assert.NotNull(state);
        Assert.Equal("user-1", state!.UserId);
        Assert.Equal("UK", state.Marketplace);
        Assert.Equal("shelf-1", state.ShelfId);
    }

    [Fact]
    public void ParseCallbackState_WithInvalidBase64_ReturnsStateWithRawValue()
    {
        var state = AudibleFunctions.ParseCallbackState("not-valid-base64!!!");

        // Falls back to { UserId = state }
        Assert.NotNull(state);
        Assert.Equal("not-valid-base64!!!", state!.UserId);
    }

    [Fact]
    public void ParseCallbackState_WithNull_ReturnsNull()
    {
        Assert.Null(AudibleFunctions.ParseCallbackState(null));
    }
}
