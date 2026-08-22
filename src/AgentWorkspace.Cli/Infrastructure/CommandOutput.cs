using System.Text.Json;
using System.Text.Json.Serialization;
using AgentWorkspace.Core.Security;
using AgentWorkspace.Models;
using AgentWorkspace.Models.Results;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AgentWorkspace.Cli.Infrastructure;

/// <summary>
/// Writes command output in whichever form the caller asked for
/// (spec sections 37, 38 and 40).
/// <para>
/// Everything a command emits goes through here so the two output modes cannot
/// drift apart. Structured output is a product interface that automation
/// depends on, not a debugging aid, so it is produced from the same data the
/// human-readable rendering uses.
/// </para>
/// </summary>
public sealed class CommandOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IAnsiConsole _console;
    private readonly GlobalSettings _settings;

    public CommandOutput(IAnsiConsole console, GlobalSettings settings)
    {
        _console = console;
        _settings = settings;
    }

    public bool IsJson => _settings.Json;

    /// <summary>Writes an object as JSON. Only called when JSON output was requested.</summary>
    public void WriteJson<T>(T value) =>
        // Written to the raw stream rather than through the console so markup
        // interpretation and line wrapping cannot corrupt the document.
        Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    /// <summary>Writes a line of ordinary output, unless the caller asked for quiet.</summary>
    public void WriteLine(string markup)
    {
        if (_settings.Quiet || _settings.Json)
        {
            return;
        }

        _console.MarkupLine(markup);
    }

    /// <summary>Writes a blank line, unless quiet.</summary>
    public void WriteBlankLine()
    {
        if (_settings.Quiet || _settings.Json)
        {
            return;
        }

        _console.WriteLine();
    }

    /// <summary>Renders a Spectre widget, unless quiet or in JSON mode.</summary>
    public void Write(IRenderable renderable)
    {
        if (_settings.Quiet || _settings.Json)
        {
            return;
        }

        _console.Write(renderable);
    }

    /// <summary>
    /// Reports a failure and returns its exit code.
    /// <para>
    /// Errors go to stderr even in quiet mode, because a script that suppressed
    /// chatter still needs to see why something failed. In JSON mode the error
    /// is emitted as a document so a caller parsing stdout is not left with
    /// nothing to read.
    /// </para>
    /// </summary>
    public int Fail(string? error, ExitCode exitCode = ExitCode.GeneralFailure)
    {
        var message = SecretRedactor.Redact(error ?? "The operation failed.");

        if (_settings.Json)
        {
            WriteJson(new { error = message, exitCode = (int)exitCode });
        }
        else
        {
            Console.Error.WriteLine(message);
        }

        return (int)exitCode;
    }

    /// <summary>Reports the failure carried by a result.</summary>
    public int Fail(OperationResult result) => Fail(result.Error, result.ExitCode);

    /// <summary>Marks success, returning the zero exit code.</summary>
    public static int Success() => (int)ExitCode.Success;
}
