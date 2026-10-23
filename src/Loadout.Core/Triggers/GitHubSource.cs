using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Models.Triggers;

namespace Loadout.Core.Triggers;

/// <summary>One repository a rule watches, and the project it belongs to.</summary>
/// <param name="Project">The project's slug, which events name as theirs.</param>
/// <param name="Repo">The repository, as <c>owner/name</c>.</param>
public sealed record WatchedRepo(string Project, string Repo);

/// <summary>What one look at GitHub found: events for the rules, and notes for the journal.</summary>
/// <param name="Events">What happened, in the order it should be decided.</param>
/// <param name="Notes">What happened to the source itself: a token lapsing, renewed, or about to expire.</param>
public sealed record GitHubLook(IReadOnlyList<TriggerEvent> Events, IReadOnlyList<TriggerEntry> Notes);

/// <summary>
/// Polls the repositories rules watch, and turns what changed into events:
/// pull requests opened, pushed to, readied, drafted, retargeted, closed,
/// merged and reopened; Actions runs that failed; issues opened; comments.
/// </summary>
/// <remarks>
/// <para>
/// Four reads a repository a look, each with the tag from the last, so a
/// repository where nothing happened costs no quota: GitHub does not count a
/// 304. Only repositories a rule watches are read at all.
/// </para>
/// <para>
/// A pull request's events share one subject, so one push supersedes
/// everything waiting on it. A failed run belongs to each open pull request
/// whose head it ran on, and to its branch where none is open; when a pull
/// request is later opened from that branch, the branch's waiting actions fold
/// into it. A reopened pull request is a fresh subject, linked to the one it
/// was, so nothing withdrawn when it closed comes back by surprise.
/// </para>
/// <para>
/// Whether each author is trusted is decided as the event is made, from
/// GitHub's association and the allow-list as they are now: trust rises only
/// with the next event, never on something already waiting. A comment on a
/// pull request is trusted only when its author and the pull request's are,
/// because the run it starts reads the pull request too.
/// </para>
/// <para>
/// The first look at each repository writes down what is there and posts
/// nothing, for the reason the other sources give. After that, a daemon that
/// was off sees each pull request once, as it is now, rather than every push
/// it missed; one that changed more than a day ago goes on the stale list.
/// </para>
/// <para>
/// Where it had got to is kept in <c>&lt;State&gt;/triggers/cursors/github.json</c>,
/// and how it is doing in <c>&lt;State&gt;/triggers/sources/github.json</c>.
/// </para>
/// </remarks>
public sealed partial class GitHubSource
{
    /// <summary>What its events name as their source.</summary>
    public const string Name = "github";

    /// <summary>How often a repository is read when quota is plentiful.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    /// <summary>The slowest a quiet repository is read when quota runs short.</summary>
    public static readonly TimeSpan Slowest = TimeSpan.FromMinutes(16);

    /// <summary>How long a repository must have been without events to count as quiet.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromHours(1);

    /// <summary>How far ahead of a token's expiry the journal is warned.</summary>
    public static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(7);

    private const string Pulls = "pulls";
    private const string Runs = "runs";
    private const string Issues = "issues";
    private const string Comments = "comments";

    private readonly IGitHubReads _reads;
    private readonly string _cursor;
    private readonly string _health;
    private readonly TimeSpan _every;

    /// <param name="state">The state directory.</param>
    /// <param name="reads">Reads from GitHub.</param>
    /// <param name="every">How often a repository is read when quota is plentiful; a test shortens it.</param>
    public GitHubSource(string state, IGitHubReads reads, TimeSpan? every = null)
    {
        _reads = reads;
        _every = every ?? Every;
        _cursor = Path.Combine(state, "triggers", "cursors", "github.json");
        _health = HealthPath(state);
    }

    /// <summary>Where the source's health is kept, for the commands that show it.</summary>
    public static string HealthPath(string state) => Path.Combine(state, "triggers", "sources", "github.json");

