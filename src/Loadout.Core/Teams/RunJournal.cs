using System.Globalization;
using System.Text.Json;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>One thing that happened during a run, as the journal recorded it.</summary>
/// <param name="At">When.</param>
/// <param name="Node">Which node, or null for the run itself.</param>
/// <param name="Kind">What happened: node.launched, node.turn, report.checked, merge.done and the rest.</param>
/// <param name="Data">Whatever that kind carries, still as JSON because each kind carries its own shape.</param>
public sealed record RunEvent(DateTimeOffset At, string? Node, string Kind, JsonElement Data)
{
    /// <summary>A string from the event's data, or null.</summary>
    /// <remarks>
    /// Names are matched exactly, so an event's data has to be written with the
    /// names its readers ask for: lower case, as every event here uses. C#
    /// anonymous-object shorthand does not do that - <c>new { one.Line }</c>
    /// writes <c>"Line"</c> and this returns null for <c>"line"</c>, silently,
    /// which is how a node's own account of itself was written to every
    /// journal and displayed by nothing for a day.
    /// </remarks>
    public string? Text(string name) =>
        Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A yes or no from the event's data, or null where it says neither.</summary>
    /// <remarks>
    /// Gates, permissions and answers all record whether they were allowed, and
    /// they record it as a JSON boolean. <see cref="Text"/> returns null for
    /// anything that is not a string, so a reader asking it for "allowed" is
    /// told nothing and reads every gate in the journal as a refusal - which is
    /// what the first version of the run summary did, reporting a merge as
    /// refused on the same line it reported it as done.
    /// </remarks>
    public bool? Flag(string name) =>
        Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out var said) => said,
                _ => null,
            }
            : null;

    /// <summary>One value from the event's data, as words, with both spellings tried.</summary>
    /// <remarks>
    /// <see cref="Text"/> matches a name exactly and says so. What it does not
    /// say is that some of these events were written from C# anonymous-object
    /// shorthand before that was corrected, so journals already on disk carry
    /// <c>Tool</c> and <c>Target</c> where their neighbours carry <c>tool</c>
    /// and <c>target</c>. Asking for one spelling reads half the journal and
    /// drops the rest without a word, which is the fault that comment
    /// describes, one layer up. Readers of old journals want this; writers
    /// still want <see cref="Text"/>, so that the next one is caught.
    /// </remarks>
    public string? Word(string name)
    {
        var said = Text(name)
            ?? Text(char.ToUpperInvariant(name[0]) + name[1..])
            ?? Text(char.ToLowerInvariant(name[0]) + name[1..]);

        return said is { Length: > 0 } && said.Trim().Length > 0 ? said.Trim() : null;
    }

    /// <summary>Whether the event said yes, with both spellings tried.</summary>
    public bool Yes(string name) =>
        Flag(name) ?? Flag(char.ToUpperInvariant(name[0]) + name[1..]) ?? false;

    /// <summary>Every string in a list from the event's data, or nothing.</summary>
    /// <remarks>
    /// A rejection carries its reasons and a gate reminder carries what it is
    /// still waiting on. Both are lists, and a reader with no way to ask for
    /// one prints the event without the only part that says why.
    /// </remarks>
    public IReadOnlyList<string> Words(string name)
    {
        if (Data.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (!Data.TryGetProperty(name, out var value)
            && !Data.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value))
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. value.EnumerateArray()
                .Where(one => one.ValueKind == JsonValueKind.String)
                .Select(one => one.GetString()!)
                .Where(one => one.Trim().Length > 0)
                .Select(one => one.Trim()),
        ];
    }

    /// <summary>
    /// The coverage a lead reported, criterion by criterion.
    /// </summary>
    /// <remarks>
    /// Its own reader because this is the one event carrying a list of objects
    /// rather than a list of strings, and <see cref="Words"/> would return
    /// nothing for it without saying so - which is how a whole account of
    /// whether the goal was met would be dropped in silence.
    /// </remarks>
    public IReadOnlyList<RunCovered> Covered()
    {
        if (Data.ValueKind != JsonValueKind.Object
            || !Data.TryGetProperty("coverage", out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var covered = new List<RunCovered>();

        foreach (var one in value.EnumerateArray())
        {
            if (one.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var criterion = Said(one, "criterion");

            if (criterion is not { Length: > 0 })
            {
                continue;
            }

            covered.Add(new RunCovered(
                criterion,
                Said(one, "verdict") ?? "unmet",
                Said(one, "because")));
        }

        return covered;
    }

    /// <summary>
    /// One string from a coverage entry, with both spellings tried.
    /// </summary>
    /// <remarks>
    /// The same accommodation <see cref="Word"/> makes, and for the same
    /// reason. These were written by anonymous-object shorthand for exactly one
    /// run of one build, which spelt them <c>Criterion</c> and <c>Because</c>;
    /// asking only for the corrected spelling would read those journals as
    /// having no coverage at all, which is the thing least worth being wrong
    /// about in a record of whether a goal was met.
    /// </remarks>
    private static string? Said(JsonElement entry, string name)
    {
        foreach (var spelling in new[] { name, char.ToUpperInvariant(name[0]) + name[1..] })
        {
            if (entry.TryGetProperty(spelling, out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 } said)
            {
                return said;
            }
        }

        return null;
    }

    /// <summary>A number from the event's data, or null.</summary>
    public decimal? Number(string name) =>
        Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;
}

/// <summary>What became of one of a run's criteria, as the lead last reported it.</summary>
/// <param name="Criterion">The criterion, in the words the run gave it.</param>
/// <param name="Verdict">met, unmet or not-attempted.</param>
/// <param name="Because">What the lead says shows it, where it said anything.</param>
public sealed record RunCovered(string Criterion, string Verdict, string? Because)
{
    /// <summary>Whether this one is settled.</summary>
    public bool Met => string.Equals(Verdict, "met", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Where a node got to.</summary>
/// <param name="Node">The node, as the run named it.</param>
/// <param name="Role">The role it played.</param>
/// <param name="State">working, done, blocked, failed, needs-decision, or ended.</param>
/// <param name="Trouble">What the node's process said on the way out, when it left badly.</param>
/// <param name="Turns">Exchanges with the model, summed across its turns.</param>
/// <param name="CostUsd">What it spent.</param>
/// <param name="LastSeen">When it last said anything.</param>
/// <param name="Branch">The branch it worked on, when it had one of its own.</param>
/// <param name="Decision">The one word it handed back, when its role hands one back.</param>
/// <param name="Denials">Tool calls its own permission rules refused.</param>
/// <param name="Started">When it was launched, for how long it has been at it.</param>
/// <param name="Doing">
/// The last thing it was seen doing: the tool it called and the one thing that
/// call was pointed at. Emptied once it reports, because a node that has
/// answered is not still doing the last thing anybody saw, and leaving it there
/// is how a finished run looks busy.
/// </param>
/// <param name="Said">
/// The last thing it said <em>about itself</em>, in its own words.
/// </param>
/// <param name="Model">
/// The model it was pinned to, or null for whatever the agent picks itself.
/// </param>
/// <param name="Base">
/// The commit its branch started from, which is what makes its diff still
/// mean the same thing after the branch has been merged and tidied away.
/// </param>
/// <param name="Session">
/// The agent's own session for this node, as its latest turn reported it,
/// which is what picking the node up again after the run ended resumes.
/// </param>
/// <remarks>
/// <paramref name="Doing"/> and <paramref name="Said"/> are two accounts and
/// neither corrects the other. The first is precise about what happened and
/// says nothing about why; the second says why and may be wrong. A node looping
/// on one file and a node carefully reading forty look identical in the first
/// and quite different in the second.
/// </remarks>
public sealed record RunNode(
    string Node,
    string Role,
    string State,
    int Turns,
    decimal CostUsd,
    DateTimeOffset LastSeen,
    string? Branch = null,
    string? Decision = null,
    int Denials = 0,
    DateTimeOffset? Started = null,
    string? Doing = null,
    string? Said = null,
    string? Model = null,
    string? Base = null,
    string? Trouble = null,
    string? Session = null)
{
    /// <summary>How long it has been going, or how long it took.</summary>
    public TimeSpan? Took =>
        Started is { } began && LastSeen > began ? LastSeen - began : null;
}

/// <summary>One exchange with a node's model, as the run paid for it.</summary>
/// <param name="At">When it came back.</param>
/// <param name="Node">Which node had it.</param>
/// <param name="Round">The round the run was in.</param>
/// <param name="Attempt">Which try this was: a second one means the first
/// report could not be read.</param>
/// <param name="Exchanges">How many messages went back and forth inside it.</param>
/// <param name="CostUsd">What it cost.</param>
/// <param name="Denials">How many things it asked for and was refused.</param>
/// <param name="Completed">Whether the node got to the end of its turn.</param>
/// <param name="Status">What the node said it had done, once it reported.</param>
/// <param name="Outcome">What the run made of that report.</param>
/// <remarks>
/// Kept one by one rather than only summed, because a node that cost five
/// dollars over forty cheap exchanges and a node that cost five dollars over
/// two enormous ones are the same number and different problems.
/// </remarks>
public sealed record RunTurn(
    DateTimeOffset At,
    string Node,
    int Round,
    int Attempt,
    int Exchanges,
    decimal CostUsd,
    int Denials,
    bool Completed,
    string? Status = null,
    string? Outcome = null);

/// <summary>One round of a run, and how long it took.</summary>
/// <param name="Number">Which round.</param>
/// <param name="Started">When it began.</param>
/// <param name="Ended">When it came back, or null for the one still going.</param>
/// <param name="Requests">How many nodes the lead asked for in it.</param>
/// <remarks>
/// Where the time goes is a question about rounds before it is a question
/// about anything else: a run that took an hour spent it somewhere, and the
/// round it was spent in is the first thing that narrows it down.
/// </remarks>
public sealed record RunRound(
    int Number,
    DateTimeOffset Started,
    DateTimeOffset? Ended = null,
    int Requests = 0)
{
    /// <summary>How long it took, or has taken so far against a given clock.</summary>
    public TimeSpan Took(DateTimeOffset now) => (Ended ?? now) - Started;
}

/// <summary>A run, read back from what it wrote down.</summary>
public sealed record RunSummary(
    string RunId,
    string Directory,
    string Team,
    string Goal,
    string Autonomy,
    DateTimeOffset Started,
    DateTimeOffset? Finished,
    string? Ended,
    decimal CostUsd,
    int Rounds,
    IReadOnlyList<RunNode> Nodes,
    IReadOnlyList<string> Merged,
    IReadOnlyList<string> Branches,
    int RoundLimit = 0,
    string? Project = null,
    string? Path = null,
    IReadOnlyList<PendingAsk>? Gates = null,
    decimal? BudgetUsd = null,
    int QuietRounds = 0,
    IReadOnlyList<RunTurn>? Exchanges = null,
    IReadOnlyList<RunRound>? Timeline = null,
    int Conflicts = 0,
    IReadOnlyList<RunCovered>? Covered = null,
    string? Outcome = null)
{
    /// <summary>Each round, with when it started and when it came back.</summary>
    public IReadOnlyList<RunRound> RoundsTaken => Timeline ?? [];

    /// <summary>
    /// What the lead last said about each of the run's criteria.
    /// </summary>
    /// <remarks>
    /// Empty for a run given no criteria, which is every run written before
    /// they existed and every run that does not want them. A run that has them
    /// is the one where "done" means something a reader can check rather than
    /// something the lead asserted.
    /// </remarks>
    public IReadOnlyList<RunCovered> Coverage => Covered ?? [];

    /// <summary>Criteria the lead has not reported as met.</summary>
    public IReadOnlyList<RunCovered> Outstanding => [.. Coverage.Where(one => !one.Met)];

    /// <summary>Every exchange the run paid for, oldest first.</summary>
    public IReadOnlyList<RunTurn> Turns => Exchanges ?? [];

    /// <summary>
    /// What the run has stopped and asked a person, and not yet been told.
    /// </summary>
    /// <remarks>
    /// Read from the run's directory rather than from its events, because a
    /// question is answered by a file appearing and the journal only learns
    /// about it afterwards. Somebody watching needs to see it while it is
    /// still true.
    /// </remarks>
    public IReadOnlyList<PendingAsk> Waiting => Gates ?? [];

    /// <summary>
    /// Whether the run has stopped and is waiting for a person.
    /// </summary>
    /// <remarks>
    /// Its own state rather than a kind of running, and the reason is somebody
    /// managing several teams: a run working and a run stopped on a question
    /// look identical until one of them is called what it is. This is the one
    /// state that says the next move is yours.
    /// </remarks>
    public bool WaitingForYou => Running && Waiting.Count > 0;

    /// <summary>
    /// What a node is doing right now, in the words somebody glancing at it
    /// needs: working, waiting for you, waiting on another node, or finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RunNode.State"/> is what the node last reported, which is
    /// not the same question. A lead that reported "blocked" and asked for a
    /// strategist is not stuck, it is waiting for the strategist; a node
    /// stopped on a permission question still reads "working" because it has
    /// not reported anything. Both looked like the wrong thing from outside,
    /// and the one that needed a person looked like the one that did not.
    /// </para>
    /// <para>
    /// Worked out here rather than on each surface, because it needs the
    /// run's questions and the other nodes, and three surfaces working it out
    /// three ways is how they come to disagree.
    /// </para>
    /// </remarks>
    public string Activity(RunNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!Running)
        {
            return node.State is "working" ? "ended" : node.State;
        }

        if (Waiting.Any(gate => string.Equals(gate.Node, node.Node, StringComparison.Ordinal)))
        {
            return "waiting for you";
        }

        if (node.State is "working")
        {
            return "working";
        }

        if (node.State is "done" or "ended")
        {
            return "done";
        }

        // The lead is the first node a run launches. Once it has reported it
        // sits in its session while the nodes it asked for work.
        if (Nodes.Count > 0 && string.Equals(Nodes[0].Node, node.Node, StringComparison.Ordinal))
        {
            var busy = Nodes
                .Where(other => other.Node != node.Node && other.State is "working")
                .Select(other => other.Node)
                .ToList();

            return busy.Count > 0
                ? $"waiting on {string.Join(", ", busy)}"
                : "between turns";
        }

        return node.State;
    }

    /// <summary>Whether the run is still going, as far as its journal knows.</summary>
    /// <remarks>
    /// A run whose coordinator was killed says nothing more and still reads
    /// as running. What settles it is whether anything has been written
    /// lately, which is the caller's to judge against the clock.
    /// </remarks>
    public bool Running => Finished is null;

    /// <summary>When the run last wrote anything at all.</summary>
    public DateTimeOffset LastSeen =>
        Nodes.Count == 0 ? Started : Nodes.Max(node => node.LastSeen) > Started ? Nodes.Max(node => node.LastSeen) : Started;

    /// <summary>How long the run has been going, or how long it took.</summary>
    public TimeSpan Elapsed => (Finished ?? LastSeen) - Started;

    /// <summary>
    /// The longest this run should still take, or null when there is nothing
    /// to base it on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ceiling, not a prediction, and the difference matters enough to be
    /// the reason this is shaped the way it is. What a lead will ask for next
    /// is not known to anybody, so the only defensible arithmetic is: this
    /// run has taken this long for the rounds it has used, and it may use its
    /// remaining rounds. Whoever shows this must say which of the two it is.
    /// </para>
    /// <para>
    /// Null until a round has finished, because an estimate from no samples
    /// is a number with nothing behind it, and a finished run has nothing
    /// left to estimate.
    /// </para>
    /// </remarks>
    public TimeSpan? AtMostRemaining
    {
        get
        {
            if (!Running || Rounds <= 0 || RoundLimit <= Rounds)
            {
                return null;
            }

            return Elapsed / Rounds * (RoundLimit - Rounds);
        }
    }
}

/// <summary>Reads what a run wrote down.</summary>
public interface IRunJournal
{
    /// <summary>Runs on this machine, newest first.</summary>
    IReadOnlyList<string> List(int limit = 20);

    /// <summary>Every event of one run, in order. A line that will not parse is left out rather than throwing.</summary>
    OperationResult<IReadOnlyList<RunEvent>> Read(string runId);

    /// <summary>One run, folded into where everything got to.</summary>
    OperationResult<RunSummary> Summarise(string runId);

    /// <summary>The directory a run wrote into.</summary>
    string DirectoryOf(string runId);

    /// <summary>
    /// Forget one run: everything it wrote down, gone from this machine.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <param name="force">
    /// Take one that has not finished. Off by default, because a run's
    /// directory is how its nodes are told things while they work: answers to
    /// gates arrive as files appearing in it, so deleting it under a live run
    /// leaves processes waiting on answers that can no longer be given.
    /// </param>
    /// <returns>What was forgotten, so a caller can say what went.</returns>
    OperationResult<RunForgotten> Forget(string runId, bool force = false);
}

/// <summary>What forgetting a run took with it.</summary>
/// <param name="RunId">The run.</param>
/// <param name="Team">The team that ran, or empty where the journal never said.</param>
/// <param name="Bytes">How much disk it was holding.</param>
/// <param name="Files">How many files it had written.</param>
/// <param name="Unmerged">
/// Branches the run made and never got merged.
/// </param>
/// <remarks>
/// <para>
/// <paramref name="Unmerged"/> is the part worth printing. Nothing here
/// touches Git — a branch outlives the run that made it, and the working trees
/// under <c>worktrees/</c> outlive it too. What the journal was, for those, is
/// the only record that says which run produced them; forget it quietly and a
/// branch called <c>teams/20260917-1116-ed59/implementer-1</c> is a name with
/// nothing on this machine left to explain it.
/// </para>
/// </remarks>
public sealed record RunForgotten(
    string RunId,
    string Team,
    long Bytes,
    int Files,
    IReadOnlyList<string> Unmerged);

/// <inheritdoc />
public sealed class RunJournal : IRunJournal
{
    private readonly Platform.Abstractions.IPlatformPaths _paths;

    public RunJournal(Platform.Abstractions.IPlatformPaths paths) => _paths = paths;

    private string Root => Path.Combine(_paths.Paths.State, "teams", "runs");

    /// <inheritdoc />
    public string DirectoryOf(string runId) =>
        Path.Combine(Root, Names(runId) ? runId : Nowhere);

    /// <summary>
    /// Whether that is a run identifier, as opposed to a path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run is called 20260918-1436-ed59: a date, a time and four hex
    /// characters. Anything else is somebody's idea rather than a run's name,
    /// and the reason to say so here is that a run identifier becomes a
    /// directory - so an identifier carrying a separator or a <c>..</c> becomes
    /// a directory somewhere else entirely.
    /// </para>
    /// <para>
    /// That is not theoretical. <c>team message '..\probe' --message x</c>
    /// created a directory outside the runs root, wrote a file in it and
    /// reported success; the same thing was reachable from the dashboard, which
    /// can be on a network. Nothing downstream was at fault: everything that
    /// takes a run identifier joins it to a path, so the check belongs where
    /// the joining happens.
    /// </para>
    /// <para>
    /// Deliberately a shape rather than a lookup. A run being on this machine
    /// is a different question with a different answer - a journal from another
    /// machine is a run somebody may legitimately be reading - and conflating
    /// the two would make this refuse things that are fine.
    /// </para>
    /// </remarks>
    public static bool Names(string? runId) =>
        runId is { Length: > 0 and <= 64 }
        && runId.All(one => char.IsAsciiLetterOrDigit(one) || one == '-');

    /// <summary>Where an identifier that is not one is sent.</summary>
    /// <remarks>
    /// A name nothing can be, so the read that follows fails as "no such run"
    /// rather than reaching anything. Returning the root itself would make a
    /// malformed identifier read the runs directory as though it were a run.
    /// </remarks>
    private const string Nowhere = "not-a-run";

    /// <inheritdoc />
    public IReadOnlyList<string> List(int limit = 20)
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        // The identifier starts with the time it began, so its own name
        // sorts them and nothing has to be opened to order them.
        return Directory.EnumerateDirectories(Root)
            .Select(Path.GetFileName)
            .Where(name => name is { Length: > 0 })
            .OrderByDescending(name => name, StringComparer.Ordinal)
            .Take(limit)
            .ToList()!;
    }

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<RunEvent>> Read(string runId)
    {
        if (!Names(runId))
        {
            return OperationResult<IReadOnlyList<RunEvent>>.Fail(
                $"'{runId}' is not a run identifier.", Models.ExitCode.InvalidArguments);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var path = Path.Combine(DirectoryOf(runId), "journal.jsonl");

        if (!File.Exists(path))
        {
            return OperationResult<IReadOnlyList<RunEvent>>.Fail(
                $"No run named '{runId}' on this machine.", Models.ExitCode.ProjectNotFound);
        }

        string[] lines;

        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<RunEvent>>.Fail($"The run's journal could not be read: {ex.Message}");
        }

        var events = new List<RunEvent>();

        foreach (var line in lines)
        {
            if (Parse(line) is { } parsed)
            {
                events.Add(parsed);
            }
        }

        return OperationResult<IReadOnlyList<RunEvent>>.Ok(events);
    }

    /// <summary>
    /// Whether an event belongs to the shape of a run rather than to the
    /// running account of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A node writes a line for every tool call it makes and every sentence it
    /// says about itself. On one four-minute run that was 42 of 81 events.
    /// Both are worth keeping - they are how you tell a node looping on one
    /// file from one carefully reading forty - and neither answers "what
    /// happened in this run", which is what a log is read for first.
    /// </para>
    /// <para>
    /// This decides one printed view. Nothing is filtered out of the journal,
    /// and the full account stays the default.
    /// </para>
    /// </remarks>
    public static bool Happened(RunEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return !string.Equals(entry.Kind, "node.doing", StringComparison.Ordinal)
            && !string.Equals(entry.Kind, "node.said", StringComparison.Ordinal);
    }

    /// <summary>The same events, in the order they happened.</summary>
    /// <remarks>
    /// <para>
    /// The journal is appended to as things happen, so its order is the order
    /// things were <em>written down</em>, and for almost everything those are
    /// the same. They are not the same for an event harvested out of a side
    /// file: a node's permission decisions are folded in at the end of its
    /// turn, and one real run therefore recorded two of them three minutes
    /// late, after the line saying the node had ended.
    /// </para>
    /// <para>
    /// Stable, so that events sharing an instant keep the order they were
    /// written in, which is the only thing left that can tell them apart.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<RunEvent> InOrder(IEnumerable<RunEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return [.. events.OrderBy(one => one.At)];
    }

    /// <summary>
    /// One line as an event, or null.
    /// </summary>
    /// <remarks>
    /// A half-written last line is the ordinary case while a run is going:
    /// the journal is appended to as things happen, and a reader that threw
    /// on it could not be used to watch one.
    /// </remarks>
    public static RunEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var at = root.TryGetProperty("at", out var when) && when.TryGetDateTimeOffset(out var moment)
                ? moment
                : DateTimeOffset.MinValue;

            var node = root.TryGetProperty("node", out var who) && who.ValueKind == JsonValueKind.String
                ? who.GetString()
                : null;

            var data = root.TryGetProperty("data", out var carried)
                ? carried.Clone()
                : default;

            return new RunEvent(at, node, kind.GetString()!, data);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public OperationResult<RunSummary> Summarise(string runId)
    {
        var read = Read(runId);

        if (read.Failed)
        {
            return OperationResult<RunSummary>.Fail(read.Error!, read.ExitCode);
        }

        return OperationResult<RunSummary>.Ok(Fold(runId, DirectoryOf(runId), read.Value!));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The summary is read before anything is deleted, for two reasons. It is
    /// what refuses a run that has not finished, and it is what the caller
    /// prints afterwards — a directory that has gone cannot be asked what was
    /// in it.
    /// </para>
    /// <para>
    /// Deliberately only this directory. A run's branches, its working trees
    /// and anything it committed are Git's, and outlive it; a command called
    /// "forget the notes about it" that also deleted the work would be the
    /// worst kind of surprise. What it does instead is say what it is leaving.
    /// </para>
    /// </remarks>
    public OperationResult<RunForgotten> Forget(string runId, bool force = false)
    {
        var summary = Summarise(runId);

        if (summary.Failed)
        {
            return OperationResult<RunForgotten>.Fail(summary.Error!, summary.ExitCode);
        }

        var run = summary.Value!;

        if (run.Running && !force)
        {
            return OperationResult<RunForgotten>.Fail(
                $"'{runId}' has not finished. Stop it first with: loadout team halt {runId}",
                Models.ExitCode.PolicyViolation);
        }

        var directory = DirectoryOf(runId);

        long bytes = 0;
        var files = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                files++;
                bytes += new FileInfo(file).Length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Measuring is a courtesy. Failing to measure is not a reason to
            // refuse the thing that was asked for.
        }

        var unmerged = run.Branches
            .Where(branch => !run.Merged.Contains(branch, StringComparer.Ordinal))
            .ToList();

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<RunForgotten>.Fail(
                $"'{runId}' could not be forgotten: {ex.Message}");
        }

        return OperationResult<RunForgotten>.Ok(
            new RunForgotten(runId, run.Team, bytes, files, unmerged));
    }

    /// <summary>Folds a run's events into where everything got to.</summary>
    public static RunSummary Fold(string runId, string directory, IReadOnlyList<RunEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var team = string.Empty;
        var goal = string.Empty;
        var autonomy = string.Empty;
        string? project = null;
        string? path = null;
        decimal? budget = null;
        var quiet = 0;
        var started = events.Count > 0 ? events[0].At : DateTimeOffset.MinValue;
        DateTimeOffset? finished = null;
        string? ended = null;
        string? outcome = null;
        var cost = 0m;
        var rounds = 0;
        var limit = 0;
        var merged = new List<string>();
        var turns = new List<RunTurn>();
        var timeline = new List<RunRound>();
        var conflicts = 0;
        IReadOnlyList<RunCovered> covered = [];

        // Insertion order, because that is the order the run briefed them
        // and the order somebody reading it will expect.
        var nodes = new Dictionary<string, RunNode>(StringComparer.Ordinal);

        void Set(string name, Func<RunNode, RunNode> change)
        {
            nodes[name] = change(nodes.TryGetValue(name, out var existing)
                ? existing
                : new RunNode(name, "?", "working", 0, 0m, DateTimeOffset.MinValue));
        }

        foreach (var entry in events)
        {
            switch (entry.Kind)
            {
                case "run.started":
                    team = entry.Text("team") ?? team;
                    goal = entry.Text("goal") ?? goal;
                    autonomy = entry.Text("autonomy") ?? autonomy;
                    limit = (int)(entry.Number("rounds") ?? limit);

                    // Null for runs written before this was recorded, which is
                    // why everything downstream treats it as optional rather
                    // than as missing.
                    project = entry.Text("project");
                    path = entry.Text("path");
                    budget = entry.Number("budget");
                    started = entry.At;
                    break;

                // Picked up again after it had ended. Running from here, so
                // the ending it had is not its ending any more; and its cost is
                // the nodes' own figures again until it writes a new total,
                // because the total the first ending wrote stops at that point.
                case "run.reopened":
                    finished = null;
                    ended = null;
                    outcome = null;
                    cost = 0m;
                    limit = (int)(entry.Number("rounds") ?? limit);

                    // What it was picked up with, which the runner starts out
                    // held to and so never writes as a run.budget of its own.
                    budget = entry.Number("budget") ?? budget;
                    break;

                // Raised (or lowered) while it ran. The latest one is what it
                // is held to now, and what the page measures the spend against.
                case "run.budget":
                    budget = entry.Number("budget") ?? budget;
                    break;

                // How many rounds in a row have asked for nothing. Two ends
                // the run; one is worth somebody knowing about.
                case "round.ended":
                    quiet = (int)(entry.Number("quiet") ?? 0);

                    if (timeline.Count > 0)
                    {
                        timeline[^1] = timeline[^1] with
                        {
                            Ended = entry.At,
                            Requests = (int)(entry.Number("requests") ?? 0),
                        };
                    }

                    break;

                case "round.started":
                    // A run still going has no ending to count rounds from,
                    // and how far through it is is the thing somebody
                    // watching most wants.
                    rounds = (int)(entry.Number("round") ?? rounds + 1);
                    limit = (int)(entry.Number("of") ?? limit);

                    timeline.Add(new RunRound(rounds, entry.At));
                    break;

                case "run.finished":
                    finished = entry.At;
                    ended = entry.Text("ended");
                    outcome = entry.Text("outcome");
                    cost = entry.Number("cost") ?? cost;
                    rounds = (int)(entry.Number("rounds") ?? rounds);

                    if (entry.Data.ValueKind == JsonValueKind.Object
                        && entry.Data.TryGetProperty("merged", out var landed)
                        && landed.ValueKind == JsonValueKind.Array)
                    {
                        // The ending lists what merged and so does each
                        // merge as it happens, and a run read after it
                        // finished has both.
                        foreach (var branch in landed.EnumerateArray()
                            .Where(b => b.ValueKind == JsonValueKind.String)
                            .Select(b => b.GetString()!)
                            .Where(branch => !merged.Contains(branch, StringComparer.Ordinal)))
                        {
                            merged.Add(branch);
                        }
                    }

                    break;

                case "node.launched" when entry.Node is { Length: > 0 } launched:
                    Set(launched, node => node with
                    {
                        Role = entry.Text("role") ?? node.Role,
                        Model = entry.Text("model") ?? node.Model,
                        Base = entry.Text("base") ?? node.Base,
                        Branch = entry.Text("worktree") ?? node.Branch,
                        State = "working",
                        LastSeen = entry.At,
                        Started = node.Started ?? entry.At,
                    });

                    break;

                case "node.doing" when entry.Node is { Length: > 0 } busy:
                    Set(busy, node => node with { Doing = entry.Text("doing"), LastSeen = entry.At });
                    break;

                // Kept beside what was observed rather than replacing it. The
                // node's account of itself says why and may be wrong; the
                // run's says what happened and says nothing about why.
                case "node.said" when entry.Node is { Length: > 0 } saying:
                    Set(saying, node => node with { Said = entry.Text("line"), LastSeen = entry.At });
                    break;

                case "node.turn" when entry.Node is { Length: > 0 } turned:
                    turns.Add(new RunTurn(
                        entry.At,
                        turned,
                        rounds,
                        (int)(entry.Number("attempt") ?? 1),
                        (int)(entry.Number("turns") ?? 0),
                        entry.Number("cost") ?? 0m,
                        (int)(entry.Number("denials") ?? 0),
                        entry.Data.ValueKind == JsonValueKind.Object
                            && entry.Data.TryGetProperty("completed", out var got)
                            && got.ValueKind == JsonValueKind.True));

                    Set(turned, node => node with
                    {
                        Session = entry.Text("session") ?? node.Session,
                        Turns = node.Turns + (int)(entry.Number("turns") ?? 0),
                        CostUsd = node.CostUsd + (entry.Number("cost") ?? 0m),
                        Denials = node.Denials + (int)(entry.Number("denials") ?? 0),
                        LastSeen = entry.At,
                    });

                    break;

                case "report.checked" when entry.Node is { Length: > 0 } reported:
                    // Onto the exchange that produced it rather than beside it.
                    // The verdict arrives as its own event a moment later, and
                    // a turn whose outcome sits in a different row is a turn
                    // somebody has to join up by eye.
                    for (var back = turns.Count - 1; back >= 0; back--)
                    {
                        if (string.Equals(turns[back].Node, reported, StringComparison.Ordinal))
                        {
                            turns[back] = turns[back] with
                            {
                                Status = entry.Text("status"),
                                Outcome = entry.Text("outcome"),
                            };

                            break;
                        }
                    }

                    Set(reported, node => node with
                    {
                        State = entry.Text("status") ?? node.State,
                        LastSeen = entry.At,
                        Doing = null,
                    });

                    // The latest account wins, because a lead sent back for an
                    // unanswered criterion reports again and the second answer
                    // is the one that is true. A worker never carries coverage,
                    // so whichever node this is, an entry here came from the
                    // node that answers for the goal.
                    if (entry.Covered() is { Count: > 0 } said)
                    {
                        covered = said;
                    }

                    break;

                case "node.ended" when entry.Node is { Length: > 0 } finishedNode:
                {
                    /*
                        A node whose process has gone is not working.

                        This used to record only that it had been heard from,
                        so a node that never reported kept whatever state it
                        was given when it launched - and a run that failed at
                        launch showed its lead as "working" for ever, minutes
                        after the run had finished and the process had exited
                        one. The state a node reached is the one thing the
                        summary exists to say, so saying "working" about a
                        process that is gone is the worst of the answers
                        available.

                        A node that did report keeps what it reported: done,
                        blocked, failed and needs-decision all come from the
                        node's own account, and ending afterwards is ordinary.
                    */
                    var exit = entry.Number("exit");
                    var trouble = entry.Text("stderr");

                    Set(finishedNode, node => node with
                    {
                        State = string.Equals(node.State, "working", StringComparison.Ordinal)
                            ? exit is null or 0 ? "ended" : "failed"
                            : node.State,
                        LastSeen = entry.At,
                        Doing = null,

                        // Only when it went wrong. A node that exits cleanly
                        // can still have written to stderr, and that is not
                        // something to put in front of somebody as a fault.
                        Trouble = exit is not (null or 0) && trouble is { Length: > 0 } said
                            ? said.Trim()
                            : node.Trouble,
                    });

                    break;
                }

                case "merge.conflicted":
                    conflicts++;
                    break;

                case "merge.done" when entry.Text("branch") is { Length: > 0 } branch:
                    if (!merged.Contains(branch, StringComparer.Ordinal))
                    {
                        merged.Add(branch);
                    }

                    break;
            }
        }

        // The run's own cost is what it wrote at the end; while it is still
        // going, the nodes' own figures are the best there is.
        var running = nodes.Values.Sum(node => node.CostUsd);

        return new RunSummary(
            runId,
            directory,
            team,
            goal,
            autonomy,
            started,
            finished,
            ended,
            cost > 0m ? cost : running,
            rounds,
            nodes.Values.ToList(),
            merged,
            nodes.Values.Where(node => node.Branch is { Length: > 0 }).Select(node => node.Branch!).ToList(),
            limit,
            project,
            path,

            // From the directory, not from the events: a question is answered
            // by a file appearing, and the journal hears about it after.
            NodePermissions.Pending(directory),
            budget,
            quiet,
            turns,
            timeline,
            conflicts,
            covered,
            outcome);
    }

    /// <summary>One event as a line somebody can read.</summary>
    public static string Describe(RunEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var who = entry.Node is { Length: > 0 } node ? node : "run";

        return $"{entry.At.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}  "
            + $"{who,-16} {Wording(entry)}";
    }

    /// <summary>
    /// What an event says, without the time or the node in front of it.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Describe"/> because two events that say the
    /// same thing are worth showing as one thing and a count, and the only
    /// part of a whole line that is ever the same is this. A node reading the
    /// same file forty times writes forty lines that differ in nothing but
    /// their clock.
    /// </remarks>
    public static string Wording(RunEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Kind switch
        {
            "run.started" => $"started {entry.Text("team")}"
                + (entry.Text("project") is { Length: > 0 } on ? $" on {on}" : string.Empty)
                + $", {entry.Text("autonomy")}: {entry.Text("goal")}",
            "run.finished" => $"finished: {entry.Text("ended")}"
                + (entry.Number("rounds") is { } rounds ? $" - {rounds:0} round(s)" : string.Empty)
                + (entry.Number("cost") is { } spent ? $", ${spent:0.00}" : string.Empty),
            "round.started" => $"round {entry.Number("round")} of {entry.Number("of")}",
            "run.reopened" => "picked up again"
                + (entry.Text("was") is { Length: > 0 } was ? $" (it had ended: {was})" : string.Empty)
                + (entry.Text("session") is { Length: > 0 } ? ", the lead resuming its own session" : ", with a fresh lead told where it got to"),
            "run.budget" => $"budget set to ${entry.Number("budget"):0.00}"
                + (entry.Text("by") is { Length: > 0 } by ? $" by {by}" : string.Empty),
            "node.doing" => entry.Text("doing") ?? "working",
            "node.said" => entry.Text("line") ?? "working",
            // The three lines one decision writes. A node stops and says what
            // it wants; a person answers; the answer is recorded against the
            // rules that would otherwise have decided it. All three printed as
            // their bare kind - "node.asked", and nothing about what was asked
            // - which made the most consequential moment in a run the least
            // legible line in its log.
            "node.asked" => $"stopped to ask: {entry.Word("tool")}"
                + (entry.Word("target") is { Length: > 0 } wanted ? $" {wanted}" : string.Empty),
            "node.answered" => (entry.Yes("allowed") ? "was allowed " : "was refused ")
                + entry.Word("tool")
                + (entry.Word("chosen") is { } chosen
                    && chosen.StartsWith("yes, and don't ask again", StringComparison.Ordinal)
                        ? ", and won't be asked again this run"
                        : string.Empty),
            "permission.asked" => (entry.Yes("allowed") ? "allowed " : "refused ")
                + entry.Word("tool")
                + (entry.Word("target") is { Length: > 0 } at ? $" {at}" : string.Empty),
            "node.launched" => $"launched as {entry.Text("role")}"
                + (entry.Text("worktree") is { Length: > 0 } tree ? $" on {tree}" : string.Empty),
            "node.turn" => $"turn {entry.Number("attempt")}: {entry.Number("turns")} exchange(s), "
                + $"${entry.Number("cost"):0.00}"
                + (entry.Number("denials") is > 0 ? $", {entry.Number("denials")} denial(s)" : string.Empty),
            "report.checked" => $"reported {entry.Text("status")}, {entry.Text("outcome")}"
                + (entry.Words("reasons") is { Count: > 0 } why
                    ? $": {string.Join("; ", why)}"
                    : string.Empty),
            "report.unreadable" => $"gave no report: {entry.Text("Error")}",
            "node.ended" => $"ended, exit {entry.Number("exit")}",
            "node.failed" => $"could not start: {entry.Text("error")}",
            "request.refused" => $"asked for a node it may not: {entry.Text("reason")}",
            "gate.reminded" => "told it finished without the merge gate"
                + (entry.Words("pending") is { Count: > 0 } waiting
                    ? $": {string.Join(", ", waiting)}"
                    : string.Empty),
            "gate.refused" => "the merge gate was not satisfied",
            "gate.opened" => $"merge gate open for {entry.Text("branch")}",
            "gate.decided" => $"merge {(entry.Yes("allowed") ? "allowed" : "refused")}",
            "merge.done" => $"merged {entry.Text("branch")} into {entry.Text("target")}",
            "merge.conflicted" => $"{entry.Text("branch")} conflicted",
            "merge.failed" => $"{entry.Text("branch")} could not be merged: {entry.Text("error")}",
            "worktree.tidied" => $"cleared away {entry.Text("branch")}",
            "node.told" => $"was told: {entry.Text("message")}",
            "brief.revised" => $"briefed instead: {entry.Text("now")}",
            "decision" => $"decided: {entry.Text("question")} -> {entry.Text("answer")}",
            _ => entry.Kind,
        };
    }
}
