using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Core.Security;

namespace Loadout.Core.Teams;

/// <summary>What one node of a run may do, written where the answerer can read it.</summary>
/// <param name="Run">The run this belongs to.</param>
/// <param name="Node">The node, as the run named it.</param>
/// <param name="Role">The role it is playing, for the reason given back to it.</param>
/// <param name="Allow">Rules that permit, in the agent's own spelling.</param>
/// <param name="Deny">Rules that forbid, in the agent's own spelling. Deny wins.</param>
/// <param name="Ask">
/// Whether a call no rule covers may be put to the person rather than refused.
/// </param>
/// <remarks>
/// <para>
/// <paramref name="Ask"/> is false unless the run knows somebody is watching
/// it. An autonomous run has nobody; a run down a pipe has nobody; and a
/// question nobody answers is a node sitting still and spending until it gives
/// up, which is worse than the refusal it would otherwise have had.
/// </para>
/// <para>
/// It never reaches a rule in <see cref="Deny"/>. A role that forbids something
/// has already decided, and asking anyway would make every deny list a
/// suggestion.
/// </para>
/// </remarks>
/// <param name="Remedies">
/// What this team has registered and what was decided about each, resolved
/// before the node started. Empty for a team with none.
/// </param>
/// <param name="Agreed">
/// Rules the person running the team agreed to earlier in this run, by
/// answering "yes, and don't ask again". Never written into the policy file:
/// read in by the answerer from the run's directory on every call, so a rule
/// agreed while one node was asking reaches the next node of the same role.
/// </param>
public sealed record NodePolicy(
    string Run,
    string Node,
    string Role,
    IReadOnlyList<string> Allow,
    IReadOnlyList<string> Deny,
    bool Ask = false,
    IReadOnlyList<RemedyStanding>? Remedies = null,
    IReadOnlyList<string>? Agreed = null);

/// <summary>
/// One of a team's remedies, and what was decided about it before the node
/// started.
/// </summary>
/// <remarks>
/// <para>
/// Resolved at launch and written into the policy rather than read per call,
/// so the thing answering a node's questions does no file reading and holds no
/// opinion: it is handed the answer the same way it is handed the allow list.
/// </para>
/// <para>
/// A remedy registered <em>during</em> this run is not here, and that is the
/// right answer rather than a gap: nobody has trusted it, so the most it could
/// ever be is "ask", which is what an unlisted script already gets from the
/// role's own rules.
/// </para>
/// </remarks>
/// <param name="Name">The remedy, as its record names it.</param>
/// <param name="Script">The script's file name, which is what a call names.</param>
/// <param name="Ruling">run, ask or refuse, as decided on this machine.</param>
/// <param name="Because">Why, in words, for whoever is asked or refused.</param>
/// <param name="What">What the remedy does, for the person being asked.</param>
/// <param name="Assumes">What it assumes about the machine it runs on.</param>
/// <param name="Proves">How somebody would know it worked.</param>
public sealed record RemedyStanding(
    string Name,
    string Script,
    string Ruling,
    string Because,
    string? What = null,
    string? Assumes = null,
    string? Proves = null)
{
    /// <summary>
    /// The question a person is actually being asked, rather than "may it use
    /// Bash".
    /// </summary>
    /// <remarks>
    /// Everything somebody needs to decide, in the order they need it: what it
    /// would do, what it takes for granted about this machine, how they would
    /// know afterwards, and why it stopped to ask at all. A held remediation
    /// that arrived as a tool and a command line was a question nobody could
    /// answer without going and reading the script themselves.
    /// </remarks>
    public string Asking(string node, string role)
    {
        var text = new System.Text.StringBuilder();

        text.Append($"{node} ({role}) wants to run the remedy '{Name}'");

        if (What is { Length: > 0 } does)
        {
            text.Append($". It {char.ToLowerInvariant(does[0])}{does[1..].TrimEnd('.')}");
        }

        if (Assumes is { Length: > 0 } takes)
        {
            text.Append($". It assumes {char.ToLowerInvariant(takes[0])}{takes[1..].TrimEnd('.')}");
        }

        if (Proves is { Length: > 0 } shows)
        {
            text.Append($". You would know it worked because {char.ToLowerInvariant(shows[0])}{shows[1..].TrimEnd('.')}");
        }

        if (Because is { Length: > 0 })
        {
            text.Append($". It is being asked about because: {Because.TrimEnd('.')}");
        }

        return text.ToString();
    }
}

