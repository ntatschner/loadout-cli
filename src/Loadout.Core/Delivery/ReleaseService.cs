using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Delivery;

/// <summary>What a release of a project would be, worked out without changing anything.</summary>
/// <param name="Project">The project's slug.</param>
/// <param name="Repository">Its checkout on this machine.</param>
/// <param name="Recipe">What a release does: <c>tag-push</c>, <c>workflow</c> or <c>none</c>.</param>
/// <param name="Branch">The branch released from: origin's default, else main, else master.</param>
/// <param name="Head">That branch's commit.</param>
/// <param name="TagPrefix">What its tags start with.</param>
/// <param name="LastTag">The newest release tag on the branch, or null when there is none.</param>
/// <param name="VersionFile">The file holding the version, or empty when none was found.</param>
/// <param name="FileVersion">The version that file holds on the branch, or null when it could not be read.</param>
/// <param name="Proposed">The version proposed for this release, or null when there is nothing to base one on.</param>
/// <param name="Bump">Why that version: major, minor or patch, and what said so.</param>
/// <param name="Since">The subjects of the commits since the last release, newest first.</param>
/// <param name="Notes">The notes proposed, to be edited rather than used as they are.</param>
/// <param name="Problems">What would stop a release now, each in a line.</param>
public sealed record ReleasePlan(
    string Project,
    string Repository,
    string Recipe,
    string Branch,
    string Head,
    string TagPrefix,
    string? LastTag,
    string VersionFile,
    string? FileVersion,
    string? Proposed,
    string Bump,
    IReadOnlyList<string> Since,
    string Notes,
    IReadOnlyList<string> Problems);

/// <summary>What a release did.</summary>
/// <param name="Tag">The tag made.</param>
/// <param name="Commit">The commit it points at.</param>
/// <param name="Bumped">Whether a commit was made to bump the version file first.</param>
/// <param name="Said">What the host said, for a workflow; empty otherwise.</param>
public sealed record ReleaseOutcome(string Tag, string Commit, bool Bumped, string Said);

/// <summary>
/// Releases a project once its work has landed, the way its manifest says.
/// </summary>
/// <remarks>
/// <para>
/// Worked out first by <see cref="PlanAsync"/>, which changes nothing and
/// proposes a version and notes, and made second by <see cref="CutAsync"/>
/// with the version and notes the person settles on. Nothing proposed is used
/// without being given back.
/// </para>
/// <para>
/// <c>tag-push</c> bumps the version file when it does not already say the
/// version, commits that as "Release x.y.z", makes an annotated tag with the
/// notes in its body, and checks the tagged commit's version file says the
/// version before anything is pushed. A tag without the bump is the mistake
/// this exists to make impossible.
/// </para>
/// </remarks>
public interface IReleaseService
{
    /// <summary>Works out what a release of a project would be.</summary>
    Task<OperationResult<ReleasePlan>> PlanAsync(string project, CancellationToken ct = default);

    /// <summary>Why this version can't be released from this plan, or null when it can.</summary>
    string? CannotCut(ReleasePlan plan, string? version, string? notes);

