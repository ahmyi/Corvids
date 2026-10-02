using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Corvids.Services;

/// <summary>One app as seen over the control API.</summary>
public record AppDto(string Id, string Name, string Status, bool Running, int? Pid, string Uptime,
    string Directory, string Command, string PortText, int[] Ports);

public record LogsDto(string Id, string Name, string[] Lines);

public record ActionResult(bool Ok, string Message);

public record ApiError(string Error);

/// <summary>
/// What the control server can ask the app to do. The implementation marshals to the UI thread; every call is
/// awaited so HTTP responses reflect the real result.
/// </summary>
public interface IControlHost
{
    Task<IReadOnlyList<AppDto>> ListAsync();
    Task<AppDto?> GetAsync(string id);
    Task<LogsDto?> LogsAsync(string id, int lines);
    Task<ActionResult> ActionAsync(string id, string action); // start | stop | restart | update
}

/// <summary>
/// A tiny loopback HTTP+JSON control API so AI agents and other tools can read and drive Corvids.
/// Binds to 127.0.0.1 only and requires a bearer token. Read endpoints plus start/stop/restart/update.
/// </summary>
public sealed class ControlServer : IDisposable
{
    private readonly IControlHost _host;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _token = "";

    public ControlServer(IControlHost host) => _host = host;

    public bool IsRunning => _listener?.IsListening == true;

    /// <summary>Starts the API on 127.0.0.1:<paramref name="port"/>. Returns null on success, else an error.</summary>
    public string? Start(int port, string token)
    {
        Stop();
        _token = token;
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        _listener = listener;
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(listener, _cts.Token);
        return null;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        try { _listener?.Stop(); _listener?.Close(); } catch { /* ignore */ }
        _listener = null;
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { break; } // listener stopped

            _ = HandleAsync(ctx); // one request at a time is fine, but do not block the accept loop
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            if (!Authorized(ctx.Request))
            {
                await WriteAsync(ctx, 401, new ApiError("unauthorized"), ApiJson.Default.ApiError);
                return;
            }

            var segments = ctx.Request.Url?.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                           ?? Array.Empty<string>();
            var method = ctx.Request.HttpMethod;

            // GET /ping
            if (segments is ["ping"] && method == "GET")
            {
                await WriteAsync(ctx, 200, new ActionResult(true, "corvids"), ApiJson.Default.ActionResult);
                return;
            }

            // GET /apps
            if (segments is ["apps"] && method == "GET")
            {
                var apps = await _host.ListAsync();
                await WriteAsync(ctx, 200, apps, ApiJson.Default.IReadOnlyListAppDto);
                return;
            }

            // GET /apps/{id}
            if (segments is ["apps", var id1] && method == "GET")
            {
                var app = await _host.GetAsync(id1);
                if (app is null) { await WriteAsync(ctx, 404, new ApiError("no such app"), ApiJson.Default.ApiError); return; }
                await WriteAsync(ctx, 200, app, ApiJson.Default.AppDto);
                return;
            }

            // GET /apps/{id}/logs?lines=N
            if (segments is ["apps", var id2, "logs"] && method == "GET")
            {
                var lines = int.TryParse(ctx.Request.QueryString["lines"], out var n) ? n : 200;
                var logs = await _host.LogsAsync(id2, lines);
                if (logs is null) { await WriteAsync(ctx, 404, new ApiError("no such app"), ApiJson.Default.ApiError); return; }
                await WriteAsync(ctx, 200, logs, ApiJson.Default.LogsDto);
                return;
            }

            // POST /apps/{id}/{action}
            if (segments is ["apps", var id3, var action] && method == "POST"
                && action is "start" or "stop" or "restart" or "update")
            {
                var result = await _host.ActionAsync(id3, action);
                await WriteAsync(ctx, result.Ok ? 200 : 400, result, ApiJson.Default.ActionResult);
                return;
            }

            await WriteAsync(ctx, 404, new ApiError("not found"), ApiJson.Default.ApiError);
        }
        catch (Exception ex)
        {
            try { await WriteAsync(ctx, 500, new ApiError(ex.Message), ApiJson.Default.ApiError); } catch { /* ignore */ }
        }
    }

    private bool Authorized(HttpListenerRequest request)
    {
        if (string.IsNullOrEmpty(_token)) return false;
        var header = request.Headers["Authorization"];
        if (header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && Constant(header["Bearer ".Length..].Trim(), _token))
            return true;
        return Constant(request.QueryString["token"] ?? "", _token);
    }

    // Length-aware, non-short-circuiting compare to avoid leaking the token by timing.
    private static bool Constant(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static async Task WriteAsync<T>(HttpListenerContext ctx, int status, T body,
        JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(body, typeInfo);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = json.Length;
        await ctx.Response.OutputStream.WriteAsync(json);
        ctx.Response.Close();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(IReadOnlyList<AppDto>))]
[JsonSerializable(typeof(AppDto))]
[JsonSerializable(typeof(LogsDto))]
[JsonSerializable(typeof(ActionResult))]
[JsonSerializable(typeof(ApiError))]
public partial class ApiJson : JsonSerializerContext;
