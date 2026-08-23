using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Common;

/// <summary>
/// Shared terminal detection (spec section 42).
/// <para>
/// The candidate list is supplied per platform; the detection mechanism is the
/// same everywhere. No emulator is a hard dependency, and an empty result is a
/// valid one: a headless Linux server has no terminal emulator and must still
/// work end to end (spec section 86), which it does because the launcher reuses
/// the terminal it is already running in.
/// </para>
/// </summary>
public abstract class TerminalProviderBase : ITerminalProvider
{
    protected TerminalProviderBase(IProcessLauncher processes, IExecutableResolver resolver)
    {
        Processes = processes;
        Resolver = resolver;
    }

    protected IProcessLauncher Processes { get; }

    protected IExecutableResolver Resolver { get; }

    /// <summary>Candidate emulators for this platform, best first.</summary>
    protected abstract IReadOnlyList<TerminalCandidate> Candidates { get; }

    /// <inheritdoc />
    public virtual IReadOnlyList<TerminalDescriptor> DetectAvailable()
    {
        var found = new List<TerminalDescriptor>();

        foreach (var candidate in Candidates)
        {
            var path = Resolver.Resolve(candidate.Executable);
            if (path is not null)
            {
                found.Add(new TerminalDescriptor(candidate.Id, candidate.DisplayName, path));
            }
        }

        return found;
    }

    /// <inheritdoc />
    public bool IsRunningInTerminal
    {
        get
        {
            try
            {
                // Redirected streams mean the launcher is in a pipe or a CI
                // job, where spec section 37 forbids menus from appearing.
                return !Console.IsOutputRedirected && !Console.IsInputRedirected;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public abstract Task<OperationResult> LaunchInNewWindowAsync(
        TerminalDescriptor terminal,
        ProcessRequest request,
        CancellationToken ct = default);
}

/// <summary>A terminal emulator the launcher knows how to look for.</summary>
/// <param name="Id">Stable configuration key, for example iterm2.</param>
/// <param name="DisplayName">Human-facing name.</param>
/// <param name="Executable">Binary name to resolve on PATH.</param>
public sealed record TerminalCandidate(string Id, string DisplayName, string Executable);
