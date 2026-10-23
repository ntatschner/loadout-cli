using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Triggers;
using Loadout.Models.Triggers;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The GitHub source: what each change to a repository's pull requests, runs,
/// issues and comments becomes, who is trusted, and what happens to the token.
/// </summary>
public sealed class GitHubSourceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly WatchedRepo Repo = new("loadout-cli", "acme/app");
    private static readonly TriggerCredential Token = TriggerCredential.Of(TriggerCredential.Dedicated, "token-one");

    private readonly string _state = Path.Combine(Path.GetTempPath(), "loadout-gh-" + Guid.NewGuid().ToString("N"));
    private readonly ScriptedGitHub _github = new();
    private readonly GitHubSource _source;
    private readonly List<TriggerAllowEntry> _allowed = [];
    private DateTimeOffset _now = Now;

    public GitHubSourceTests()
    {
        _source = new GitHubSource(_state, _github);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_state, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task The_first_look_writes_down_what_is_there_and_posts_nothing()
    {
        _github.Pulls = [Pull(1)];
        _github.Runs = [Run(10, "abc")];
        _github.Issues = [Issue(5)];
        _github.Comments = [Comment(100, 1, "@loadout review")];

        (await LookAsync()).Events.Should().BeEmpty();
    }

    [Fact]
    public async Task A_pull_request_opened_pushed_to_readied_retargeted_and_merged_is_an_event_each_on_one_subject()
    {
        await LookAsync();

        _github.Pulls = [Pull(7, draft: true)];
        var opened = (await LookAsync()).Events.Should().ContainSingle().Subject;
        opened.Kind.Should().Be(TriggerEventKinds.PrOpened);
        opened.Subject.Should().Be("pr:acme/app#7");
        opened.Field("draft").Should().Be("true");
        opened.Field(TriggerSubjects.FoldsField).Should().Be("branch:acme/app:feature", "its branch is in the repository");

        _github.Pulls = [Pull(7, head: "def456", baseRef: "release", draft: false)];
        var changed = (await LookAsync()).Events;
        changed.Select(one => one.Kind).Should().Equal(
            TriggerEventKinds.PrRetargeted, TriggerEventKinds.PrReady, TriggerEventKinds.PrUpdated);
        changed[0].Field("was-base").Should().Be("main");
        changed.Select(one => one.Subject).Should().AllBe("pr:acme/app#7");

        _github.Pulls = [Pull(7, head: "def456", baseRef: "release", merged: true)];
        var merged = (await LookAsync()).Events.Should().ContainSingle().Subject;
        merged.Kind.Should().Be(TriggerEventKinds.PrMerged);
        merged.Field("state").Should().Be("merged");
    }

    [Fact]
    public async Task A_reopened_pull_request_is_a_fresh_subject_linked_to_the_one_it_was()
    {
        _github.Pulls = [Pull(3)];
        await LookAsync();

        _github.Pulls = [Pull(3, closed: true)];
        (await LookAsync()).Events.Should().ContainSingle().Which.Kind.Should().Be(TriggerEventKinds.PrClosed);

        _github.Pulls = [Pull(3)];
        var reopened = (await LookAsync()).Events.Should().ContainSingle().Subject;

        reopened.Kind.Should().Be(TriggerEventKinds.PrReopened);
        reopened.Subject.Should().Be("pr:acme/app#3/2");
        reopened.Field("was").Should().Be("pr:acme/app#3");

        _github.Pulls = [Pull(3, head: "later")];
        (await LookAsync()).Events.Should().ContainSingle().Which.Subject.Should().Be("pr:acme/app#3/2");
    }

    [Fact]
    public async Task A_failed_run_belongs_to_every_open_pull_request_on_its_head_and_to_its_branch_where_none_is_open()
    {
        _github.Pulls = [Pull(1, head: "abc", baseRef: "main"), Pull(2, head: "abc", baseRef: "release")];
        await LookAsync();

        // Newest first, as GitHub lists them.
        _github.Runs = [Run(22, "ok", conclusion: "success"), Run(21, "zzz", branch: "hotfix"), Run(20, "abc", branch: "feature")];
        var events = (await LookAsync()).Events;

        events.Should().HaveCount(3, "one for each pull request on abc, one for hotfix's branch, none for a success");
        events.Take(2).Select(one => one.Subject).Should().Equal("pr:acme/app#1", "pr:acme/app#2");
        events.Take(2).Select(one => one.Field("base")).Should().Equal("main", "release");
        events.Take(2).Select(one => one.Field("run")).Should().AllBe("20");

        var branch = events[2];
        branch.Subject.Should().Be("branch:acme/app:hotfix");
        branch.Field("trust").Should().Be("trusted");
        branch.Field("because").Should().Be("pushed to the repository itself");

        (await LookAsync()).Events.Should().BeEmpty("a run is news once");
    }

    [Fact]
    public async Task A_failed_run_from_a_fork_with_no_pull_request_open_is_outside()
    {
        await LookAsync();

        _github.Runs = [Run(30, "fff", branch: "patch", from: "stranger/app")];

        var failed = (await LookAsync()).Events.Should().ContainSingle().Subject;
        failed.Field("trust").Should().Be("outside");
        failed.Field("because").Should().Contain("fork");
    }

    [Fact]
    public async Task Trust_comes_from_the_association_then_the_allow_list_by_id_and_scope()
    {
        await LookAsync();

        _github.Pulls =
        [
            Pull(1, association: "MEMBER", login: "alice", userId: 1),
            Pull(2, association: "NONE", login: "dependabot[bot]", userId: 49699333),
            Pull(3, association: "CONTRIBUTOR", login: "bob", userId: 3),
        ];
        _allowed.Add(new TriggerAllowEntry { Id = 49699333, Login = "dependabot[bot]" });
        _allowed.Add(new TriggerAllowEntry { Id = 3, Login = "bob", Repo = "acme/other" });

        var events = (await LookAsync()).Events;

        events.Select(one => one.Field("trust")).Should().Equal("trusted", "trusted", "outside");
        events[0].Field("because").Should().Be("association MEMBER");
        events[1].Field("because").Should().Be("allow-list dependabot[bot] (everywhere)");
        events[2].Field("because").Should().Be("association CONTRIBUTOR", "bob is trusted on another repository only");
    }

    [Fact]
    public async Task An_issue_is_news_once_and_the_issues_list_carrying_pull_requests_adds_nothing()
    {
        _github.Issues = [Issue(4)];
        await LookAsync();

        _github.Issues = [Issue(6), Issue(5, isPull: true), Issue(4)];

        var opened = (await LookAsync()).Events.Should().ContainSingle().Subject;
        opened.Kind.Should().Be(TriggerEventKinds.IssueOpened);
        opened.Subject.Should().Be("issue:acme/app#6");
        opened.Field("trust").Should().Be("outside");
    }

    [Fact]
    public async Task A_comment_asking_for_loadout_names_the_word_and_is_trusted_only_when_the_pull_request_is()
    {
        _github.Pulls = [Pull(8, association: "NONE", login: "stranger", userId: 99)];
        await LookAsync();

        // Newest first, as GitHub lists them.
        _github.Comments =
        [
            Comment(202, 12, "no mention here", association: "OWNER"),
            Comment(201, 8, "Thanks!\n@loadout Review please", association: "OWNER"),
        ];

        var events = (await LookAsync()).Events;

        events.Should().HaveCount(2);
        events[0].Subject.Should().Be("pr:acme/app#8");
        events[0].Field("on").Should().Be("pr");
        events[0].Field("mention").Should().Be("review");
        events[0].Field("trust").Should().Be("outside", "the run it starts reads the outside author's pull request");
        events[0].Field("because").Should().Contain("the pull request's author");

        events[1].Subject.Should().Be("issue:acme/app#12");
        events[1].Field("mention").Should().BeEmpty();
        events[1].Field("trust").Should().Be("trusted");
    }

    [Fact]
    public async Task An_unchanged_answer_sends_the_tag_back_and_posts_nothing()
    {
        _github.ETag = "\"v1\"";
        await LookAsync();

        _github.Unchanged = true;
        _github.Pulls = [Pull(9)];

        (await LookAsync()).Events.Should().BeEmpty("GitHub said nothing changed");
        _github.SentTags.Should().Contain("\"v1\"");
    }

    [Fact]
    public async Task A_refused_dedicated_token_stops_polling_until_it_is_replaced_and_then_catches_up()
    {
        await LookAsync();

        _github.Refuse = true;
        _github.Pulls = [Pull(4)];

        var lapsed = await LookAsync();
        lapsed.Events.Should().BeEmpty();
        lapsed.Notes.Should().ContainSingle().Which.Kind.Should().Be(TriggerEntryKinds.Lapsed);
        GitHubSource.ReadHealth(_state)!.Problem.Should().Contain("trigger token set");

        _github.Refuse = false;
        var reads = _github.Reads;

        (await LookAsync()).Events.Should().BeEmpty("the same token is not tried again");
        _github.Reads.Should().Be(reads);

        var renewed = await LookAsync(TriggerCredential.Of(TriggerCredential.Dedicated, "token-two"));
        renewed.Notes.Should().ContainSingle().Which.Kind.Should().Be(TriggerEntryKinds.Renewed);
        renewed.Events.Should().ContainSingle().Which.Kind.Should().Be(TriggerEventKinds.PrOpened);
    }

    [Fact]
    public async Task An_expiry_inside_a_week_and_a_token_that_can_write_are_each_said_once()
    {
        _github.Expires = Now.AddDays(5);
        _github.Scopes = "read:org, repo";

        var first = await LookAsync();
        first.Notes.Select(one => one.Reason).Should().Contain(one => one!.Contains("expires 2026-10-28"))
            .And.Contain(one => one!.Contains("can write (repo)"));

        (await LookAsync()).Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task With_no_token_at_all_nothing_is_read_and_the_health_says_how_to_set_one()
    {
        (await LookAsync(TriggerCredential.Missing)).Events.Should().BeEmpty();

        _github.Reads.Should().Be(0);
        GitHubSource.ReadHealth(_state)!.Problem.Should().Contain("gh auth login");
    }

    [Fact]
    public async Task Short_quota_slows_quiet_repositories_and_leaves_busy_ones_alone()
    {
        _github.Remaining = 500;
        _github.Limit = 5000;

        var quiet = new WatchedRepo("quiet", "acme/quiet");
        await _source.LookAsync([Repo, quiet], Token, _allowed, _now, CancellationToken.None);

        _github.Pulls = [Pull(1)];
        _now = Now.AddMinutes(20);
        await _source.LookAsync([Repo, quiet], Token, _allowed, _now, CancellationToken.None);

        var health = GitHubSource.ReadHealth(_state)!;
        health.Repos["acme/app"].Slowed.Should().BeFalse("it has just had news");
        health.Repos["acme/quiet"].Slowed.Should().BeTrue();
        health.Repos["acme/quiet"].Every.Should().BeGreaterThan(GitHubSource.Every);
    }

    [Fact]
    public void A_classic_token_with_write_scopes_is_named_and_one_with_read_scopes_is_not()
    {
        GitHubSource.WritesWith("repo, read:org").Should().Be("repo");
        GitHubSource.WritesWith("read:org, public_repo, write:packages").Should().Be("public_repo, write:packages");
        GitHubSource.WritesWith("read:org").Should().BeNull();
        GitHubSource.WritesWith(null).Should().BeNull();
    }

    [Fact]
    public void GitHubs_expiry_header_is_read()
    {
        GitHubApi.Expiry("2026-11-01 12:00:00 UTC").Should().Be(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        GitHubApi.Expiry("nonsense").Should().BeNull();
    }

    private async Task<GitHubLook> LookAsync(TriggerCredential? credential = null)
    {
        _now = _now.AddMinutes(2);

        return await _source.LookAsync([Repo], credential ?? Token, _allowed, _now, CancellationToken.None);
    }

    private static object Pull(
        int number,
        string head = "abc123",
        string baseRef = "main",
        bool draft = false,
        bool closed = false,
        bool merged = false,
        string association = "OWNER",
        string login = "owner",
        long userId = 1) => new
        {
            number,
            state = closed || merged ? "closed" : "open",
            merged_at = merged ? "2026-10-23T11:00:00Z" : null,
            draft,
            title = $"Pull {number}",
            html_url = $"https://github.com/acme/app/pull/{number}",
            updated_at = "2026-10-23T11:00:00Z",
            author_association = association,
            user = new { login, id = userId },
            head = new { sha = head, @ref = "feature", repo = new { full_name = "acme/app" } },
            @base = new { @ref = baseRef },
        };

    private static object Run(long id, string head, string branch = "feature", string conclusion = "failure", string from = "acme/app") => new
    {
        id,
        name = "CI",
        conclusion,
        head_sha = head,
        head_branch = branch,
        html_url = $"https://github.com/acme/app/actions/runs/{id}",
        updated_at = "2026-10-23T11:30:00Z",
        display_title = "Build",
        head_repository = new { full_name = from },
        actor = new { login = "pusher", id = 5 },
    };

    private static object Issue(int number, bool isPull = false) => isPull
        ? new
        {
            number,
            title = "A pull",
            html_url = "x",
            created_at = "2026-10-23T11:00:00Z",
            author_association = "NONE",
            user = new { login = "someone", id = 7 },
            pull_request = new { url = "x" },
        }
        : new
        {
            number,
            title = $"Issue {number}",
            html_url = $"https://github.com/acme/app/issues/{number}",
            created_at = "2026-10-23T11:00:00Z",
            author_association = "NONE",
            user = new { login = "someone", id = 7 },
        };

    private static object Comment(long id, int number, string body, string association = "OWNER") => new
    {
        id,
        body,
        issue_url = $"https://api.github.com/repos/acme/app/issues/{number}",
        html_url = $"https://github.com/acme/app/issues/{number}#issuecomment-{id}",
        created_at = "2026-10-23T11:40:00Z",
        author_association = association,
        user = new { login = "commenter", id = 11 },
    };

    private sealed class ScriptedGitHub : IGitHubReads
    {
        public List<object> Pulls { get; set; } = [];

        public List<object> Runs { get; set; } = [];

        public List<object> Issues { get; set; } = [];

        public List<object> Comments { get; set; } = [];

        public bool Refuse { get; set; }

        public bool Unchanged { get; set; }

        public string? ETag { get; set; }

        public string? Scopes { get; set; }

        public DateTimeOffset? Expires { get; set; }

        public int? Remaining { get; set; }

        public int? Limit { get; set; }

        public int Reads { get; private set; }

        public List<string> SentTags { get; } = [];

        public Task<GitHubReply> GetAsync(TriggerCredential credential, string path, string? etag, CancellationToken ct)
        {
            Reads++;

            if (etag is not null)
            {
                SentTags.Add(etag);
            }

            if (Refuse)
            {
                return Task.FromResult(new GitHubReply(401));
            }

            if (Unchanged)
            {
                return Task.FromResult(new GitHubReply(304, ETag: ETag, Remaining: Remaining, Limit: Limit));
            }

            // Only the repository under test has anything in it.
            var mine = path.StartsWith("repos/acme/app/", StringComparison.Ordinal);

            object body = path switch
            {
                _ when path.Contains("/pulls?", StringComparison.Ordinal) => mine ? Pulls : [],
                _ when path.Contains("/actions/runs", StringComparison.Ordinal) => new { workflow_runs = mine ? Runs : [] },
                _ when path.Contains("/issues/comments", StringComparison.Ordinal) => mine ? Comments : [],
                _ => mine ? Issues : [],
            };

            return Task.FromResult(new GitHubReply(
                200, JsonSerializer.Serialize(body), ETag, Remaining, Limit, Scopes, Expires));
        }
    }
}