    /// <summary>Where the source last got to and how it is doing, or nothing before its first look.</summary>
    public static GitHubHealth? ReadHealth(string state) => ReadJson<GitHubHealth>(HealthPath(state));

    /// <summary>The logins last seen for each account id, for noticing a renamed allow-list entry.</summary>
    public static IReadOnlyDictionary<long, string> Logins(string state) =>
        ReadJson<GitHubCursor>(Path.Combine(state, "triggers", "cursors", "github.json"))?.Logins
        ?? new Dictionary<long, string>();

    /// <summary>Reads each watched repository that is due, and says what changed.</summary>
    public async Task<GitHubLook> LookAsync(
        IReadOnlyList<WatchedRepo> watched,
        TriggerCredential credential,
        IReadOnlyList<TriggerAllowEntry> allowed,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(watched);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(allowed);

        var cursor = ReadJson<GitHubCursor>(_cursor) ?? new GitHubCursor();
        var health = ReadJson<GitHubHealth>(_health) ?? new GitHubHealth();
        var events = new List<TriggerEvent>();
        var notes = new List<TriggerEntry>();

        TriggerEntry Note(string kind, string reason) =>
            new(now, kind, Source: Name, Reason: reason);

        health.Credential = credential.Kind;
        health.Looked = now;

        if (credential.Token is null)
        {
            health.Problem = "No token to read GitHub with. Set one with 'loadout trigger token set', "
                + "or sign in with 'gh auth login'.";
            Save(_health, health);

            return new GitHubLook([], []);
        }

        // A lapsed token stops polling until it is changed, rather than
        // quietly falling back to the gh token: the fallback is for somebody
        // who never set one, not cover for one that has run out.
        if (health.Lapsed is { } lapsed)
        {
            if (string.Equals(lapsed, credential.Fingerprint, StringComparison.Ordinal))
            {
                Save(_health, health);

                return new GitHubLook([], []);
            }

            health.Lapsed = null;
            health.Problem = null;
            notes.Add(Note(TriggerEntryKinds.Renewed,
                $"the {credential.Kind} changed, so polling starts again and catches up on each subject as it is now"));
        }

        foreach (var one in watched.DistinctBy(each => each.Repo, StringComparer.OrdinalIgnoreCase))
        {
            if (!cursor.Repos.TryGetValue(one.Repo, out var seen))
            {
                seen = new RepoSeen();
                cursor.Repos[one.Repo] = seen;
            }

            var shown = health.Repos.TryGetValue(one.Repo, out var was) ? was : new RepoHealth();
            health.Repos[one.Repo] = shown;

            if (seen.Next is { } next && next > now)
            {
                continue;
            }

            var before = events.Count;
            var problem = await ReadRepoAsync(one, seen, credential, allowed, cursor, health, events, now, ct).ConfigureAwait(false);

            if (problem is GitHubReply { Refused: true })
            {
                health.Lapsed = credential.Fingerprint;
                health.Problem = $"GitHub refused the {credential.Kind}, so polling has stopped until it is replaced. "
                    + (credential.Kind == TriggerCredential.Dedicated
                        ? "Set a new one with: loadout trigger token set"
                        : "Sign in again with: gh auth login");
                notes.Add(Note(TriggerEntryKinds.Lapsed,
                    $"GitHub refused the {credential.Kind}; polling stopped until it is replaced, then catches up"));
                events.RemoveRange(before, events.Count - before);

                break;
            }

            shown.Last = now;
            shown.Problem = problem switch
            {
                null => null,
                { Problem: { } said } => said,
                var reply => $"GitHub answered {reply.Status}",
            };

            if (events.Count > before)
            {
                seen.LastEvent = now;
            }

            var interval = Interval(seen, health, now, _every);

            seen.Next = now + interval;
            shown.Every = interval;
            shown.Slowed = interval > _every;
        }

        if (health.Expires is { } expires && expires - now <= ExpiryWarning
            && health.WarnedExpiry != expires)
        {
            health.WarnedExpiry = expires;
            notes.Add(Note(TriggerEntryKinds.Warning,
                $"the {credential.Kind} expires {expires:yyyy-MM-dd HH:mm} UTC; renew it with: loadout trigger token set"));
        }

        if (WritesWith(health.Scopes) is { } scopes && health.ScopeWarned != credential.Fingerprint)
        {
            health.ScopeWarned = credential.Fingerprint;
            notes.Add(Note(TriggerEntryKinds.Warning,
                $"the {credential.Kind} can write ({scopes}); polling only reads, so a read-only token is safer "
                + "in a process nobody is watching"));
        }

        // Written before the events are handed on, so a daemon killed between
        // the two misses them rather than acting on them twice.
        Save(_cursor, cursor);
        Save(_health, health);

        return new GitHubLook(events, notes);
    }

