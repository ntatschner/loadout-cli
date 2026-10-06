using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Being shown what already covers this before a second topic is started.
/// </summary>
/// <remarks>
/// <para>
/// Contradictions arrive one fact at a time. Nothing is overwritten and both
/// topics are indexed, so a later session is given two answers with nothing to
/// choose between them — and the moment something could have been shown is the
/// moment the second one is written.
/// </para>
/// <para>
/// The tests that keep it quiet matter as much as the one that makes it speak.
/// A check that interrupts every write is one whose override becomes a habit,
/// and then it is worse than nothing: it looks like a guard and is a reflex.
/// </para>
/// </remarks>
public sealed class MemoryNeighbourTests : IDisposable
{
    private const string Slug = "starstats";

    private readonly string _root;
    private readonly MemoryService _memory = new(TimeProvider.System);

    public MemoryNeighbourTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-near-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path.Combine(_root, "projects", Slug, "memory"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    [Fact]
    public async Task A_second_topic_on_the_same_ground_is_stopped_and_the_first_named()
    {
        await SeedAsync(
            "restart-manager",
            "why installers fail with 1603 over a running app",
            "The MSI Restart Manager is disabled by policy, so an upgrade over a running app fails.");

        var second = await WriteAsync(
            "installer-failures",
            "why an upgrade over a running application fails",
            "An upgrade over a running app fails because the Restart Manager is disabled.");

        second.Failed.Should().BeTrue();
        second.ExitCode.Should().Be(ExitCode.InvalidArguments);
        second.Error.Should().Contain("restart-manager");

        // And nothing was written, so the store never held both.
        File.Exists(Path.Combine(_root, "projects", Slug, "memory", "installer-failures.md"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task A_topic_about_something_else_is_written_without_a_word()
    {
        await SeedAsync(
            "restart-manager",
            "why installers fail with 1603 over a running app",
            "The MSI Restart Manager is disabled by policy, so an upgrade fails.");

        var second = await WriteAsync(
            "palette-events",
            "the command palette list does not raise Accepted",
            "Subscribe to Accepting instead; Accepted never fires on the palette's list.");

        second.Succeeded.Should().BeTrue(second.Error);
    }

    [Fact]
    public async Task One_word_in_common_is_not_enough_to_stop_a_write()
    {
        // "launcher" is in half a store. Stopping on one shared word would
        // interrupt every write, and an override used every time is a reflex
        // rather than a decision.
        //
        // Exactly one word overlaps, and it has to: two topics sharing nothing
        // at all would pass whatever the threshold was, which is a test that
        // agrees with itself. The comparison is made against the name and the
        // facts, so the shared word has to be in those rather than only in a
        // description.
        await SeedAsync(
            "restart-manager",
            "why installers fail over a running app",
            "The launcher closes the app before upgrading.");

        var second = await WriteAsync(
            "statusline-colours",
            "what shows in the status line",
            "The launcher shows branch and context usage.");

        second.Succeeded.Should().BeTrue(second.Error);
    }

    [Fact]
    public async Task A_file_name_both_topics_mention_counts_once()
    {
        // The first real onboarding: a store holding only how to build and test
        // a script, then a topic on what the script detects. Both name the
        // script, and its name splits into three words, so one shared name
        // passed for three shared words and stopped a topic about something
        // else entirely.
        await SeedAsync(
            "verify",
            "how to build and test this repository",
            "Run Invoke-Pester against tests/Invoke-SetupCheck.Tests.ps1 after building.");

        var second = await WriteAsync(
            "detection-behaviour",
            "what the setup check reports as missing",
            "Invoke-SetupCheck.ps1 reports a missing Git identity and an unset PATH entry.");

        second.Succeeded.Should().BeTrue(second.Error);
    }

    [Fact]
    public async Task Sharing_a_project_s_vocabulary_is_not_covering_the_same_ground()
    {
        // Topics in one project share its words. Two of them in common used to
        // be enough, which stopped all but two of 688 real topics written
        // afresh; what matters is how much of the new fact is already said.
        await SeedAsync(
            "docker-builds",
            "why the docker build needs the pinned sdk",
            "The docker build restores locked packages against the pinned SDK image.");

        var second = await WriteAsync(
            "release-tags",
            "where the release notes come from",
            "The release workflow reads notes from the annotated tag body, not the docker build log.");

        second.Succeeded.Should().BeTrue(second.Error);
    }

    [Fact]
    public async Task Adding_to_a_topic_that_already_exists_is_never_questioned()
    {
        // Extending is the thing this exists to encourage. Asking about it would
        // make the flag habitual, which is how a guard stops being one.
        await SeedAsync(
            "restart-manager",
            "why installers fail with 1603 over a running app",
            "The Restart Manager is disabled by policy.");

        var again = await WriteAsync(
            "restart-manager",
            "why installers fail with 1603 over a running app",
            "The Restart Manager is disabled by policy, so close the app first.");

        again.Succeeded.Should().BeTrue(again.Error);
    }

    [Fact]
    public async Task Saying_it_is_separate_writes_it()
    {
        await SeedAsync(
            "restart-manager",
            "why installers fail with 1603 over a running app",
            "The MSI Restart Manager is disabled by policy, so an upgrade over a running app fails.");

        var second = await WriteAsync(
            "installer-failures",
            "why an upgrade over a running application fails",
            "An upgrade over a running app fails because the Restart Manager is disabled.",
            separate: true);

        second.Succeeded.Should().BeTrue(second.Error);
    }

    [Fact]
    public async Task The_first_topic_in_an_empty_store_is_never_stopped()
    {
        var first = await WriteAsync(
            "restart-manager",
            "why installers fail with 1603 over a running app",
            "The Restart Manager is disabled by policy on this machine.");

        first.Succeeded.Should().BeTrue(first.Error);
    }

    private async Task SeedAsync(string name, string description, string fact) =>
        (await WriteAsync(name, description, fact, separate: true))
            .Succeeded.Should().BeTrue("the fixture has to be written before anything is compared to it");

    private Task<OperationResult<MemoryTopic>> WriteAsync(
        string name,
        string description,
        string fact,
        bool separate = false) =>
        _memory.WriteAsync(_root, Slug, name, description, MemoryKind.Lesson, [fact], separate);
}
