using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Agents.Ideas;
using Loadout.Core.Diagnostics;
using Loadout.Models.Agents;
using Loadout.Models.Results;

namespace Loadout.Tests.Fakes;

/// <summary>
/// A detached launcher whose agent says the lines given, one per turn's worth,
/// and records what it was started with and told.
/// </summary>
public sealed class ScriptedDetachedLauncher(params string[] lines) : IDetachedLauncher
{
    /// <summary>A result line from Claude, with the structured answer given, or an error.</summary>
    public static string Result(string? structured, bool error = false) =>
        $$"""{"type":"result","subtype":"{{(error ? "error_during_execution" : "success")}}","is_error":{{(error ? "true" : "false")}},"num_turns":1,"duration_ms":10,"total_cost_usd":0.05,"usage":{}{{(structured is null ? string.Empty : $",\"structured_output\":{structured}")}}}""";

    public DetachedLaunchRequest? Request { get; private set; }

    public HeadlessOptions? Options { get; private set; }

    public StubProcessLauncher.StubPipedProcess? Pipe { get; private set; }

    public int Started { get; private set; }

    public Task<OperationResult<HeadlessLaunch>> StartAsync(
        DetachedLaunchRequest request,
        HeadlessOptions options,
        CancellationToken ct = default)
    {
        Request = request;
        Options = options;
        Started++;
        Pipe = new StubProcessLauncher.StubPipedProcess(string.Join('\n', lines) + "\n", 0);

        var plan = new LaunchPlan("claude", [], request.WorkingDirectory, [], [], null, 0, 0, null, null);

        return Task.FromResult(OperationResult<HeadlessLaunch>.Ok(new HeadlessLaunch(
            plan,
            [],
            new PreflightResult([], new Dictionary<string, string>()),
            request.Purpose,
            "claude",
            null,
            request.DryRun ? null : new HeadlessSession(Pipe, ClaudeHeadlessProtocol.Instance),
            (_, _) => Task.CompletedTask)));
    }
}
