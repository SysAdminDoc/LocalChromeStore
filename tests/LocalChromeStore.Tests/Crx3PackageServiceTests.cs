using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using LocalChromeStore.Services.Crx;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class Crx3PackageServiceTests
{
    [Fact]
    public void DeriveExtensionId_UsesChromeAlphabet()
    {
        var id = Crx3PackageService.DeriveExtensionId("abc"u8.ToArray());

        Assert.Equal("lkhibglpipabmpokebebeanofnkocccd", id);
        Assert.True(Crx3PackageService.IsValidExtensionId(id));
    }

    [Fact]
    public void PackDirectory_CreatesVerifiableCrx3()
    {
        var root = CreateExtensionFixture();
        var keyPath = Path.Combine(Path.GetTempPath(), "lcs-crx-key-" + Guid.NewGuid().ToString("N") + ".pem");
        var crxPath = Path.Combine(Path.GetTempPath(), "lcs-crx-" + Guid.NewGuid().ToString("N") + ".crx");
        try
        {
            using var rsa = RSA.Create(2048);
            File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());

            var result = Crx3PackageService.PackDirectory(root, keyPath, crxPath);
            var crxBytes = File.ReadAllBytes(crxPath);
            var verification = Crx3PackageService.VerifyPackage(crxBytes);

            Assert.True(File.Exists(crxPath));
            Assert.Equal(result.ExtensionId, verification.ExtensionId);
            Assert.Equal(result.PublicKeySha256, verification.PublicKeySha256);
            Assert.True(verification.SignatureValid);
            Assert.True(verification.ExtensionIdMatchesPublicKey);
            Assert.Equal("Cr24", System.Text.Encoding.ASCII.GetString(crxBytes, 0, 4));

            using var zipStream = new MemoryStream(crxBytes, verification.ZipPayloadOffset, verification.ZipPayloadLength);
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
            Assert.NotNull(zip.GetEntry("manifest.json"));
            Assert.NotNull(zip.GetEntry("service-worker.js"));
        }
        finally
        {
            TryDelete(root);
            TryDelete(keyPath);
            TryDelete(crxPath);
        }
    }

    [Fact]
    public void PackDirectory_RejectsMismatchedPreviousSigningKey()
    {
        var root = CreateExtensionFixture();
        var keyPath = Path.Combine(Path.GetTempPath(), "lcs-crx-key-" + Guid.NewGuid().ToString("N") + ".pem");
        var crxPath = Path.Combine(Path.GetTempPath(), "lcs-crx-" + Guid.NewGuid().ToString("N") + ".crx");
        try
        {
            using var rsa = RSA.Create(2048);
            File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Crx3PackageService.PackDirectory(root, keyPath, crxPath, new string('0', 64)));
            Assert.Contains("signing key does not match", ex.Message);
        }
        finally
        {
            TryDelete(root);
            TryDelete(keyPath);
            TryDelete(crxPath);
        }
    }

    [Fact]
    public void UpdateXmlService_CreatesChromeUpdateManifest()
    {
        var xml = UpdateXmlService.Create(
            "abcdefghijklmnopabcdefghijklmnop",
            new Uri("https://example.test/extensions/sample.crx"),
            "1.2.3");

        var doc = XDocument.Parse(xml);
        var ns = XNamespace.Get("http://www.google.com/update2/response");
        var update = doc.Root?.Element(ns + "app")?.Element(ns + "updatecheck");

        Assert.Equal("gupdate", doc.Root?.Name.LocalName);
        Assert.Equal("2.0", doc.Root?.Attribute("protocol")?.Value);
        Assert.Equal("abcdefghijklmnopabcdefghijklmnop", doc.Root?.Element(ns + "app")?.Attribute("appid")?.Value);
        Assert.Equal("https://example.test/extensions/sample.crx", update?.Attribute("codebase")?.Value);
        Assert.Equal("1.2.3", update?.Attribute("version")?.Value);
    }

    private static string CreateExtensionFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "lcs-extension-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "manifest_version": 3,
              "name": "CRX fixture",
              "version": "1.0.0",
              "background": { "service_worker": "service-worker.js" }
            }
            """);
        File.WriteAllText(Path.Combine(root, "service-worker.js"), "chrome.runtime.onInstalled.addListener(() => {});");
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Test cleanup best effort only.
        }
    }
}
