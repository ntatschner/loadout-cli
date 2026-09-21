using Loadout.Models.Results;

namespace Loadout.Platform.Abstractions;

/// <summary>
/// Whether something runs when this person logs in.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IDesktopIntegration"/>, which puts the launcher
/// where somebody can find it. This starts a process without being asked,
/// which is a different kind of decision and belongs behind its own command
/// and its own word.
/// </para>
/// <para>
/// Per user, never for the machine. Nothing here needs administrator rights
/// and nothing here should: a login item that needed elevation to remove would
/// be a thing somebody could not undo on their own laptop.
/// </para>
/// </remarks>
public interface IAutostart
{
    /// <summary>What this writes, in this platform's own terms, for a preview to name.</summary>
    string Describe();

    /// <summary>Whether the entry exists for this user.</summary>
    OperationResult<bool> IsInstalled();

    /// <summary>
    /// Writes the entry, replacing any it already wrote.
    /// </summary>
    /// <param name="command">
    /// The whole command line to run, already quoted, as
    /// <c>LauncherInvocation.Current()</c> spells it.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    Task<OperationResult> InstallAsync(string command, CancellationToken ct = default);

    /// <summary>Removes the entry, and says so even when there was none.</summary>
    Task<OperationResult> UninstallAsync(CancellationToken ct = default);
}
