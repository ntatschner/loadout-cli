using System.Globalization;
using System.Text.Json;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Git;

/// <summary>A pull request's title and description, as they read now.</summary>
/// <param name="Number">Its number.</param>
/// <param name="Title">Its title.</param>
/// <param name="Body">Its description, which may be empty.</param>
public sealed record PullRequestText(int Number, string Title, string Body)
{
    /// <summary>
    /// The title and description as one piece of writing, the way a squash
    /// merge's message is a subject and a body.
    /// </summary>
    public string Text => Body.Trim().Length == 0 ? Title : $"{Title}\n\n{Body}";
}

/// <summary>Reads pull requests. Never writes to one.</summary>
public interface IPullRequestReader
{
    /// <summary>One pull request by number.</summary>
    Task<OperationResult<PullRequestText>> ReadAsync(string repository, int number, CancellationToken ct = default);

    /// <summary>The most recently merged pull requests, newest first.</summary>
    Task<OperationResult<IReadOnlyList<PullRequestText>>> MergedAsync(string repository, int limit, CancellationToken ct = default);
}

/// <summary>Reads pull requests through the GitHub CLI.</summary>
/// <remarks>
/// Only <c>gh pr view</c> and <c>gh pr list</c>, both of which read. Asking gh
/// rather than the API directly means its sign-in, its host and its handling
/// of forks are the person's own, with no token of theirs passing through
/// Loadout.
/// </remarks>
internal sealed class GhPullRequestReader : IPullRequestReader
{
    private const string Fields = "number,title,body";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;

    public GhPullRequestReader(IProcessLauncher processes, IExecutableResolver resolver)
    {
        _processes = processes;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public async Task<OperationResult<PullRequestText>> ReadAsync(string repository, int number, CancellationToken ct = default)
    {
        var ran = await RunAsync(
            repository, ["pr", "view", number.ToString(CultureInfo.InvariantCulture), "--json", Fields], ct).ConfigureAwait(false);

        if (ran.Failed)
        {
            return OperationResult<PullRequestText>.Fail(ran.Error!, ran.ExitCode);
        }

        try
        {
            using var document = JsonDocument.Parse(ran.Value!);

            return OperationResult<PullRequestText>.Ok(Parse(document.RootElement));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return OperationResult<PullRequestText>.Fail(
                $"gh answered in a shape Loadout does not read: {e.Message}", ExitCode.GeneralFailure);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<PullRequestText>>> MergedAsync(string repository, int limit, CancellationToken ct = default)
    {
        var ran = await RunAsync(
            repository,
            ["pr", "list", "--state", "merged", "--limit", limit.ToString(CultureInfo.InvariantCulture), "--json", Fields],
            ct).ConfigureAwait(false);

        if (ran.Failed)
        {
            return OperationResult<IReadOnlyList<PullRequestText>>.Fail(ran.Error!, ran.ExitCode);
        }

        try
        {
            using var document = JsonDocument.Parse(ran.Value!);

            return OperationResult<IReadOnlyList<PullRequestText>>.Ok(
                document.RootElement.EnumerateArray().Select(Parse).ToList());
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return OperationResult<IReadOnlyList<PullRequestText>>.Fail(
                $"gh answered in a shape Loadout does not read: {e.Message}", ExitCode.GeneralFailure);
        }
    }

    private async Task<OperationResult<string>> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        if (_resolver.Resolve("gh") is not { } gh)
        {
            return OperationResult<string>.Fail(
                "The GitHub CLI is not on PATH, so pull requests cannot be read. Install gh and sign in with: gh auth login",
                ExitCode.AgentUnavailable);
        }

        var ran = await _processes.RunAsync(new ProcessRequest(gh, arguments, repository), Timeout, ct).ConfigureAwait(false);

        if (ran.Failed)
        {
            return OperationResult<string>.Fail(ran.Error!, ran.ExitCode);
        }

        if (!ran.Value!.Succeeded)
        {
            var said = ran.Value.StandardError.Trim();

            return OperationResult<string>.Fail(
                "gh could not read the pull request" + (said.Length > 0 ? $": {said}" : "."),
                ExitCode.GeneralFailure);
        }

        return OperationResult<string>.Ok(ran.Value.StandardOutput);
    }

    private static PullRequestText Parse(JsonElement element) => new(
        element.GetProperty("number").GetInt32(),
        element.GetProperty("title").GetString() ?? string.Empty,
        element.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty);
}
