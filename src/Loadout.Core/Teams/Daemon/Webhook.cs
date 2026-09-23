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
/// <param name="Criteria">
/// What the run is judged on, each one checkable, or null for a run with
/// nothing but its goal. The lead owes a verdict and evidence on every one, and
/// a done that leaves one unmet or unanswered is sent back to it.
/// </param>
/// <param name="Agent">
/// Which agent runs the team's nodes, or null for the project's own and then
/// this machine's. Offered as a list for the reason the team name is - a
/// free-text box accepted a name nothing could resolve, said the run had
/// started, and sent the refusal to a terminal behind the browser.
/// </param>
/// <param name="Model">
/// A model for every node of the run, spelled as the agent spells it, or null
/// to leave each node and then the project to decide.
/// <para>
/// <c>team run</c> has taken <c>--model</c> since teams existed and nothing
/// here carried it, so a run started from the page or by a webhook could not
/// name one at all: it took whatever the team file or the project pinned, and
/// somebody who chose a model on the page had nowhere to put it.
/// </para>
/// </param>
/// <remarks>
/// <para>
/// Nothing here is checked against anything. Whether that team exists, whether
/// that project is registered and whether that autonomy is a word at all are
/// questions the command line already answers, and answering them twice is how
/// two answers start to disagree.
/// </para>
/// <para>
/// That holds, and it was read too widely: it was taken to mean the page
/// should not be told what the teams are either, so the form offered a
/// free-text box and the only refusal went to a terminal nobody was watching.
/// Offering the real list is not deciding — see <see cref="Choosable"/> — and
/// what arrives here is still whatever the person sent.
/// </para>
/// </remarks>
public sealed record StartRequest(
    string Team,
    string Goal,
    string? Project = null,
    int? Rounds = null,
    string? Autonomy = null,
    IReadOnlyList<string>? Criteria = null,
    string? Model = null,
    string? Agent = null);

/// <summary>A team the page asked be written.</summary>
/// <param name="Name">What to call it. Lowercase and hyphenated, as the built-ins are.</param>
/// <param name="From">A team to copy as the starting point, or null for an empty one.</param>
/// <param name="Project">
/// The project to write it under, so only that project sees it, or null to
/// write it for all of them.
/// </param>
/// <remarks>
/// <para>
/// The page had a form headed "Start it" that asked for a team name, what it
/// was for, a project and an autonomy, and somebody reasonably read that as
/// making a team. It was not: it started a run of a team that already existed,
/// and typing a name that did not exist got a success message on the page and
/// a refusal in a terminal behind it.
/// </para>
/// <para>
/// So this is the thing that form looked like. Like every other change the
/// page can make, it maps onto the command somebody would have typed —
/// <c>team new</c> — and decides nothing itself.
/// </para>
/// </remarks>
public sealed record MakeRequest(string Name, string? From = null, string? Project = null);

/// <summary>Something the page asked be done to the schedules.</summary>
/// <param name="Verb">add, or remove.</param>
/// <param name="Name">What the schedule is called, which is how the other commands name it.</param>
/// <param name="Team">The team to run, for an add.</param>
/// <param name="Goal">What to ask it for, for an add.</param>
/// <param name="Project">Which project it works on, for an add.</param>
/// <param name="Every">How often: 30m, 2h, 1d.</param>
/// <param name="At">The time of day, as 09:00.</param>
/// <param name="On">Something to watch for instead of a clock, such as a commit.</param>
/// <param name="Autonomy">supervised or autonomous. Never manual: nobody is watching when it fires.</param>
/// <remarks>
/// <para>
/// One shape for both verbs, like <see cref="RunAction"/> and for the same
/// reason: they end the same way, with the daemon running the command somebody
/// would have typed. Nothing here decides what either of them means.
/// </para>
/// <para>
/// The page had no way to make a run happen again. Schedules existed, the
/// waiting area showed them, and the only way to make one was a terminal — so a
/// dashboard somebody leaves open on a second monitor could start a run once
/// and never arrange for it to happen nightly, which is most of what a machine
/// that stays up is for.
/// </para>
/// </remarks>
public sealed record ScheduleAction(
    string Verb,
    string Name,
    string? Team = null,
    string? Goal = null,
    string? Project = null,
    string? Every = null,
    string? At = null,
    string? On = null,
    string? Autonomy = null);

