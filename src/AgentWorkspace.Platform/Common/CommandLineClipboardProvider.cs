using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Platform.Common;

/// <summary>
/// Writes to the clipboard by piping text into the platform's clipboard tool
/// (spec section 74). One implementation serves all three platforms because
/// only the command differs; the platform layer supplies the candidates.
/// </summary>
/// <remarks>
/// Several candidates are tried in order because Linux has no single answer:
/// Wayland sessions use wl-copy, X11 sessions xclip or xsel, and a headless
/// box has none of them. Exhausting the list is reported as an unsupported
/// capability, not an error.
/// </remarks>
public sealed class CommandLineClipboardProvider : IClipboardProvider
{
    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;
    private readonly IReadOnlyList<ClipboardCommand> _candidates;

    public CommandLineClipboardProvider(
        IProcessLauncher processes,
        IExecutableResolver resolver,
        IReadOnlyList<ClipboardCommand> candidates)
    {
        _processes = processes;
        _resolver = resolver;
        _candidates = candidates;
    }

    /// <summary>True when at least one clipboard tool is present.</summary>
    public bool IsAvailable => ResolveFirstAvailable() is not null;

    /// <summary>The tool that would be used, for diagnostics.</summary>
    public string? AvailableToolName => ResolveFirstAvailable()?.Command.Executable;

    /// <inheritdoc />
    public async Task<OperationResult> SetTextAsync(string text, CancellationToken ct = default)
    {
        var resolved = ResolveFirstAvailable();

        if (resolved is null)
        {
            var names = string.Join(", ", _candidates.Select(c => c.Executable));
            return OperationResult.Fail(
                $"No clipboard tool is available. Tried: {names}.");
        }

        var (command, path) = resolved.Value;

        var result = await _processes.RunAsync(
            new ProcessRequest(path, command.Arguments, StandardInput: text),
            TimeSpan.FromSeconds(15),
            ct).ConfigureAwait(false);

        if (result.Failed || result.Value is null)
        {
            return OperationResult.Fail(result.Error ?? "The clipboard tool could not be run.");
        }

        return result.Value.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(
                $"'{command.Executable}' failed: {result.Value.StandardError.Trim()}");
    }

    private (ClipboardCommand Command, string Path)? ResolveFirstAvailable()
    {
        foreach (var candidate in _candidates)
        {
            var path = _resolver.Resolve(candidate.Executable);
            if (path is not null)
            {
                return (candidate, path);
            }
        }

        return null;
    }
}

/// <summary>One clipboard tool and the arguments that make it read stdin.</summary>
/// <param name="Executable">Tool name, resolved on PATH.</param>
/// <param name="Arguments">Arguments that put it into "read stdin to clipboard" mode.</param>
public sealed record ClipboardCommand(string Executable, IReadOnlyList<string> Arguments);