    /// <summary>Releases the version planned, with these notes.</summary>
    Task<OperationResult<ReleaseOutcome>> CutAsync(ReleasePlan plan, string version, string notes, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed partial class ReleaseService : IReleaseService
{
    /// <summary>Pushes a tag, after bumping and committing the version file where it needs it.</summary>
    public const string TagPush = "tag-push";

    /// <summary>Starts a GitHub Actions workflow with the version.</summary>
    public const string Workflow = "workflow";

    /// <summary>Nothing: the project is not released from here.</summary>
    public const string None = "none";

    /// <summary>Every recipe word, in the order they are offered.</summary>
    public static IReadOnlyList<string> Recipes { get; } = [TagPush, Workflow, None];

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly ILocalSquash _branches;
    private readonly IRepositoryHosts _hosts;
    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;

    public ReleaseService(
        IProjectService projects,
        IWorkspaceManager workspace,
        ILocalSquash branches,
        IRepositoryHosts hosts,
        IProcessLauncher processes,
        IExecutableResolver resolver)
    {
        _projects = projects;
        _workspace = workspace;
        _branches = branches;
        _hosts = hosts;
        _processes = processes;
        _resolver = resolver;
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex Semver();

    [GeneratedRegex(@"<Version>\s*([^<\s]+)\s*</Version>")]
    private static partial Regex MsBuildVersion();

    [GeneratedRegex("\"version\"\\s*:\\s*\"([^\"]+)\"")]
    private static partial Regex PackageVersion();

    [GeneratedRegex(@"#(\d+)\b")]
    private static partial Regex PullRequestNumber();

    [GeneratedRegex(@"^(?:[A-Za-z][\w /.-]*:\s*)?(fix|fixes|fixed|repair|repairs|correct|corrects|revert|reverts|bump|bumps)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RepairSubject();

    [GeneratedRegex(@"^\w+(\([^)]*\))?!:|BREAKING CHANGE", RegexOptions.IgnoreCase)]
    private static partial Regex BreakingSubject();

    /// <summary>The recipe a project's word names, or a refusal; empty is <see cref="None"/>.</summary>
    public static OperationResult<string> RecipeOf(ProjectRelease? release)
    {
        var word = release?.Recipe?.Trim().ToLowerInvariant() ?? string.Empty;

        return word.Length == 0
            ? OperationResult<string>.Ok(None)
            : Recipes.Contains(word)
                ? OperationResult<string>.Ok(word)
                : OperationResult<string>.Fail(
                    $"'{release!.Recipe}' is not a release recipe. Give one of: {string.Join(", ", Recipes)}.",
                    ExitCode.InvalidArguments);
    }

    /// <summary>
    /// The version a release after <paramref name="last"/> should be, and why,
    /// from the subjects of the commits since.
    /// </summary>
    /// <remarks>
    /// Major when a subject says it breaks something, conventional-commit style.
    /// Patch when every subject is a repair: fix, repair, correct, revert or
    /// bump, after an optional "Area: " prefix. Minor otherwise, since new work
    /// that is not a repair is new capability. A proposal, never used unedited.
    /// </remarks>
    public static (string? Version, string Why) Propose(string? last, IReadOnlyList<string> since)
    {
        ArgumentNullException.ThrowIfNull(since);

        if (last is null || !Semver().IsMatch(last))
        {
            return (null, "there is no earlier release version to count from");
        }

        if (since.Count == 0)
        {
            return (null, "nothing has been committed since the last release");
        }

        var parts = last.Split('-')[0].Split('.').Select(one => int.Parse(one, CultureInfo.InvariantCulture)).ToArray();

        if (since.Any(one => BreakingSubject().IsMatch(one)))
        {
            return ($"{parts[0] + 1}.0.0", "major: a commit says it breaks something");
        }

        if (since.All(one => RepairSubject().IsMatch(one)))
        {
            return ($"{parts[0]}.{parts[1]}.{parts[2] + 1}", "patch: every commit since is a repair");
        }

        return ($"{parts[0]}.{parts[1] + 1}.0", "minor: there is new work that is not a repair");
    }

    /// <summary>
    /// Notes proposed for a release: a line to replace with what changed for
    /// somebody deciding whether to update, and the pull requests it carried.
    /// </summary>
    /// <remarks>
    /// The pull request line starts with words, because git drops every line of
    /// an annotated tag's message that starts with '#'.
    /// </remarks>
    public static string ProposeNotes(IReadOnlyList<string> since)
    {
        ArgumentNullException.ThrowIfNull(since);

        var numbers = since
            .SelectMany(one => PullRequestNumber().Matches(one).Select(match => match.Groups[1].Value))
            .Distinct()
            .OrderBy(one => int.Parse(one, CultureInfo.InvariantCulture))
            .ToList();

        var notes = new StringBuilder("Say what changed for somebody deciding whether to update, in two or three sentences.");

        if (numbers.Count > 0)
        {
            notes.Append("\n\nPull requests: ").Append(string.Join(" ", numbers.Select(one => "#" + one)));
        }

        return notes.ToString();
    }

    /// <summary>The version a file's text holds, by the pattern given or the one its name implies.</summary>
    public static string? VersionIn(string file, string text, string? pattern)
    {
        var regex = Pattern(file, pattern);

        return regex?.Match(text) is { Success: true } match ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>A file's text with its version replaced, or null when the pattern finds none.</summary>
    public static string? WithVersion(string file, string text, string? pattern, string version)
    {
        var regex = Pattern(file, pattern);

        if (regex?.Match(text) is not { Success: true } match)
        {
            return null;
        }

        var group = match.Groups[1];

        return string.Concat(text.AsSpan(0, group.Index), version, text.AsSpan(group.Index + group.Length));
    }

    private static Regex? Pattern(string file, string? pattern)
    {
        if (pattern?.Trim() is { Length: > 0 } given)
        {
            try
            {
                return new Regex(given, RegexOptions.None, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        var name = Path.GetFileName(file).ToLowerInvariant();

        return name.EndsWith(".props", StringComparison.Ordinal) || name.EndsWith("proj", StringComparison.Ordinal)
            ? MsBuildVersion()
            : name == "package.json" ? PackageVersion() : null;
    }

    /// <inheritdoc />
    public async Task<OperationResult<ReleasePlan>> PlanAsync(string project, CancellationToken ct = default)
    {
        var resolved = await _projects.ResolveAsync(project, ct).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return OperationResult<ReleasePlan>.Fail(resolved.Error!, resolved.ExitCode);
        }

        if (resolved.Value!.LocalPath is not { } repository)
        {
            return OperationResult<ReleasePlan>.Fail(
                $"{resolved.Value.Entry.Slug} has no checkout on this machine, so it can't be released from here.",
                ExitCode.RepositoryUnavailable);
        }

        var slug = resolved.Value.Entry.Slug;
        var manifest = await _workspace.ReadProjectAsync(slug, ct).ConfigureAwait(false);
        var release = manifest.Succeeded ? manifest.Value!.Delivery.Release : new ProjectRelease();
        var recipe = RecipeOf(release);

        if (recipe.Failed)
        {
            return OperationResult<ReleasePlan>.Fail(recipe.Error!, recipe.ExitCode);
        }

        var branch = await _branches.TargetAsync(repository, ct).ConfigureAwait(false);

        if (branch.Failed)
        {
            return OperationResult<ReleasePlan>.Fail(branch.Error!, branch.ExitCode);
        }

        var problems = new List<string>();
        var prefix = release.TagPrefix.Trim().Length > 0 ? release.TagPrefix.Trim() : "v";
        var head = (await GitAsync(repository, ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch.Value}^{{commit}}"], ct)
            .ConfigureAwait(false)).Output;

        if (head.Length == 0)
        {
            return OperationResult<ReleasePlan>.Fail($"{branch.Value} has no commit to release.", ExitCode.RepositoryUnavailable);
        }

        var described = await GitAsync(repository, ["describe", "--tags", "--abbrev=0", "--match", prefix + "*", head], ct)
            .ConfigureAwait(false);
        var lastTag = described.Code == 0 && described.Output.Length > 0 ? described.Output : null;
        var range = lastTag is null ? head : $"{lastTag}..{head}";
        var log = await GitAsync(repository, ["log", "--first-parent", "--format=%s", range], ct).ConfigureAwait(false);
        var since = log.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(one => !one.StartsWith("Release ", StringComparison.Ordinal))
            .ToList();

        var file = release.VersionFile.Trim().Length > 0 ? release.VersionFile.Trim() : await FindVersionFileAsync(repository, head, ct).ConfigureAwait(false);
        string? fileVersion = null;

        if (file.Length > 0)
        {
            var shown = await GitAsync(repository, ["show", $"{head}:{file.Replace('\\', '/')}"], ct).ConfigureAwait(false);
            fileVersion = shown.Code == 0 ? VersionIn(file, shown.Output, release.VersionPattern) : null;
        }

        var last = lastTag is null ? fileVersion : lastTag[prefix.Length..];
        var (proposed, why) = Propose(last, since);

        if (recipe.Value == None)
        {
            problems.Add($"{slug} has no release recipe. Set delivery.release.recipe in its project.yaml: tag-push or workflow.");
        }

        if (recipe.Value == TagPush && fileVersion is null)
        {
            problems.Add(file.Length == 0
                ? "No version file was found. Set delivery.release.version_file, and version_pattern if it is not MSBuild or package.json."
                : $"{file} holds no version Loadout can read. Set delivery.release.version_pattern.");
        }

        if (recipe.Value == Workflow && release.Workflow.Trim().Length == 0)
        {
            problems.Add("The workflow recipe needs delivery.release.workflow: the workflow to start.");
        }

        var state = await GitAsync(repository, ["status", "--porcelain", "--untracked-files=no"], ct).ConfigureAwait(false);
        var current = await GitAsync(repository, ["symbolic-ref", "--quiet", "--short", "HEAD"], ct).ConfigureAwait(false);

        if (recipe.Value == TagPush && !string.Equals(current.Output, branch.Value, StringComparison.Ordinal))
        {
            problems.Add($"The checkout at {repository} is on {(current.Output.Length > 0 ? current.Output : "no branch")}, not {branch.Value}.");
        }

        if (recipe.Value == TagPush && state.Output.Length > 0)
        {
            problems.Add($"The checkout at {repository} has changes not committed.");
        }

        var upstream = await GitAsync(repository, ["rev-parse", "--verify", "--quiet", $"refs/remotes/origin/{branch.Value}"], ct)
            .ConfigureAwait(false);

        if (upstream.Output.Length > 0 && !string.Equals(upstream.Output, head, StringComparison.Ordinal))
        {
            problems.Add($"{branch.Value} is not where origin/{branch.Value} was when it was last fetched. Pull or push first.");
        }

        return OperationResult<ReleasePlan>.Ok(new ReleasePlan(
            slug, repository, recipe.Value!, branch.Value!, head, prefix, lastTag, file, fileVersion,
            proposed, why, since, ProposeNotes(since), problems));
    }

    /// <inheritdoc />
    public string? CannotCut(ReleasePlan plan, string? version, string? notes)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Problems.Count > 0)
        {
            return plan.Problems[0];
        }

        if (version?.Trim() is not { Length: > 0 } given)
        {
            return plan.Proposed is null
                ? "Give the version with --version."
                : $"Give the version with --version. {plan.Proposed} is proposed ({plan.Bump}).";
        }

        if (!Semver().IsMatch(given))
        {
            return $"'{given}' is not a version: give it as major.minor.patch, such as {plan.Proposed ?? "1.2.3"}.";
        }

        if (plan.LastTag is { } last && string.Equals(last, plan.TagPrefix + given, StringComparison.Ordinal))
        {
            return $"{last} is already released.";
        }

        return notes?.Trim() is { Length: > 0 }
            ? null
            : "Write the release notes with --notes. 'loadout release plan' proposes some to start from.";
    }

    /// <inheritdoc />
    public async Task<OperationResult<ReleaseOutcome>> CutAsync(ReleasePlan plan, string version, string notes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (CannotCut(plan, version, notes) is { } refused)
        {
            return OperationResult<ReleaseOutcome>.Fail(refused, ExitCode.InvalidArguments);
        }

        version = version.Trim();
        notes = notes.Trim();
        var tag = plan.TagPrefix + version;

        var existing = await GitAsync(plan.Repository, ["rev-parse", "--verify", "--quiet", "refs/tags/" + tag], ct).ConfigureAwait(false);

        if (existing.Output.Length > 0)
        {
            return OperationResult<ReleaseOutcome>.Fail($"{tag} exists already.", ExitCode.InvalidArguments);
        }

        return plan.Recipe == Workflow
            ? await DispatchAsync(plan, version, tag, ct).ConfigureAwait(false)
            : await TagAsync(plan, version, notes, tag, ct).ConfigureAwait(false);
    }

    private async Task<OperationResult<ReleaseOutcome>> TagAsync(ReleasePlan plan, string version, string notes, string tag, CancellationToken ct)
    {
        var repository = plan.Repository;

        // The plan is only believed while the branch is where it was shown.
        var head = await GitAsync(repository, ["rev-parse", "HEAD"], ct).ConfigureAwait(false);

        if (!string.Equals(head.Output, plan.Head, StringComparison.Ordinal))
        {
            return OperationResult<ReleaseOutcome>.Fail(
                $"{plan.Branch} moved since the release was worked out. Nothing was changed; run it again.", ExitCode.GeneralFailure);
        }

        var bumped = false;

        if (!string.Equals(plan.FileVersion, version, StringComparison.Ordinal))
        {
            var path = Path.Combine(repository, plan.VersionFile);
            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var manifest = await _workspace.ReadProjectAsync(plan.Project, ct).ConfigureAwait(false);
            var pattern = manifest.Succeeded ? manifest.Value!.Delivery.Release.VersionPattern : null;

            if (WithVersion(plan.VersionFile, text, pattern, version) is not { } changed)
            {
                return OperationResult<ReleaseOutcome>.Fail($"{plan.VersionFile} holds no version to bump.", ExitCode.GeneralFailure);
            }

            await File.WriteAllTextAsync(path, changed, ct).ConfigureAwait(false);

            var committed = await GitAsync(repository, ["commit", "--quiet", "-m", $"Release {version}", "--", plan.VersionFile], ct)
                .ConfigureAwait(false);

            if (committed.Code != 0)
            {
                await GitAsync(repository, ["checkout", "--", plan.VersionFile], ct).ConfigureAwait(false);

                return OperationResult<ReleaseOutcome>.Fail(
                    $"The version bump could not be committed, so it was put back: {committed.Error}", ExitCode.GeneralFailure);
            }

            bumped = true;
        }

        var tagged = await GitAsync(repository, ["tag", "-a", tag, "-m", $"Release {version}", "-m", notes], ct).ConfigureAwait(false);

        if (tagged.Code != 0)
        {
            return OperationResult<ReleaseOutcome>.Fail(
                $"{tag} could not be made{(bumped ? "; the bump commit is left on " + plan.Branch + ", unpushed" : string.Empty)}: {tagged.Error}",
                ExitCode.GeneralFailure);
        }

        // Checked on the tagged commit itself, before anything leaves this machine.
        var shown = await GitAsync(repository, ["show", $"{tag}:{plan.VersionFile.Replace('\\', '/')}"], ct).ConfigureAwait(false);
        var manifestNow = await _workspace.ReadProjectAsync(plan.Project, ct).ConfigureAwait(false);
        var patternNow = manifestNow.Succeeded ? manifestNow.Value!.Delivery.Release.VersionPattern : null;

        if (shown.Code != 0 || VersionIn(plan.VersionFile, shown.Output, patternNow) != version)
        {
            await GitAsync(repository, ["tag", "-d", tag], ct).ConfigureAwait(false);

            return OperationResult<ReleaseOutcome>.Fail(
                $"{tag} was taken back before anything was pushed: {plan.VersionFile} on the tagged commit does not say {version}."
                + (bumped ? $" The bump commit is left on {plan.Branch}, unpushed." : string.Empty),
                ExitCode.GeneralFailure);
        }

        var commit = (await GitAsync(repository, ["rev-parse", tag + "^{commit}"], ct).ConfigureAwait(false)).Output;

        if (bumped)
        {
            var pushedBranch = await GitAsync(repository, ["push", "origin", plan.Branch], ct).ConfigureAwait(false);

            if (pushedBranch.Code != 0)
            {
                return OperationResult<ReleaseOutcome>.Fail(
                    $"{tag} is made here but {plan.Branch} could not be pushed, so the tag was not pushed either: {pushedBranch.Error}",
                    ExitCode.GeneralFailure);
            }
        }

        var pushedTag = await GitAsync(repository, ["push", "origin", tag], ct).ConfigureAwait(false);

        return pushedTag.Code != 0
            ? OperationResult<ReleaseOutcome>.Fail($"{tag} is made here but could not be pushed: {pushedTag.Error}", ExitCode.GeneralFailure)
            : OperationResult<ReleaseOutcome>.Ok(new ReleaseOutcome(tag, commit, bumped, string.Empty));
    }

    private async Task<OperationResult<ReleaseOutcome>> DispatchAsync(ReleasePlan plan, string version, string tag, CancellationToken ct)
    {
        var host = await _hosts.ForAsync(plan.Project, plan.Repository, ct).ConfigureAwait(false);

        if (host.Failed || host.Value!.Host.Provider != RepositoryHosts.GitHub)
        {
            return OperationResult<ReleaseOutcome>.Fail(
                "The workflow recipe starts GitHub Actions workflows only; this project does not land through GitHub.",
                ExitCode.InvalidArguments);
        }

        if (await host.Value.Host.UnavailableAsync(plan.Repository, ct).ConfigureAwait(false) is { } cannot)
        {
            return OperationResult<ReleaseOutcome>.Fail(cannot, ExitCode.AgentUnavailable);
        }

        var manifest = await _workspace.ReadProjectAsync(plan.Project, ct).ConfigureAwait(false);
        var workflow = manifest.Succeeded ? manifest.Value!.Delivery.Release.Workflow.Trim() : string.Empty;

        if (_resolver.Resolve("gh") is not { } gh)
        {
            return OperationResult<ReleaseOutcome>.Fail("the GitHub CLI (gh) is not on PATH", ExitCode.AgentUnavailable);
        }

        var ran = await _processes.RunAsync(
            new ProcessRequest(gh, DispatchArguments(workflow, plan.Branch, version), plan.Repository), Timeout, ct).ConfigureAwait(false);

        return ran.Failed || ran.Value?.Succeeded != true
            ? OperationResult<ReleaseOutcome>.Fail(
                "gh could not start the workflow: " + (ran.Value?.StandardError.Trim() is { Length: > 0 } said ? said : ran.Error),
                ExitCode.GeneralFailure)
            : OperationResult<ReleaseOutcome>.Ok(new ReleaseOutcome(tag, plan.Head, false, ran.Value.StandardOutput.Trim()));
    }

    /// <summary>The arguments gh is given to start a release workflow.</summary>
    internal static IReadOnlyList<string> DispatchArguments(string workflow, string branch, string version) =>
        ["workflow", "run", workflow, "--ref", branch, "-f", $"version={version}"];

    private async Task<string> FindVersionFileAsync(string repository, string head, CancellationToken ct)
    {
        foreach (var candidate in new[] { "Directory.Build.props", "package.json" })
        {
            if ((await GitAsync(repository, ["cat-file", "-e", $"{head}:{candidate}"], ct).ConfigureAwait(false)).Code == 0)
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private async Task<(int Code, string Output, string Error)> GitAsync(string directory, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        if (_resolver.Resolve("git") is not { } git)
        {
            return (-1, string.Empty, "git is not on PATH");
        }

        var ran = await _processes.RunAsync(new ProcessRequest(git, arguments, directory), Timeout, ct).ConfigureAwait(false);

        return ran.Failed
            ? (-1, string.Empty, ran.Error ?? string.Empty)
            : (ran.Value!.ExitCode, ran.Value.StandardOutput.Trim(), ran.Value.StandardError.Trim());
    }
}
