using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

public interface IExtensionSource
{
    string SourceName { get; }
    Task<IReadOnlyList<ExtensionInfo>> DiscoverAsync(AppSettings settings, IProgress<string>? log = null, CancellationToken ct = default);
}
