using Loadout.Core.Diagnostics;
using Loadout.Models.Diagnostics;

namespace Loadout.Core.Sessions;

/// <summary>
/// Reports a session marker that has outlived every session.
/// </summary>
/// <remarks>
/// <para>
/// The marker exists so the Windows installer can tell that closing the
/// launcher would end somebody's session, which it cannot work out for itself.
/// Its whole value is that an installer trusts it — which is also what makes a
/// stale one expensive: it refuses every install until something clears it, and
/// the person hitting that has no reason to know the file exists.
/// </para>
/// <para>
/// A launcher killed outright leaves one behind. The next launch sweeps it,
/// because registering a session clears abandoned entries first, so this only
/// says anything on a machine where nothing has been launched since. That is
/// exactly the machine somebody is about to run an installer on.
/// </para>
/// </remarks>
internal sealed class SessionDiagnosticContributor : IDiagnosticContributor
{
    private const string Category = "Sessions";

    private readonly ISessionRegistry _sessions;

    public SessionDiagnosticContributor(ISessionRegistry sessions) => _sessions = sessions;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiagnosticCheck>> ContributeAsync(CancellationToken ct = default)
    {
        bool marked;

        try
        {
            marked = File.Exists(_sessions.InProgressPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var running = await _sessions.ListAsync(ct).ConfigureAwait(false);

        if (running.Count > 0)
        {
            // Said either way, because "a session is running" is the reason an
            // installer will refuse, and somebody who has just been refused
            // should be able to find out why from the tool rather than guess.
            return
            [
                DiagnosticCheck.Ok(
                    Category,
                    "Running",
                    running.Count == 1
                        ? "1 session. An installer will ask you to close it before upgrading."
                        : $"{running.Count} sessions. An installer will ask you to close them "
                          + "before upgrading."),
            ];
        }

        if (!marked)
        {
            return [DiagnosticCheck.Ok(Category, "Running", "none")];
        }

        return
        [
            DiagnosticCheck.Warn(
                Category,
                "Session marker",
                "says a session is running, and none is. A launcher was killed before it could "
                + "clear this. Installing will be refused until it goes.",
                new Remedy(
                    RemedyKind.ClearStaleSessionMarker,
                    "Remove the session marker left behind by a launcher that did not exit.",
                    _sessions.InProgressPath)),
        ];
    }
}
