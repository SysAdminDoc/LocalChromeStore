using System;
using System.Collections.Generic;

namespace LocalChromeStore.Models;

/// <summary>
/// A named subset of installed extensions used to define a browser launch profile.
/// When <see cref="ExtensionKeys"/> is <see langword="null"/> the set targets all installed extensions.
/// </summary>
public sealed class LoadSet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string Name { get; set; }

    /// <summary>
    /// Ordered list of "owner/repo" keys included in this set.
    /// <see langword="null"/> means "all currently installed extensions".
    /// </summary>
    public List<string>? ExtensionKeys { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
