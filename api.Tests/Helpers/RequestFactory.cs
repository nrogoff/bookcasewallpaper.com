using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Text;
using System.Text.Json;

namespace BookshelfWallpaper.Api.Tests.Helpers;

/// <summary>Builds <see cref="HttpRequest"/> objects for unit tests.</summary>
internal static class RequestFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Creates a plain JSON request with optional headers, body and query string.</summary>
    internal static HttpRequest Create(
        string? userId = null,
        object? body = null,
        Dictionary<string, string>? query = null,
        string method = "GET")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("localhost");

        if (userId is not null)
            ctx.Request.Headers["x-ms-client-principal-id"] = userId;

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Request.Body = new MemoryStream(bytes);
            ctx.Request.ContentLength = bytes.Length;
            ctx.Request.ContentType = "application/json";
        }

        if (query is not null)
        {
            ctx.Request.QueryString = new QueryString(
                "?" + string.Join("&",
                    query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}")));
        }

        return ctx.Request;
    }

    /// <summary>Creates a multipart form request with a file attachment.</summary>
    internal static HttpRequest WithFormFile(
        string shelfId = "shelf-1",
        string fileContent = "",
        string fileName = "books.csv",
        string? userId = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("localhost");

        if (userId is not null)
            ctx.Request.Headers["x-ms-client-principal-id"] = userId;

        var fileBytes = Encoding.UTF8.GetBytes(fileContent);
        var file = new FormFile(
            new MemoryStream(fileBytes), 0, fileBytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary { ["Content-Type"] = "text/csv" },
        };

        ctx.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["shelfId"] = shelfId,
            },
            new FormFileCollection { file });

        return ctx.Request;
    }

    /// <summary>Creates a form request with no file (shelfId only).</summary>
    internal static HttpRequest WithFormNoFile(string shelfId = "shelf-1", string? userId = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        if (userId is not null)
            ctx.Request.Headers["x-ms-client-principal-id"] = userId;
        ctx.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["shelfId"] = shelfId,
            });
        return ctx.Request;
    }

    /// <summary>Creates a form request with a file but no shelfId.</summary>
    internal static HttpRequest WithFormFileNoShelf(string fileContent = "Title,Author", string? userId = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        if (userId is not null)
            ctx.Request.Headers["x-ms-client-principal-id"] = userId;
        var fileBytes = Encoding.UTF8.GetBytes(fileContent);
        var file = new FormFile(new MemoryStream(fileBytes), 0, fileBytes.Length, "file", "books.csv");
        ctx.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(),
            new FormFileCollection { file });
        return ctx.Request;
    }
}
