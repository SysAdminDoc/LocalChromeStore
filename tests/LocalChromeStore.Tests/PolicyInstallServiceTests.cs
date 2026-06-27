using System.Net;
using LocalChromeStore.Models;
using LocalChromeStore.Services;
using LocalChromeStore.Services.Crx;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class PolicyInstallServiceTests
{
    private const string ExtensionId = "abcdefghijklmnopabcdefghijklmnop";
    private static readonly Uri UpdateUrl = new("https://updates.example.test/update.xml");
    private static readonly Uri CrxUrl = new("https://updates.example.test/sample.crx");

    [Fact]
    public void Install_RequiresExplicitConsent()
    {
        var registry = new MemoryPolicyRegistry();
        var service = new PolicyInstallService(registry, new HttpClient(new StaticHttpHandler()));
        var request = new PolicyInstallRequest(BrowserKind.Chrome, ExtensionId, UpdateUrl, "Sample");

        var ex = Assert.Throws<InvalidOperationException>(() => service.Install(request, consentConfirmed: false));

        Assert.Contains("explicit user consent", ex.Message);
        Assert.Empty(registry.AllValues);
    }

    [Fact]
    public void Install_WritesChromePolicyEntryAndUpdatesExistingExtensionId()
    {
        var registry = new MemoryPolicyRegistry();
        var service = new PolicyInstallService(registry, new HttpClient(new StaticHttpHandler()));
        var request = new PolicyInstallRequest(BrowserKind.Chrome, ExtensionId, UpdateUrl, "Sample");
        var replacement = request with { UpdateXmlUrl = new Uri("https://updates.example.test/replacement.xml") };

        var first = service.Install(request, consentConfirmed: true);
        var second = service.Install(replacement, consentConfirmed: true);

        Assert.Equal(@"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist", first.Target.RegistrySubKey);
        Assert.Equal("1", first.ValueName);
        Assert.Equal("1", second.ValueName);
        Assert.Single(registry.ReadStringValues(first.Target.RegistrySubKey));
        Assert.Equal(
            $"{ExtensionId};https://updates.example.test/replacement.xml",
            registry.ReadStringValues(first.Target.RegistrySubKey)["1"]);
    }

    [Fact]
    public void Rollback_RemovesMatchingPolicyEntriesOnly()
    {
        var registry = new MemoryPolicyRegistry();
        var service = new PolicyInstallService(registry, new HttpClient(new StaticHttpHandler()));
        var keepId = "bcdefghijklmnopabcdefghijklmnopa";
        var target = PolicyInstallService.SupportedTargets.Single(t => t.BrowserKind == BrowserKind.Brave);
        registry.SetStringValue(target.RegistrySubKey, "1", $"{ExtensionId};https://updates.example.test/update.xml");
        registry.SetStringValue(target.RegistrySubKey, "2", $"{keepId};https://updates.example.test/keep.xml");

        var result = service.Rollback(BrowserKind.Brave, new[] { ExtensionId });

        Assert.Equal(new[] { "1" }, result.RemovedValueNames);
        var values = registry.ReadStringValues(target.RegistrySubKey);
        Assert.Single(values);
        Assert.Equal($"{keepId};https://updates.example.test/keep.xml", values["2"]);
    }

    [Fact]
    public void BrowserTargets_MapKnownEnterprisePolicyKeys()
    {
        Assert.True(PolicyInstallService.TryGetTarget(BrowserKind.Chrome, out var chrome));
        Assert.Equal(@"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist", chrome.RegistrySubKey);

        Assert.True(PolicyInstallService.TryGetTarget(BrowserKind.Edge, out var edge));
        Assert.Equal(@"SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist", edge.RegistrySubKey);

        Assert.True(PolicyInstallService.TryGetTarget(BrowserKind.Brave, out var brave));
        Assert.Equal(@"SOFTWARE\Policies\BraveSoftware\Brave\ExtensionInstallForcelist", brave.RegistrySubKey);

        Assert.False(PolicyInstallService.TryGetTarget(BrowserKind.Vivaldi, out _));
        Assert.False(PolicyInstallService.TryGetTarget(BrowserKind.Opera, out _));
    }

    [Fact]
    public void ConsentPrompt_ExplainsHklmImpactAndRollback()
    {
        var prompt = PolicyInstallService.BuildConsentPrompt(
            new[] { new PolicyInstallRequest(BrowserKind.Chrome, ExtensionId, UpdateUrl, "Sample") },
            new EnrollmentState(DomainJoined: false, EntraJoined: false, CbcmEnrolled: false));

        Assert.Contains("HKLM", prompt);
        Assert.Contains("ExtensionInstallForcelist", prompt);
        Assert.Contains("not enrolled", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rollback removes only the browser policy registry entries", prompt);
    }

    [Fact]
    public async Task CheckHealthAsync_PassesForInstalledPolicyAndReachableUpdateFeed()
    {
        var registry = new MemoryPolicyRegistry();
        var handler = new StaticHttpHandler();
        handler.Set(HttpMethod.Get, UpdateUrl, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(UpdateXmlService.Create(ExtensionId, CrxUrl, "1.2.3"))
        });
        handler.Set(HttpMethod.Head, CrxUrl, new HttpResponseMessage(HttpStatusCode.OK));
        var service = new PolicyInstallService(registry, new HttpClient(handler));
        var request = new PolicyInstallRequest(BrowserKind.Chrome, ExtensionId, UpdateUrl, "Sample");
        service.Install(request, consentConfirmed: true);

        var report = await service.CheckHealthAsync(request);

        Assert.True(report.Healthy);
        Assert.Equal(CrxUrl, report.CrxUrl);
        Assert.All(report.Checks, check => Assert.NotEqual(PolicyHealthStatus.Fail, check.Status));
        Assert.Contains(report.Checks, c => c.Name == "Registry state" && c.Status == PolicyHealthStatus.Pass);
        Assert.Contains(report.Checks, c => c.Name == "Update XML" && c.Status == PolicyHealthStatus.Pass);
        Assert.Contains(report.Checks, c => c.Name == "CRX reachability" && c.Status == PolicyHealthStatus.Pass);
    }

    [Fact]
    public async Task CheckHealthAsync_FlagsMismatchedRegistryAndBadUpdateXml()
    {
        var registry = new MemoryPolicyRegistry();
        var handler = new StaticHttpHandler();
        handler.Set(HttpMethod.Get, UpdateUrl, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<not-gupdate />")
        });
        var target = PolicyInstallService.SupportedTargets.Single(t => t.BrowserKind == BrowserKind.Edge);
        registry.SetStringValue(target.RegistrySubKey, "7", $"{ExtensionId};https://updates.example.test/other.xml");
        var service = new PolicyInstallService(registry, new HttpClient(handler));
        var request = new PolicyInstallRequest(BrowserKind.Edge, ExtensionId, UpdateUrl, "Sample");

        var report = await service.CheckHealthAsync(request);

        Assert.False(report.Healthy);
        Assert.Contains(report.Checks, c => c.Name == "Registry state" && c.Status == PolicyHealthStatus.Fail);
        Assert.Contains(report.Checks, c => c.Name == "Update XML" && c.Status == PolicyHealthStatus.Fail);
        Assert.Contains(report.Checks, c => c.Name == "CRX reachability" && c.Status == PolicyHealthStatus.Fail);
    }

    private sealed class MemoryPolicyRegistry : IPolicyRegistry
    {
        private readonly Dictionary<string, Dictionary<string, string>> _keys = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> AllValues =>
            _keys.SelectMany(k => k.Value.Select(v => new KeyValuePair<string, string>($"{k.Key}\\{v.Key}", v.Value)))
                .ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> ReadStringValues(string subKey) =>
            _keys.TryGetValue(subKey, out var values)
                ? new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void SetStringValue(string subKey, string valueName, string value)
        {
            if (!_keys.TryGetValue(subKey, out var values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _keys[subKey] = values;
            }
            values[valueName] = value;
        }

        public void DeleteValue(string subKey, string valueName)
        {
            if (_keys.TryGetValue(subKey, out var values))
                values.Remove(valueName);
        }
    }

    private sealed class StaticHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<(HttpMethod Method, Uri Uri), HttpResponseMessage> _responses = new();

        public void Set(HttpMethod method, Uri uri, HttpResponseMessage response) =>
            _responses[(method, uri)] = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null &&
                _responses.TryGetValue((request.Method, request.RequestUri), out var response))
            {
                return Task.FromResult(Clone(response));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Clone(HttpResponseMessage source)
        {
            var clone = new HttpResponseMessage(source.StatusCode);
            if (source.Content is not null)
                clone.Content = new StringContent(source.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return clone;
        }
    }
}