    /// <summary>How long until a repository is read again.</summary>
    /// <remarks>
    /// Quiet repositories give way first when quota runs short, so those with
    /// something going on stay quick. A slowed read is shown in the source's
    /// health, and catch-up on each subject's latest state covers anything a
    /// slower read would otherwise have seen in between.
    /// </remarks>
    internal static TimeSpan Interval(RepoSeen seen, GitHubHealth health, DateTimeOffset now, TimeSpan every)
    {
        if (health.Remaining is not { } remaining || health.Limit is not { } limit || limit <= 0)
        {
            return every;
        }

        var quiet = seen.LastEvent is not { } last || now - last >= Quiet;

        if (remaining * 5 < limit)
        {
            if (!quiet)
            {
                seen.Every = every;

                return every;
            }

            var slower = TimeSpan.FromTicks(Math.Max(seen.Every.Ticks, every.Ticks) * 2);
            seen.Every = slower > Slowest ? Slowest : slower;

            return seen.Every;
        }

        if (remaining * 2 > limit)
        {
            seen.Every = every;
        }

        return seen.Every > every ? seen.Every : every;
    }

    /// <summary>The write scopes a classic token holds, or null where it holds none or GitHub did not say.</summary>
    public static string? WritesWith(string? scopes)
    {
        if (scopes is not { Length: > 0 })
        {
            return null;
        }

        var writing = scopes
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(one => one is "repo" or "public_repo" or "workflow" or "delete_repo"
                || one.StartsWith("write:", StringComparison.Ordinal)
                || one.StartsWith("admin:", StringComparison.Ordinal))
            .ToList();

        return writing.Count > 0 ? string.Join(", ", writing) : null;
    }

    private async Task<GitHubReply?> ReadRepoAsync(
        WatchedRepo one,
        RepoSeen seen,
        TriggerCredential credential,
        IReadOnlyList<TriggerAllowEntry> allowed,
        GitHubCursor cursor,
        GitHubHealth health,
        List<TriggerEvent> events,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var repo = one.Repo;

        foreach (var (endpoint, path) in new[]
        {
            (Pulls, $"repos/{repo}/pulls?state=all&sort=updated&direction=desc&per_page=50"),
            (Runs, $"repos/{repo}/actions/runs?status=failure&per_page=30"),
            (Issues, $"repos/{repo}/issues?state=all&sort=created&direction=desc&per_page=30"),
            (Comments, $"repos/{repo}/issues/comments?sort=created&direction=desc&per_page=30"),
        })
        {
            var reply = await _reads.GetAsync(credential, path, seen.Tags.GetValueOrDefault(endpoint), ct).ConfigureAwait(false);

            health.Remaining = reply.Remaining ?? health.Remaining;
            health.Limit = reply.Limit ?? health.Limit;
            health.Scopes = reply.Scopes ?? health.Scopes;
            health.Expires = reply.Expires ?? health.Expires;

            if (reply.Refused)
            {
                return reply;
            }

            if (reply.Unchanged)
            {
                continue;
            }

            if (reply.Status != 200 || reply.Body is null)
            {
                return reply;
            }

            var first = !seen.Looked.Contains(endpoint);
            List<TriggerEvent> found;

            try
            {
                using var document = JsonDocument.Parse(reply.Body);
                var root = endpoint == Runs
                    ? document.RootElement.GetProperty("workflow_runs")
                    : document.RootElement;

                found = endpoint switch
                {
                    Pulls => ReadPulls(one, seen, root, allowed, cursor, first),
                    Runs => ReadRuns(one, seen, root, allowed, cursor, first),
                    Issues => ReadIssues(one, seen, root, allowed, cursor, first),
                    _ => ReadComments(one, seen, root, allowed, cursor, first),
                };
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                return new GitHubReply(reply.Status, Problem: $"GitHub answered {endpoint} in a shape Loadout does not read: {ex.Message}");
            }

            seen.Tags[endpoint] = reply.ETag ?? string.Empty;
            seen.Looked.Add(endpoint);
            events.AddRange(found);
        }

        return null;
    }

