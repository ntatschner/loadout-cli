using System.Security.Cryptography;
using System.Text;
using Loadout.Models;
using Loadout.Models.Configuration;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams.Daemon;

/// <summary>What a webhook request asked for.</summary>
/// <param name="Team">The team to run, as <c>team list</c> names it.</param>
/// <param name="Goal">What the run is for, in the caller's words.</param>
/// <param name="Project">Which project it works on, as the registry names it.</param>
public sealed record TriggerRequest(string Team, string Goal, string? Project = null);

/// <summary>Work the page asked be started.</summary>
/// <param name="Team">The team to run, as <c>team list</c> names it.</param>
/// <param name="Goal">What the run is for, in the person's words.</param>
/// <param name="Project">Which project it works on, as the registry names it.</param>
/// <param name="Rounds">How many rounds it may take, or null for the default.</param>
/// <param name="Autonomy">manual, supervised or autonomous, or null for the team's own.</param>
/// <remarks>
/// Nothing here is checked against anything. Whether that team exists, whether
/// that project is registered and whether that autonomy is a word at all are
/// questions the command line already answers, and answering them twice is how
/// two answers start to disagree.
/// </remarks>
public sealed record StartRequest(
    string Team,
    string Goal,
    string? Project = null,
    int? Rounds = null,
    string? Autonomy = null);

/// <summary>Something the page asked be done to a run.</summary>
/// <param name="Run">Which run, as the journal names it.</param>
/// <param name="Verb">gates, message, pause, resume or stop.</param>
/// <param name="Gate">Which question is being answered, for a gate.</param>
/// <param name="Answer">What was chosen: an option, or yes or no.</param>
/// <param name="Reason">Why, where somebody gave one. Reaches the node.</param>
/// <param name="Message">What to say to the lead, for a message.</param>
/// <param name="Room">What to call the run's room, for a rename.</param>
/// <param name="Node">Whose work, for anything about one node.</param>
/// <remarks>
/// One shape for all five because they all end the same way: the daemon runs
/// the command somebody would have typed. Nothing here decides what any of
/// them mean.
/// </remarks>
public sealed record RunAction(
    string Run,
    string Verb,
    string? Gate = null,
    string? Answer = null,
    string? Reason = null,
    string? Message = null,
    string? Room = null,
    string? Node = null);

/// <summary>
/// Letting something outside this machine start a run.
/// </summary>
/// <remarks>
/// <para>
/// Off unless somebody turned it on, and off again the moment either half is
/// missing. A run costs money and edits a repository, so the question this
/// answers is not "is the caller allowed" but "did the person who owns this
/// machine say, in advance and by name, that this team may be started this
/// way".
/// </para>
/// <para>
/// Three things have to be true and they are deliberately separate. There has
/// to be a token, which lives in the operating system's credential store and
/// never in a file. The team has to be named in this machine's own list, so
/// turning the webhook on grants nothing by itself. And the run it would start
/// must not be manual, because manual means a person at every step and a
/// webhook is the case where there is nobody.
/// </para>
/// <para>
/// The token is compared in fixed time. Over a network the timing signal is
/// noisier than on loopback, which makes it slower to exploit rather than
/// impossible, and "slower" is not an argument for leaking it.
/// </para>
/// </remarks>
public static class Webhook
{
    /// <summary>Where the token lives, in the credential store's own terms.</summary>
    public const string Reference = "loadout/team-webhook";

    /// <summary>Why a trigger was refused, as a status and a sentence.</summary>
    /// <param name="Status">The HTTP status to answer with.</param>
    /// <param name="Detail">What to say, which never names the token or the teams.</param>
    public sealed record Refusal(int Status, string Detail);

    /// <summary>A token nobody has to invent: 32 bytes, printed once.</summary>
    public static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>
    /// Whether this request may start this run, and why not when it may not.
    /// </summary>
    /// <param name="given">The token the caller presented, or null.</param>
    /// <param name="expected">The token this machine holds, or null when there is none.</param>
    /// <param name="team">The team asked for.</param>
    /// <param name="allowed">The teams this machine lets a webhook start.</param>
    /// <returns>Null when it may proceed.</returns>
    public static Refusal? Refuse(
        string? given,
        string? expected,
        string team,
        IReadOnlyList<string>? allowed)
    {
        if (expected is not { Length: > 0 })
        {
            // Said as "not here" rather than "not configured". Anything that
            // reached this is being told what it needs to know and no more.
            return new Refusal(404, "This machine does not accept triggered runs.");
        }

        if (given is not { Length: > 0 }
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
        {
            return new Refusal(403, "This needs the token this machine was given.");
        }

        if (string.IsNullOrWhiteSpace(team))
        {
            return new Refusal(400, "Name a team to run.");
        }

        // The ceiling, and the reason turning the webhook on is not enough on
        // its own. Same shape as the outward-action ceiling: the caller asks,
        // this machine decides, and it decided in advance.
        if (!(allowed ?? []).Any(one =>
                string.Equals(one.Trim(), team.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return new Refusal(
                403,
                $"'{team}' is not a team this machine accepts triggered runs of. "
                + "Add it with: loadout config set team-webhook-teams");
        }

        return null;
    }

    /// <summary>
    /// Reads the token this machine holds, or null when there is not one.
    /// </summary>
    /// <remarks>
    /// A failure to read is the same answer as no token. The credential store
    /// being unavailable must refuse requests rather than let them past, and
    /// there is nowhere useful to report it from inside a request.
    /// </remarks>
    public static async Task<string?> TokenAsync(ISecretProvider secrets, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        var read = await secrets.GetAsync(Reference, ct).ConfigureAwait(false);

        return read.Succeeded && read.Value is { Length: > 0 } token ? token : null;
    }

    /// <summary>
    /// Generates a token, keeps it in the credential store, and hands it back
    /// once.
    /// </summary>
    /// <remarks>
    /// Returned rather than printed here, so the one place it is shown is the
    /// command that asked for it. Nothing reads it back out afterwards for
    /// display: a token that can be re-read is a token in every screenshot of
    /// the machine it is on.
    /// </remarks>
    public static async Task<OperationResult<string>> EnableAsync(
        ISecretProvider secrets,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        var available = await secrets.IsAvailableAsync(ct).ConfigureAwait(false);

        if (available.Failed)
        {
            return OperationResult<string>.Fail(
                $"This machine's credential store cannot be used: {available.Error} "
                + "A webhook token is not written to a file, so there is nowhere else to keep it.",
                ExitCode.GeneralFailure);
        }

        var token = NewToken();

        var stored = await secrets.SetAsync(Reference, token, ct).ConfigureAwait(false);

        return stored.Failed
            ? OperationResult<string>.Fail(stored.Error!, stored.ExitCode)
            : OperationResult<string>.Ok(token);
    }

    /// <summary>Forgets the token, which turns the webhook off however it was reached.</summary>
    public static Task<OperationResult> DisableAsync(
        ISecretProvider secrets,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        return secrets.RemoveAsync(Reference, ct);
    }

    /// <summary>
    /// Where the server should listen, from what the machine was told.
    /// </summary>
    /// <remarks>
    /// Loopback unless somebody asked for otherwise, and asking for otherwise
    /// is a deliberate act with its own words. An address is never inferred
    /// from the webhook being on: a machine that accepts triggered runs from
    /// its own git hook wants nothing bound to the network at all.
    /// </remarks>
    public static string Listen(MachineTeams? teams) =>
        teams?.WebhookListen is { } asked && asked.Trim() is { Length: > 0 } where
            ? where
            : "127.0.0.1";
}
