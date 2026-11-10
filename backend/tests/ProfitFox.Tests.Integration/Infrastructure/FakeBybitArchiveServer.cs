using System.Collections.Concurrent;
using System.IO.Compression;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ProfitFox.Tests.Integration.Infrastructure;

/// <summary>
///     The daily archive of trades of Bybit (<c>public.bybit.com</c>) on a local port: it serves the files a test gives it, compressed with
///     gzip, answers 404 for any other path, and counts the requests.
/// </summary>
public sealed class FakeBybitArchiveServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, byte[]> _files = new ConcurrentDictionary<string, byte[]>();

    private FakeBybitArchiveServer(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Like <c>http://127.0.0.1:5298</c>.</summary>
    public string Address { get; private set; } = "";

    /// <summary>The paths asked for, in order.</summary>
    public ConcurrentQueue<string> Requests { get; } = new ConcurrentQueue<string>();

    /// <summary>An answer every request gets instead of the files, such as 503.</summary>
    public int? Status { get; set; }

    public static async Task<FakeBybitArchiveServer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        var server = new FakeBybitArchiveServer(app);
        app.Run(server.AnswerAsync);
        await app.StartAsync();
        server.Address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        return server;
    }

    /// <summary>Serves <paramref name="content" />, compressed, at <paramref name="path" />, such as <c>/trading/SOLUSDT/SOLUSDT2026-11-09.csv.gz</c>.</summary>
    public void Serve(string path, byte[] content)
    {
        using var compressed = new MemoryStream();

        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest))
        {
            gzip.Write(content);
        }

        _files[path] = compressed.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task AnswerAsync(HttpContext context)
    {
        string path = context.Request.Path.Value ?? "";
        Requests.Enqueue(path);

        if (Status is { } status)
        {
            context.Response.StatusCode = status;

            return;
        }

        if (!_files.TryGetValue(path, out byte[]? file))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        context.Response.ContentType = "application/gzip";
        await context.Response.Body.WriteAsync(file);
    }
}