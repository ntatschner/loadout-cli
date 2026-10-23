using Loadout.Core.Configuration;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Triggers;

namespace Loadout.Core.Triggers;

/// <summary>Whether what somebody wrote is trusted, and what said so.</summary>
/// <param name="Trusted">True for a collaborator or somebody on the allow-list.</param>
/// <param name="Because">The association or allow-list entry that decided it, for the journal.</param>
public sealed record TrustVerdict(bool Trusted, string Because)
{
    /// <summary>What an event's <c>trust</c> field says.</summary>
    public string Word => Trusted ? TrustedWord : OutsideWord;

    /// <summary>The field's value for a trusted author.</summary>
    public const string TrustedWord = "trusted";

    /// <summary>The field's value for everybody else.</summary>
    public const string OutsideWord = "outside";
}

/// <summary>Decides whose words count as trusted.</summary>
/// <remarks>
/// <para>
/// GitHub's own <c>author_association</c> first: it comes with every pull
/// request, issue and comment, so it costs nothing to read, and it is the same
/// on a polled event and one sent by webhook. Owners, members and
/// collaborators can push to the repository already, so nothing they write
/// gives them more than they have.
/// </para>
/// <para>
/// Then the allow-list, for the accounts GitHub would call outsiders and you
/// would not: dependabot and its kind, and people you work with closely. Matched
/// by numeric id, scoped to a repository or an owner where the entry says so.
/// </para>
/// <para>
/// Outside is not refused. An outside subject still gets its read-only run; its
/// text reaches the model quoted as somebody else's words, never as yours.
/// </para>
/// </remarks>
public static class TriggerTrust
{
    /// <summary>The associations that mean somebody can already push here.</summary>
    public static readonly IReadOnlyList<string> Collaborators = ["OWNER", "MEMBER", "COLLABORATOR"];

    /// <summary>Whether an author is trusted on a repository.</summary>
    /// <param name="association">GitHub's <c>author_association</c>, or null where the event had none.</param>
    /// <param name="author">The author's numeric id, or null where unknown.</param>
    /// <param name="repo">The repository, as <c>owner/name</c>.</param>
    /// <param name="allowed">The allow-list.</param>
    public static TrustVerdict Decide(string? association, long? author, string repo, IEnumerable<TriggerAllowEntry> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);

        if (association is { Length: > 0 } && Collaborators.Contains(association, StringComparer.OrdinalIgnoreCase))
        {
            return new TrustVerdict(true, $"association {association.ToUpperInvariant()}");
        }

        if (author is { } id && allowed.FirstOrDefault(one => one.Id == id && Covers(one, repo)) is { } entry)
        {
            return new TrustVerdict(true, $"allow-list {entry.Login} ({entry.Scope})");
        }