/// <summary>
/// Something a run has stopped on, waiting for a person.
/// </summary>
/// <param name="Id">Names the pair of files this and its answer live in.</param>
/// <param name="Node">The node that stopped, or the run's lead for a run-level gate.</param>
/// <param name="Role">What it is playing, so the question can say who is asking.</param>
/// <param name="Tool">The tool it asked about, for a permission.</param>
/// <param name="Target">The one thing the call is pointed at, where there is one.</param>
/// <param name="At">When it asked.</param>
/// <param name="Kind">
/// <c>permission</c> for a call no rule covers, <c>confirm</c> for a step a
/// manual run holds, <c>question</c> for a decision a lead may not make.
/// </param>
/// <param name="Asked">
/// The question in the run's own words, for the kinds that have one. A
/// permission builds its own from the node, the tool and the target.
/// </param>
/// <param name="Options">
/// What may be chosen. Empty means yes or no, which is what a permission and a
/// confirm are.
/// </param>
/// <param name="Recommendation">What the lead would choose, for a question.</param>
/// <param name="Until">
/// When whoever asked stops waiting, so a page can say how long is left. Null
/// on a question written before this was carried, which reads as no deadline
/// shown rather than as no deadline.
/// </param>
/// <remarks>
/// One shape for all three because they are one thing to whoever is answering:
/// the run has stopped and wants a person. They were separate while only a
/// terminal could answer - a terminal can block on each in its own way - and a
/// browser cannot block on anything, so it needs them written down.
/// </remarks>
public sealed record PendingAsk(
    string Id,
    string Node,
    string Role,
    string Tool,
    string? Target,
    DateTimeOffset At,
    string Kind = "permission",
    string? Asked = null,
    IReadOnlyList<string>? Options = null,
    string? Recommendation = null,
    DateTimeOffset? Until = null)
{
    /// <summary>The question, as a person reads it.</summary>
    /// <remarks>
    /// Names the node, the role and what it is pointed at, because "may it run
    /// Bash?" is not a question anybody can answer. The target is already
    /// redacted by the time it is written.
    /// <para>
    /// No question mark: whoever asks adds one, which is the convention every
    /// other gate here follows. Having one too meant the first real run asked
    /// somebody "Let it??".
    /// </para>
    /// </remarks>
    public string Question =>
        Asked is { Length: > 0 } said
            ? said
            : $"{Node} ({Role}) wants to use {Tool}"
                + (Target is { Length: > 0 } ? $" for '{Target}'" : string.Empty)
                + ". Nothing in its role allows that. Let it";

    /// <summary>The question with one question mark, however it was written.</summary>
    /// <remarks>
    /// <para>
    /// The convention above - whoever asks adds the mark - is right for a
    /// permission question, which is built here without one. It is wrong for a
    /// lead's own question, which arrives already written as one: "Merge now?"
    /// went to the rail, to the notice on somebody's phone and to what the CLI
    /// prints as "Merge now??".
    /// </para>
    /// <para>
    /// So the adding is the part that knows, and every surface goes through
    /// here. The dashboard had already learnt this in its own script and was
    /// the only one getting it right.
    /// </para>
    /// </remarks>
    public string Asking
    {
        get
        {
            var said = Question.TrimEnd();

            return said.Length > 0 && said[^1] is '?' or '!' or '.'
                ? said
                : said + "?";
        }
    }

    /// <summary>What may be chosen, with yes and no as the default pair.</summary>
    public IReadOnlyList<string> Choices =>
        Options is { Count: > 0 } given ? given : ["yes", "no"];
}

