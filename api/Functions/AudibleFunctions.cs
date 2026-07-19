using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace BookshelfWallpaper.Api.Functions;

public sealed class AudibleFunctions
{
    private readonly ICosmosDbService _cosmos;
    private readonly IAudibleService _audibleService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AudibleFunctions> _logger;

    private static readonly Dictionary<string, string> AuthBases = new()
    {
        ["US"] = "https://www.amazon.com",
        ["UK"] = "https://www.amazon.co.uk",
        ["DE"] = "https://www.amazon.de",
        ["FR"] = "https://www.amazon.fr",
        ["AU"] = "https://www.amazon.com.au",
        ["CA"] = "https://www.amazon.ca",
        ["JP"] = "https://www.amazon.co.jp",
        ["IT"] = "https://www.amazon.it",
        ["ES"] = "https://www.amazon.es",
        ["IN"] = "https://www.amazon.in",
    };

    public AudibleFunctions(ICosmosDbService cosmos, IAudibleService audibleService, IHttpClientFactory httpClientFactory, ILogger<AudibleFunctions> logger)
    {
        _cosmos = cosmos;
        _audibleService = audibleService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static string GetUserId(HttpRequest req)
        => req.Headers.TryGetValue("x-ms-client-principal-id", out var v) && !string.IsNullOrEmpty(v) ? v.ToString() : "anonymous";

    [Function("getAudibleAuthUrl")]
    public IActionResult GetAudibleAuthUrl(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "getAudibleAuthUrl")] HttpRequest req)
    {
        _logger.LogInformation("getAudibleAuthUrl triggered");
        var clientId = Environment.GetEnvironmentVariable("AMAZON_CLIENT_ID");
        if (string.IsNullOrEmpty(clientId))
            return new ObjectResult(new { error = "Audible OAuth is not configured on this server" }) { StatusCode = 503 };

        var marketplace = req.Query["marketplace"].ToString().NullIfEmpty() ?? "UK";
        var shelfId = req.Query["shelfId"].ToString().NullIfEmpty();
        var userId = GetUserId(req);
        var redirectUri = ResolveRedirectUri(req);

        var stateJson = JsonSerializer.Serialize(new { userId, marketplace, shelfId });
        var state = Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(stateJson)));
        var authBase = AuthBases.GetValueOrDefault(marketplace, "https://www.amazon.co.uk");
        var authUrl = $"{authBase}/ap/oa?client_id={Uri.EscapeDataString(clientId)}&scope=profile+profile%3Auser_id&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={state}";

        return new OkObjectResult(new { authUrl });
    }

    [Function("audibleCallback")]
    public async Task<IActionResult> AudibleCallback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "audibleCallback")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("audibleCallback triggered");

        var oauthError = req.Query["error"].ToString().NullIfEmpty();
        if (oauthError is not null)
            return Redirect(req, new { audible = "error", reason = oauthError });

        var code = req.Query["code"].ToString().NullIfEmpty();
        if (code is null) return new BadRequestObjectResult(new { error = "Missing required query parameter: code" });

        var clientId = Environment.GetEnvironmentVariable("AMAZON_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("AMAZON_CLIENT_SECRET");
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return new ObjectResult(new { error = "Audible OAuth is not configured. Set AMAZON_CLIENT_ID and AMAZON_CLIENT_SECRET." }) { StatusCode = 503 };

        var redirectUri = ResolveRedirectUri(req);
        var state = ParseCallbackState(req.Query["state"].ToString().NullIfEmpty());
        var userId = state?.UserId ?? GetUserId(req);
        var marketplace = state?.Marketplace ?? req.Query["marketplace"].ToString().NullIfEmpty() ?? "UK";

        try
        {
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
            });

            var httpClient = _httpClientFactory.CreateClient();
            using var tokenResp = await httpClient.PostAsync("https://api.amazon.com/auth/o2/token", body, cancellationToken);
            tokenResp.EnsureSuccessStatusCode();

            var tokenData = await JsonSerializer.DeserializeAsync<AmazonTokenDto>(
                await tokenResp.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Empty token response");

            var now = DateTime.UtcNow;
            var connection = new AudibleConnection
            {
                Id = userId,
                UserId = userId,
                AccessToken = tokenData.AccessToken,
                RefreshToken = tokenData.RefreshToken,
                TokenType = tokenData.TokenType,
                ExpiresAt = tokenData.ExpiresIn.HasValue
                    ? now.AddSeconds(tokenData.ExpiresIn.Value).ToString("O") : null,
                Marketplace = marketplace,
                CreatedAt = now.ToString("O"),
                UpdatedAt = now.ToString("O"),
            };

            var container = await _cosmos.GetAudibleConnectionsContainerAsync(cancellationToken);
            await container.UpsertItemAsync(connection, new PartitionKey(userId), cancellationToken: cancellationToken);

            var targetShelfId = state?.ShelfId ?? await _audibleService.GetLatestShelfIdForUserAsync(userId, cancellationToken);
            if (targetShelfId is null)
                return Redirect(req, new { audible = "connected", sync = "skipped", reason = "noShelf" });

            var syncResult = await _audibleService.SyncAudibleIntoShelfAsync(userId, targetShelfId, marketplace, _logger, cancellationToken);
            return Redirect(req, new
            {
                audible = "connected",
                sync = "done",
                shelfId = targetShelfId,
                booksAdded = syncResult.BooksAdded.ToString(),
                booksFound = syncResult.BooksFound.ToString(),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "audibleCallback flow failed");
            return Redirect(req, new { audible = "error", sync = "failed" });
        }
    }

    [Function("getAudibleConnectionStatus")]
    public async Task<IActionResult> GetAudibleConnectionStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "getAudibleConnectionStatus")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("getAudibleConnectionStatus triggered");
        try
        {
            var userId = GetUserId(req);
            var container = await _cosmos.GetAudibleConnectionsContainerAsync(cancellationToken);
            AudibleConnection? conn = null;
            try
            {
                var resp = await container.ReadItemAsync<AudibleConnection>(userId, new PartitionKey(userId), cancellationToken: cancellationToken);
                conn = resp.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }

            if (conn?.AccessToken is null)
                return new OkObjectResult(new { connected = false });

            var isExpired = conn.ExpiresAt is not null && DateTime.TryParse(conn.ExpiresAt, out var exp) && exp <= DateTime.UtcNow;
            return new OkObjectResult(new { connected = !isExpired, marketplace = conn.Marketplace, expiresAt = conn.ExpiresAt });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "getAudibleConnectionStatus error");
            return new ObjectResult(new { error = "Failed to retrieve Audible connection status" }) { StatusCode = 500 };
        }
    }

    [Function("disconnectAudible")]
    public async Task<IActionResult> DisconnectAudible(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "disconnectAudible")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("disconnectAudible triggered");
        try
        {
            var userId = GetUserId(req);
            var container = await _cosmos.GetAudibleConnectionsContainerAsync(cancellationToken);
            try { await container.DeleteItemAsync<AudibleConnection>(userId, new PartitionKey(userId), cancellationToken: cancellationToken); }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
            return new OkObjectResult(new { connected = false });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "disconnectAudible error");
            return new ObjectResult(new { error = "Failed to disconnect Audible" }) { StatusCode = 500 };
        }
    }

    [Function("syncAudible")]
    public async Task<IActionResult> SyncAudible(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "syncAudible")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("syncAudible triggered");
        try
        {
            var userId = GetUserId(req);
            var body = await JsonSerializer.DeserializeAsync<SyncAudibleRequest>(req.Body,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }, cancellationToken);

            if (string.IsNullOrEmpty(body?.ShelfId))
                return new BadRequestObjectResult(new { error = "shelfId is required" });

            var result = await _audibleService.SyncAudibleIntoShelfAsync(userId, body.ShelfId, body.Marketplace, _logger, cancellationToken);
            return new OkObjectResult(result);
        }
        catch (AudibleSyncException ex)
        {
            return new ObjectResult(new { error = ex.Message }) { StatusCode = ex.Status };
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing request body" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "syncAudible error");
            return new ObjectResult(new { error = "Failed to sync Audible library" }) { StatusCode = 500 };
        }
    }

    private static IActionResult Redirect(HttpRequest req, object queryParams)
    {
        var configured = Environment.GetEnvironmentVariable("AUDIBLE_POST_CONNECT_URL");
        var origin = $"{req.Scheme}://{req.Host}";
        var fallback = origin.Contains("localhost:7071") ? "https://localhost:5173/library" : $"{origin}/library";
        var dest = new UriBuilder(configured ?? fallback);

        var props = queryParams.GetType().GetProperties();
        var q = System.Web.HttpUtility.ParseQueryString(dest.Query);
        foreach (var p in props)
        {
            var val = p.GetValue(queryParams)?.ToString();
            if (val is null) continue;
            if (p.Name.Equals("shelfId", StringComparison.OrdinalIgnoreCase))
            {
                dest.Path = $"/bookshelf/{val}";
            }
            else
            {
                q[p.Name] = val;
            }
        }
        dest.Query = q.ToString();
        return new RedirectResult(dest.ToString());
    }

    private static string ResolveRedirectUri(HttpRequest req)
    {
        var env = Environment.GetEnvironmentVariable("AUDIBLE_REDIRECT_URI");
        if (!string.IsNullOrEmpty(env)) return env;
        return $"{req.Scheme}://{req.Host}/api/audibleCallback";
    }

    internal static CallbackState? ParseCallbackState(string? state)
    {
        if (state is null) return null;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(state));
            return JsonSerializer.Deserialize<CallbackState>(decoded, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        catch { return new CallbackState { UserId = state }; }
    }
}

internal sealed class CallbackState
{
    public string? UserId { get; set; }
    public string? Marketplace { get; set; }
    public string? ShelfId { get; set; }
}

file sealed class AmazonTokenDto
{
    [System.Text.Json.Serialization.JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
}

file sealed class SyncAudibleRequest
{
    public string? ShelfId { get; set; }
    public string? Marketplace { get; set; }
}

file static class StringExts
{
    public static string? NullIfEmpty(this string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
