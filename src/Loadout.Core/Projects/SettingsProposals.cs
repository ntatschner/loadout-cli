using System.Security.Cryptography;
using System.Text;
using Loadout.Core.Configuration;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Projects;
using Loadout.Models.Results;

namespace Loadout.Core.Projects;

/// <summary>A proposal, with the manifest it would replace, ready to show.</summary>
/// <param name="Proposal">What was proposed.</param>
/// <param name="Changes">The proposal against the current file, line by line.</param>
/// <param name="Stale">Whether the file has changed since the proposal was made.</param>
public sealed record SettingsProposalView(
    SettingsProposal Proposal,
    IReadOnlyList<string> Changes,
    bool Stale);

/// <summary>
/// Suggested changes to a project's settings, held until a person applies them.
/// </summary>
public interface ISettingsProposals
{
    /// <summary>
    /// Records a proposed <c>project.yaml</c>, replacing any earlier one. Refused
    /// when it is not a manifest, changes nothing, or touches a guarded section.
    /// </summary>
    Task<OperationResult<SettingsProposalView>> ProposeAsync(
        string slug,
        string manifestYaml,
        string reasons,
        string proposedBy,
        CancellationToken ct = default);

    /// <summary>The pending proposal, or null when there is none.</summary>
    Task<OperationResult<SettingsProposalView?>> ReadAsync(string slug, CancellationToken ct = default);

    /// <summary>Writes the proposal over <c>project.yaml</c> and removes it.</summary>
    Task<OperationResult<SettingsProposalView>> ApplyAsync(string slug, CancellationToken ct = default);