        return new TrustVerdict(
            false,
            association is { Length: > 0 } ? $"association {association.ToUpperInvariant()}" : "no association given");
    }

    /// <summary>Whether an entry holds on a repository.</summary>
    public static bool Covers(TriggerAllowEntry entry, string repo)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Repo.Length > 0)
        {
            return string.Equals(entry.Repo, repo, StringComparison.OrdinalIgnoreCase);
        }

        if (entry.Owner.Length > 0)
        {
            return repo.StartsWith(entry.Owner + "/", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}

/// <summary>The allow-list on this machine.</summary>
/// <remarks>
/// One file, <c>&lt;State&gt;/triggers/allowed.yaml</c>, beside the rules and
/// machine-local like them: who you trust to start runs here is a decision
/// about this machine's money.
/// </remarks>
public sealed class TriggerAllowStore
{
    private readonly string _path;
    private readonly YamlStore _yaml;

    /// <param name="state">The state directory.</param>
    /// <param name="yaml">Reads and writes the file.</param>
    public TriggerAllowStore(string state, YamlStore yaml)
    {
        _path = Path.Combine(state, "triggers", "allowed.yaml");
        _yaml = yaml;
    }

    /// <summary>Where the list is kept.</summary>
    public string FilePath => _path;

    /// <summary>Everybody on it, in the order they were added.</summary>
    public async Task<OperationResult<IReadOnlyList<TriggerAllowEntry>>> ListAsync(CancellationToken ct = default)
    {
        var loaded = await _yaml.LoadAsync(_path, () => new TriggerAllowList(), ct).ConfigureAwait(false);

        return loaded.Failed
            ? OperationResult<IReadOnlyList<TriggerAllowEntry>>.Fail(loaded.Error!, loaded.ExitCode)
            : OperationResult<IReadOnlyList<TriggerAllowEntry>>.Ok(loaded.Value!.Items);
    }

    /// <summary>Adds somebody, replacing an entry for the same account with the same scope.</summary>
    /// <remarks>
    /// An account can hold several entries, one per scope, so trusting somebody
    /// on a second repository never quietly widens or narrows the first.
    /// </remarks>
    public async Task<OperationResult> AddAsync(TriggerAllowEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Id <= 0)
        {
            return OperationResult.Fail("An allow-list entry needs the account's GitHub id.", ExitCode.InvalidArguments);
        }

        if (entry.Repo.Length > 0 && entry.Owner.Length > 0)
        {
            return OperationResult.Fail(
                "An allow-list entry holds on one repository or on one owner's, not both.", ExitCode.InvalidArguments);
        }

        if (entry.Repo.Length > 0 && GitHubRepo.Parse(entry.Repo) is null)
        {
            return OperationResult.Fail(
                $"'{entry.Repo}' is not a repository: name one as owner/name.", ExitCode.InvalidArguments);
        }

        var written = await _yaml.UpdateAsync<TriggerAllowList>(
            _path,
            () => new TriggerAllowList(),
            list =>
            {
                list.Items.RemoveAll(one => Same(one, entry));
                list.Items.Add(entry);
            },
            true,
            ct).ConfigureAwait(false);

        return written.Failed ? OperationResult.Fail(written.Error!, written.ExitCode) : OperationResult.Ok();
    }

    /// <summary>Takes somebody off, every scope or the one named, and says which entries went.</summary>
    /// <param name="login">The login, as listed.</param>
    /// <param name="repo">Only the entry for this repository.</param>
    /// <param name="owner">Only the entry for this owner.</param>
    /// <param name="ct">Cancels the write.</param>
    public async Task<OperationResult<IReadOnlyList<TriggerAllowEntry>>> RemoveAsync(
        string login, string? repo = null, string? owner = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);

        var gone = new List<TriggerAllowEntry>();

        var written = await _yaml.UpdateAsync<TriggerAllowList>(
            _path,
            () => new TriggerAllowList(),
            list =>
            {
                gone.AddRange(list.Items.Where(one => Named(one, login, repo, owner)));
                list.Items.RemoveAll(one => Named(one, login, repo, owner));
            },
            true,
            ct).ConfigureAwait(false);

        if (written.Failed)
        {
            return OperationResult<IReadOnlyList<TriggerAllowEntry>>.Fail(written.Error!, written.ExitCode);
        }

        return gone.Count > 0
            ? OperationResult<IReadOnlyList<TriggerAllowEntry>>.Ok(gone)
            : OperationResult<IReadOnlyList<TriggerAllowEntry>>.Fail(
                $"'{login}' is not on the allow-list{(repo ?? owner) switch { null => string.Empty, var where => $" for {where}" }}.",
                ExitCode.ProjectNotFound);
    }

    private static bool Same(TriggerAllowEntry one, TriggerAllowEntry other) =>
        one.Id == other.Id
        && string.Equals(one.Repo, other.Repo, StringComparison.OrdinalIgnoreCase)
        && string.Equals(one.Owner, other.Owner, StringComparison.OrdinalIgnoreCase);

    private static bool Named(TriggerAllowEntry one, string login, string? repo, string? owner) =>
        string.Equals(one.Login, login, StringComparison.OrdinalIgnoreCase)
        && (repo is null || string.Equals(one.Repo, repo, StringComparison.OrdinalIgnoreCase))
        && (owner is null || string.Equals(one.Owner, owner, StringComparison.OrdinalIgnoreCase));
}
