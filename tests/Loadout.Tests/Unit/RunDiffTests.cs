using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a node actually did, as opposed to its account of it.
/// </summary>
/// <remarks>
/// <para>
/// The property that matters is where the diff is measured from. A node's work
/// is what it did to the commit its branch started at, and that stays true
/// after the branch has been merged and tidied away - which is exactly when
/// somebody wants to look. Measured against whatever HEAD is now, a merged
/// node's diff is empty, and empty reads as "it did nothing" rather than as
/// "this cannot be shown".
/// </para>
/// <para>
/// Every way this fails is a sentence rather than a status. The repository has
/// moved, the run predates the branch point being written down, the branch was
/// tidied away: all ordinary, none of them the page's fault.
/// </para>
/// </remarks>
public sealed class RunDiffTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private const string Branch = "teams-20260918-1200-aaaa-implementer-1";
    private const string Started = "1111111111111111111111111111111111111111";
    private const string Ended = "2222222222222222222222222222222222222222";

    private static RunNode Node(string? branch = Branch, string? at = Started) =>
        new("implementer/1", "role.implementer", "reported", 3, 0.2m, Noon,
            Branch: branch, Base: at);

    private static FakeGit Repository(string patch = "diff --git a/one.txt b/one.txt\n+added\n")
    {
        var git = new FakeGit("D:/repo");

        git.Commits[Branch] = Ended;
        git.Diffs[$"{Started}..{Ended}"] = patch;
        git.Diffs[$"{Started}..{Ended} --stat"] = " one.txt | 1 +\n";

        return git;
    }

    [Fact]
    public async Task What_a_node_changed_is_measured_from_where_its_branch_started()
    {
        var read = await RunDiff.ForAsync(Repository(), "D:/repo", Node(), []);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.From.Should().Be(Started);
        read.Value.Patch.Should().Contain("+added");
        read.Value.Summary.Should().Contain("one.txt");
        read.Value.Cut.Should().BeFalse();
    }

    [Fact]
    public async Task A_branch_the_run_merged_says_so()
    {
        var read = await RunDiff.ForAsync(Repository(), "D:/repo", Node(), [Branch]);

        read.Value!.Merged.Should().BeTrue();

        // And is still shown, which is the whole reason the branch point is
        // written down: against HEAD this diff would be empty.
        read.Value.Patch.Should().Contain("+added");
    }

    [Fact]
    public async Task A_run_too_old_to_have_written_down_where_its_branch_started_says_that()
    {
        var read = await RunDiff.ForAsync(Repository(), "D:/repo", Node(at: null), []);

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain("did not write down where");

        // Not guessed at. A diff measured from the wrong place is worse than
        // no diff, because it looks like one.
        read.Error.Should().NotContain("HEAD");
    }

    [Fact]
    public async Task A_branch_that_has_been_tidied_away_says_that_rather_than_nothing()
    {
        var git = Repository();

        git.Commits.Clear();

        var read = await RunDiff.ForAsync(git, "D:/repo", Node(), [Branch]);

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain("tidied away");
    }

    [Fact]
    public async Task A_node_that_worked_in_the_repository_itself_has_no_branch_to_diff()
    {
        var read = await RunDiff.ForAsync(Repository(), "D:/repo", Node(branch: null), []);

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain("rather than on a branch of its own");
    }

    [Fact]
    public async Task A_run_that_did_not_record_its_repository_says_so()
    {
        var read = await RunDiff.ForAsync(Repository(), null, Node(), []);

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain("which repository");
    }

    [Fact]
    public async Task A_patch_too_big_for_a_browser_is_cut_and_the_summary_is_not()
    {
        var huge = string.Concat(Enumerable.Repeat("+a line that was added\n", (RunDiff.Most / 22) + 100));

        var read = await RunDiff.ForAsync(Repository(huge), "D:/repo", Node(), []);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Cut.Should().BeTrue();

        // What was touched stays complete even when the patch does not, so a
        // cut patch never hides a whole file.
        read.Value.Summary.Should().Contain("one.txt");
        read.Value.Patch.Should().Contain("cut here");
        read.Value.Patch.Should().Contain("git diff", "it says how to see the rest");
    }

    [Fact]
    public async Task What_looks_like_a_credential_never_reaches_the_page()
    {
        var read = await RunDiff.ForAsync(
            Repository("+const token = \"ghp_0123456789abcdefghijklmnopqrstuvwxyzAB\";\n"),
            "D:/repo",
            Node(),
            []);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Patch.Should().NotContain("ghp_0123456789abcdefghijklmnopqrstuvwxyzAB");
    }
}
