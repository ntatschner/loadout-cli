using Loadout.Core.Instructions;
using Loadout.Core.Mcp;
using Loadout.Core.Packs;
using Loadout.Core.Workspace;
using Loadout.Models.Instructions;
using Loadout.Models.Results;

namespace Loadout.Core.Manager;

/// <summary>What sort of thing is loaded.</summary>
public enum ManagedKind
{
    /// <summary>A specialist pack fetched from a Git remote.</summary>
    Pack,

    /// <summary>A skill: a procedure an agent follows when asked for it.</summary>
    Skill,

    /// <summary>An MCP server, which is a program that runs with the agent's reach.</summary>
    Server,

    /// <summary>A plugin the agent has installed.</summary>
    Plugin,
}

/// <summary>
/// One thing that is loaded, and the four facts worth knowing about it.
/// </summary>
/// <param name="Kind">Pack, skill or server.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="Scope">Which scope it applies at.</param>
/// <param name="Standing">What state it is in, said plainly.</param>
/// <param name="NeedsAttention">
/// Whether somebody has to decide something before this is settled — an
/// unapproved pack, a name declared twice.
/// </param>
public sealed record ManagedItem(
    ManagedKind Kind,
    string Name,
    string Source,
    string Scope,
    string Standing,
    bool NeedsAttention);

/// <summary>Everything loaded, and anything worth saying about the whole of it.</summary>
public sealed record ManagerView(
    IReadOnlyList<ManagedItem> Items,
    IReadOnlyList<string> Notes)
{
    public static readonly ManagerView Empty = new([], []);

    public IEnumerable<ManagedItem> OfKind(ManagedKind kind) =>
        Items.Where(item => item.Kind == kind);
}

