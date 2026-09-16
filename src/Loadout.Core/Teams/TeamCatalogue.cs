using System.Text;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Loadout.Models.Teams;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Loadout.Core.Teams;

/// <summary>Every team this launcher can run, and what is wrong with any of them.</summary>
/// <param name="Teams">By name, later origins having replaced earlier.</param>
/// <param name="Findings">What did not load, or loaded with a problem, each naming the team.</param>
/// <param name="Origins">Which layer each team's surviving definition came from.</param>
public sealed record TeamCatalogueResult(
    IReadOnlyDictionary<string, TeamDefinition> Teams,
    IReadOnlyList<RuleFinding> Findings,
    IReadOnlyDictionary<string, SpecialistOrigin> Origins)
{
    /// <summary>A team by name, or null.</summary>
    public TeamDefinition? Find(string name) =>
        Teams.TryGetValue(name, out var team) ? team : null;

    /// <summary>
    /// Where a team came from, which a person deciding whether to run it wants
    /// to know: a team from a pack is somebody else's repository, read once and
    /// pinned, and it says which roles run with which permissions.
    /// </summary>
    /// <remarks>
    /// Kept beside the teams rather than on <see cref="TeamDefinition"/>,
    /// because that type is what a YAML file deserialises into and a file that
    /// could set its own origin could call itself built in.
    /// </remarks>
    public SpecialistOrigin Origin(string name) =>
        Origins.TryGetValue(name, out var origin) ? origin : SpecialistOrigin.BuiltIn;
}

/// <summary>Loads and checks the teams available to a project.</summary>
public interface ITeamCatalogue
{
    /// <summary>
    /// The built-in teams, then the approved packs', then the workspace's,
    /// then the project's, later replacing earlier by name, each checked
    /// against the specialist catalogue so a team naming a role that does not
    /// exist is a finding here rather than a failure mid-run.
    /// </summary>
    Task<TeamCatalogueResult> LoadAsync(
        string? workspaceRoot,
        string? slug,
        SpecialistCatalogue specialists,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class TeamCatalogue : ITeamCatalogue
{
    private const string BuiltInPrefix = "Loadout.Core.Teams.Catalogue.";

    /// <summary>
    /// Where the teams of approved packs live, asked for at load time.
    /// </summary>
    /// <remarks>
    /// A delegate rather than the pack service itself, so the catalogue keeps
    /// no opinion about remotes, approvals or Git - it is handed directories
    /// that have already passed the gate, the same way the specialist library
    /// is.
    /// </remarks>
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _packs;

    /// <param name="packs">
    /// The teams directory of each approved pack. A pack that is declared but
    /// unapproved must never reach here: a team file says which roles run with
    /// which permissions, so it is content somebody has to have read.
    /// </param>
    public TeamCatalogue(Func<CancellationToken, Task<IReadOnlyList<string>>>? packs = null) =>
        _packs = packs ?? (_ => Task.FromResult<IReadOnlyList<string>>([]));

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly HashSet<string> Autonomies = new(StringComparer.Ordinal)
    {
        "manual", "supervised", "autonomous",
    };

    /// <inheritdoc />
    public async Task<TeamCatalogueResult> LoadAsync(
        string? workspaceRoot,
        string? slug,
        SpecialistCatalogue specialists,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(specialists);

        var teams = new Dictionary<string, TeamDefinition>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<RuleFinding>();
        var origins = new Dictionary<string, SpecialistOrigin>(StringComparer.OrdinalIgnoreCase);

        LoadBuiltIn(teams, origins, findings);

        // Packs layer over the built-ins and under the workspace, so a team a
        // pack ships can be replaced by one of your own of the same name, and
        // never the other way round.
        foreach (var directory in await _packs(ct).ConfigureAwait(false))
        {
            await LoadDirectoryAsync(directory, SpecialistOrigin.Pack, teams, origins, findings, ct)
                .ConfigureAwait(false);
        }

        if (workspaceRoot is { Length: > 0 })
        {
            await LoadDirectoryAsync(
                Path.Combine(workspaceRoot, "global", "teams"),
                SpecialistOrigin.Workspace, teams, origins, findings, ct).ConfigureAwait(false);

            if (slug is { Length: > 0 })
            {
                await LoadDirectoryAsync(
                    Path.Combine(workspaceRoot, "projects", slug, "teams"),
                    SpecialistOrigin.Project, teams, origins, findings, ct).ConfigureAwait(false);
            }
        }

        foreach (var team in teams.Values)
        {
            findings.AddRange(Check(team, specialists));
        }

        return new TeamCatalogueResult(teams, findings, origins);
    }

    /// <summary>Reads one team from its text, or says why the text is not one.</summary>
    public static TeamDefinition? Parse(string text, string path, List<RuleFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(findings);

        TeamDefinition? team;

        try
        {
            team = Yaml.Deserialize<TeamDefinition>(text);
        }
        catch (YamlException ex)
        {
            findings.Add(new RuleFinding(
                Path.GetFileNameWithoutExtension(path), RuleFindingSeverity.Error, "team-yaml",
                $"'{Path.GetFileName(path)}' could not be read as a team: {ex.Message}"));

            return null;
        }

        if (team is null || string.IsNullOrWhiteSpace(team.Name))
        {
            findings.Add(new RuleFinding(
                Path.GetFileNameWithoutExtension(path), RuleFindingSeverity.Error, "team-name",
                $"'{Path.GetFileName(path)}' names no team."));

            return null;
        }

        team.Name = team.Name.Trim();

        return team;
    }

    /// <summary>
    /// The rules a team file has to satisfy before a run would make sense.
    /// </summary>
    /// <remarks>
    /// Each is a sentence naming the team and what to change, because a team
    /// file is something a person writes and the finding is what they read.
    /// </remarks>
    public static IReadOnlyList<RuleFinding> Check(TeamDefinition team, SpecialistCatalogue specialists)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(specialists);

        var findings = new List<RuleFinding>();

        void Error(string rule, string detail) =>
            findings.Add(new RuleFinding(team.Name, RuleFindingSeverity.Error, rule, detail));

        if (team.Nodes.Count == 0)
        {
            Error("team-nodes", $"Team '{team.Name}' has no nodes.");
        }

        if (string.IsNullOrWhiteSpace(team.Lead))
        {
            Error("team-lead", $"Team '{team.Name}' names no lead.");
        }
        else if (!team.Nodes.ContainsKey(team.Lead))
        {
            Error("team-lead", $"Team '{team.Name}' names '{team.Lead}' as its lead, which is not one of its nodes.");
        }

        foreach (var (name, node) in team.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Role))
            {
                Error("team-role", $"Node '{name}' of team '{team.Name}' names no role.");
            }
            else if (specialists.Find(node.Role) is not { Kind: SpecialistKind.Role })
            {
                Error("team-role", $"Node '{name}' of team '{team.Name}' plays '{node.Role}', which is not a role in the library.");
            }

            foreach (var delegate_ in node.Delegates)
            {
                if (!team.Nodes.ContainsKey(delegate_))
                {
                    Error("team-delegate", $"Node '{name}' of team '{team.Name}' may request '{delegate_}', which is not one of its nodes.");
                }
            }

            if (node.Parallel < 1)
            {
                Error("team-parallel", $"Node '{name}' of team '{team.Name}' has parallel {node.Parallel}; it needs at least 1.");
            }
        }

