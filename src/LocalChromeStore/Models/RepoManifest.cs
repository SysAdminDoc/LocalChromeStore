namespace LocalChromeStore.Models;

/// <summary>
/// Optional <c>localchromestore.json</c> placed in a repo root to provide
/// catalog-level metadata.  All fields are optional; omitted fields leave the
/// corresponding ExtensionInfo values untouched.
/// </summary>
public sealed class RepoManifest
{
    public string? DisplayName    { get; set; }
    public string? Description    { get; set; }
    public string? HomepageUrl    { get; set; }
    public string? Category       { get; set; }
    public string? IconUrl        { get; set; }
    public string? Keywords       { get; set; }
    public bool?   HideFromCatalog { get; set; }

    private static readonly HashSet<string> KnownCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "productivity", "developer-tools", "entertainment", "social",
        "accessibility", "privacy", "security", "utilities", "education",
        "news", "shopping", "sports", "travel"
    };

    /// <summary>
    /// F005: validate the deserialized manifest.  Returns a list of
    /// human-readable warning strings (empty = no issues).
    /// </summary>
    public static List<string> Validate(RepoManifest m)
    {
        var errors = new List<string>();

        if (m.DisplayName is { Length: > 64 })
            errors.Add("localchromestore.json: 'displayName' exceeds 64 characters.");

        if (m.Description is { Length: > 280 })
            errors.Add("localchromestore.json: 'description' exceeds 280 characters.");

        if (m.Category is not null && !KnownCategories.Contains(m.Category))
            errors.Add($"localchromestore.json: unknown category '{m.Category}'. " +
                       $"Known values: {string.Join(", ", KnownCategories.Order())}.");

        if (m.HomepageUrl is not null && !Uri.TryCreate(m.HomepageUrl, UriKind.Absolute, out _))
            errors.Add("localchromestore.json: 'homepageUrl' is not a valid absolute URL.");

        if (m.IconUrl is not null && !Uri.TryCreate(m.IconUrl, UriKind.Absolute, out _))
            errors.Add("localchromestore.json: 'iconUrl' is not a valid absolute URL.");

        return errors;
    }
}
