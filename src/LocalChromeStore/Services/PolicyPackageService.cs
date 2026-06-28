using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalChromeStore.Models;
using LocalChromeStore.Services.Crx;

namespace LocalChromeStore.Services;

public sealed record PolicyPackageRequest(
    InstalledExtension Installed,
    Uri CrxUrl,
    Uri UpdateXmlUrl,
    string? ExistingUpdateXmlPath = null);

public sealed record PolicyPackageResult(
    InstalledExtension Installed,
    string PackageDirectory,
    string PrivateKeyPath,
    string CrxPath,
    string UpdateXmlPath,
    string ManifestVersion,
    Crx3PackageResult Package,
    Uri CrxUrl,
    Uri UpdateXmlUrl)
{
    public PolicyInstallRequest ToInstallRequest(BrowserKind browserKind) =>
        new(browserKind, Package.ExtensionId, UpdateXmlUrl, Installed.DisplayName ?? Installed.Key);
}

public sealed class PolicyPackageService
{
    private readonly SettingsService _settings;

    public PolicyPackageService(SettingsService settings)
    {
        _settings = settings;
    }

    public PolicyPackageResult Prepare(PolicyPackageRequest request, IProgress<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Installed);
        ValidateHttpUrl(request.CrxUrl, nameof(request.CrxUrl));
        ValidateHttpUrl(request.UpdateXmlUrl, nameof(request.UpdateXmlUrl));

        var installed = request.Installed;
        var installRoot = Path.GetFullPath(installed.InstallPath);
        if (!Directory.Exists(installRoot))
            throw new DirectoryNotFoundException($"Installed extension directory was not found: {installRoot}");

        var manifestPath = ResolveManifestPath(installed);
        var manifestVersion = ReadManifestVersion(manifestPath);
        var packageDirectory = PackageDirectoryFor(installed);
        Directory.CreateDirectory(packageDirectory);

        var keyPath = KeyPathFor(installed);
        EnsureSigningKey(keyPath, log);

        var crxPath = Path.Combine(packageDirectory, DefaultCrxFileName(installed));
        var crx = Crx3PackageService.PackDirectory(installRoot, keyPath, crxPath);
        log?.Report($"Policy CRX3 package: {crx.CrxPath}");
        log?.Report($"Policy extension ID: {crx.ExtensionId}");
        log?.Report($"Policy package SHA-256: {crx.PackageSha256}");

        var updateXmlPath = Path.Combine(packageDirectory, "update.xml");
        if (string.IsNullOrWhiteSpace(request.ExistingUpdateXmlPath))
        {
            var xml = UpdateXmlService.Create(crx.ExtensionId, request.CrxUrl, manifestVersion);
            File.WriteAllText(updateXmlPath, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            log?.Report($"Generated update.xml: {updateXmlPath}");
        }
        else
        {
            var source = Path.GetFullPath(request.ExistingUpdateXmlPath);
            if (!File.Exists(source))
                throw new FileNotFoundException("Selected update.xml was not found.", source);
            if (!string.Equals(source, Path.GetFullPath(updateXmlPath), StringComparison.OrdinalIgnoreCase))
                File.Copy(source, updateXmlPath, overwrite: true);
            log?.Report($"Copied selected update.xml: {source}");
        }

        return new PolicyPackageResult(
            installed,
            packageDirectory,
            keyPath,
            crx.CrxPath,
            updateXmlPath,
            manifestVersion,
            crx,
            request.CrxUrl,
            request.UpdateXmlUrl);
    }

    public bool TryDeriveExtensionId(InstalledExtension installed, out string extensionId, out string keyPath)
    {
        ArgumentNullException.ThrowIfNull(installed);
        keyPath = KeyPathFor(installed);
        extensionId = string.Empty;
        if (!File.Exists(keyPath)) return false;

        try
        {
            using var rsa = LoadPrivateKey(keyPath);
            extensionId = Crx3PackageService.DeriveExtensionId(rsa.ExportSubjectPublicKeyInfo());
            return true;
        }
        catch
        {
            extensionId = string.Empty;
            return false;
        }
    }

    public string KeyPathFor(InstalledExtension installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        Directory.CreateDirectory(_settings.PolicyKeysDir);
        return Path.Combine(
            _settings.PolicyKeysDir,
            $"{SanitizePathSegment(installed.RepoOwner)}__{SanitizePathSegment(installed.RepoName)}.pem");
    }

    public string PackageDirectoryFor(InstalledExtension installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        return Path.Combine(
            _settings.PolicyPackagesRoot,
            SanitizePathSegment(installed.RepoOwner),
            SanitizePathSegment(installed.RepoName),
            SanitizePathSegment(installed.Version));
    }

    public static string DefaultCrxFileName(InstalledExtension installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        return SanitizePathSegment($"{installed.RepoName}-{installed.Version}.crx");
    }

    public static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "_";
        var invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            buffer[i] = invalid.Contains(c) || c is '/' or '\\' or ':' ? '_' : c;
        }
        return new string(buffer).Trim();
    }

    private static void EnsureSigningKey(string keyPath, IProgress<string>? log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyPath))!);
        if (File.Exists(keyPath))
        {
            using var existing = LoadPrivateKey(keyPath);
            log?.Report($"Reusing CRX signing key: {keyPath}");
            return;
        }

        using var rsa = RSA.Create(2048);
        File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem(), Encoding.ASCII);
        log?.Report($"Created CRX signing key: {keyPath}");
    }

    private static RSA LoadPrivateKey(string keyPath)
    {
        var pem = File.ReadAllText(keyPath);
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            rsa.Dispose();
            throw new InvalidOperationException("CRX signing key must be an RSA private key PEM.", ex);
        }

        if (rsa.KeySize < 2048)
        {
            rsa.Dispose();
            throw new InvalidOperationException("CRX signing requires an RSA key of at least 2048 bits.");
        }
        return rsa;
    }

    private static string ResolveManifestPath(InstalledExtension installed)
    {
        var manifestPath = Path.GetFullPath(installed.ManifestPath);
        if (!File.Exists(manifestPath))
            manifestPath = Path.Combine(Path.GetFullPath(installed.InstallPath), "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Installed extension manifest.json was not found.", manifestPath);
        return manifestPath;
    }

    private static string ReadManifestVersion(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!doc.RootElement.TryGetProperty("version", out var versionElement))
            throw new InvalidOperationException("Installed extension manifest.json does not contain a version.");
        var version = versionElement.GetString();
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("Installed extension manifest.json has a blank version.");
        return version.Trim();
    }

    private static void ValidateHttpUrl(Uri uri, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(uri, parameterName);
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("Policy package URLs must be absolute http or https URLs.", parameterName);
    }
}
