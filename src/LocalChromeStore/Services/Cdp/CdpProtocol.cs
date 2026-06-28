using System.Text;
using System.Text.Json;

namespace LocalChromeStore.Services.Cdp;

/// <summary>
/// Wire framing and command construction for the Chrome DevTools Protocol over a
/// <c>--remote-debugging-pipe</c> connection. Messages on the pipe are UTF-8 JSON objects, each
/// terminated by a single NUL (<c>\0</c>) byte. This is the only sanctioned programmatic way to load
/// an unpacked extension into branded Chrome 137+/142+ (which removed command-line
/// <c>--load-extension</c>), via the <c>Extensions.loadUnpacked</c> command behind
/// <c>--enable-unsafe-extension-debugging</c>.
///
/// Dependency note: this is hand-rolled rather than taking a PuppeteerSharp dependency, to keep
/// Octokit the only third-party runtime dependency (minimal-dependency project philosophy). Only the
/// tiny slice of CDP we need (browser-target loadUnpacked) is implemented.
/// </summary>
public static class CdpProtocol
{
    public const byte MessageTerminator = 0x00;

    public const string RemoteDebuggingPipeFlag = "--remote-debugging-pipe";
    public const string EnableUnsafeExtensionDebuggingFlag = "--enable-unsafe-extension-debugging";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The browser flags required for a CDP pipe session that can load unpacked extensions.</summary>
    public static IReadOnlyList<string> RequiredLaunchFlags => new[]
    {
        RemoteDebuggingPipeFlag,
        EnableUnsafeExtensionDebuggingFlag
    };

    /// <summary>Builds the <c>Extensions.loadUnpacked</c> command object for one extension directory.</summary>
    public static CdpCommand BuildLoadUnpacked(int id, string extensionPath) => new()
    {
        Id = id,
        Method = "Extensions.loadUnpacked",
        Params = new Dictionary<string, object> { ["path"] = extensionPath }
    };

    /// <summary>Serializes a command to a NUL-terminated UTF-8 frame ready to write to the pipe.</summary>
    public static byte[] EncodeFrame(CdpCommand command)
    {
        var json = JsonSerializer.Serialize(command, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        var frame = new byte[bytes.Length + 1];
        Array.Copy(bytes, frame, bytes.Length);
        frame[^1] = MessageTerminator;
        return frame;
    }

    /// <summary>
    /// Splits a (possibly partial) byte buffer into complete NUL-delimited JSON messages, returning
    /// the decoded strings and the leftover bytes after the last terminator (a partial next message).
    /// </summary>
    public static (List<string> messages, byte[] remainder) DecodeFrames(ReadOnlySpan<byte> buffer)
    {
        var messages = new List<string>();
        var start = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != MessageTerminator) continue;
            var slice = buffer.Slice(start, i - start);
            if (slice.Length > 0) messages.Add(Encoding.UTF8.GetString(slice));
            start = i + 1;
        }
        var remainder = buffer.Slice(start).ToArray();
        return (messages, remainder);
    }

    /// <summary>
    /// Parses a CDP response frame. <c>Extensions.loadUnpacked</c> returns the loaded extension ID
    /// as <c>result.id</c>; errors carry the exact browser message under <c>error.message</c>.
    /// </summary>
    public static CdpResponse ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int? id = root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var v) ? v : null;
            string? extensionId = null;
            if (root.TryGetProperty("result", out var resultEl) &&
                resultEl.ValueKind == JsonValueKind.Object &&
                resultEl.TryGetProperty("id", out var extensionIdEl))
            {
                extensionId = extensionIdEl.GetString();
            }
            string? error = null;
            if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.Object)
                error = errEl.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "unknown CDP error";
            return new CdpResponse(id, extensionId, error, IsEvent: id is null);
        }
        catch (JsonException)
        {
            return new CdpResponse(null, null, $"unparseable CDP frame: {json}", IsEvent: false);
        }
    }
}

public sealed class CdpCommand
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public int Id { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("method")]
    public string Method { get; init; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("params")]
    public Dictionary<string, object>? Params { get; init; }
}

public sealed record CdpResponse(int? Id, string? ExtensionId, string? Error, bool IsEvent)
{
    public bool IsError => Error is not null;
}