/// <summary>What a person said about one call.</summary>
/// <param name="Allowed">Whether it may. For a question, whether one was chosen at all.</param>
/// <param name="Reason">What to tell the node, whichever way it went.</param>
/// <param name="Chosen">The option picked, for a question that had several.</param>
/// <param name="By">
/// Where the answer came from: <c>terminal</c> or <c>dashboard</c>. Kept
/// because "somebody allowed this" and "somebody allowed this from a browser
/// on the other side of the house" are different sentences to whoever reads
/// the run back.
/// </param>
/// <param name="Instead">
/// For a brief offered for changing: the one to send instead. Null leaves it
/// alone, which is what happens almost every time.
/// </param>
public sealed record AskAnswer(
    bool Allowed,
    string Reason,
    string? Chosen = null,
    string By = "terminal",
    string? Instead = null);

/// <summary>An answer to "may I do this", and why.</summary>
/// <param name="Allowed">Whether it may.</param>
/// <param name="Reason">Why, in words the node can act on. Never empty.</param>
/// <param name="Rule">The rule that settled it, or null when nothing matched.</param>
/// <param name="Remedy">
/// The remedy this was about, where it was about one, so whoever is asked is
/// asked about the remedy rather than about Bash.
/// </param>
public sealed record PermissionDecision(
    bool Allowed,
    string Reason,
    string? Rule = null,
    RemedyStanding? Remedy = null);

/// <summary>
/// Decides what a node may do when its agent stops to ask.
/// </summary>
/// <remarks>
/// <para>
/// Without this, anything a node's allow list did not already cover was
/// denied by the agent with nothing said: the node saw a refusal it could not
/// explain, the run recorded a count, and whoever read it afterwards could
/// not tell a role that was too narrow from a node that tried something it
/// should not have.
/// </para>
/// <para>
/// The rules are the role's own, in the spelling the role files already use,
/// which is the agent's: a bare tool name, or a tool with a pattern for the
/// one thing the call is pointed at. Reading the role and reading the agent's
/// settings are then the same skill.
/// </para>
/// <para>
/// Deny wins, and what nothing matches is denied. A node whose brief does not
/// say it may do a thing may not do it - the safe direction when nobody is
/// there to ask, and the reason says which of the two it was.
/// </para>
/// </remarks>
public static partial class NodePermissions
{
    /// <summary>The file a run writes for a node, under the run's own directory.</summary>
    public static string FileName(string node) => $"policy-{Safe(node)}.json";

    /// <summary>Where the answerer records what it was asked.</summary>
    public static string AskedFileName(string node) => $"asked-{Safe(node)}.jsonl";

    /// <summary>How long a node waits for a person before giving up.</summary>
    /// <remarks>
    /// A node waiting is a node spending nothing and doing nothing, so this is
    /// not about cost. It is about a run that somebody walked away from ending
    /// in a refusal it can report rather than sitting there until the agent's
    /// own timeout kills it with nothing written down.
    /// <para>
    /// This is the wait for "may I use Bash for this", where an agent is
    /// stopped mid-turn and the answer is one word. It is not the wait for a
    /// question somebody has to think about: see <see cref="PersonPatience" />.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    /// <summary>How long a question meant for a person waits.</summary>
    /// <remarks>
    /// <para>
    /// A lead's question, a gate and a brief are all put to somebody who has to
    /// read them before they can answer, and they had the five minutes above
    /// because there was one number and nobody had separated the two cases.
    /// The first real test of the dashboard is what separated them: the lead
    /// asked which of three retention policies to take - each option a full
    /// line of prose - at 14:43:55, gave up at 14:48:55, and finished the run
    /// as "stopped at a decision". The answer arrived at 14:51:33, was written
    /// into the directory of a run that no longer existed, and the page said it
    /// had gone through.
    /// </para>
    /// <para>
    /// An hour, rather than no limit at all: a run nobody comes back to should
    /// still end in something it can report. The page says how long is left, so
    /// the limit is a fact somebody can act on rather than one they find out
    /// about afterwards.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan PersonPatience = TimeSpan.FromHours(1);

