namespace Loadout.Models.Triggers;

/// <summary>
/// Somebody whose pull requests, issues and comments count as trusted though
/// GitHub does not call them a collaborator: a bot, or a close contributor.
/// </summary>
/// <remarks>
/// Keyed by GitHub's numeric user id, never the login. A login can be given up
/// and taken by somebody else; the id goes with the account. The login is kept
/// beside it for people to read, and for noticing when the account behind the
/// id has been renamed.
/// </remarks>
public sealed class TriggerAllowEntry
{
    /// <summary>GitHub's numeric id for the account.</summary>
    public long Id { get; set; }

    /// <summary>The login it had when it was added.</summary>
    public string Login { get; set; } = string.Empty;

    /// <summary>The one repository, as <c>owner/name</c>, it is trusted on, or empty.</summary>
    public string Repo { get; set; } = string.Empty;

    /// <summary>The owner whose repositories it is trusted on, or empty.</summary>
    /// <remarks>With neither this nor <see cref="Repo"/>, the entry holds everywhere.</remarks>
    public string Owner { get; set; } = string.Empty;

    /// <summary>When it was added.</summary>
    public DateTimeOffset Added { get; set; }

    /// <summary>Where it holds, in words.</summary>
    public string Scope =>
        Repo.Length > 0 ? Repo : Owner.Length > 0 ? $"{Owner}/*" : "everywhere";
}

/// <summary>Everybody trusted on this machine beyond GitHub's own collaborators.</summary>
public sealed class TriggerAllowList
{
    public int SchemaVersion { get; set; } = 1;

    public List<TriggerAllowEntry> Items { get; set; } = [];
}
