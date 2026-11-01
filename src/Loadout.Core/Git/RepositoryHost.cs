using System.Globalization;
using System.Text.Json;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Git;

/// <summary>A pull request to open.</summary>
/// <param name="Head">The branch it is opened from, already pushed.</param>
/// <param name="Title">Its title.</param>
/// <param name="Body">Its description.</param>
/// <param name="Draft">Whether it opens as a draft rather than ready for review.</param>
public sealed record PullRequestOpening(string Head, string Title, string Body, bool Draft);

/// <summary>Where a pull request stands on its host.</summary>
public enum PullRequestState
{
    /// <summary>Still open, as a draft or ready.</summary>
    Open,

    /// <summary>Merged.</summary>
    Merged,

    /// <summary>Closed without being merged.</summary>
    Closed,
}

/// <summary>
/// Where a repository's pull requests live, and the changes Loadout may make there.
/// </summary>
/// <remarks>
/// <para>
/// One of these for each kind of host: GitHub through gh, GitLab through glab,
/// Azure DevOps through az, a command of the person's own, and local git,
/// which has no pull requests at all. Each drives the host's own CLI, so the
/// person's sign-in is the one used and no token passes through Loadout.
/// </para>
/// <para>
/// A pull request is named by its address throughout, as the host gave it
/// when it was opened, so nothing has to remember which host made it.
/// </para>
/// </remarks>
public interface IRepositoryHost
{
    /// <summary>The provider's word, as <c>delivery.provider</c> takes it.</summary>
    string Provider { get; }

    /// <summary>
    /// Whether this host has pull requests at all. Local git does not: a
    /// branch there is squash-merged by <see cref="LocalSquash"/> instead.
    /// </summary>
    bool OpensPullRequests { get; }

    /// <summary>
    /// Why nothing can be opened from this checkout, or null when it can -
    /// asked before a branch is pushed, so a branch never reaches the remote
    /// for a pull request that was never going to open. A host's CLI that is
    /// missing or signed out is the usual reason.
    /// </summary>
    /// <param name="repository">A checkout of the repository, where the host's CLI is run.</param>
    /// <param name="ct">Cancels the call.</param>
    Task<string?> UnavailableAsync(string repository, CancellationToken ct = default);

    /// <summary>Opens a pull request, returning its address.</summary>
    Task<OperationResult<string>> OpenAsync(string repository, PullRequestOpening opening, CancellationToken ct = default);

    /// <summary>Marks a draft pull request ready for review.</summary>
    Task<OperationResult> MarkReadyAsync(string repository, string pullRequest, CancellationToken ct = default);

    /// <summary>
    /// Asks the host to squash-merge a pull request with this message once its
    /// required checks pass, returning what the host said.
    /// </summary>
    /// <remarks>
    /// The host does the merging, so this returns before anything has merged,
    /// and a pull request with nothing left to wait for may be merged at once.
    /// </remarks>
    Task<OperationResult<string>> MergeWhenReadyAsync(
        string repository,
        string pullRequest,
        string subject,
        string body,
        CancellationToken ct = default);

    /// <summary>Closes a pull request without merging it.</summary>
    Task<OperationResult> CloseAsync(string repository, string pullRequest, CancellationToken ct = default);

    /// <summary>Where a pull request stands now.</summary>
    Task<OperationResult<PullRequestState>> StateAsync(string repository, string pullRequest, CancellationToken ct = default);
}