    /// <summary>How often each side looks for the other's file.</summary>
    public static readonly TimeSpan Glance = TimeSpan.FromMilliseconds(250);

    private static string AskPath(string directory, string id) =>
        Path.Combine(directory, $"ask-{Safe(id)}.json");

    private static string AnswerPath(string directory, string id) =>
        Path.Combine(directory, $"answer-{Safe(id)}.json");

    /// <summary>
    /// Puts a call to whoever is running the team, and waits for the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two files in the run's own directory rather than anything cleverer. The
    /// answerer is a different process from the coordinator - the agent starts
    /// it, not us - so they have no channel between them but the directory the
    /// run already writes to, and a directory is a channel that survives either
    /// side restarting.
    /// </para>
    /// <para>
    /// Null when nobody answered in time. The caller refuses and says that is
    /// why, which is a different thing from the role refusing and reads
    /// differently to whoever picks the run up afterwards.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Writes the question where the run will find it, and says whether it got
    /// there.
    /// </summary>
    /// <remarks>
    /// Separate from the waiting because they fail differently and are worth
    /// telling apart: a question that could not be written is one nobody will
    /// ever see, and a question that was written and not answered is one
    /// somebody walked away from.
    /// </remarks>
    public static async Task<bool> PutAsync(
        string directory,
        PendingAsk ask,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ask);

