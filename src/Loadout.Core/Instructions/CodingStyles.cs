using Loadout.Models.Instructions;

namespace Loadout.Core.Instructions;

/// <summary>Which part of a style a specialist is.</summary>
public enum StylePart
{
    /// <summary>The rules that load on every launch the style is in force: <c>style.work</c>.</summary>
    Core,

    /// <summary>Rules for one language, loaded only with that language: <c>style.work.csharp</c>.</summary>
    Language,

    /// <summary>A named pattern with example code, loaded when the task points at it: <c>style.work.pattern.repository</c>.</summary>
    Pattern,
}

/// <summary>Which of the three style layers a style belongs to, least specific first.</summary>
public enum StyleLayer
{
    /// <summary>Your own defaults, in force everywhere.</summary>
    Personal,

    /// <summary>The style chosen for a kind of work, per project or profile.</summary>
    Named,

    /// <summary>One codebase's own style.</summary>
    Codebase,
}

/// <summary>One style in a library, with its parts.</summary>
/// <param name="Name">What it is called, such as <c>work</c>.</param>
/// <param name="Core">Its core, or null when it has only language files and patterns.</param>
/// <param name="Languages">Its language files.</param>
/// <param name="Patterns">Its patterns.</param>
public sealed record CodingStyle(
    string Name,
    SpecialistDocument? Core,
    IReadOnlyList<SpecialistDocument> Languages,
    IReadOnlyList<SpecialistDocument> Patterns)
{
    /// <summary>Every part, core first.</summary>
    public IEnumerable<SpecialistDocument> Parts =>
        new[] { Core }.OfType<SpecialistDocument>().Concat(Languages).Concat(Patterns);
}

/// <summary>
/// How coding styles sit in the specialist library, and which of them are in force.
/// </summary>
/// <remarks>
/// <para>
/// A style is not a new kind of document. It is a set of specialists of the
/// style kind sharing a name in their ids — <c>style.work</c>,
/// <c>style.work.csharp</c>, <c>style.work.pattern.repository</c> — so it is
/// loaded, layered, budgeted and explained by the machinery every other
/// specialist already goes through, and a workspace or project copy overrides
/// a part by id exactly as it would anything else.
/// </para>
/// <para>
/// What is new is which of them may load. Every other specialist is a candidate
/// whenever its evidence appears. A style is a candidate only when it is in
/// force: the personal style always, the named style when the project or
/// profile chose it, and the codebase style for the project it belongs to.
/// Somebody else's style turning up because a task mentioned "repository" would
/// be exactly the wrong way round.
/// </para>
/// <para>
/// The layer order does not follow where a file came from. An imported named
/// style has to beat your personal one — you chose it for this kind of work, in
/// the same way a project overrides the workspace — so the order is by layer:
/// personal, then named, then codebase, each read after the last.
/// </para>
/// </remarks>
public static class CodingStyles
{
    /// <summary>The name of the style that is in force everywhere.</summary>
    public const string Personal = "personal";

    /// <summary>The name of a project's own style.</summary>
    public const string Codebase = "codebase";

    /// <summary>The id segment that marks a pattern.</summary>
    public const string PatternSegment = "pattern";

    /// <summary>
    /// The most a style's core should cost, in estimated tokens.
    /// </summary>
    /// <remarks>
    /// A core is paid for on every launch and is never dropped to fit the
    /// budget, so it has to stay small. A thousand tokens is roughly a page of
    /// rules — enough for the ones that matter everywhere, and a prompt to move
    /// the rest into a language file or a pattern, which load only when needed.
    /// </remarks>
    public const int CoreTokenCeiling = 1_000;

    /// <summary>The style an id belongs to, or null when it is not a style id.</summary>
    public static string? NameOf(string id)
    {
        var segments = Segments(id);

        return segments.Length >= 2
            && string.Equals(segments[0], "style", StringComparison.OrdinalIgnoreCase)
            ? segments[1].ToLowerInvariant()
            : null;
    }

    /// <summary>Which part of its style an id names, or null when the id is not a well-formed style id.</summary>
    public static StylePart? PartOf(string id)
    {
        var segments = Segments(id);

        if (NameOf(id) is null)
        {
            return null;
        }

        return segments.Length switch
        {
            2 => StylePart.Core,
            3 when !string.Equals(segments[2], PatternSegment, StringComparison.OrdinalIgnoreCase) => StylePart.Language,
            4 when string.Equals(segments[2], PatternSegment, StringComparison.OrdinalIgnoreCase) => StylePart.Pattern,
            _ => null,
        };
    }