    private static List<TriggerEvent> ReadPulls(
        WatchedRepo one, RepoSeen seen, JsonElement root, IReadOnlyList<TriggerAllowEntry> allowed, GitHubCursor cursor, bool first)
    {
        var events = new List<TriggerEvent>();
        var listed = new HashSet<int>();

        foreach (var item in root.EnumerateArray())
        {
            var now = PullSeen.From(item);
            var at = Time(item, "updated_at");
            listed.Add(now.Number);
            cursor.Logins[now.AuthorId] = now.Author;

            if (!seen.Pulls.TryGetValue(now.Number, out var was))
            {
                seen.Pulls[now.Number] = now;

                // Opened and closed between two looks, or there before the
                // first: written down, not news.
                if (!first && now.State == "open")
                {
                    events.Add(PullEvent(TriggerEventKinds.PrOpened, one, now, allowed, at, Folds(one.Repo, now)));
                }

                continue;
            }

            now.Generation = was.Generation;
            seen.Pulls[now.Number] = now;

            if (first)
            {
                continue;
            }

            if (was.State == "open" && now.State != "open")
            {
                events.Add(PullEvent(
                    now.State == "merged" ? TriggerEventKinds.PrMerged : TriggerEventKinds.PrClosed, one, now, allowed, at));

                continue;
            }

            if (was.State != "open" && now.State == "open")
            {
                var before = Subject(one.Repo, now.Number, was.Generation);

                now.Generation = was.Generation + 1;
                events.Add(PullEvent(
                    TriggerEventKinds.PrReopened, one, now, allowed, at,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["was"] = before }));

                continue;
            }

            if (now.State != "open")
            {
                continue;
            }

            if (!string.Equals(was.Base, now.Base, StringComparison.Ordinal))
            {
                events.Add(PullEvent(
                    TriggerEventKinds.PrRetargeted, one, now, allowed, at,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["was-base"] = was.Base }));
            }

            if (was.Draft != now.Draft)
            {
                events.Add(PullEvent(now.Draft ? TriggerEventKinds.PrDraft : TriggerEventKinds.PrReady, one, now, allowed, at));
            }

