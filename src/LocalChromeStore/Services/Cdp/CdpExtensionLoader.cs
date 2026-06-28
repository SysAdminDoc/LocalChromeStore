using System.IO;

namespace LocalChromeStore.Services.Cdp;

/// <summary>One unpacked-extension load attempt over CDP.</summary>
public sealed record CdpLoadAttempt(string ExtensionPath, bool Success, string? ExtensionId, string Detail);

/// <summary>Outcome of a CDP load attempt batch, surfaced to the activity log.</summary>
public sealed record CdpLoadResult(bool Success, int Loaded, int Total, string Detail, IReadOnlyList<CdpLoadAttempt> Attempts)
{
    public static CdpLoadResult Skipped(string why) => new(false, 0, 0, why, []);
}

public interface ICdpExtensionLoader
{
    Task<CdpLoadResult> LaunchAndLoadAsync(
        string browserExePath,
        IReadOnlyList<string> extensionPaths,
        IReadOnlyList<string> extraArgs,
        CancellationToken ct = default);
}

/// <summary>
/// Loads unpacked extensions into branded Chrome 137+/142+ over a remote-debugging pipe CDP
/// connection. On Windows, Chrome reads/writes CDP on inherited anonymous-pipe handles exposed to
/// its C runtime as fd 3 and fd 4; <see cref="CdpPipeProcess"/> creates those pipes and launches
/// with the MSVCRT inheritance block that <see cref="System.Diagnostics.ProcessStartInfo"/> cannot
/// express.
/// </summary>
public sealed class CdpExtensionLoader : ICdpExtensionLoader
{
    private readonly TimeSpan _responseTimeout;

    public CdpExtensionLoader(TimeSpan? responseTimeout = null)
        => _responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(10);

    /// <summary>
    /// Launches <paramref name="browserExePath"/> with the CDP pipe flags and loads each extension
    /// directory. Returns a non-fatal result so callers can fall back to manual/policy guidance.
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

            var attempts = new List<CdpLoadAttempt>();
            var id = 0;
            foreach (var path in extensionPaths)
            {
                ct.ThrowIfCancellationRequested();
                attempts.Add(await SendLoadUnpackedAsync(session.Writer, session.Reader, ++id, path, ct));
            }

            var loaded = attempts.Count(a => a.Success);
            return loaded == extensionPaths.Count
                ? new CdpLoadResult(true, loaded, extensionPaths.Count, "all extensions loaded via CDP", attempts)
                : new CdpLoadResult(loaded > 0, loaded, extensionPaths.Count,
                    $"loaded {loaded}/{extensionPaths.Count} extensions via CDP", attempts);
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

    private async Task<CdpLoadAttempt> SendLoadUnpackedAsync(Stream writer, Stream reader, int id, string path, CancellationToken ct)
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
                if (n == 0) return new CdpLoadAttempt(path, false, null, "CDP pipe closed before a response arrived");

                var combined = Concat(pending, buf.AsSpan(0, n));
                var (messages, remainder) = CdpProtocol.DecodeFrames(combined);
                pending = remainder;
                foreach (var msg in messages)
                {
                    var resp = CdpProtocol.ParseResponse(msg);
                    if (resp.Id != id) continue;
                    if (resp.IsError)
                        return new CdpLoadAttempt(path, false, null, resp.Error ?? "unknown CDP error");

                    var detail = string.IsNullOrWhiteSpace(resp.ExtensionId)
                        ? "CDP loadUnpacked returned success without an extension ID"
                        : $"CDP loadUnpacked returned extension ID {resp.ExtensionId}";
                    return new CdpLoadAttempt(path, true, resp.ExtensionId, detail);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CdpLoadAttempt(path, false, null, "CDP response timed out");
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
