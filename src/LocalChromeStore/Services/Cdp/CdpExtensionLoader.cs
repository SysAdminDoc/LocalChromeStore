using System.IO;

namespace LocalChromeStore.Services.Cdp;

/// <summary>Outcome of a CDP load attempt — surfaced to the activity log.</summary>
public sealed record CdpLoadResult(bool Success, int Loaded, int Total, string Detail)
{
    public static CdpLoadResult Skipped(string why) => new(false, 0, 0, why);
}

/// <summary>
/// Loads unpacked extensions into branded Chrome 137+/142+ over a <c>--remote-debugging-pipe</c>
/// CDP connection — the only sanctioned dev-load path after command-line <c>--load-extension</c> was
/// removed. On Windows, Chrome reads/writes CDP on two inherited anonymous-pipe handles exposed to
/// its C runtime as fd 3 (read) and fd 4 (write); <see cref="CdpPipeProcess"/> creates those pipes
/// and launches with the MSVCRT inheritance block so the handshake actually completes (plain
/// <c>Process.Start</c> cannot map handles onto fd 3/4). We then issue
/// <c>Extensions.loadUnpacked</c> for each directory.
///
/// IMPORTANT: end-to-end loading requires branded Chrome 142+ at runtime and could not be exercised
/// in the build environment. Every failure path is non-fatal — callers fall back to the launch
/// strategy resolver's manual/policy path (see <see cref="BrowserLauncher.ResolveStrategy"/>). The
/// wire protocol and the fd-inheritance block are unit-tested in isolation (<c>CdpProtocolTests</c>,
/// <c>CdpPipeProcessTests</c>). Two things still need a live Chrome 142+ host to confirm: whether the
/// browser keeps running after the controlling pipe closes, and the actual load result frames.
/// </summary>
public sealed class CdpExtensionLoader
{
    private readonly TimeSpan _responseTimeout;

    public CdpExtensionLoader(TimeSpan? responseTimeout = null)
        => _responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(10);

    /// <summary>
    /// Launches <paramref name="browserExePath"/> with the CDP pipe flags and loads each extension
    /// directory. Returns a non-fatal result; on any error the caller should fall back.
    /// </summary>
    public async Task<CdpLoadResult> LaunchAndLoadAsync(
        string browserExePath,
        IReadOnlyList<string> extensionPaths,
        IReadOnlyList<string> extraArgs,
        CancellationToken ct = default)
    {
        if (extensionPaths.Count == 0) return CdpLoadResult.Skipped("no extensions to load");
        if (!OperatingSystem.IsWindows()) return CdpLoadResult.Skipped("CDP pipe loader is Windows-only");

        CdpPipeProcess? session = null;
        try
        {
            var args = new List<string>(CdpProtocol.RequiredLaunchFlags);
            args.AddRange(extraArgs);
            session = CdpPipeProcess.Launch(browserExePath, args);

            var loaded = 0;
            var id = 0;
            foreach (var path in extensionPaths)
            {
                ct.ThrowIfCancellationRequested();
                if (await SendLoadUnpackedAsync(session.Writer, session.Reader, ++id, path, ct))
                    loaded++;
            }

            return loaded == extensionPaths.Count
                ? new CdpLoadResult(true, loaded, extensionPaths.Count, "all extensions loaded via CDP")
                : new CdpLoadResult(loaded > 0, loaded, extensionPaths.Count,
                    $"loaded {loaded}/{extensionPaths.Count} extensions via CDP");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return CdpLoadResult.Skipped($"CDP load failed ({ex.Message}); fall back to manual load");
        }
        finally
        {
            session?.Dispose();
        }
    }

    private async Task<bool> SendLoadUnpackedAsync(Stream writer, Stream reader, int id, string path, CancellationToken ct)
    {
        var frame = CdpProtocol.EncodeFrame(CdpProtocol.BuildLoadUnpacked(id, path));
        await writer.WriteAsync(frame, ct);
        await writer.FlushAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_responseTimeout);
        try
        {
            var buf = new byte[8192];
            var pending = Array.Empty<byte>();
            while (true)
            {
                var n = await reader.ReadAsync(buf, timeoutCts.Token);
                if (n == 0) return false; // pipe closed
                var combined = Concat(pending, buf.AsSpan(0, n));
                var (messages, remainder) = CdpProtocol.DecodeFrames(combined);
                pending = remainder;
                foreach (var msg in messages)
                {
                    var resp = CdpProtocol.ParseResponse(msg);
                    if (resp.Id == id) return !resp.IsError;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false; // response timeout — non-fatal
        }
    }

    private static byte[] Concat(byte[] head, ReadOnlySpan<byte> tail)
    {
        if (head.Length == 0) return tail.ToArray();
        var result = new byte[head.Length + tail.Length];
        Array.Copy(head, result, head.Length);
        tail.CopyTo(result.AsSpan(head.Length));
        return result;
    }
}
