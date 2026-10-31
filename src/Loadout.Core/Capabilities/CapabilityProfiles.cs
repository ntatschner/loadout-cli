using System.Text.Json;
using Loadout.Core.Configuration;
using Loadout.Core.Triggers;
using Loadout.Core.Workspace;
using Loadout.Models.Capabilities;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;

namespace Loadout.Core.Capabilities;

/// <summary>Builds a project's capability profile, and keeps the last one built for showing.</summary>
public interface ICapabilityProfiles
{
    /// <summary>The profile last kept for a project, or null when none has been.</summary>
    Task<CapabilityProfile?> CachedAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Builds a profile from the repository's files and, when
    /// <paramref name="askGitHub"/>, from GitHub. Nothing is kept: that is
    /// <see cref="KeepAsync"/>.
    /// </summary>
    Task<CapabilityProfile> BuildAsync(ProjectResolution project, bool askGitHub, CancellationToken ct = default);

    /// <summary>Keeps a profile as the one shown until the next is built.</summary>
    Task<OperationResult> KeepAsync(CapabilityProfile profile, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// GitHub is asked only when the caller says so, because a profile is shown
/// far more often than it decides anything, and only a decision is worth the
/// network and the quota. Each call has a short limit; one that runs out is
/// unknown, which holds nothing, so a slow network can never block work.
/// </para>
/// <para>
/// A token comes from <c>gh auth token</c> where the GitHub CLI is signed in,
/// and otherwise from <c>GITHUB_TOKEN</c>. It is never written anywhere: the
/// profile keeps only which of the two was used.
/// </para>
/// </remarks>
public sealed class CapabilityProfiles : ICapabilityProfiles
{
    /// <summary>How long one GitHub call may take before its check is unknown.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>What <see cref="CapabilityProfile.GitHub"/> says when GitHub was not asked.</summary>
    public const string NotAsked = "not asked";

    /// <summary>What it says when GitHub was to be asked and there was no token.</summary>
    public const string NoToken = "no token";

    /// <summary>What it says when the project's remote is not on github.com.</summary>
    public const string NotOnGitHub = "not on github.com";

    /// <summary>The kind of credential read from the environment.</summary>
    public const string EnvironmentToken = "GITHUB_TOKEN";

    private readonly IWorkspaceManager _workspace;
    private readonly YamlStore _yaml;
    private readonly IGitHubReads _reads;
    private readonly Func<CancellationToken, Task<TriggerCredential>> _credential;
    private readonly TimeProvider _time;

    public CapabilityProfiles(
        IWorkspaceManager workspace,
        YamlStore yaml,
        HttpClient client,
        IExecutableResolver resolver,
        IProcessLauncher processes,
        IEnvironmentProvider environment,
        TimeProvider time)
        : this(workspace, yaml, new GitHubApi(client), ct => CredentialAsync(resolver, processes, environment, ct), time)
    {
    }

    /// <summary>For tests: GitHub's reads and the token are given rather than found.</summary>
    internal CapabilityProfiles(
        IWorkspaceManager workspace,
        YamlStore yaml,
        IGitHubReads reads,
        Func<CancellationToken, Task<TriggerCredential>> credential,
        TimeProvider time)
    {
        _workspace = workspace;
        _yaml = yaml;
        _reads = reads;
        _credential = credential;
        _time = time;
    }

    /// <summary>Where a project's profile is kept.</summary>
    public static string PathFor(string workspace, string slug) => Path.Combine(workspace, "projects", slug, "capabilities.yaml");

    /// <inheritdoc />
    public async Task<CapabilityProfile?> CachedAsync(string slug, CancellationToken ct = default)
    {
        var path = PathFor(_workspace.LocalPath, slug);

        if (!File.Exists(path))
        {
            return null;
        }

        var loaded = await _yaml.LoadAsync(path, () => new CapabilityProfile(), ct).ConfigureAwait(false);

        return loaded.Succeeded ? loaded.Value : null;
    }

    /// <inheritdoc />
    public Task<OperationResult> KeepAsync(CapabilityProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return _yaml.SaveAsync(PathFor(_workspace.LocalPath, profile.Project), profile, true, ct);
    }

    /// <inheritdoc />
    public async Task<CapabilityProfile> BuildAsync(ProjectResolution project, bool askGitHub, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var local = project.LocalPath is { } path && Directory.Exists(path) ? path : null;
        var repo = GitHubRepo.From(project.Entry.Remote);
        var (workflows, unreadable) = local is null ? ([], []) : LocalProbes.Workflows(local);
        var stacks = local is null ? [] : LocalProbes.Stacks(local);

        var profile = new CapabilityProfile
        {
            Project = project.Entry.Slug,
            BuiltUtc = _time.GetUtcNow(),
            Stacks = [.. stacks],
        };

        GitHubFacts github;

        if (!askGitHub)
        {
            profile.GitHub = NotAsked;
            github = GitHubFacts.All(Unknown("GitHub was not asked; --refresh asks it."));
        }
        else if (repo is null)
        {
            profile.GitHub = NotOnGitHub;
            github = GitHubFacts.All(Unknown("The project's remote is not on github.com, so GitHub cannot say."));
        }
        else if (await _credential(ct).ConfigureAwait(false) is not { Token: { Length: > 0 } } credential)
        {
            profile.GitHub = NoToken;
            github = GitHubFacts.All(Unknown("No GitHub token: sign in with gh auth login, or set GITHUB_TOKEN."));
        }
        else
        {
            profile.GitHub = credential.Kind;
            github = await AskAsync(credential, repo, ct).ConfigureAwait(false);
        }

        profile.Checks =
        [
            Ci(local, workflows),
            Release(local, workflows, github),
            Named(CapabilityChecks.Environments, github.Environments),
            Named(CapabilityChecks.BranchProtection, github.Protection),
        ];

        foreach (var stack in LocalProbes.Unbuilt(stacks, workflows))
        {
            profile.Notes.Add($"No workflow appears to build the {stack} stack.");
        }

        foreach (var file in unreadable)
        {
            profile.Notes.Add($"{file} could not be read as a workflow, so it was left out.");
        }

        return profile;
    }

    /// <summary>The token: <c>gh</c>'s where it is signed in, else <c>GITHUB_TOKEN</c>, else none.</summary>
    public static async Task<TriggerCredential> CredentialAsync(
        IExecutableResolver resolver,
        IProcessLauncher processes,
        IEnvironmentProvider environment,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(environment);

        if (resolver.Resolve("gh") is { } gh)
        {
            var ran = await processes.RunAsync(
                new ProcessRequest(gh, ["auth", "token"], Invocation.CurrentDirectory),
                TimeSpan.FromSeconds(20),
                ct).ConfigureAwait(false);

            if (ran.Succeeded && ran.Value!.Succeeded && ran.Value.StandardOutput.Trim() is { Length: > 0 } token)
            {
                return TriggerCredential.Of(TriggerCredential.GhToken, token);
            }
        }

        return environment.GetVariable(EnvironmentToken) is { Length: > 0 } set
            ? TriggerCredential.Of(EnvironmentToken, set.Trim())
            : TriggerCredential.Missing;
    }

    private static CapabilityResult Ci(string? local, IReadOnlyList<WorkflowFile> workflows)
    {
        if (local is null)
        {
            return Result(CapabilityChecks.Ci, Unknown("The project is not on this machine, so its workflows could not be read."));
        }

        var ci = workflows.Where(workflow => workflow.Ci).ToList();

        if (ci.FirstOrDefault(workflow => !workflow.Delegated) is { } own)
        {
            return Result(CapabilityChecks.Ci, new(CapabilityState.Present, $"{own.File} runs on push or pull request."));
        }

        return ci.FirstOrDefault() is { } calling
            ? Result(CapabilityChecks.Ci, new(CapabilityState.Delegated, $"{calling.File} calls {calling.Calls[0]}."))
            : Result(CapabilityChecks.Ci, new(CapabilityState.Gap, "No workflow in .github/workflows runs on push or pull request."));
    }

    private static CapabilityResult Release(string? local, IReadOnlyList<WorkflowFile> workflows, GitHubFacts github)
    {
        if (local is null)
        {
            return Result(CapabilityChecks.Release, Unknown("The project is not on this machine, so its workflows could not be read."));
        }

        // A file named for releasing first, so the evidence names the workflow
        // that releases rather than one that runs after a release, such as a
        // package tap's.
        var releasing = workflows
            .Where(workflow => workflow.Release)
            .OrderByDescending(workflow => workflow.File.Contains("release", StringComparison.OrdinalIgnoreCase)
                || workflow.File.Contains("publish", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (releasing.FirstOrDefault(workflow => !workflow.Delegated) is { } own)
        {
            return Result(CapabilityChecks.Release, new(CapabilityState.Present, $"{own.File} releases."));
        }

        if (releasing.FirstOrDefault() is { } calling)
        {
            return Result(CapabilityChecks.Release, new(CapabilityState.Delegated, $"{calling.File} calls {calling.Calls[0]}."));
        }

        // No workflow, which is seen. Whether that is a gap or a project
        // released by hand is GitHub's to say, so without it this is unknown.
        return github.Releases switch
        {
            { State: CapabilityState.Present } found => Result(
                CapabilityChecks.Release,
                new(CapabilityState.ManualRelease, $"No release workflow, but {found.Evidence}")),
            { State: CapabilityState.Gap } => Result(
                CapabilityChecks.Release,
                new(CapabilityState.Gap, "No release workflow, and no release has been published.")),
            var other => Result(CapabilityChecks.Release, other with
            {
                Evidence = $"No release workflow. Whether releases are published is unknown: {other.Evidence}",
            }),
        };
    }

    private static CapabilityResult Named(string id, Finding finding) => Result(id, finding);

    private static CapabilityResult Result(string id, Finding finding) =>
        new() { Id = id, State = finding.State, Evidence = finding.Evidence, Scope = finding.Scope };

    private static Finding Unknown(string why) => new(CapabilityState.Unknown, why);

    /// <summary>What GitHub says, asked one call at a time, each within <see cref="Patience"/>.</summary>
    private async Task<GitHubFacts> AskAsync(TriggerCredential credential, string repo, CancellationToken ct)
    {
        var about = await GetAsync(credential, $"repos/{repo}", ct).ConfigureAwait(false);

        if (about.Status != 200)
        {
            return GitHubFacts.All(Refused(about, "Metadata: read", $"read {repo}"));
        }

        var branch = Field(about.Body, "default_branch") ?? "main";

        var environments = await GetAsync(credential, $"repos/{repo}/environments", ct).ConfigureAwait(false);
        var environmentsFound = environments.Status == 200
            ? Count(environments.Body, "total_count") is > 0 and var many
                ? new Finding(CapabilityState.Present, $"{many} environment(s) set up on GitHub.")
                : new Finding(CapabilityState.Gap, "No deployment environment is set up on GitHub.")
            : Refused(environments, "Environments: read", "list its environments");

        var rules = await GetAsync(credential, $"repos/{repo}/rules/branches/{Uri.EscapeDataString(branch)}", ct).ConfigureAwait(false);
        Finding protection;

        if (rules.Status == 200 && Length(rules.Body) is > 0 and var ruled)
        {
            protection = new Finding(CapabilityState.Present, $"{ruled} ruleset rule(s) apply to {branch}.");
        }
        else
        {
            var protecting = await GetAsync(credential, $"repos/{repo}/branches/{Uri.EscapeDataString(branch)}/protection", ct)
                .ConfigureAwait(false);

            protection = protecting.Status switch
            {
                200 => new Finding(CapabilityState.Present, $"{branch} has branch protection."),

                // Seen only because the repository itself was readable: GitHub
                // says not found for a branch with nothing protecting it.
                404 => new Finding(CapabilityState.Gap, $"{branch} has no branch protection and no ruleset."),
                _ => Refused(protecting, "Administration: read", $"read the protection on {branch}"),
            };
        }

        var releases = await GetAsync(credential, $"repos/{repo}/releases?per_page=1", ct).ConfigureAwait(false);
        var releasesFound = releases.Status == 200
            ? Length(releases.Body) is > 0
                ? new Finding(CapabilityState.Present, "releases have been published on GitHub.")
                : new Finding(CapabilityState.Gap, "No release has been published.")
            : Refused(releases, "Contents: read", "list its releases");

        return new GitHubFacts(environmentsFound, protection, releasesFound);
    }

    private async Task<GitHubReply> GetAsync(TriggerCredential credential, string path, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Patience);

        try
        {
            return await _reads.GetAsync(credential, path, null, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new GitHubReply(0, Problem: $"GitHub did not answer within {Patience.TotalSeconds:0} seconds");
        }
    }

    /// <summary>
    /// A refusal or a failure as a state: a token GitHub turned away or a
    /// network that failed is unknown, and a refusal of the request itself is
    /// no permission, naming what it would take.
    /// </summary>
    private static Finding Refused(GitHubReply reply, string scope, string doing) => reply.Status switch
    {
        0 => Unknown($"{reply.Problem ?? "GitHub could not be reached"}."),
        401 => Unknown("GitHub refused the token. Sign in again with gh auth login, or renew GITHUB_TOKEN."),
        403 or 404 => new Finding(CapabilityState.NoPermission, $"GitHub would not let the token {doing} ({reply.Status}).", scope),
        _ => Unknown($"GitHub answered {reply.Status} when asked to {doing}."),
    };

    private static string? Field(string? body, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(body ?? "{}");

            return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? Count(string? body, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(body ?? "{}");

            return document.RootElement.TryGetProperty(name, out var value) && value.TryGetInt32(out var count) ? count : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? Length(string? body)
    {
        try
        {
            using var document = JsonDocument.Parse(body ?? "[]");

            return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>One check's state, its evidence, and for a refusal the permission wanted.</summary>
    private sealed record Finding(CapabilityState State, string Evidence, string Scope = "");

    /// <summary>What GitHub said for each of the checks only it can answer.</summary>
    private sealed record GitHubFacts(Finding Environments, Finding Protection, Finding Releases)
    {
        public static GitHubFacts All(Finding one) => new(one, one, one);
    }
}