        try
        {
            // Redacted on the way in, the same rule a node's progress line
            // follows and for the same reason. This file is read by the
            // dashboard, the launcher's screen, `team status`, `team gate`,
            // the MCP tool and the notice that goes to somebody's phone, and
            // the words in it were written by a model that had spent the last
            // ten minutes reading a repository. A lead quoting what it found
            // in order to ask about it is exactly where a token ends up.
            //
            // The options are left as they are: they are matched against the
            // answer that comes back rather than only shown, and a changed
            // one is a question nobody can answer.
            var safe = ask with
            {
                Target = Clean(ask.Target),
                Asked = Clean(ask.Asked),
                Recommendation = Clean(ask.Recommendation),
            };

            Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(
                AskPath(directory, ask.Id), JsonSerializer.Serialize(safe), ct).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            // The redactor gives up on a long unbroken run of characters, and
            // the only two answers are to write text nothing has checked or to
            // write none. Nothing here is worth the first: the caller reads a
            // false as "it could not be asked", which ends the wait the way
            // nobody answering does.
            return false;
        }
    }

    /// <remarks>
    /// <c>patience</c> is <see cref="Patience" /> when omitted, which is the
    /// node's wait. Anything put to a person passes
    /// <see cref="PersonPatience" />.
    /// </remarks>
    public static async Task<AskAnswer?> AskAsync(
        string directory,
        PendingAsk ask,
        TimeProvider time,
        TimeSpan? patience = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ask);
        ArgumentNullException.ThrowIfNull(time);

        var waiting = patience ?? Patience;

        // Written into the question rather than kept here, because the thing
        // that has to know is a page in a browser somewhere else. A question
        // that does not say when it stops being answerable is one somebody
        // answers too late and is told it worked.
        if (!await PutAsync(directory, ask with { Until = time.GetUtcNow() + waiting }, ct)
            .ConfigureAwait(false))
        {
            return null;
        }

        return await WaitAsync(directory, ask.Id, time, waiting, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for an answer to a question that is already in the queue.
    /// </summary>
    /// <remarks>
    /// Separate from asking because something has to wait on a question it did
    /// not write. The watcher carries a node's question up to whoever is
    /// running the team, and a console that answers into this same directory
    /// has nothing to be told - the question is already where it reads. Asking
    /// it again there put one question in the queue twice, under two ids.
    /// </remarks>
    public static async Task<AskAnswer?> WaitAsync(
        string directory,
        string id,
        TimeProvider time,
        TimeSpan? patience = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(time);

        var until = time.GetUtcNow() + (patience ?? Patience);

        while (time.GetUtcNow() < until)
        {
            ct.ThrowIfCancellationRequested();

            if (Answered(directory, id) is { } said)
            {
                return said;
            }

            await Task.Delay(Glance, time, ct).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Text with anything credential-shaped taken out, or null as it was.</summary>
    private static string? Clean(string? text) =>
        text is { Length: > 0 } said ? SecretRedactor.Redact(said) : text;

    /// <summary>Questions in this run that nobody has answered yet.</summary>
    public static IReadOnlyList<PendingAsk> Pending(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var waiting = new List<PendingAsk>();

        foreach (var file in Directory.EnumerateFiles(directory, "ask-*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<PendingAsk>(File.ReadAllText(file)) is not { } ask
                    || File.Exists(AnswerPath(directory, ask.Id)))
                {
                    continue;
                }

                waiting.Add(ask);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Being written as it was read. The next glance gets it.
            }
        }

        return [.. waiting.OrderBy(ask => ask.At)];
    }

    /// <summary>What somebody decided about one question, or null if nobody has.</summary>
    /// <remarks>
    /// The glance behind a wait, for a caller that has other things to do
    /// between looks. <see cref="WaitAsync"/> is this in a loop.
    /// </remarks>
    public static AskAnswer? Answered(string directory, string id)
    {
        var answer = AnswerPath(directory, id);

        if (!File.Exists(answer))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AskAnswer>(File.ReadAllText(answer));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Half written. The next glance gets it.
            return null;
        }
    }

    /// <summary>Answers one question, for the node waiting on it to read.</summary>
    public static async Task AnswerAsync(
        string directory,
        string id,
        AskAnswer answer,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(answer);

        // Written beside, then moved into place, so the waiting side never
        // reads half an answer and treats it as no answer.
        var settled = AnswerPath(directory, id);
        var writing = settled + ".writing";

        await File.WriteAllTextAsync(writing, JsonSerializer.Serialize(answer), ct).ConfigureAwait(false);

        File.Move(writing, settled, overwrite: true);
    }

    /// <summary>A node name as a file name: instance names carry a slash.</summary>
    public static string FileSafe(string node) => Safe(node);

    private static string Safe(string node) =>
        string.Concat(node.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-'));

    /// <summary>Writes a node's policy, returning where it went.</summary>
    public static async Task<string> WriteAsync(string directory, NodePolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var path = Path.Combine(directory, FileName(policy.Node));

        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(policy, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);

        return path;
    }

    /// <summary>Reads a policy, or null when it is missing or unreadable.</summary>
    public static NodePolicy? Read(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<NodePolicy>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A policy that cannot be read is not a policy that permits
            // anything. The caller denies and says so.
            return null;
        }
    }

    /// <summary>Whether this node may make this call.</summary>
    public static PermissionDecision Decide(NodePolicy? policy, string tool, string? inputJson)
    {
        if (policy is null)
        {
            return new PermissionDecision(
                false,
                "This session has no policy to answer from, so nothing beyond what it was already "
                + "given is allowed. Report what you could not do rather than working around it.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tool);

        var target = Target(inputJson);

        foreach (var rule in policy.Deny)
        {
            if (Matches(rule, tool, target))
            {
                return new PermissionDecision(
                    false,
                    $"The {policy.Role} role forbids this: '{rule}'. Report it as a blocker rather "
                    + "than finding another way to do it.",
                    rule);
            }
        }

        // The role's own rules first, then what the person agreed to during
        // the run. Both after the deny list: agreeing to something for a role
        // cannot reach past what the role forbids, any more than asking can.
        var agreed = policy.Agreed ?? [];

        foreach (var (rule, person) in policy.Allow.Select(rule => (rule, false))
            .Concat(agreed.Select(rule => (rule, true))))
        {
            if (!Matches(rule, tool, target))
            {
                continue;
            }

            // A prefix the person agreed to covers that command and nothing
            // chained after it. "Don't ask again for git status" is not
            // agreement to "git status && rm -rf .", and the offer was built
            // from a single command for exactly that reason.
            if (person && (tool is "Bash" or "PowerShell") && Chained(target))
            {
                continue;
            }

            // The role allows it. A remedy can still hold it or refuse it -
            // never the other way about. Trust is not a way to get Bash: if the
            // role had not allowed this, nothing above would have reached here,
            // and a remedy that could widen a role would be a file an agent
            // writes deciding what an agent may do.
            if (Standing(policy, target) is { } standing)
            {
                return standing.Ruling switch
                {
                    "run" => new PermissionDecision(
                        true,
                        $"'{standing.Name}' is trusted here, and this machine lets it run. {standing.Because}",
                        rule),

                    "refuse" => new PermissionDecision(
                        false,
                        $"'{standing.Name}' is refused on this machine. {standing.Because} "
                        + "Report it as a blocker rather than finding another way to do it.",

                        // Named as a rule so this is a decision rather than a
                        // gap, which is what stops it being put to a person:
                        // a machine that said never is not asked again.
                        $"remedy:{standing.Name}"),

                    // No rule named, which is what puts it to a person rather
                    // than deciding it. The remedy travels with it so whoever
                    // is asked is asked about the remedy and not about Bash.
                    _ => new PermissionDecision(
                        false,
                        $"'{standing.Name}' has to be agreed to before it runs. {standing.Because}",
                        Remedy: standing),
                };
            }

            return new PermissionDecision(
                true,
                person
                    ? $"The person running this team agreed to '{rule}' for the {policy.Role} role earlier in this run."
                    : $"The {policy.Role} role allows '{rule}'.",
                rule);
        }

        return new PermissionDecision(
            false,
            $"Nothing in the {policy.Role} role allows {tool}"
            + (target is { Length: > 0 } ? $" for '{target}'" : string.Empty)
            + ". Report what you needed and why, and let whoever briefed you decide.");
    }

    /// <summary>
    /// The remedy a call is pointed at, or null where it is pointed at
    /// something else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By the script's file name appearing in what the call is aimed at, which
    /// is how a shell command names one. Not by path: the same script is
    /// reached by an absolute path, a relative one and a shell variable, and a
    /// comparison that only caught the first would be a gate somebody walks
    /// round by typing <c>cd</c>.
    /// </para>
    /// <para>
    /// A name that appears in an unrelated command is a false match, and the
    /// consequence of one is being asked about something that did not need it.
    /// That is the right way round: the other error is running something
    /// unattended that nobody agreed to.
    /// </para>
    /// </remarks>
    internal static RemedyStanding? Standing(NodePolicy policy, string? target)
    {
        if (target is not { Length: > 0 } || policy.Remedies is not { Count: > 0 } remedies)
        {
            return null;
        }

        foreach (var remedy in remedies)
        {
            if (remedy.Script is { Length: > 0 } script
                && target.Contains(script, StringComparison.OrdinalIgnoreCase))
            {
                return remedy;
            }
        }

        return null;
    }

    /// <summary>Where a run keeps what the person agreed to for one role.</summary>
    public static string AgreedFileName(string role) => $"agreed-{Safe(role)}.jsonl";

    /// <summary>The option that allows a call and stops asking about ones like it.</summary>
    public static string AlwaysOption(string rule) => $"yes, and don't ask again for {rule}";

    /// <summary>What the node is told when the person allowed it.</summary>
    public const string AllowedOnce = "The person running this team allowed it, for this call only.";

    /// <summary>What the node is told when the person refused it.</summary>
    public const string RefusedPlainly =
        "The person running this team refused it. Report what you needed and why rather than "
        + "finding another way to do it.";

    /// <summary>
    /// What a node is told about a permission somebody answered, with their
    /// own words where they gave any.
    /// </summary>
    /// <remarks>
    /// The person's words are the point of refusing with a reason - "no, use
    /// the script in build/ instead" - so they are passed on whole, after a
    /// clause saying which way it went. Words alone read to the node as advice
    /// with no verdict attached.
    /// </remarks>
    public static string Told(bool allowed, string? words) =>
        words?.Trim() is { Length: > 0 } said
            ? allowed
                ? $"The person running this team allowed it, and said: {said}"
                : $"The person running this team refused it, and said: {said}"
            : allowed ? AllowedOnce : RefusedPlainly;

    /// <summary>
    /// The rule a "don't ask again" would agree to for this call, or null
    /// where there is none worth offering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same shapes the agent itself offers. A command is agreed to by its
    /// command and subcommand - <c>Bash(git status:*)</c> - so the next
    /// <c>git status --short</c> is not asked about. An edit is agreed to for
    /// the tool as a whole, the way the agent's own "allow all edits" is. A
    /// fetch is agreed to for its site, not for the web.
    /// </para>
    /// <para>
    /// A chained command gets nothing: a prefix agreed from
    /// <c>dotnet build &amp;&amp; dotnet test</c> would be a prefix of anything,
    /// and an exact rule would never match again. A command with no target at
    /// all gets nothing either, because the only rule left is the whole tool.
    /// </para>
    /// <para>
    /// Nothing is offered that the redactor would change. The option is shown
    /// on the page and written to disk as it is, because it is matched against
    /// the answer, so a rule carrying a credential would put one on a screen.
    /// </para>
    /// </remarks>
    public static string? Rememberable(string tool, string? target)
    {
        if (string.IsNullOrWhiteSpace(tool))
        {
            return null;
        }

        string? rule;

        if (tool is "Bash" or "PowerShell")
        {
            if (target is not { Length: > 0 } command || Chained(command))
            {
                return null;
            }

            var words = command.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 0)
            {
                return null;
            }

            // The command and its subcommand - "git status", "dotnet test" -
            // covers the next one like it. Where the second word is a flag, a
            // path or an argument, a prefix of the first word alone would be
            // "git:*", which agrees to git push as well; so that one is agreed
            // to exactly, and only a command of one word gets a bare prefix.
            if (words.Length == 1)
            {
                rule = $"{tool}({words[0]}:*)";
            }
            else if (Subcommand().IsMatch(words[1]))
            {
                rule = $"{tool}({words[0]} {words[1]}:*)";
            }
            else if (command.TrimEnd().EndsWith('*'))
            {
                // Exact would be read as a prefix, since that is what a
                // trailing star means in a rule.
                return null;
            }
            else
            {
                rule = $"{tool}({command.Trim()})";
            }
        }
        else if (tool is "WebFetch")
        {
            if (target is not { Length: > 0 } url
                || !Uri.TryCreate(url, UriKind.Absolute, out var site)
                || site.Scheme is not ("http" or "https"))
            {
                return null;
            }

            rule = $"WebFetch({site.GetLeftPart(UriPartial.Authority)}/*)";
        }
        else
        {
            rule = tool;
        }

        try
        {
            return SecretRedactor.Redact(rule) == rule ? rule : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>What the person has agreed to for one role in this run.</summary>
    public static IReadOnlyList<string> Agreed(string directory, string role)
    {
        var path = Path.Combine(directory, AgreedFileName(role));

        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var rules = new List<string>();

            foreach (var line in File.ReadAllLines(path))
            {
                try
                {
                    if (line.Length > 0 && JsonSerializer.Deserialize<string>(line) is { Length: > 0 } rule)
                    {
                        rules.Add(rule);
                    }
                }
                catch (JsonException)
                {
                    // Half a line from a write in progress. The next call
                    // reads it whole.
                }
            }

            return rules;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing agreed is the safe reading: the call is asked about
            // again rather than allowed on a file nobody could read.
            return [];
        }
    }

    /// <summary>Records that the person agreed to a rule for one role, for the rest of this run.</summary>
    /// <remarks>
    /// Appended a line at a time, because two nodes of one role can each be
    /// answered at once, in two processes, and a whole-file rewrite would let
    /// the second lose the first.
    /// </remarks>
    public static void Agree(string directory, string role, string rule)
    {
        Directory.CreateDirectory(directory);

        File.AppendAllText(
            Path.Combine(directory, AgreedFileName(role)),
            JsonSerializer.Serialize(rule) + "\n");
    }

    /// <summary>Whether a command runs more than one thing.</summary>
    private static bool Chained(string? command) =>
        command is { Length: > 0 }
        && (command.IndexOfAny(['|', ';', '&', '`', '>', '<', '\n', '\r']) >= 0
            || command.Contains("$(", StringComparison.Ordinal));

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$")]
    private static partial Regex Subcommand();

    /// <summary>
    /// Whether a call is worth putting to a person rather than answering from
    /// the policy alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three things have to be true, and each is load-bearing. The run has to
    /// have said somebody is watching. The call has to have been refused,
    /// because asking about one already allowed would stop a node on something
    /// its role said yes to. And nothing may have matched it — a rule that
    /// decided has decided, and a deny somebody could be asked past would make
    /// every deny list a suggestion.
    /// </para>
    /// <para>
    /// Here rather than at the answerer, because this is the boundary and the
    /// boundary is one place. A condition spelled out at a call site is a
    /// condition the next call site spells differently.
    /// </para>
    /// </remarks>
    public static bool Askable(NodePolicy? policy, PermissionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return policy is { Ask: true } && !decision.Allowed && decision.Rule is null;
    }

    /// <summary>
    /// Whether one rule covers one call.
    /// </summary>
    /// <remarks>
    /// Two shapes, both the agent's own: <c>Read</c> for any use of a tool,
    /// and <c>Bash(git status:*)</c> for a tool pointed at something in
    /// particular. A trailing <c>:*</c> or <c>*</c> is a prefix; anything else
    /// inside the brackets must match exactly. Deliberately not a glob - a
    /// rule nobody can predict the meaning of is worse than a narrow one.
    /// </remarks>
    public static bool Matches(string rule, string tool, string? target)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        var open = rule.IndexOf('(', StringComparison.Ordinal);

        if (open < 0 || !rule.EndsWith(')'))
        {
            return string.Equals(rule.Trim(), tool, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.Equals(rule[..open].Trim(), tool, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pattern = rule[(open + 1)..^1].Trim();

        if (pattern is "*" or ":*")
        {
            return true;
        }

        if (target is not { Length: > 0 })
        {
            return false;
        }

        if (pattern.EndsWith(":*", StringComparison.Ordinal))
        {
            return target.StartsWith(pattern[..^2], StringComparison.Ordinal);
        }

        if (pattern.EndsWith('*'))
        {
            return target.StartsWith(pattern[..^1], StringComparison.Ordinal);
        }

        return string.Equals(target, pattern, StringComparison.Ordinal);
    }

    /// <summary>The one thing a call is pointed at, from the names tools use for it.</summary>
    public static string? Target(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(inputJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in (string[])["command", "file_path", "path", "url", "pattern"])
            {
                if (document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text)
                {
                    return text;
                }
            }
        }
        catch (JsonException)
        {
            // A call whose input is not an object is judged on its tool name
            // alone, which is what a rule without brackets says anyway.
        }

        return null;
    }
}
