using Loadout.Models.Results;
using Loadout.Models.Updates;

namespace Loadout.Core.Updates;

/// <summary>
/// Checks a release source and installs what it offers (spec section 79).
/// <para>
/// The launcher replaces its own executable, which is the most dangerous thing
/// it does. Every path here therefore verifies a SHA-256 the feed committed to
/// before anything is put in place, and the previous binary is kept rather than
/// deleted, so a bad update can be walked back by hand.
/// </para>
/// </summary>
public interface IUpdateService
{
    /// <summary>Asks the configured source what it has for this platform.</summary>
    Task<OperationResult<UpdateCheck>> CheckAsync(CancellationToken ct = default);

    /// <summary>
    /// Downloads, verifies and installs an update.
    /// <para>
    /// The running executable is moved aside rather than overwritten: Windows
    /// will not let a running image be replaced, but it will let it be renamed.
    /// </para>
    /// </summary>
    /// <returns>The path the previous executable was kept at.</returns>
    Task<OperationResult<string>> ApplyAsync(UpdateCheck check, CancellationToken ct = default);

    /// <summary>The running executable, which is what an update replaces.</summary>
    string Executable { get; }

    /// <summary>How the running copy was installed, and so how it is replaced.</summary>
    InstallChannel Channel { get; }

    /// <summary>
    /// Downloads and verifies an update without installing it, for the helper
    /// to install once the daemon has gone.
    /// </summary>
    /// <returns>The verified binary for an archive install, or the verified MSI.</returns>
    Task<OperationResult<string>> StageAsync(UpdateCheck check, InstallChannel channel, CancellationToken ct = default);
}
