using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Git;

/// <summary>The host a project's branches land through, and how that was decided.</summary>
/// <param name="Host">The host.</param>
/// <param name="From">Where the choice came from, in words: "the project's delivery.provider" or "origin's address".</param>
public sealed record HostChoice(IRepositoryHost Host, string From);

/// <summary>Picks the host each project's branches land through.</summary>
public interface IRepositoryHosts
{
    /// <summary>The host for a project, from its manifest or else its checkout's origin.</summary>
    /// <param name="project">The project's slug.</param>
    /// <param name="repository">The project's checkout on this machine.</param>
    /// <param name="ct">Cancels the call.</param>
    Task<OperationResult<HostChoice>> ForAsync(string project, string repository, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// A project's <c>delivery.provider</c> wins when it is set. Otherwise the
/// host is read from origin's address: GitHub, GitLab or Azure DevOps by
/// name, and local git when there is no origin at all. An origin that names
/// none of them is refused rather than guessed at, since guessing GitHub for
/// a self-hosted server would push a branch somewhere nobody meant.
/// </para>
/// <para>
/// A self-hosted GitLab whose address does not say "gitlab" is set by hand
/// with <c>delivery.provider: gitlab</c>.
/// </para>
/// </remarks>
public sealed class RepositoryHosts : IRepositoryHosts
{
    /// <summary>GitHub, through gh.</summary>
    public const string GitHub = "github";

    /// <summary>GitLab, through glab.</summary>
    public const string GitLab = "gitlab";

    /// <summary>Azure DevOps, through az repos.</summary>
    public const string AzureDevOps = "azure-devops";

    /// <summary>Local git: no pull requests, squash-merged on this machine after confirming.</summary>
    public const string Local = "local";

    /// <summary>A command of the project's own, named by <c>delivery.command</c>.</summary>
    public const string Command = "command";

    /// <summary>Every provider word, in the order they are offered.</summary>
    public static IReadOnlyList<string> Words { get; } = [GitHub, GitLab, AzureDevOps, Local, Command];

    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;
    private readonly IWorkspaceManager _workspace;
    private readonly IGitManager _git;

    public RepositoryHosts(
        IProcessLauncher processes,
        IExecutableResolver resolver,
        IWorkspaceManager workspace,
        IGitManager git)
    {
        _processes = processes;
        _resolver = resolver;
        _workspace = workspace;
        _git = git;
    }

    /// <summary>
    /// The provider an origin's address names, <see cref="Local"/> when there is
    /// no origin, or null when the address names no host Loadout knows.
    /// </summary>
    public static string? FromRemote(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote))
        {
            return Local;
        }

        var address = remote.Trim().ToLowerInvariant();

        if (address.Contains("github", StringComparison.Ordinal))
        {
            return GitHub;
        }

        if (address.Contains("gitlab", StringComparison.Ordinal))
        {
            return GitLab;
        }

        if (address.Contains("dev.azure.com", StringComparison.Ordinal)
            || address.Contains("visualstudio.com", StringComparison.Ordinal))
        {
            return AzureDevOps;
        }

        return null;
    }

    /// <summary>The refusal for a word that is not a provider.</summary>
    public static string Refusal(string word) =>
        $"'{word}' is not a delivery provider. Give one of: {string.Join(", ", Words)}.";

    /// <summary>The host a provider word names, or a refusal.</summary>
    public OperationResult<IRepositoryHost> Named(string provider, string? command)
    {
        return provider.Trim().ToLowerInvariant() switch
        {
            GitHub => OperationResult<IRepositoryHost>.Ok(new GhRepositoryHost(_processes, _resolver)),
            GitLab => OperationResult<IRepositoryHost>.Ok(new GlabRepositoryHost(_processes, _resolver)),
            AzureDevOps => OperationResult<IRepositoryHost>.Ok(new AzureRepositoryHost(_processes, _resolver)),
            Local => OperationResult<IRepositoryHost>.Ok(new LocalRepositoryHost()),
            Command when command?.Trim() is { Length: > 0 } named =>
                OperationResult<IRepositoryHost>.Ok(new CommandRepositoryHost(_processes, _resolver, named)),
            Command => OperationResult<IRepositoryHost>.Fail(
                "delivery.provider is command, but delivery.command names no command to run.", ExitCode.InvalidArguments),
            _ => OperationResult<IRepositoryHost>.Fail(Refusal(provider), ExitCode.InvalidArguments),
        };
    }

    /// <inheritdoc />
    public async Task<OperationResult<HostChoice>> ForAsync(string project, string repository, CancellationToken ct = default)
    {
        var manifest = await _workspace.ReadProjectAsync(project, ct).ConfigureAwait(false);
        var delivery = manifest.Succeeded ? manifest.Value!.Delivery : null;

        if (delivery?.Provider is { Length: > 0 } set)
        {
            var named = Named(set, delivery.Command);

            return named.Failed
                ? OperationResult<HostChoice>.Fail(named.Error!, named.ExitCode)
                : OperationResult<HostChoice>.Ok(new HostChoice(named.Value!, "the project's delivery.provider"));
        }

        var state = await _git.GetStateAsync(repository, ct).ConfigureAwait(false);

        if (state.Failed)
        {
            return OperationResult<HostChoice>.Fail(
                $"{project}'s repository could not be read to work out where it lands: {state.Error}", state.ExitCode);
        }

        if (FromRemote(state.Value!.RemoteUrl) is not { } provider)
        {
            return OperationResult<HostChoice>.Fail(
                $"{project}'s origin names no host Loadout knows. Set it in project.yaml with "
                + $"delivery.provider: one of {string.Join(", ", Words)}.",
                ExitCode.InvalidArguments);
        }

        var host = Named(provider, delivery?.Command);

        return host.Failed
            ? OperationResult<HostChoice>.Fail(host.Error!, host.ExitCode)
            : OperationResult<HostChoice>.Ok(new HostChoice(
                host.Value!, state.Value.RemoteUrl is null ? "having no origin" : "origin's address"));
    }
}
