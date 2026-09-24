using Loadout.Core.Teams;
using Loadout.Models.Configuration;
using Loadout.Models.Teams;

namespace Loadout.Core.Tools;

/// <summary>
/// The catalogue's tools as a remediator is offered them: each active version
/// shown to the remedy ruling as though it were a remedy, and decided the same
/// way.
/// </summary>
/// <remarks>
/// <para>
/// The ruling is <see cref="RemedyCeiling.Decide" /> unchanged. A tool is shown
/// to it as a remedy named <c>tool:&lt;name&gt;@&lt;version&gt;</c> of the
/// tool's kind, and what this machine agreed to is read from
/// <see cref="MachineTeams.TrustedTools" />, never from the catalogue, which
/// agents write.
/// </para>
/// <para>
/// Named by the versioned file name, so the permission check that matches a
/// call by the script's file name matches one version and not the next.
/// </para>
/// </remarks>
public static class ToolOffer
{
    /// <summary>The one role that runs scripts, and so the one role offered a tool.</summary>
    public const string Remediator = "role.remediator";

    /// <summary>What a tool version is called where a remedy's name would be.</summary>
    public static string Named(string tool, string version) => $"tool:{tool}@{version}";

    /// <summary>
    /// What a node in <paramref name="role" /> is offered from the catalogue,
    /// with what this machine decided about each.
    /// </summary>
    public static IReadOnlyList<RemedyStanding> For(
        IToolRegistry? registry,
        string role,
        IReadOnlyDictionary<string, string>? rules,
        IReadOnlyList<TrustedTool>? trusted)
    {
        if (registry is null || !string.Equals(role, Remediator, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var standing = new List<RemedyStanding>();

        foreach (var (record, version, script) in registry.Offerable())
        {
            var remedy = new Remedy
            {
                Name = Named(record.Name, version.Version),
                Kind = record.Kind,
                What = record.Summary,
                Assumes = string.Join("; ", version.Dependencies),
                Proves = version.Outputs.Stdout,
                Script = version.Script,
            };

            var rule = rules is not null
                && rules.TryGetValue(record.Kind is { Length: > 0 } kind ? kind.Trim() : "unclassified", out var said)
                    ? said
                    : RemedyRules.Default;

            var decided = RemedyCeiling.Decide(remedy, rule, script, Agreed(trusted, record.Name, version.Version));

            standing.Add(new RemedyStanding(
                remedy.Name,
                remedy.Script,
                decided.Ruling.ToString().ToLowerInvariant(),
                decided.Because,
                remedy.What,
                remedy.Assumes,
                remedy.Proves));
        }

        return standing;
    }

    /// <summary>This machine's agreements about one version, in the shape the ruling reads.</summary>
    private static IReadOnlyList<TrustedRemedy> Agreed(IReadOnlyList<TrustedTool>? trusted, string tool, string version) =>
    [
        .. (trusted ?? [])
            .Where(one => string.Equals(one.Tool, tool, StringComparison.OrdinalIgnoreCase)
                && string.Equals(one.Version, version, StringComparison.Ordinal))
            .Select(one => new TrustedRemedy
            {
                Remedy = Named(tool, version),
                Fingerprint = one.Fingerprint,
                By = one.By,
                At = one.At,
            }),
    ];
}
