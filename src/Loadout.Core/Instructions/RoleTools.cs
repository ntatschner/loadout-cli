namespace Loadout.Core.Instructions;

/// <summary>
/// The tools a role gets from its posture, before its own frontmatter says
/// anything.
/// </summary>
/// <remarks>
/// <para>
/// Every role file used to carry its whole allow and deny list, and 23 hand
/// copies of one list drifted the way copies do. The fixer's probe looks for
/// the reproduction being run before and after, and its role could not run
/// anything. The release validator's probe looks for the suite, and the
/// reproducer's for something that failed, and neither could run a test.
/// The tagger was the one role with no deny for <c>git push</c>. Every one of
/// those was a line somebody forgot to copy.
/// </para>
/// <para>
/// So what follows from the posture is written once, here, and a role file
/// lists only how it differs. A role's own word beats the default in both
/// directions: a rule it allows is never denied by its mode, and a rule it
/// denies is never allowed by it. Deny still wins between the two finished
/// lists, as it always has.
/// </para>
/// <para>
/// A role with no mode, which is <c>role.member</c>, gets nothing: it is the
/// contract every node reports against, not a job. Nor does a role that says
/// <c>defaults: false</c>, whose list is narrower than any posture's on
/// purpose: the roles guarding the tool catalogue and the remedies.
/// </para>
/// <para>
/// A shell command is not held to the directories the agent is given, so
/// <c>cp</c>, which exists to write a file somewhere, is the implementer's
/// alone rather than every implementing role's.
/// </para>
/// </remarks>
public static class RoleTools
{
    /// <summary>What every node may do whatever its posture: read the tree and its history.</summary>
    private static readonly string[] Reading =
    [
        "Read",
        "Grep",
        "Glob",
        "Bash(git diff:*)",
        "Bash(git log:*)",
        "Bash(git status:*)",
        "Bash(git show:*)",
        "Bash(git rev-parse:*)",
        "Bash(git worktree list:*)",
        "Bash(pwd)",
        "Bash(ls)",
        "Bash(ls :*)",
        "Bash(cat :*)",
        "Bash(head :*)",
        "Bash(tail :*)",
        "Bash(wc :*)",
        "Bash(grep :*)",

        // To print an exit code, which half of what one run's reviewer and
        // verifier were refused came down to. Echo into a file is judged as
        // a write, so this does not let a reading role write one.
        "Bash(echo)",
        "Bash(echo :*)",
    ];

    /// <summary>
    /// Building and testing, for the postures that prove something: a change
    /// made, a change judged, a cause found.
    /// </summary>
    /// <remarks>
    /// The toolchains a repository is most often built with, rather than only
    /// this one's: a team runs against whatever project it was pointed at, and
    /// a role that can only run <c>dotnet test</c> cannot verify anything else.
    /// </remarks>
    private static readonly string[] Proving =
    [
        "Bash(dotnet --version)",
        "Bash(dotnet --info)",
        "Bash(dotnet build:*)",
        "Bash(dotnet test:*)",
        "Bash(npm test:*)",
        "Bash(npm run test:*)",
        "Bash(npm run build:*)",
        "Bash(cargo build:*)",
        "Bash(cargo test:*)",
        "Bash(go build:*)",
        "Bash(go test:*)",
        "Bash(go vet:*)",
        "Bash(pytest:*)",
        "Bash(python -m pytest:*)",
    ];

    /// <summary>Changing files in the node's own tree.</summary>
    private static readonly string[] Editing = ["Edit", "Write", "MultiEdit"];

    /// <summary>Recording a change on the node's own branch.</summary>
    private static readonly string[] Committing = ["Bash(git add:*)", "Bash(git commit:*)"];

    /// <summary>
    /// What no node may do in any posture: change history, or reach past the
    /// machine. Merging and pushing are the run's and the person's.
    /// </summary>
    private static readonly string[] Rewriting =
    [
        "Bash(git push:*)",
        "Bash(git push --force:*)",
        "Bash(git rebase:*)",
        "Bash(git reset:*)",
        "Bash(git commit --amend:*)",
        "Bash(git branch -D:*)",
        "Bash(git tag -d:*)",
    ];

    /// <summary>One posture's defaults.</summary>
    private sealed record Posture(IReadOnlyList<string> Allow, IReadOnlyList<string> Deny);

    /// <summary>
    /// The five postures a role may name. The spelling is the mode
    /// specialists' own.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Posture> Postures =
        new Dictionary<string, Posture>(StringComparer.OrdinalIgnoreCase)
        {
            // Writes a new document - a plan, a strategy, notes - and changes
            // nothing that is already there.
            ["advise"] = new([.. Reading, "Write"], [.. Rewriting, "Edit", "MultiEdit", .. Committing]),

            // Runs things to find out. Whether it may write a file to do so
            // is the role's to say: a reproducer writes one, a verifier none.
            ["investigate"] = new([.. Reading, .. Proving], [.. Rewriting, .. Committing]),

            // Changes the tree. Committing it is the deliverable's to say.
            ["implement"] = new([.. Reading, .. Proving, .. Editing], Rewriting),

            // Judges a change and makes none.
            ["review"] = new([.. Reading, .. Proving], [.. Rewriting, .. Editing, .. Committing]),

            // Delegates. A lead that edits is a lead doing a worker's job
            // with none of a worker's review.
            ["coordinate"] = new(Reading, [.. Rewriting, .. Editing, .. Committing]),
        };

    /// <summary>The postures that have defaults, for anything that wants to list them.</summary>
    public static IReadOnlyCollection<string> Modes => [.. Postures.Keys];

    /// <summary>
    /// A role's finished allow and deny lists: its posture's defaults with its
    /// own frontmatter laid over them.
    /// </summary>
    /// <param name="mode">The role's posture, or null for one with none.</param>
    /// <param name="deliverable">What its report carries; a commit brings <c>git add</c> and <c>git commit</c>.</param>
    /// <param name="allowed">What the role's own file allows.</param>
    /// <param name="denied">What the role's own file denies.</param>
    /// <remarks>
    /// A mode nothing here knows gets no defaults rather than a guess, and the
    /// role is exactly what its file says, which is what every role was before.
    /// </remarks>
    public static (IReadOnlyList<string> Allowed, IReadOnlyList<string> Denied) Compose(
        string? mode,
        string? deliverable,
        IReadOnlyList<string> allowed,
        IReadOnlyList<string> denied)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(denied);

        if (mode is null || !Postures.TryGetValue(mode, out var posture))
        {
            return (allowed, denied);
        }

        IEnumerable<string> defaults = posture.Allow;

        if (string.Equals(deliverable, "commit", StringComparison.OrdinalIgnoreCase))
        {
            defaults = defaults.Concat(Committing);
        }

        // Ordinal, as the gate matches: a role naming a rule in other words
        // has named a different rule, and gets both.
        var allow = defaults.Where(rule => !denied.Contains(rule, StringComparer.Ordinal))
            .Concat(allowed)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // A posture's deny never reaches what the role itself allows, nor
        // what its deliverable needs: a commit role in a posture that denied
        // committing would be a role that cannot deliver.
        var deny = posture.Deny.Where(rule => !allow.Contains(rule, StringComparer.Ordinal))
            .Concat(denied)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return (allow, deny);
    }
}
