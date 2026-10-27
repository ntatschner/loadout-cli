using Loadout.Agents.Ideas;
using Loadout.Core.Ask;
using Loadout.Core.Configuration;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Ask;

/// <summary>
/// Asks the configured agent, headless, to place a request Loadout could not.
/// </summary>
/// <remarks>
/// <para>
/// One turn of a round like an idea's: started, asked, read, ended. The
/// agent is given Loadout's own MCP server started with --read-only, so the
/// only tools it can reach are ones that read, and every tool that could write
/// a file, run a command or fetch from the web is denied by name. Whatever it
/// proposes comes back as a request for <see cref="IAskService"/> to run like
/// any other, which for anything that starts work means a plan somebody has to
/// approve.
/// </para>
/// <para>
/// A follow-up carries on the same conversation by the agent's own session
/// id, so it remembers what it was asked before without a process kept alive
/// between requests.
/// </para>
/// </remarks>
internal sealed class AskAgentRound : IAskAgent
{
    private readonly IDetachedLauncher _launcher;
    private readonly IConfigurationService _configuration;
    private readonly IPlatformPaths _paths;

    public AskAgentRound(IDetachedLauncher launcher, IConfigurationService configuration, IPlatformPaths paths)
    {
        _launcher = launcher;
        _configuration = configuration;
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<OperationResult<AskAgentTurn>> AskAsync(
        string query,
        AskAgentContext context,
        string? resumeSessionId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);

        var config = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

        if (config.Failed)
        {
            return OperationResult<AskAgentTurn>.Fail(config.Error!, config.ExitCode);
        }

        var home = Path.Combine(_paths.Paths.State, "ask");
        var round = Path.Combine(home, "round-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(round);

        try
        {
            var warnings = new List<string>();
            var servers = SelfServerConfig.Write(
                config.Value!.AgentTools.Enabled, context.Project, round, warnings, readOnly: true);

            var asked = await SchemaRound.AskAsync(
                _launcher,
                new DetachedLaunchRequest(
                    home,
                    "Answering a request from Ask Loadout",
                    McpConfigFiles: servers,
                    ResumeSessionId: resumeSessionId),
                servers.Count > 0 ? ["mcp__loadout"] : [],
                AskAgentSchema.Json,
                AskAgentSchema.Contract,
                AskAgentSchema.Prompt(query, context),
                json => AskAgentSchema.Read(json, context.Vocabulary),
                null,
                ct).ConfigureAwait(false);

            if (asked.Failed)
            {
                return OperationResult<AskAgentTurn>.Fail(asked.Error!, asked.ExitCode);
            }

            var outcome = asked.Value!;

            if (outcome.Answer.Failed)
            {
                return OperationResult<AskAgentTurn>.Fail(
                    $"{outcome.Answer.Error} The agent's turn cost ${outcome.CostUsd:0.00}.",
                    ExitCode.GeneralFailure);
            }

            var (answer, request) = outcome.Answer.Value;

            return OperationResult<AskAgentTurn>.Ok(new AskAgentTurn(answer, request, outcome.CostUsd, outcome.SessionId));
        }
        finally
        {
            try
            {
                Directory.Delete(round, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The declaration names this machine's executable and nothing
                // secret; a leftover is untidy, not a leak.
            }
        }
    }
}