    /// <summary>
    /// The styles in force, least specific first, with the layer each is in.
    /// </summary>
    /// <param name="chosen">The named style chosen for this work, or null.</param>
    /// <remarks>
    /// Choosing <c>personal</c> or <c>codebase</c> as the named style adds
    /// nothing, since both are in force already; it is not an error, because
    /// the answer to "which style is this project using" can honestly be
    /// "just mine".
    /// </remarks>
    public static IReadOnlyList<(string Name, StyleLayer Layer)> InForce(string? chosen)
    {
        var layers = new List<(string, StyleLayer)> { (Personal, StyleLayer.Personal) };

        if (Normalise(chosen) is { } named && named != Personal && named != Codebase)
        {
            layers.Add((named, StyleLayer.Named));
        }

        layers.Add((Codebase, StyleLayer.Codebase));

        return layers;
    }

    /// <summary>The layer a style is in for this work, or null when it is not in force.</summary>
    public static StyleLayer? LayerOf(string name, string? chosen)
    {
        foreach (var (inForce, layer) in InForce(chosen))
        {
            if (string.Equals(inForce, name, StringComparison.OrdinalIgnoreCase))
            {
                return layer;
            }
        }

        return null;
    }

    /// <summary>
    /// The library with every style that is not in force taken out.
    /// </summary>
    /// <remarks>
    /// What the resolver draws evidence-driven candidates from. The full
    /// library is still used for anything named explicitly, so asking for a
    /// pattern from another style by name works and says so.
    /// </remarks>
    public static SpecialistCatalogue InForceOnly(SpecialistCatalogue catalogue, string? chosen)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var kept = new Dictionary<string, SpecialistDocument>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, specialist) in catalogue.Specialists)
        {
            if (specialist.Kind != SpecialistKind.Style
                || (NameOf(id) is { } name && LayerOf(name, chosen) is not null))
            {
                kept[id] = specialist;
            }
        }

        return new SpecialistCatalogue(kept, catalogue.Findings);
    }

    /// <summary>
    /// Where a style specialist composes among the others of its kind.
    /// </summary>
    /// <remarks>
    /// By layer, so a more specific style is read later and wins where two
    /// disagree; then core, language files and patterns, so the general rules
    /// come before the narrower ones that refine them. Styles not in force sort
    /// last, which only matters for the list of what was not loaded.
    /// </remarks>
    public static int Rank(SpecialistDocument specialist, string? chosen)
    {
        ArgumentNullException.ThrowIfNull(specialist);

        if (specialist.Kind != SpecialistKind.Style)
        {
            return 0;
        }

        var layer = NameOf(specialist.Id) is { } name && LayerOf(name, chosen) is { } found
            ? (int)found
            : Enum.GetValues<StyleLayer>().Length;

        var part = PartOf(specialist.Id) is { } known ? (int)known : Enum.GetValues<StylePart>().Length;

        return (layer * 10) + part;
    }

    /// <summary>Every style in a library, by name.</summary>
    public static IReadOnlyList<CodingStyle> All(SpecialistCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        return catalogue.OfKind(SpecialistKind.Style)
            .Where(s => NameOf(s.Id) is not null && PartOf(s.Id) is not null)
            .GroupBy(s => NameOf(s.Id)!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CodingStyle(
                g.Key,
                g.FirstOrDefault(s => PartOf(s.Id) == StylePart.Core),
                g.Where(s => PartOf(s.Id) == StylePart.Language).OrderBy(s => s.Id, StringComparer.Ordinal).ToList(),
                g.Where(s => PartOf(s.Id) == StylePart.Pattern).OrderBy(s => s.Id, StringComparer.Ordinal).ToList()))
            .ToList();
    }

    /// <summary>One style by name, or null when the library has no part of it.</summary>
    public static CodingStyle? Find(SpecialistCatalogue catalogue, string name) =>
        All(catalogue).FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The reason a core is in force, in words somebody can check.</summary>
    public static string Reason(StyleLayer layer, string name) => layer switch
    {
        StyleLayer.Personal => "your personal style",
        StyleLayer.Codebase => "this codebase's style",
        _ => $"the '{name}' style chosen for this work",
    };

    private static string? Normalise(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim().ToLowerInvariant();

    private static string[] Segments(string id) =>
        string.IsNullOrWhiteSpace(id) ? [] : id.Trim().Split('.');
}