/// <summary>
/// One answer to "what is loaded, and why".
/// </summary>
/// <remarks>
/// <para>
/// Packs, skills and MCP servers each already answer that question, and each
/// answers it differently: a pack through its approval standing, a skill
/// through where in the library it was found, a server through the scope it
/// was declared at. Reading them into one shape is the whole of this type, and
/// it exists so the screen has one thing to render rather than three services
/// to thread through a constructor that already carries twenty.
/// </para>
/// <para>
/// It only reads. Everything that changes any of this goes through the
/// commands, because a screen that implemented approving a pack would be a
/// second implementation of the trust boundary and one of the two would drift.
/// </para>
/// </remarks>
public interface IManagerInventory
{
    Task<OperationResult<ManagerView>> ReadAsync(
        string slug,
        string repositoryPath,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ManagerInventory : IManagerInventory
{
    private readonly IPackService _packs;
    private readonly IMcpService _mcp;
    private readonly IInstalledMcpReader _installed;
    private readonly IInstalledPluginReader _plugins;
    private readonly IInstructionService _instructions;
    private readonly IWorkspaceManager _workspace;

    public ManagerInventory(
        IPackService packs,
        IMcpService mcp,
        IInstalledMcpReader installed,
        IInstalledPluginReader plugins,
        IInstructionService instructions,
        IWorkspaceManager workspace)
    {
        _packs = packs;
        _mcp = mcp;
        _installed = installed;
        _plugins = plugins;
        _instructions = instructions;
        _workspace = workspace;
    }

    /// <inheritdoc />
    public async Task<OperationResult<ManagerView>> ReadAsync(
        string slug,
        string repositoryPath,
        CancellationToken ct = default)
    {
        var items = new List<ManagedItem>();
        var notes = new List<string>();

        // Each of the three is allowed to fail on its own. A machine with no
        // packs declared, or an agent that is not installed, is an ordinary
        // state — reporting the whole screen as broken because one section had
        // nothing to say would make it useless exactly when it is most wanted.
        await AddPacksAsync(items, notes, ct).ConfigureAwait(false);
        await AddSkillsAsync(items, slug, ct).ConfigureAwait(false);
        await AddServersAsync(items, notes, slug, repositoryPath, ct).ConfigureAwait(false);
        await AddPluginsAsync(items, repositoryPath, ct).ConfigureAwait(false);

        return OperationResult<ManagerView>.Ok(new ManagerView(items, notes));
    }

    private async Task AddPacksAsync(List<ManagedItem> items, List<string> notes, CancellationToken ct)
    {
        var standing = await _packs.StandingAsync(ct).ConfigureAwait(false);

        if (standing.Failed)
        {
            notes.Add("Packs could not be read: " + standing.Error);
            return;
        }

        foreach (var pack in standing.Value!)
        {
            items.Add(new ManagedItem(
                ManagedKind.Pack,
                pack.Pack.Name,
                pack.Pack.Remote,

                // Approval is this machine's, never the workspace's, and saying
                // so on every row is the point: the declaration is shared and
                // the decision is not.
                "machine",
                PackGate.Explain(pack),
                !pack.IsActive));
        }
    }

    private async Task AddSkillsAsync(List<ManagedItem> items, string slug, CancellationToken ct)
    {
        var library = await _instructions
            .LibraryAsync(_workspace.LocalPath, slug, ct)
            .ConfigureAwait(false);

        foreach (var skill in library.OfKind(SpecialistKind.Skill).OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            items.Add(new ManagedItem(
                ManagedKind.Skill,
                skill.Id,
                Where(skill.Origin),
                skill.Origin == SpecialistOrigin.Project ? "project" : "workspace",
                "loaded when asked for",
                NeedsAttention: false));
        }
    }

    private async Task AddServersAsync(
        List<ManagedItem> items,
        List<string> notes,
        string slug,
        string repositoryPath,
        CancellationToken ct)
    {
        var installed = await _installed.ReadAsync(repositoryPath, ct).ConfigureAwait(false);
        var resolved = await _mcp.ResolveAsync(slug, installed, ct).ConfigureAwait(false);

        if (resolved.Failed)
        {
            notes.Add("Servers could not be read: " + resolved.Error);
            return;
        }

        var clashing = resolved.Value!.Clashes
            .SelectMany(clash => clash.Names)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in resolved.Value!.Servers.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            items.Add(new ManagedItem(
                ManagedKind.Server,
                entry.Name,

                // Identity rather than the full command line. A server's
                // environment can carry a token, and this is read on a screen
                // somebody may be sharing.
                entry.Server.Identity,
                entry.Scope switch
                {
                    McpScope.Global => "every project",
                    McpScope.Project => "project",
                    _ => "installed",
                },
                clashing.Contains(entry.Name) ? "declared more than once" : "active",
                clashing.Contains(entry.Name)));
        }

        foreach (var clash in resolved.Value!.Clashes)
        {
            notes.Add(clash.Detail);
        }
    }

    private async Task AddPluginsAsync(
        List<ManagedItem> items,
        string repositoryPath,
        CancellationToken ct)
    {
        foreach (var plugin in await _plugins.ReadAsync(repositoryPath, ct).ConfigureAwait(false))
        {
            // The id carries the marketplace after an @, which is the "where it
            // came from" this screen wants, and the bare name is what a person
            // calls it.
            var at = plugin.Id.LastIndexOf('@');

            items.Add(new ManagedItem(
                ManagedKind.Plugin,
                at > 0 ? plugin.Id[..at] : plugin.Id,
                at > 0 && at < plugin.Id.Length - 1
                    ? plugin.Id[(at + 1)..] + " " + plugin.Version
                    : plugin.Version,
                plugin.Scope,

                // Disabled is a settled state somebody chose, not something
                // waiting on a decision, so it is said and not flagged.
                plugin.Enabled ? "enabled" : "disabled",
                NeedsAttention: false));
        }
    }

    /// <summary>Where a specialist was found, in the words the provenance uses.</summary>
    private static string Where(SpecialistOrigin origin) => origin switch
    {
        SpecialistOrigin.BuiltIn => "built-in",
        SpecialistOrigin.Pack => "pack",
        SpecialistOrigin.Project => "project",
        _ => "workspace",
    };
}
