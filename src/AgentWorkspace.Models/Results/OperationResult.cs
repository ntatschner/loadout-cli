namespace AgentWorkspace.Models.Results;

/// <summary>
/// Outcome envelope used by every service method in the launcher.
/// Expected failures are returned, never thrown — matching the house
/// convention. Only genuinely exceptional conditions (a bug, a corrupt
/// runtime) are allowed to propagate as exceptions.
/// </summary>
public class OperationResult
{
    protected OperationResult(bool succeeded, string? error, ExitCode exitCode)
    {
        Succeeded = succeeded;
        Error = error;
        ExitCode = exitCode;
    }

    public bool Succeeded { get; }

    public bool Failed => !Succeeded;

    /// <summary>Human-readable failure reason. Always redacted before it is stored or displayed.</summary>
    public string? Error { get; }

    /// <summary>The process exit code this outcome maps to (spec section 40).</summary>
    public ExitCode ExitCode { get; }

    public static OperationResult Ok() => new(true, null, Models.ExitCode.Success);

    public static OperationResult Fail(string error, ExitCode exitCode = Models.ExitCode.GeneralFailure) =>
        new(false, error, exitCode);
}

/// <summary>An <see cref="OperationResult"/> that carries a value on success.</summary>
public sealed class OperationResult<T> : OperationResult
{
    private OperationResult(bool succeeded, T? value, string? error, ExitCode exitCode)
        : base(succeeded, error, exitCode)
        => Value = value;

    /// <summary>The result value. Only meaningful when <see cref="OperationResult.Succeeded"/> is true.</summary>
    public T? Value { get; }

    public static OperationResult<T> Ok(T value) => new(true, value, null, Models.ExitCode.Success);

    public static new OperationResult<T> Fail(string error, ExitCode exitCode = Models.ExitCode.GeneralFailure) =>
        new(false, default, error, exitCode);
}
