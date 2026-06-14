using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace LocalChromeStore.Services.Cdp;

/// <summary>Outcome of a CDP load attempt — surfaced to the activity log.</summary>
public sealed record CdpLoadResult(bool Success, int Loaded, int Total, string Detail)
{
    public static CdpLoadResult Skipped(string why) => new(false, 0, 0, why);
}

/// <summary>
/// Loads unpacked extensions into branded Chrome 137+/142+ over a <c>--remote-debugging-pipe</c>
/// CDP connection — the only sanctioned dev-load path after command-line <c>--load-extension</c> was
/// removed. On Windows, Chrome reads/writes CDP on two inherited anonymous-pipe handles passed as
/// fds 3 (read) and 4 (write); we create those pipes, launch with handle inheritance, and issue
/// <c>Extensions.loadUnpacked</c> for each directory.
///
/// IMPORTANT: end-to-end loading requires branded Chrome 142+ at runtime and could not be exercised
/// in the build environment. Every failure path is non-fatal — callers fall back to the launch
/// strategy resolver's manual/policy path (see <see cref="BrowserLauncher.ResolveStrategy"/>). The
/// wire protocol and command construction are unit-tested in isolation (<c>CdpProtocolTests</c>).
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

        // Chrome ↔ us pipes. Chrome reads from the "in" pipe (its fd 3) and writes to the "out"
        // pipe (its fd 4); we hold the opposite ends.
        AnonymousPipeServerStream? toBrowser = null;
        AnonymousPipeServerStream? fromBrowser = null;
        Process? process = null;
        try
        {
            toBrowser = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            fromBrowser = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

            var psi = new ProcessStartInfo
            {
                FileName = browserExePath,
                UseShellExecute = false
            };
            foreach (var f in CdpProtocol.RequiredLaunchFlags) psi.ArgumentList.Add(f);
            foreach (var a in extraArgs) psi.ArgumentList.Add(a);

            process = Process.Start(psi);
            if (process is null) return CdpLoadResult.Skipped("browser process failed to start");

            // Dispose the inheritable client handles on our side once the child owns them.
            var loaded = 0;
            await using (var writer = toBrowser)
            await using (var reader = fromBrowser)
            {
                var id = 0;
                foreach (var path in extensionPaths)
                {
                    ct.ThrowIfCancellationRequested();
                    var ok = await SendLoadUnpackedAsync(writer, reader, ++id, path, ct);
                    if (ok) loaded++;
                }
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
            // Streams disposed above on the happy path; guard the early-return paths.
            toBrowser?.Dispose();
            fromBrowser?.Dispose();
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
