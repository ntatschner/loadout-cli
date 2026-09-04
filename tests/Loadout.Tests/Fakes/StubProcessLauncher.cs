using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Tests.Fakes;

/// <summary>
/// A process launcher that returns prepared output instead of running anything.
/// <para>
/// Used where the thing under test is how a program's output is read, not
/// whether the program runs. Running the real agent would make the test depend
/// on which servers happen to be configured on the machine it runs on.
/// </para>
/// </summary>
public sealed class StubProcessLauncher : IProcessLauncher
{
    private readonly string _standardOutput;
    private readonly int _exitCode;

    public StubProcessLauncher(string standardOutput, int exitCode = 0)
    {
        _standardOutput = standardOutput;
        _exitCode = exitCode;
    }

    /// <summary>Every request the test made, so a caller can assert on it.</summary>
    public List<ProcessRequest> Requests { get; } = [];

    /// <inheritdoc />
    public Task<OperationResult<ProcessOutcome>> RunAsync(
        ProcessRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        Requests.Add(request);

        return Task.FromResult(OperationResult<ProcessOutcome>.Ok(
            new ProcessOutcome(_exitCode, _standardOutput, string.Empty)));
    }

    /// <summary>What was last run in the terminal, for a test to inspect.</summary>
    /// <remarks>
    /// Recorded rather than refused. A terminal editor is started this way and
    /// a windowed one is not, and which of the two happened is the assertion.
    /// </remarks>
    public ProcessRequest? Interactive { get; private set; }

    /// <inheritdoc />
    public Task<OperationResult<int>> RunInteractiveAsync(
        ProcessRequest request,
        CancellationToken ct = default)
    {
        Interactive = request;
        Requests.Add(request);

        return Task.FromResult(OperationResult<int>.Ok(_exitCode));
    }

    /// <summary>What was last started detached, for a test to inspect.</summary>
    public ProcessRequest? Detached { get; private set; }

    /// <inheritdoc />
    public OperationResult StartDetached(ProcessRequest request)
    {
        Detached = request;
        Requests.Add(request);

        return OperationResult.Ok();
    }
}

/// <summary>An executable resolver that answers with one fixed path, or with nothing.</summary>
public sealed class StubResolver : IExecutableResolver
{
    private readonly string? _path;

    public StubResolver(string? path) => _path = path;

    /// <inheritdoc />
    public IReadOnlyList<string> StandardSearchPaths => [];

    /// <inheritdoc />
    public string? Resolve(string name, IReadOnlyList<string>? additionalPaths = null) => _path;
}
