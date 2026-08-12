using System.IO;
using System.Text;

namespace LocalChromeStore.Services;

internal static class JsonFileLimits
{
    public const long SettingsBytes = 4L * 1024 * 1024;
    public const long CatalogCacheBytes = 16L * 1024 * 1024;
    public const long UsageStatsBytes = 4L * 1024 * 1024;
}

internal static class JsonFileReader
{
    public static bool TryRead(string path, long maxBytes, out string json)
    {
        json = string.Empty;
        if (maxBytes <= 0) return false;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.SequentialScan);
            if (stream.Length > maxBytes) return false;

            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            json = reader.ReadToEnd();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