/// <summary>
/// Clearing out runs in a batch, as the page asks for it.
/// </summary>
/// <param name="Outcome">Only runs that ended this way, or null for any ending.</param>
/// <param name="OlderThan">Only runs older than this, written 30d, 12h or 90m, or null for any age.</param>
/// <param name="Keep">Never go below this many of the newest, or null for no floor.</param>
/// <param name="IncludeUnmerged">Take runs that left a branch nothing merged. Off by default.</param>
/// <remarks>
/// <para>
/// Not a <see cref="RunAction"/>, because it names no run: that is the whole
/// difference between "forget that one" and "clear out the failed ones", and
/// the second is the one nobody could do without a terminal. The page had a
/// Forget button inside each run and nothing above the list, so being rid of
/// twenty runs meant opening twenty runs.
/// </para>
/// <para>
/// Nothing here decides which runs those are. It ends the same way the rest
/// do, with the command somebody would have typed - and that command shows
/// what it picked and asks before it takes anything, which is the property
/// worth not reimplementing behind a button.
/// </para>
/// </remarks>
public sealed record PruneAction(
    string? Outcome = null,
    string? OlderThan = null,
    int? Keep = null,
    bool IncludeUnmerged = false);

/// <summary>One team, as a page offering it needs to know it.</summary>
/// <param name="Name">What to run it by.</param>
/// <param name="Description">The sentence the team file gives itself.</param>
/// <param name="Whose">Where it came from: built in, a pack, your workspace, this project.</param>
/// <param name="Template">A shape to copy rather than a team to run.</param>
/// <param name="Trouble">What is wrong with it, or null where nothing is.</param>
/// <param name="Nodes">How many nodes it has, so a page can say how big a thing this is.</param>
/// <param name="Autonomy">manual, supervised or autonomous, as its own rules set it.</param>
public sealed record ChoosableTeam(
    string Name,
    string Description,
    string Whose,
    bool Template,
    string? Trouble,
    int Nodes,
    string Autonomy);

/// <summary>
/// What can be started from this page.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the alternative was worse, not because the page should
/// hold opinions. The start form offered a free-text box whose suggestions came
/// from the runs already on the machine — so a machine that had run two teams
/// offered two of the eight that ship, and every other team could only be
/// reached by typing its name from memory. A name typed wrong was accepted, the
/// page said the run was starting, and the refusal appeared in the terminal
/// behind the browser where nobody was looking.
/// </para>
/// <para>
/// Serving the catalogue is not a second answer to "does this team exist". It
/// is the same answer, read from the same loader <c>team list</c> reads, sent to
/// the page so it can offer the real list rather than a guess. What happens
/// after somebody picks one is unchanged: the command line decides, as it
/// always did.
/// </para>
/// </remarks>
/// <param name="Teams">Every team this machine could run.</param>
/// <param name="Projects">Every registered project, as the registry names them.</param>
/// <param name="Here">
/// The project the server's own directory is in, or null. A dashboard is not
/// "where you are" — the daemon's directory is wherever it was started, which
/// is how a run launched from the page reported that <c>d:\git</c> is not a
/// repository — so this is offered as a default and never assumed.
/// </param>
/// <param name="Agents">
/// The agents this launcher knows how to start. Every adapter rather than only
/// the installed ones: detecting what is installed spawns a process per agent
/// and this is read on every request, and an agent this machine has not got is
/// refused by the launcher with its own sentence - the same division of labour
/// every other field on this form follows.
/// </param>
public sealed record Choosable(
    IReadOnlyList<ChoosableTeam> Teams,
    IReadOnlyList<string> Projects,
    string? Here = null,
    IReadOnlyList<string>? Agents = null);

/// <summary>Something the page asked be done to a run.</summary>
/// <param name="Run">Which run, as the journal names it.</param>
/// <param name="Verb">gates, message, pause, resume or stop.</param>
/// <param name="Gate">Which question is being answered, for a gate.</param>
/// <param name="Answer">What was chosen: an option, or yes or no.</param>
/// <param name="Reason">Why, where somebody gave one. Reaches the node.</param>
/// <param name="Message">What to say to the lead, for a message.</param>
/// <param name="Room">What to call the run's room, for a rename.</param>
/// <param name="Node">Whose work, for anything about one node.</param>
/// <param name="Instead">For a brief offered for changing: the one to send.</param>
/// <param name="Budget">What the run may spend in all, in USD, for a budget change.</param>
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
    string? Node = null,
    string? Instead = null,
    string? Budget = null);

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