            if (!string.Equals(was.Head, now.Head, StringComparison.Ordinal))
            {
                events.Add(PullEvent(TriggerEventKinds.PrUpdated, one, now, allowed, at));
            }
        }

        // Closed ones that have dropped off the list have nothing more to say;
        // open ones are kept, so a failed run can still find them.
        foreach (var gone in seen.Pulls.Where(each => each.Value.State != "open" && !listed.Contains(each.Key)).ToList())
        {
            seen.Pulls.Remove(gone.Key);
        }

        return events;
    }

    private static List<TriggerEvent> ReadRuns(
        WatchedRepo one, RepoSeen seen, JsonElement root, IReadOnlyList<TriggerAllowEntry> allowed, GitHubCursor cursor, bool first)
    {
        var events = new List<TriggerEvent>();

        // Oldest first, so the journal reads in the order they happened.
        foreach (var item in root.EnumerateArray().Reverse())
        {
            var id = item.GetProperty("id").GetInt64();

            if (seen.Runs.Contains(id))
            {
                continue;
            }

            seen.Runs.Add(id);

            if (first || Text(item, "conclusion") != "failure")
            {
                continue;
            }

            var head = Text(item, "head_sha");
            var branch = Text(item, "head_branch");
            var from = item.TryGetProperty("head_repository", out var headRepo) && headRepo.ValueKind == JsonValueKind.Object
                ? Text(headRepo, "full_name")
                : one.Repo;
            var at = Time(item, "updated_at");
            var run = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["workflow"] = Text(item, "name"),
                ["run"] = id.ToString(CultureInfo.InvariantCulture),
                ["url"] = Text(item, "html_url"),
            };

            // Every open pull request whose head it ran on: a branch can have
            // one into main and one into a release branch, and rules can tell
            // them apart by base.
            var pulls = seen.Pulls.Values
                .Where(pull => pull.State == "open"
                    && (string.Equals(pull.Head, head, StringComparison.Ordinal)
                        || (string.Equals(pull.Branch, branch, StringComparison.Ordinal)
                            && string.Equals(pull.HeadRepo, from, StringComparison.OrdinalIgnoreCase))))
                .OrderBy(pull => pull.Number)
                .ToList();

            if (pulls.Count > 0)
            {
                foreach (var pull in pulls)
                {
                    events.Add(PullEvent(TriggerEventKinds.CiFailed, one, pull, allowed, at, run, $":run{id}"));
                }

                continue;
            }

            var actor = item.TryGetProperty("actor", out var who) && who.ValueKind == JsonValueKind.Object ? who : default;
            var login = actor.ValueKind == JsonValueKind.Object ? Text(actor, "login") : string.Empty;
            var actorId = actor.ValueKind == JsonValueKind.Object && actor.TryGetProperty("id", out var number) ? number.GetInt64() : 0;
            var own = string.Equals(from, one.Repo, StringComparison.OrdinalIgnoreCase);

            if (actorId > 0)
            {
                cursor.Logins[actorId] = login;
            }

            // A branch in the repository itself was pushed by somebody who can
            // push there; one from a fork was not.
            var trust = own
                ? new TrustVerdict(true, "pushed to the repository itself")
                : TriggerTrust.Decide(null, actorId > 0 ? actorId : null, one.Repo, allowed) is { Trusted: true } listedTrust
                    ? listedTrust
                    : new TrustVerdict(false, $"from a fork, {from}");

            var fields = new Dictionary<string, string>(run, StringComparer.OrdinalIgnoreCase)
            {
                ["repo"] = one.Repo,
                ["number"] = string.Empty,
                ["base"] = string.Empty,
                ["branch"] = branch,
                ["head"] = head,
                ["author"] = login,
                ["author-id"] = actorId.ToString(CultureInfo.InvariantCulture),
                ["trust"] = trust.Word,
                ["because"] = trust.Because,
                ["draft"] = "false",
                ["state"] = "open",
                ["title"] = Text(item, "display_title"),
            };

            events.Add(new TriggerEvent(
                $"github:{one.Repo}:run{id}", at, Name, TriggerEventKinds.CiFailed,
                BranchSubject(one.Repo, branch), one.Project, fields, []));
        }

        // A run listed once is never listed as new again; the last few hundred
        // are plenty to remember, because the list is newest first and short.
        if (seen.Runs.Count > 300)
        {
            seen.Runs.RemoveRange(0, seen.Runs.Count - 300);
        }

        return events;
    }

    private static List<TriggerEvent> ReadIssues(
        WatchedRepo one, RepoSeen seen, JsonElement root, IReadOnlyList<TriggerAllowEntry> allowed, GitHubCursor cursor, bool first)
    {
        var events = new List<TriggerEvent>();
        var highest = seen.Issue;

        foreach (var item in root.EnumerateArray().Reverse())
        {
            var number = item.GetProperty("number").GetInt32();
            highest = Math.Max(highest, number);

            // The issues list carries pull requests as well; they have events of their own.
            if (first || number <= seen.Issue || item.TryGetProperty("pull_request", out _))
            {
                continue;
            }

            var (login, id) = User(item);
            var trust = TriggerTrust.Decide(Text(item, "author_association"), id, one.Repo, allowed);

            cursor.Logins[id] = login;

            events.Add(new TriggerEvent(
                $"github:{one.Repo}#{number}:issue-opened",
                Time(item, "created_at"),
                Name,
                TriggerEventKinds.IssueOpened,
                $"issue:{one.Repo}#{number}",
                one.Project,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["repo"] = one.Repo,
                    ["number"] = number.ToString(CultureInfo.InvariantCulture),
                    ["author"] = login,
                    ["author-id"] = id.ToString(CultureInfo.InvariantCulture),
                    ["trust"] = trust.Word,
                    ["because"] = trust.Because,
                    ["title"] = Text(item, "title"),
                    ["url"] = Text(item, "html_url"),
                },
                []));
        }

        seen.Issue = highest;

        return events;
    }

    private static List<TriggerEvent> ReadComments(
        WatchedRepo one, RepoSeen seen, JsonElement root, IReadOnlyList<TriggerAllowEntry> allowed, GitHubCursor cursor, bool first)
    {
        var events = new List<TriggerEvent>();
        var highest = seen.Comment;

        foreach (var item in root.EnumerateArray().Reverse())
        {
            var id = item.GetProperty("id").GetInt64();
            highest = Math.Max(highest, id);

            if (first || id <= seen.Comment)
            {
                continue;
            }

            var number = Number(Text(item, "issue_url"));
            var (login, author) = User(item);
            var trust = TriggerTrust.Decide(Text(item, "author_association"), author, one.Repo, allowed);
            var pull = seen.Pulls.GetValueOrDefault(number);

            cursor.Logins[author] = login;

            // The run a comment starts reads the pull request too, so the
            // pull request's own author has to be trusted as well.
            if (pull is not null && trust.Trusted
                && TriggerTrust.Decide(pull.Association, pull.AuthorId, one.Repo, allowed) is { Trusted: false } opened)
            {
                trust = new TrustVerdict(false, $"the pull request's author: {opened.Because}");
            }

            var mention = Mention().Match(Text(item, "body"));

            events.Add(new TriggerEvent(
                $"github:{one.Repo}:comment{id}",
                Time(item, "created_at"),
                Name,
                TriggerEventKinds.Comment,
                pull is null ? $"issue:{one.Repo}#{number}" : Subject(one.Repo, number, pull.Generation),
                one.Project,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["repo"] = one.Repo,
                    ["number"] = number.ToString(CultureInfo.InvariantCulture),
                    ["on"] = pull is null ? "issue" : "pr",
                    ["author"] = login,
                    ["author-id"] = author.ToString(CultureInfo.InvariantCulture),
                    ["trust"] = trust.Word,
                    ["because"] = trust.Because,
                    ["mention"] = mention.Success ? mention.Groups[1].Value.ToLowerInvariant() : string.Empty,
                    ["draft"] = pull?.Draft == true ? "true" : "false",
                    ["state"] = pull?.State ?? "open",
                    ["url"] = Text(item, "html_url"),
                },
                []));
        }

        seen.Comment = highest;

        return events;
    }

    /// <summary>One pull request event, with the fields every one of them carries.</summary>
    private static TriggerEvent PullEvent(
        string kind,
        WatchedRepo one,
        PullSeen pull,
        IReadOnlyList<TriggerAllowEntry> allowed,
        DateTimeOffset at,
        IReadOnlyDictionary<string, string>? more = null,
        string? suffix = null)
    {
        var trust = TriggerTrust.Decide(pull.Association, pull.AuthorId, one.Repo, allowed);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["repo"] = one.Repo,
            ["number"] = pull.Number.ToString(CultureInfo.InvariantCulture),
            ["base"] = pull.Base,
            ["branch"] = pull.Branch,
            ["head"] = pull.Head,
            ["author"] = pull.Author,
            ["author-id"] = pull.AuthorId.ToString(CultureInfo.InvariantCulture),
            ["trust"] = trust.Word,
            ["because"] = trust.Because,
            ["draft"] = pull.Draft ? "true" : "false",
            ["state"] = pull.State,
            ["title"] = pull.Title,
            ["url"] = pull.Url,
        };

        foreach (var (name, value) in more ?? new Dictionary<string, string>())
        {
            fields[name] = value;
        }

        return new TriggerEvent(
            $"github:{one.Repo}#{pull.Number}:{kind}:{pull.Head[..Math.Min(12, pull.Head.Length)]}:{at:yyyyMMddTHHmmss}{suffix}",
            at,
            Name,
            kind,
            Subject(one.Repo, pull.Number, pull.Generation),
            one.Project,
            fields,
            []);
    }

    /// <summary>The branch subject a newly opened pull request takes over, where its branch is in the repository itself.</summary>
    private static Dictionary<string, string>? Folds(string repo, PullSeen pull) =>
        string.Equals(pull.HeadRepo, repo, StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [TriggerSubjects.FoldsField] = BranchSubject(repo, pull.Branch) }
            : null;

    /// <summary>A pull request's subject: a reopened one is a fresh subject, numbered on.</summary>
    public static string Subject(string repo, int number, int generation) =>
        generation <= 1 ? $"pr:{repo}#{number}" : $"pr:{repo}#{number}/{generation}";

    /// <summary>A branch's subject, for a failed run on it with no pull request open.</summary>
    public static string BranchSubject(string repo, string branch) => $"branch:{repo}:{branch}";

    private static (string Login, long Id) User(JsonElement item) =>
        item.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object
            ? (Text(user, "login"), user.TryGetProperty("id", out var id) ? id.GetInt64() : 0)
            : (string.Empty, 0);

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static DateTimeOffset Time(JsonElement item, string name) =>
        DateTimeOffset.Parse(Text(item, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static int Number(string issueUrl) =>
        int.TryParse(issueUrl[(issueUrl.LastIndexOf('/') + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;

    /// <summary>A comment asking for Loadout by name, and the word after it: <c>@loadout review</c>.</summary>
    [GeneratedRegex(@"(?:^|\s)@loadout\s+([a-z][a-z0-9-]*)", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Mention();

    private static T? ReadJson<T>(string path)
        where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), TriggerJournal.Json) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A cursor that cannot be read is a first look at everything.
            return null;
        }
    }

    private static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var aside = path + ".tmp";
        File.WriteAllText(aside, JsonSerializer.Serialize(value, TriggerJournal.Json));
        File.Move(aside, path, overwrite: true);
    }
}

/// <summary>How the GitHub source is doing, as the commands show it.</summary>
public sealed class GitHubHealth
{
    /// <summary>Which token it reads with.</summary>
    public string Credential { get; set; } = TriggerCredential.None;

    /// <summary>When it last looked.</summary>
    public DateTimeOffset? Looked { get; set; }

    /// <summary>What stops it, where something does.</summary>
    public string? Problem { get; set; }

    /// <summary>The fingerprint of the token GitHub refused; polling waits for a different one.</summary>
    public string? Lapsed { get; set; }

    /// <summary>Requests left in the hour, as GitHub last said.</summary>
    public int? Remaining { get; set; }

    /// <summary>Requests allowed in the hour.</summary>
    public int? Limit { get; set; }

    /// <summary>A classic token's scopes, as GitHub said.</summary>
    public string? Scopes { get; set; }

    /// <summary>When the token expires, where it does.</summary>
    public DateTimeOffset? Expires { get; set; }

    /// <summary>The expiry already warned about, so it is said once.</summary>
    public DateTimeOffset? WarnedExpiry { get; set; }

    /// <summary>The token already warned about for its write scopes.</summary>
    public string? ScopeWarned { get; set; }

    /// <summary>Each repository read.</summary>
    public Dictionary<string, RepoHealth> Repos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>How one repository's reads are going.</summary>
public sealed class RepoHealth
{
    public DateTimeOffset? Last { get; set; }

    /// <summary>How often it is read now.</summary>
    public TimeSpan Every { get; set; }

    /// <summary>Whether it is read less often than usual, to save quota.</summary>
    public bool Slowed { get; set; }

    public string? Problem { get; set; }
}

/// <summary>Where the source had got to in each repository.</summary>
internal sealed class GitHubCursor
{
    public Dictionary<string, RepoSeen> Repos { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The login last seen for each account id.</summary>
    public Dictionary<long, string> Logins { get; set; } = [];
}

/// <summary>Where the source had got to in one repository.</summary>
internal sealed class RepoSeen
{
    /// <summary>The tag from the last read of each list.</summary>
    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The lists read at least once, whose first read posted nothing.</summary>
    public HashSet<string> Looked { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<int, PullSeen> Pulls { get; set; } = [];

    /// <summary>Failed runs already seen, oldest first.</summary>
    public List<long> Runs { get; set; } = [];

    /// <summary>The highest issue number seen.</summary>
    public int Issue { get; set; }

    /// <summary>The highest comment id seen.</summary>
    public long Comment { get; set; }

    public DateTimeOffset? Next { get; set; }

    public DateTimeOffset? LastEvent { get; set; }

    public TimeSpan Every { get; set; }
}

/// <summary>A pull request as last seen.</summary>
internal sealed class PullSeen
{
    public int Number { get; set; }

    /// <summary><c>open</c>, <c>closed</c> or <c>merged</c>.</summary>
    public string State { get; set; } = "open";

    public string Head { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;

    /// <summary>The repository its branch is in, which for a fork is not this one.</summary>
    public string HeadRepo { get; set; } = string.Empty;

    public string Base { get; set; } = string.Empty;

    public bool Draft { get; set; }

    /// <summary>Counts reopenings, each of which is a fresh subject.</summary>
    public int Generation { get; set; } = 1;

    public string Author { get; set; } = string.Empty;

    public long AuthorId { get; set; }

    public string Association { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public static PullSeen From(JsonElement item)
    {
        var head = item.GetProperty("head");
        var user = item.TryGetProperty("user", out var who) && who.ValueKind == JsonValueKind.Object ? who : default;

        return new PullSeen
        {
            Number = item.GetProperty("number").GetInt32(),
            State = item.TryGetProperty("merged_at", out var merged) && merged.ValueKind == JsonValueKind.String
                ? "merged"
                : item.GetProperty("state").GetString() ?? "open",
            Head = head.GetProperty("sha").GetString() ?? string.Empty,
            Branch = head.GetProperty("ref").GetString() ?? string.Empty,
            HeadRepo = head.TryGetProperty("repo", out var repo) && repo.ValueKind == JsonValueKind.Object
                ? repo.GetProperty("full_name").GetString() ?? string.Empty
                : string.Empty,
            Base = item.GetProperty("base").GetProperty("ref").GetString() ?? string.Empty,
            Draft = item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True,
            Author = user.ValueKind == JsonValueKind.Object ? user.GetProperty("login").GetString() ?? string.Empty : string.Empty,
            AuthorId = user.ValueKind == JsonValueKind.Object ? user.GetProperty("id").GetInt64() : 0,
            Association = item.TryGetProperty("author_association", out var association)
                ? association.GetString() ?? string.Empty
                : string.Empty,
            Title = item.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty,
            Url = item.TryGetProperty("html_url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
        };
    }
}