/// <summary>
/// What every host driven through a command line shares: finding the
/// command, running it in the checkout, and saying what it said when it refused.
/// </summary>
internal abstract class CliRepositoryHost : IRepositoryHost
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;

    protected CliRepositoryHost(IProcessLauncher processes, IExecutableResolver resolver)
    {
        _processes = processes;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public abstract string Provider { get; }

    /// <inheritdoc />
    public bool OpensPullRequests => true;

    /// <summary>The command run, as it is looked for on PATH.</summary>
    protected abstract string Executable { get; }

    /// <summary>The host's name in a sentence, such as "the GitHub CLI".</summary>
    protected abstract string Named { get; }

    /// <summary>The arguments that succeed only when the command is signed in.</summary>
    protected abstract IReadOnlyList<string> SignedInArguments { get; }

    /// <summary>How to sign in, said when it is not.</summary>
    protected abstract string SignIn { get; }

    /// <summary>The arguments that open <paramref name="opening"/>.</summary>
    protected abstract IReadOnlyList<string> OpenArguments(PullRequestOpening opening);

    /// <summary>The pull request's address from what the open printed.</summary>
    protected virtual string OpenedAddress(string printed) => printed.Split('\n').LastOrDefault()?.Trim() ?? string.Empty;

    /// <summary>The arguments that mark <paramref name="pullRequest"/> ready.</summary>
    protected abstract IReadOnlyList<string> ReadyArguments(string pullRequest);

    /// <summary>The arguments that ask for <paramref name="pullRequest"/> to be squash-merged when its checks pass.</summary>
    protected abstract IReadOnlyList<string> MergeArguments(string pullRequest, string subject, string body);

    /// <summary>The arguments that close <paramref name="pullRequest"/> unmerged.</summary>
    protected abstract IReadOnlyList<string> CloseArguments(string pullRequest);

    /// <summary>The arguments that print where <paramref name="pullRequest"/> stands.</summary>
    protected abstract IReadOnlyList<string> StateArguments(string pullRequest);

    /// <summary>Where a pull request stands, read from what the state arguments printed.</summary>
    protected abstract PullRequestState? ReadState(string printed);

    /// <summary>The number at the end of a pull request's address, as a string, or the address when there is none.</summary>
    protected static string Id(string pullRequest) =>
        Delivery.DeliveryService.Number(pullRequest) is { } number
            ? number.ToString(CultureInfo.InvariantCulture)
            : pullRequest;

    /// <summary>A squash commit's message as one piece: the subject, and the body after a blank line when there is one.</summary>
    protected static string Message(string subject, string body) =>
        body.Trim().Length == 0 ? subject : $"{subject}\n\n{body}";

    /// <summary>A JSON string property, or null when the text is not JSON or has no such property.</summary>
    protected static string? Property(string printed, params string[] path)
    {
        try
        {
            using var document = JsonDocument.Parse(printed);
            var element = document.RootElement;

            foreach (var step in path)
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(step, out element))
                {
                    return null;
                }
            }

            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Exposed for the tests that pin each host's exact arguments.
    internal IReadOnlyList<string> Opens(PullRequestOpening opening) => OpenArguments(opening);

    internal IReadOnlyList<string> Readies(string pullRequest) => ReadyArguments(pullRequest);

    internal IReadOnlyList<string> Merges(string pullRequest, string subject, string body) => MergeArguments(pullRequest, subject, body);

    internal IReadOnlyList<string> Closes(string pullRequest) => CloseArguments(pullRequest);

    internal IReadOnlyList<string> States(string pullRequest) => StateArguments(pullRequest);

    internal IReadOnlyList<string> SignsIn() => SignedInArguments;

    internal PullRequestState? Reads(string printed) => ReadState(printed);

    /// <inheritdoc />
    /// <remarks>
    /// What the command prints is never passed on: a sign-in check names the
    /// account and the start of its token, and only whether it succeeded is needed.
    /// </remarks>
    public async Task<string?> UnavailableAsync(string repository, CancellationToken ct = default)
    {
        if (_resolver.Resolve(Executable) is not { } command)
        {
            return $"{Named} ({Executable}) is not on PATH";
        }

        var ran = await _processes.RunAsync(new ProcessRequest(command, SignedInArguments, repository), Timeout, ct)
            .ConfigureAwait(false);

        return ran.Failed || ran.Value?.Succeeded != true
            ? $"{Named} is not signed in. Sign in with: {SignIn}"
            : null;
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> OpenAsync(string repository, PullRequestOpening opening, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opening);

        var ran = await RunAsync(repository, OpenArguments(opening), "open it", ct).ConfigureAwait(false);

        if (ran.Failed)
        {
            return ran;
        }

        return OpenedAddress(ran.Value!) is { Length: > 0 } address
            ? OperationResult<string>.Ok(address)
            : OperationResult<string>.Fail(
                $"{Executable} said it opened it but gave no address Loadout could read.", ExitCode.GeneralFailure);
    }

    /// <inheritdoc />
    public async Task<OperationResult> MarkReadyAsync(string repository, string pullRequest, CancellationToken ct = default) =>
        Plain(await RunAsync(repository, ReadyArguments(pullRequest), "mark it ready", ct).ConfigureAwait(false));

    /// <inheritdoc />
    public Task<OperationResult<string>> MergeWhenReadyAsync(
        string repository,
        string pullRequest,
        string subject,
        string body,
        CancellationToken ct = default) =>
        RunAsync(repository, MergeArguments(pullRequest, subject, body), "merge it", ct);

    /// <inheritdoc />
    public async Task<OperationResult> CloseAsync(string repository, string pullRequest, CancellationToken ct = default) =>
        Plain(await RunAsync(repository, CloseArguments(pullRequest), "close it", ct).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task<OperationResult<PullRequestState>> StateAsync(string repository, string pullRequest, CancellationToken ct = default)
    {
        var ran = await RunAsync(repository, StateArguments(pullRequest), "read it", ct).ConfigureAwait(false);

        if (ran.Failed)
        {
            return OperationResult<PullRequestState>.Fail(ran.Error!, ran.ExitCode);
        }

        return ReadState(ran.Value!) is { } state
            ? OperationResult<PullRequestState>.Ok(state)
            : OperationResult<PullRequestState>.Fail(
                $"{Executable} answered in a shape Loadout does not read.", ExitCode.GeneralFailure);
    }

    private static OperationResult Plain(OperationResult<string> ran) =>
        ran.Failed ? OperationResult.Fail(ran.Error!, ran.ExitCode) : OperationResult.Ok();

    /// <summary>Runs the command, returning what it printed, or what it said when it refused.</summary>
    private async Task<OperationResult<string>> RunAsync(
        string repository,
        IReadOnlyList<string> arguments,
        string doing,
        CancellationToken ct)
    {
        if (_resolver.Resolve(Executable) is not { } command)
        {
            return OperationResult<string>.Fail($"{Named} ({Executable}) is not on PATH", ExitCode.AgentUnavailable);
        }

        var ran = await _processes.RunAsync(new ProcessRequest(command, arguments, repository), Timeout, ct)
            .ConfigureAwait(false);

        if (ran.Failed || ran.Value?.Succeeded != true)
        {
            return OperationResult<string>.Fail(
                $"{Executable} could not {doing}: " + (ran.Value?.StandardError.Trim() is { Length: > 0 } said ? said : ran.Error),
                ExitCode.GeneralFailure);
        }

        return OperationResult<string>.Ok(ran.Value.StandardOutput.Trim());
    }
}

/// <summary>Pull requests on GitHub, through the GitHub CLI.</summary>
internal sealed class GhRepositoryHost : CliRepositoryHost
{
    public GhRepositoryHost(IProcessLauncher processes, IExecutableResolver resolver)
        : base(processes, resolver)
    {
    }

    /// <inheritdoc />
    public override string Provider => RepositoryHosts.GitHub;

    /// <inheritdoc />
    protected override string Executable => "gh";

    /// <inheritdoc />
    protected override string Named => "the GitHub CLI";

    /// <inheritdoc />
    /// <remarks><c>--active</c>, so an account signed out on some other host does not count against this one.</remarks>
    protected override IReadOnlyList<string> SignedInArguments => ["auth", "status", "--active"];

    /// <inheritdoc />
    protected override string SignIn => "gh auth login";

    /// <inheritdoc />
    protected override IReadOnlyList<string> OpenArguments(PullRequestOpening opening) =>
    [
        "pr", "create",
        .. opening.Draft ? new[] { "--draft" } : [],
        "--head", opening.Head,
        "--title", opening.Title,
        "--body", opening.Body,
    ];

    /// <inheritdoc />
    protected override IReadOnlyList<string> ReadyArguments(string pullRequest) => ["pr", "ready", pullRequest];

    /// <inheritdoc />
    /// <remarks>
    /// The body is always passed, empty or not: left out, gh fills the squash
    /// commit's body with the branch's commit messages, which is not the
    /// message the person wrote.
    /// </remarks>
    protected override IReadOnlyList<string> MergeArguments(string pullRequest, string subject, string body) =>
        ["pr", "merge", pullRequest, "--squash", "--auto", "--subject", subject, "--body", body];

    /// <inheritdoc />
    protected override IReadOnlyList<string> CloseArguments(string pullRequest) => ["pr", "close", pullRequest];

    /// <inheritdoc />
    protected override IReadOnlyList<string> StateArguments(string pullRequest) => ["pr", "view", pullRequest, "--json", "state"];

    /// <inheritdoc />
    protected override PullRequestState? ReadState(string printed) => Property(printed, "state")?.ToUpperInvariant() switch
    {
        "OPEN" => PullRequestState.Open,
        "MERGED" => PullRequestState.Merged,
        "CLOSED" => PullRequestState.Closed,
        _ => null,
    };
}

/// <summary>Merge requests on GitLab, through the GitLab CLI.</summary>
/// <remarks>
/// glab names a merge request by its number, so the number at the end of the
/// address is what it is given.
/// </remarks>
internal sealed class GlabRepositoryHost : CliRepositoryHost
{
    public GlabRepositoryHost(IProcessLauncher processes, IExecutableResolver resolver)
        : base(processes, resolver)
    {
    }

    /// <inheritdoc />
    public override string Provider => RepositoryHosts.GitLab;

    /// <inheritdoc />
    protected override string Executable => "glab";

    /// <inheritdoc />
    protected override string Named => "the GitLab CLI";

    /// <inheritdoc />
    protected override IReadOnlyList<string> SignedInArguments => ["auth", "status"];

    /// <inheritdoc />
    protected override string SignIn => "glab auth login";

    /// <inheritdoc />
    protected override IReadOnlyList<string> OpenArguments(PullRequestOpening opening) =>
    [
        "mr", "create",
        "--source-branch", opening.Head,
        "--title", opening.Title,
        "--description", opening.Body,
        .. opening.Draft ? new[] { "--draft" } : [],
        "--yes",
    ];

    /// <inheritdoc />
    protected override IReadOnlyList<string> ReadyArguments(string pullRequest) => ["mr", "update", Id(pullRequest), "--ready"];

    /// <inheritdoc />
    /// <remarks>The message is passed whole, so GitLab does not write one from the branch's commits.</remarks>
    protected override IReadOnlyList<string> MergeArguments(string pullRequest, string subject, string body) =>
        ["mr", "merge", Id(pullRequest), "--squash", "--auto-merge", "--squash-message", Message(subject, body), "--yes"];

    /// <inheritdoc />
    protected override IReadOnlyList<string> CloseArguments(string pullRequest) => ["mr", "close", Id(pullRequest)];

    /// <inheritdoc />
    protected override IReadOnlyList<string> StateArguments(string pullRequest) => ["mr", "view", Id(pullRequest), "--output", "json"];

    /// <inheritdoc />
    protected override PullRequestState? ReadState(string printed) => Property(printed, "state")?.ToLowerInvariant() switch
    {
        "opened" or "locked" => PullRequestState.Open,
        "merged" => PullRequestState.Merged,
        "closed" => PullRequestState.Closed,
        _ => null,
    };
}

/// <summary>Pull requests on Azure DevOps, through the Azure CLI's repos commands.</summary>
/// <remarks>
/// az works out the organisation, project and repository from the checkout's
/// remote. It answers in JSON, and the pull request's address is made from the
/// repository's web address and its number, since az prints no address itself.
/// Closing unmerged is what Azure DevOps calls abandoning.
/// </remarks>
internal sealed class AzureRepositoryHost : CliRepositoryHost
{
    public AzureRepositoryHost(IProcessLauncher processes, IExecutableResolver resolver)
        : base(processes, resolver)
    {
    }

    /// <inheritdoc />
    public override string Provider => RepositoryHosts.AzureDevOps;

    /// <inheritdoc />
    protected override string Executable => "az";

    /// <inheritdoc />
    protected override string Named => "the Azure CLI";

    /// <inheritdoc />
    protected override IReadOnlyList<string> SignedInArguments => ["account", "show", "--output", "none"];

    /// <inheritdoc />
    protected override string SignIn => "az login";

    /// <inheritdoc />
    protected override IReadOnlyList<string> OpenArguments(PullRequestOpening opening) =>
    [
        "repos", "pr", "create",
        "--source-branch", opening.Head,
        "--title", opening.Title,
        "--description", opening.Body,
        "--draft", opening.Draft ? "true" : "false",
        "--output", "json",
    ];

    /// <inheritdoc />
    protected override string OpenedAddress(string printed) =>
        (Property(printed, "repository", "webUrl"), Property(printed, "pullRequestId")) is ({ Length: > 0 } web, { Length: > 0 } id)
            ? $"{web.TrimEnd('/')}/pullrequest/{id}"
            : string.Empty;

    /// <inheritdoc />
    protected override IReadOnlyList<string> ReadyArguments(string pullRequest) =>
        ["repos", "pr", "update", "--id", Id(pullRequest), "--draft", "false", "--output", "none"];

    /// <inheritdoc />
    /// <remarks>
    /// Auto-complete is Azure DevOps's way of merging once the policies pass;
    /// the source branch is kept, as a merge on the other hosts keeps it.
    /// </remarks>
    protected override IReadOnlyList<string> MergeArguments(string pullRequest, string subject, string body) =>
    [
        "repos", "pr", "update", "--id", Id(pullRequest),
        "--auto-complete", "true",
        "--squash", "true",
        "--delete-source-branch", "false",
        "--merge-commit-message", Message(subject, body),
        "--output", "none",
    ];

    /// <inheritdoc />
    protected override IReadOnlyList<string> CloseArguments(string pullRequest) =>
        ["repos", "pr", "update", "--id", Id(pullRequest), "--status", "abandoned", "--output", "none"];

    /// <inheritdoc />
    protected override IReadOnlyList<string> StateArguments(string pullRequest) =>
        ["repos", "pr", "show", "--id", Id(pullRequest), "--output", "json"];

    /// <inheritdoc />
    protected override PullRequestState? ReadState(string printed) => Property(printed, "status")?.ToLowerInvariant() switch
    {
        "active" => PullRequestState.Open,
        "completed" => PullRequestState.Merged,
        "abandoned" => PullRequestState.Closed,
        _ => null,
    };
}

/// <summary>
/// Pull requests on a host Loadout does not know, through a command of the
/// person's own that answers a small set of verbs.
/// </summary>
/// <remarks>
/// <para>
/// The command is run in the checkout as <c>&lt;command&gt; &lt;verb&gt; ...</c>:
/// </para>
/// <list type="bullet">
/// <item><c>check</c> succeeds when the command can reach its host.</item>
/// <item><c>open --head B --title T --body X [--draft]</c> prints the new pull request's address as its last line.</item>
/// <item><c>ready &lt;address&gt;</c>, <c>close &lt;address&gt;</c>.</item>
/// <item><c>merge &lt;address&gt; --subject S --body X</c> asks for a squash-merge once its checks pass.</item>
/// <item><c>state &lt;address&gt;</c> prints <c>open</c>, <c>merged</c> or <c>closed</c>.</item>
/// </list>
/// <para>
/// A non-zero exit is a refusal, and what it printed to standard error is the reason given.
/// </para>
/// </remarks>
internal sealed class CommandRepositoryHost : CliRepositoryHost
{
    private readonly string _command;

    public CommandRepositoryHost(IProcessLauncher processes, IExecutableResolver resolver, string command)
        : base(processes, resolver)
    {
        _command = command;
    }

    /// <inheritdoc />
    public override string Provider => RepositoryHosts.Command;

    /// <inheritdoc />
    protected override string Executable => _command;

    /// <inheritdoc />
    protected override string Named => "the project's delivery command";

    /// <inheritdoc />
    protected override IReadOnlyList<string> SignedInArguments => ["check"];

    /// <inheritdoc />
    protected override string SignIn => $"whatever {_command} needs; '{_command} check' refused";

    /// <inheritdoc />
    protected override IReadOnlyList<string> OpenArguments(PullRequestOpening opening) =>
    [
        "open", "--head", opening.Head, "--title", opening.Title, "--body", opening.Body,
        .. opening.Draft ? new[] { "--draft" } : [],
    ];

    /// <inheritdoc />
    protected override IReadOnlyList<string> ReadyArguments(string pullRequest) => ["ready", pullRequest];

    /// <inheritdoc />
    protected override IReadOnlyList<string> MergeArguments(string pullRequest, string subject, string body) =>
        ["merge", pullRequest, "--subject", subject, "--body", body];

    /// <inheritdoc />
    protected override IReadOnlyList<string> CloseArguments(string pullRequest) => ["close", pullRequest];

    /// <inheritdoc />
    protected override IReadOnlyList<string> StateArguments(string pullRequest) => ["state", pullRequest];

    /// <inheritdoc />
    protected override PullRequestState? ReadState(string printed) =>
        printed.Trim().Split('\n').LastOrDefault()?.Trim().ToLowerInvariant() switch
        {
            "open" => PullRequestState.Open,
            "merged" => PullRequestState.Merged,
            "closed" => PullRequestState.Closed,
            _ => null,
        };
}

/// <summary>
/// A repository with no host: nothing to push to and no pull requests, so a
/// waiting branch is squash-merged into its target on this machine instead,
/// by <see cref="LocalSquash"/>, and only after the person confirms.
/// </summary>
internal sealed class LocalRepositoryHost : IRepositoryHost
{
    private const string None =
        "this project lands branches with local git, which has no pull requests. Merge it here with: loadout pr merge <branch> --subject ...";

    /// <inheritdoc />
    public string Provider => RepositoryHosts.Local;

    /// <inheritdoc />
    public bool OpensPullRequests => false;

    /// <inheritdoc />
    public Task<string?> UnavailableAsync(string repository, CancellationToken ct = default) => Task.FromResult<string?>(None);

    /// <inheritdoc />
    public Task<OperationResult<string>> OpenAsync(string repository, PullRequestOpening opening, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<string>.Fail(None, ExitCode.InvalidArguments));

    /// <inheritdoc />
    public Task<OperationResult> MarkReadyAsync(string repository, string pullRequest, CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Fail(None, ExitCode.InvalidArguments));

    /// <inheritdoc />
    public Task<OperationResult<string>> MergeWhenReadyAsync(
        string repository,
        string pullRequest,
        string subject,
        string body,
        CancellationToken ct = default) =>
        Task.FromResult(OperationResult<string>.Fail(None, ExitCode.InvalidArguments));

    /// <inheritdoc />
    public Task<OperationResult> CloseAsync(string repository, string pullRequest, CancellationToken ct = default) =>
        Task.FromResult(OperationResult.Fail(None, ExitCode.InvalidArguments));

    /// <inheritdoc />
    public Task<OperationResult<PullRequestState>> StateAsync(string repository, string pullRequest, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<PullRequestState>.Fail(None, ExitCode.InvalidArguments));
}
