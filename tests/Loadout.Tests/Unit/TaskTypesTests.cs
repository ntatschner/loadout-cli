using FluentAssertions;
using Loadout.Core.Tasks;
using Loadout.Models.Tasks;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The first guess at what a piece of work is about, from its words and where
/// it was captured, and the names a type goes by on every surface.
/// </summary>
public sealed class TaskTypesTests
{
    [Theory]
    [InlineData("The login page crashes when the password is empty", TaskType.BugFix)]
    [InlineData("Fix the flaky permission test", TaskType.BugFix)]
    [InlineData("Add a CI pipeline for the Node front end", TaskType.CiCd)]
    [InlineData("Write the release notes for 0.110.0 and publish to winget", TaskType.Release)]
    [InlineData("Update the README with the new install steps", TaskType.Docs)]
    [InlineData("Bump the dependencies and tidy up the old helpers", TaskType.Maintenance)]
    [InlineData("Start a new app for tracking the allotment", TaskType.NewProject)]
    [InlineData("Show the run's budget each round", TaskType.Feature)]
    public void A_project_task_is_typed_by_its_words(string words, TaskType expected) =>
        TaskTypes.Guess(words, TaskKind.Task, "website").Should().Be(expected);

    [Fact]
    public void Words_are_matched_whole_so_a_prefix_is_not_a_fix() =>
        TaskTypes.Guess("Add a prefix to every docker image tag", TaskKind.Task, "website")
            .Should().Be(TaskType.Feature, "'prefix' contains 'fix' but says nothing about a bug");

    [Fact]
    public void The_type_with_the_most_words_wins() =>
        TaskTypes.Guess("Release: publish the changelog, then fix a typo", TaskKind.Task, "website")
            .Should().Be(TaskType.Release, "three release words outweigh one bug word");

    [Fact]
    public void A_tie_goes_to_the_bug() =>
        TaskTypes.Guess("The release is broken", TaskKind.Task, "website")
            .Should().Be(TaskType.BugFix, "a broken release is a bug before it is a release");

    [Fact]
    public void An_idea_with_no_telling_words_on_the_workspace_wide_list_is_a_new_project() =>
        TaskTypes.Guess("Something to track what the cats eat", TaskKind.Idea, null)
            .Should().Be(TaskType.NewProject, "an idea that belongs to no project yet is most likely a new one");

    [Fact]
    public void The_same_words_on_a_project_are_a_feature() =>
        TaskTypes.Guess("Something to track what the cats eat", TaskKind.Idea, "website")
            .Should().Be(TaskType.Feature);

    [Fact]
    public void A_task_on_the_workspace_wide_list_is_not_taken_for_a_project() =>
        TaskTypes.Guess("Renew the certificate", TaskKind.Task, null)
            .Should().Be(TaskType.Feature, "only an idea is a new project by where it was captured");

    [Fact]
    public void Nothing_said_is_still_a_type() =>
        TaskTypes.Guess(null, TaskKind.Task, "website").Should().Be(TaskType.Feature);

    [Fact]
    public void Every_type_reads_back_from_its_name_its_words_and_its_spelling()
    {
        foreach (var type in TaskTypes.All)
        {
            TaskTypes.Parse(TaskTypes.Name(type)).Should().Be(type);
            TaskTypes.Parse(TaskTypes.Words(type).ToUpperInvariant()).Should().Be(type);
            TaskTypes.Parse(type.ToString()).Should().Be(type);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("chore")]
    [InlineData("3")]
    public void Anything_else_is_no_type(string? said) => TaskTypes.Parse(said).Should().BeNull();

    [Fact]
    public void The_names_are_the_seven_the_command_line_takes() =>
        TaskTypes.Names.Should().Equal("new-project", "feature", "bug-fix", "ci-cd", "release", "docs", "maintenance");
}