        if (!Autonomies.Contains(team.Rules.Autonomy))
        {
            Error("team-autonomy", $"Team '{team.Name}' has autonomy '{team.Rules.Autonomy}'; it must be manual, supervised or autonomous.");
        }

        if (!string.Equals(team.Rules.Gates.Outward, "ask", StringComparison.Ordinal))
        {
            Error("team-outward", $"Team '{team.Name}' sets gates.outward to '{team.Rules.Gates.Outward}'. A team file may only ask; outward actions are allowed per node, in the run.");
        }

        foreach (var gate in team.Rules.Gates.Merge)
        {
            if (!team.Nodes.ContainsKey(gate))
            {
                Error("team-merge-gate", $"Team '{team.Name}' needs '{gate}' to decide a merge, which is not one of its nodes.");
            }
        }

        return findings;
    }

    private static void LoadBuiltIn(
        Dictionary<string, TeamDefinition> teams,
        Dictionary<string, SpecialistOrigin> origins,
        List<RuleFinding> findings)
    {
        var assembly = typeof(TeamCatalogue).Assembly;

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(BuiltInPrefix, StringComparison.Ordinal)
                || !resource.EndsWith(".yaml", StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource);

            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);

            if (Parse(reader.ReadToEnd(), resource, findings) is { } team)
            {
                teams[team.Name] = team;
                origins[team.Name] = SpecialistOrigin.BuiltIn;
            }
        }
    }

    private static async Task LoadDirectoryAsync(
        string directory,
        SpecialistOrigin origin,
        Dictionary<string, TeamDefinition> teams,
        Dictionary<string, SpecialistOrigin> origins,
        List<RuleFinding> findings,
        CancellationToken ct)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();

            string text;

            try
            {
                text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                findings.Add(new RuleFinding(
                    Path.GetFileNameWithoutExtension(file), RuleFindingSeverity.Error, "team-read",
                    $"'{Path.GetFileName(file)}' could not be read: {ex.Message}"));

                continue;
            }

            if (Parse(text, file, findings) is { } team)
            {
                // Later replaces earlier, whole: a workspace team of the same
                // name is that team, not a mixture of it and the built-in. The
                // origin is replaced with it, because the surviving definition
                // is the one a person is being told about.
                teams[team.Name] = team;
                origins[team.Name] = origin;
            }
        }
    }
}
