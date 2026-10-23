using System.Text.Json;
using Loadout.Models.Triggers;

namespace Loadout.Core.Triggers;

/// <summary>A change to the rules, asked of the running daemon.</summary>
/// <param name="Id">
/// Its own name. A retry carries the same one, so a change that landed before
/// its answer was lost is not made twice.
/// </param>
/// <param name="Verb">One of <see cref="TriggerVerbs"/>.</param>
/// <param name="Rule">For a save: the rule.</param>
/// <param name="RuleId">For a remove, a resume or a release: which rule.</param>
/// <param name="Subject">For a release: only this subject, or every stale one of the rule when null.</param>
/// <param name="Author">For a distrust: the GitHub id of the account taken off the allow-list.</param>
/// <param name="Login">For a distrust: its login, for the journal.</param>
public sealed record TriggerAsk(
    string Id,
    string Verb,
    TriggerRule? Rule = null,
    string? RuleId = null,
    string? Subject = null,
    long? Author = null,
    string? Login = null);

/// <summary>What the daemon made of a change it was asked for.</summary>
/// <param name="Id">The request it answers.</param>
/// <param name="Applied">Whether the change was made, now or by an earlier copy of the same request.</param>
/// <param name="Message">What happened, in words for the person who asked.</param>
/// <param name="Version">For a save: the version the rule now has.</param>
public sealed record TriggerAnswer(string Id, bool Applied, string Message, int? Version = null);

/// <summary>What a request to the daemon may ask.</summary>
public static class TriggerVerbs
{
    public const string Save = "save";
    public const string Remove = "remove";
    public const string Resume = "resume";
    public const string Release = "release";

    /// <summary>Somebody was taken off the allow-list: withdraw what waits on their trust.</summary>
    public const string Distrust = "distrust";
}

/// <summary>
/// Where changes to the rules wait for the daemon, and where its answers wait
/// for whoever asked.
/// </summary>
/// <remarks>
/// <para>
/// Files under <c>&lt;State&gt;/triggers/inbox</c> rather than a route on the
/// dashboard, because a daemon started with <c>--no-dashboard</c> serves no
/// page and still acts on rules. Each request is <c>&lt;id&gt;.ask.json</c>,
/// written aside and moved so the daemon never reads half of one; each answer
/// is <c>&lt;id&gt;.answer.json</c>, written the same way.
/// </para>
/// <para>
/// The daemon answers a request once: a request whose answer is already there
/// is taken as handled and dropped, which is what lets a retry carry the same
/// id safely.
/// </para>
/// </remarks>
public sealed class TriggerInbox
{
    /// <summary>How long an answer nobody collected is kept.</summary>
    public static readonly TimeSpan AnswersKept = TimeSpan.FromDays(1);

    /// <param name="state">The state directory.</param>
    public TriggerInbox(string state) => Directory = Path.Combine(state, "triggers", "inbox");

    /// <summary>Where the requests and answers are.</summary>
    public string Directory { get; }

    private string AskPath(string id) => Path.Combine(Directory, id + ".ask.json");

    private string AnswerPath(string id) => Path.Combine(Directory, id + ".answer.json");

    /// <summary>
    /// Leaves a request and waits for its answer, or null when none came in
    /// time. The answer is taken away once read.
    /// </summary>
    public async Task<TriggerAnswer?> AskAsync(TriggerAsk asking, TimeSpan wait, TimeProvider time, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(asking);
        ArgumentNullException.ThrowIfNull(time);

        // A retry finds the first attempt's answer here when the first attempt
        // landed after its asker had given up.
        if (Take(asking.Id) is { } already)
        {
            return already;
        }

        Write(AskPath(asking.Id), asking);

        // Elapsed time, not the time of day: a clock moved while somebody
        // waited would otherwise cut the wait short or make it endless.
        var started = time.GetTimestamp();

        while (time.GetElapsedTime(started) < wait)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), time, ct).ConfigureAwait(false);

            if (Take(asking.Id) is { } answer)
            {
                return answer;
            }
        }

        return null;
    }

    /// <summary>The requests waiting, oldest first, each with a way to answer it.</summary>
    /// <remarks>
    /// A request already answered is dropped rather than returned, so the
    /// daemon applies each one once whatever the asker does.
    /// </remarks>
    public IReadOnlyList<TriggerAsk> Waiting()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var waiting = new List<TriggerAsk>();

        foreach (var file in new DirectoryInfo(Directory).GetFiles("*.ask.json").OrderBy(one => one.LastWriteTimeUtc))
        {
            var id = file.Name[..^".ask.json".Length];

            if (File.Exists(AnswerPath(id)))
            {
                Delete(file.FullName);

                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<TriggerAsk>(File.ReadAllText(file.FullName), TriggerJournal.Json) is { } one
                    && one.Id == id)
                {
                    waiting.Add(one);
                }
                else
                {
                    Answer(new TriggerAnswer(id, false, "The request could not be read."));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Being written as it was read; the next look takes it.
            }
        }

        return waiting;
    }

    /// <summary>Writes the answer to a request, and drops the request.</summary>
    public void Answer(TriggerAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        Write(AnswerPath(answer.Id), answer);
        Delete(AskPath(answer.Id));
    }

    /// <summary>Drops answers nobody collected.</summary>
    public void Sweep(DateTimeOffset now)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        foreach (var file in new DirectoryInfo(Directory).GetFiles("*.answer.json"))
        {
            if (now - file.LastWriteTimeUtc > AnswersKept)
            {
                Delete(file.FullName);
            }
        }
    }

    private TriggerAnswer? Take(string id)
    {
        var path = AnswerPath(id);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var answer = JsonSerializer.Deserialize<TriggerAnswer>(File.ReadAllText(path), TriggerJournal.Json);
            Delete(path);

            return answer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void Write<T>(string path, T value)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var aside = path + ".tmp";
        File.WriteAllText(aside, JsonSerializer.Serialize(value, TriggerJournal.Json));
        File.Move(aside, path, overwrite: true);
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Gone already, or held for a moment; the next sweep takes it.
        }
    }
}
