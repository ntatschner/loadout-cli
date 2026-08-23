using AgentWorkspace.Models.Results;
using AgentWorkspace.Models.Updates;

namespace AgentWorkspace.Core.Updates;

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
}
