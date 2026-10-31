using System.Runtime.CompilerServices;
using System.Text.Json;
using Loadout.Core.Agents;
using Loadout.Core.Updates;
using Loadout.Platform.Abstractions;

namespace Loadout.Tui;

/// <summary>
/// Runs <c>loadout update --yes --progress</c> on this same binary, as a piped
/// child, and hands back each step it reports.
/// </summary>
/// <remarks>
/// <para>
/// A child rather than the updater in this process, because the update has to
/// write its progress somewhere and the screen belongs to Terminal.Gui: an
/// in-process update writing to the console would break the screen. Piped, so
/// no console window opens and every line comes back to the About window.
/// </para>
/// <para>
/// The command line is the one a person would type, after the prefix
/// <see cref="LauncherInvocation.Parts"/> gives, so a development build runs
/// itself through the host rather than naming <c>dotnet</c> with nothing to run.
/// </para>
/// </remarks>
internal sealed class AboutUpdateRunner
{
    /// <summary>What follows the launcher's own prefix.</summary>
    internal static readonly IReadOnlyList<string> Arguments = ["update", "--yes", "--progress"];

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IProcessLauncher _processes;
    private readonly Func<(string Command, IReadOnlyList<string> Prefix)?> _self;

    internal AboutUpdateRunner(
        IProcessLauncher processes,
        Func<(string Command, IReadOnlyList<string> Prefix)?>? self = null)
    {
        ArgumentNullException.ThrowIfNull(processes);

        _processes = processes;
        _self = self ?? LauncherInvocation.Parts;
    }

    /// <summary>
    /// Each step the update reports, in order. Throws
    /// <see cref="InvalidOperationException"/> with the child's own words when
    /// it could not be started or exited with a failure.
    /// </summary>
    internal async IAsyncEnumerable<UpdateProgress> RunAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var self = _self()
            ?? throw new InvalidOperationException("This launcher cannot tell how it was started, so it cannot run its own updater.");

        var started = await _processes.StartPipedAsync(
            new ProcessRequest(self.Command, [.. self.Prefix, .. Arguments]),
            ct).ConfigureAwait(false);

        if (started.Failed)
        {
            throw new InvalidOperationException(started.Error ?? "The updater could not be started.");
        }

        await using var child = started.Value!;

        await child.CloseInputAsync().ConfigureAwait(false);

        // Read at the same time as the output, so a child with a lot to say on
        // stderr cannot fill the pipe and stall waiting for somebody to read it.
        var error = child.Error.ReadToEndAsync(ct);

        while (await child.Output.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (Parse(line) is { } step)
            {
                yield return step;
            }
        }

        var exitCode = await child.Exited.WaitAsync(ct).ConfigureAwait(false);
        var said = (await error.ConfigureAwait(false)).Trim();

        if (exitCode != 0)
        {
            throw new InvalidOperationException(said.Length > 0
                ? said
                : $"The updater exited with code {exitCode}.");
        }
    }

    /// <summary>One line as a step, or null for a line that is not one.</summary>
    internal static UpdateProgress? Parse(string line)
    {
        if (!line.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<UpdateProgress>(line, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