    /// <summary>Removes the proposal without applying it.</summary>
    Task<OperationResult> DiscardAsync(string slug, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// Some sections are never proposed. Identity and the repository are how every
/// machine recognises the project, and a change there is a re-registration, not
/// a setting. The environment bindings choose which credentials reach the agent
/// and which security profile binds it, so a proposal to change them would be an
/// agent asking for different credentials or a looser sandbox — exactly the
/// change that has to be made by the person, in the file, on purpose.
/// </para>
/// </remarks>
public sealed class SettingsProposals : ISettingsProposals
{
    private readonly IWorkspaceManager _workspace;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;

    public SettingsProposals(IWorkspaceManager workspace, YamlStore yaml, TimeProvider time)
    {
        _workspace = workspace;
        _yaml = yaml;
        _time = time;
    }

    /// <summary>The sections a proposal may not change, as they are named in the file.</summary>
    public static IReadOnlyList<string> Guarded { get; } =
        ["schema_version", "id", "slug", "repository", "environment", "environments"];

    /// <inheritdoc />
    public async Task<OperationResult<SettingsProposalView>> ProposeAsync(
        string slug,
        string manifestYaml,
        string reasons,
        string proposedBy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        if (string.IsNullOrWhiteSpace(reasons))
        {
            return OperationResult<SettingsProposalView>.Fail(
                "Say why. A setting nobody can account for is the first thing the next person removes.",
                ExitCode.InvalidArguments);
        }

        var current = await _workspace.ReadProjectAsync(slug, ct).ConfigureAwait(false);

        if (current.Failed)
        {
            return OperationResult<SettingsProposalView>.Fail(current.Error!, current.ExitCode);
        }

        var proposed = _yaml.Parse<ProjectManifest>(manifestYaml);

        if (proposed.Failed)
        {
            return OperationResult<SettingsProposalView>.Fail(
                $"That is not a project.yaml: {proposed.Error}", proposed.ExitCode);
        }

        var touched = GuardedChanges(current.Value!, proposed.Value!);

        if (touched.Count > 0)
        {
            return OperationResult<SettingsProposalView>.Fail(
                $"A proposal may not change {string.Join(", ", touched)}. Those decide which project "
                + "this is and which credentials and sandbox a session gets, so they are changed by a "
                + "person, in the file. Leave them as they are and propose the rest.",
                ExitCode.InvalidArguments);
        }

        var before = _yaml.Render(current.Value!);
        var after = _yaml.Render(proposed.Value!);

        if (before == after)
        {
            return OperationResult<SettingsProposalView>.Fail(
                "That is the project.yaml as it already is, so there is nothing to propose.",
                ExitCode.InvalidArguments);
        }

        var proposal = new SettingsProposal
        {
            ProposedBy = proposedBy,
            ProposedUtc = _time.GetUtcNow(),
            Reasons = reasons.Trim(),
            Base = Fingerprint(before),
            Manifest = proposed.Value!,
        };

        var saved = await _yaml
            .SaveAsync(PathFor(slug), proposal, restrictPermissions: false, ct)
            .ConfigureAwait(false);

        return saved.Failed
            ? OperationResult<SettingsProposalView>.Fail(saved.Error!, saved.ExitCode)
            : OperationResult<SettingsProposalView>.Ok(
                new SettingsProposalView(proposal, Changes(before, after), Stale: false));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingsProposalView?>> ReadAsync(
        string slug,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var path = PathFor(slug);

        if (!File.Exists(path))
        {
            return OperationResult<SettingsProposalView?>.Ok(null);
        }

        var proposal = await _yaml
            .LoadAsync(path, () => new SettingsProposal(), ct)
            .ConfigureAwait(false);

        if (proposal.Failed)
        {
            return OperationResult<SettingsProposalView?>.Fail(proposal.Error!, proposal.ExitCode);
        }

        var current = await _workspace.ReadProjectAsync(slug, ct).ConfigureAwait(false);

        if (current.Failed)
        {
            return OperationResult<SettingsProposalView?>.Fail(current.Error!, current.ExitCode);
        }

        var before = _yaml.Render(current.Value!);

        return OperationResult<SettingsProposalView?>.Ok(new SettingsProposalView(
            proposal.Value!,
            Changes(before, _yaml.Render(proposal.Value!.Manifest)),
            Stale: Fingerprint(before) != proposal.Value.Base));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingsProposalView>> ApplyAsync(
        string slug,
        CancellationToken ct = default)
    {
        var read = await ReadAsync(slug, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<SettingsProposalView>.Fail(read.Error!, read.ExitCode);
        }

        if (read.Value is not { } view)
        {
            return OperationResult<SettingsProposalView>.Fail(
                $"There is no proposal for '{slug}'.", ExitCode.InvalidArguments);
        }

        // Applying a proposal made against an older file would put back
        // whatever the edit since took out, and nothing would say so.
        if (view.Stale)
        {
            return OperationResult<SettingsProposalView>.Fail(
                "project.yaml has changed since this was proposed, so applying it would undo that "
                + "change. Discard it and propose again against the file as it is now.",
                ExitCode.InvalidArguments);
        }

        var current = await _workspace.ReadProjectAsync(slug, ct).ConfigureAwait(false);

        // Checked again at the point of writing, not only when the proposal
        // was made: the file on disk is what is about to be replaced.
        if (current.Failed || GuardedChanges(current.Value!, view.Proposal.Manifest).Count > 0)
        {
            return OperationResult<SettingsProposalView>.Fail(
                current.Error ?? "The proposal changes a guarded section, so it cannot be applied.",
                ExitCode.InvalidArguments);
        }

        var written = await _workspace.WriteProjectAsync(view.Proposal.Manifest, ct).ConfigureAwait(false);

        if (written.Failed)
        {
            return OperationResult<SettingsProposalView>.Fail(written.Error!, written.ExitCode);
        }

        File.Delete(PathFor(slug));

        return OperationResult<SettingsProposalView>.Ok(view);
    }

    /// <inheritdoc />
    public Task<OperationResult> DiscardAsync(string slug, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var path = PathFor(slug);

        if (!File.Exists(path))
        {
            return Task.FromResult(OperationResult.Fail(
                $"There is no proposal for '{slug}'.", ExitCode.InvalidArguments));
        }

        File.Delete(path);

        return Task.FromResult(OperationResult.Ok());
    }

    private string PathFor(string slug) =>
        Path.Combine(_workspace.LocalPath, "projects", slug, "proposals", "settings.yaml");

    /// <summary>The guarded sections that differ between two manifests.</summary>
    private List<string> GuardedChanges(ProjectManifest current, ProjectManifest proposed)
    {
        var touched = new List<string>();

        void Check(string name, object? before, object? after)
        {
            if (_yaml.Render(before) != _yaml.Render(after))
            {
                touched.Add(name);
            }
        }

        Check("schema_version", current.SchemaVersion, proposed.SchemaVersion);
        Check("id", current.Id, proposed.Id);
        Check("slug", current.Slug, proposed.Slug);
        Check("repository", current.Repository, proposed.Repository);
        Check("environment", current.Environment, proposed.Environment);
        Check("environments", current.Environments, proposed.Environments);

        return touched;
    }

    private static string Fingerprint(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    /// <summary>
    /// The lines that differ, marked <c>-</c> and <c>+</c>, with the line
    /// before each run of changes kept so a reader can tell which section it is in.
    /// </summary>
    /// <remarks>
    /// A longest-common-subsequence over lines. Manifests are a few dozen
    /// lines, so the quadratic table costs nothing, and a dependency for it would
    /// cost more than these lines do.
    /// </remarks>
    public static IReadOnlyList<string> Changes(string before, string after)
    {
        var a = Lines(before);
        var b = Lines(after);
        var common = new int[a.Length + 1, b.Length + 1];

        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                common[i, j] = a[i] == b[j]
                    ? common[i + 1, j + 1] + 1
                    : Math.Max(common[i + 1, j], common[i, j + 1]);
            }
        }

        var lines = new List<string>();
        var x = 0;
        var y = 0;
        string? context = null;

        void Mark(string line)
        {
            if (context is not null)
            {
                lines.Add("  " + context);
                context = null;
            }

            lines.Add(line);
        }

        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                context = a[x];
                x++;
                y++;
            }
            // Removals before additions, so a changed line reads as the old
            // value followed by the new one.
            else if (x < a.Length && (y == b.Length || common[x + 1, y] >= common[x, y + 1]))
            {
                Mark("- " + a[x++]);
            }
            else
            {
                Mark("+ " + b[y++]);
            }
        }

        return lines;
    }

    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
}
