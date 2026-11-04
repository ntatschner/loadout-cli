using System.Text.Json;

namespace Loadout.Core.About;

/// <summary>
/// The feature matrix and the guide table, shipped inside the binary.
/// </summary>
/// <remarks>
/// Embedded rather than read from the repository, because an installed copy has
/// no repository beside it. Every guide link any surface shows comes from
/// <c>guides.json</c>, and every feature names its guide by key, so a link is
/// changed in one place.
/// </remarks>
internal static class AboutManifest
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The rows of <c>features.json</c>, in the order written.</summary>
    public static IReadOnlyList<FeatureRow> Features() =>
        Load<List<FeatureRow>>("about/features.json");

    /// <summary>The guides of <c>guides.json</c>, in the order written.</summary>
    public static IReadOnlyList<AboutGuide> Guides() =>
        Load<Dictionary<string, GuideRow>>("about/guides.json")
            .Select(pair => new AboutGuide(pair.Key, pair.Value.Title, pair.Value.Url))
            .ToList();

    private static T Load<T>(string name)
    {
        using var stream = typeof(AboutManifest).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded resource '{name}' is missing from this build.");

        return JsonSerializer.Deserialize<T>(stream, Json)
            ?? throw new InvalidOperationException($"The embedded resource '{name}' is empty.");
    }

    /// <summary>One feature as written, before it is compared with what is running.</summary>
    internal sealed record FeatureRow(string Id, string Name, string Since, string Status, string Guide);

    /// <summary>One guide as written.</summary>
    internal sealed record GuideRow(string Title, string Url);
}
