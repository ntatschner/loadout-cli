using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Drafting a specialist somebody can then write.
/// </summary>
/// <remarks>
/// The library could always be extended and there was no way to extend it:
/// adding one meant knowing ten frontmatter keys, which of them your layer
/// uses, that the identifier and the kind have to agree, and which directory it
/// belongs in — and finding out you were wrong afterwards.
/// </remarks>
public sealed class SpecialistScaffoldTests
{
    private static SpecialistDraft Draft(string id) =>
        SpecialistScaffold.Draft(id).Value
        ?? throw new InvalidOperationException($"'{id}' did not draft.");

    /// <summary>Reads a draft back through the library's own parser.</summary>
    private static SpecialistDocument Parse(SpecialistDraft draft)
    {
        var parsed = SpecialistLibrary.Parse(draft.Content, draft.FileName, SpecialistOrigin.Workspace);

        parsed.Succeeded.Should().BeTrue(parsed.Error ?? string.Empty);

        return parsed.Value!;
    }

    [Fact]
    public void A_draft_is_something_the_library_will_actually_load()
    {
        var draft = Draft("skill.deploy-checklist");

        // The point of the whole command. A template that produces a file the
        // parser rejects is worse than no template, because the failure arrives
        // later and looks like the library is broken.
        var document = Parse(draft);

        document.Id.Should().Be("skill.deploy-checklist");
        document.Kind.Should().Be(SpecialistKind.Skill);
        document.Title.Should().Be("Deploy checklist");
        document.Body.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_drafted_skill_can_be_reached_by_the_words_of_a_task()
    {
        var draft = Draft("skill.deploy-checklist");
        var document = Parse(draft);

        // A skill nothing can select is a file, not a skill.
        document.Activation.TaskPhraseList.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("foundation.house-style", SpecialistKind.Foundation)]
    [InlineData("mode.review", SpecialistKind.Mode)]
    [InlineData("language.rust", SpecialistKind.Language)]
    [InlineData("framework.svelte", SpecialistKind.Framework)]
    [InlineData("database.mongo", SpecialistKind.Database)]
    [InlineData("platform.android", SpecialistKind.Platform)]
    [InlineData("cloud.hetzner", SpecialistKind.Cloud)]
    [InlineData("function.accessibility", SpecialistKind.Function)]
    [InlineData("skill.incident-review", SpecialistKind.Skill)]
    public void Every_layer_drafts_something_that_loads(string id, SpecialistKind kind)
    {
        var draft = Draft(id);

        draft.Kind.Should().Be(kind);

        var document = Parse(draft);

        document.Id.Should().Be(id);
        document.Kind.Should().Be(kind);
    }

    [Fact]
    public void A_foundation_applies_always_and_asks_for_nothing_else()
    {
        var document = Parse(Draft("foundation.house-style"));

        // Foundations are the floor. Giving one a task phrase would be offering
        // a choice that is not there.
        document.Activation.Always.Should().BeTrue();
        document.Activation.TaskPhraseList.Should().BeEmpty();
    }

    [Fact]
    public void A_language_is_found_by_the_repository_rather_than_the_words()
    {
        var document = Parse(Draft("language.rust"));

        document.Activation.GlobList.Should().NotBeEmpty();
        document.Activation.DependencyList.Should().NotBeEmpty();
        document.Activation.TaskPhraseList.Should().BeEmpty();
    }

    [Fact]
    public void The_file_goes_where_its_layer_keeps_them()
    {
        SpecialistScaffold.DirectoryFor(SpecialistKind.Skill).Should().Be("skill");
        SpecialistScaffold.DirectoryFor(SpecialistKind.Database).Should().Be("database");

        Draft("skill.deploy-checklist").FileName.Should().Be("deploy-checklist.md");
    }

    [Theory]
    [InlineData("", "no identifier")]
    [InlineData("   ", "no identifier")]
    [InlineData("deploy", "no layer named")]
    [InlineData("skill.", "nothing after the layer")]
    [InlineData(".deploy", "nothing before the dot")]
    [InlineData("wizard.gandalf", "not a layer at all")]
    [InlineData("skill.deploy checklist", "a space in an address")]
    public void An_identifier_that_could_not_work_is_refused(string id, string why)
    {
        var result = SpecialistScaffold.Draft(id);

        result.Failed.Should().BeTrue(why);
        result.ExitCode.Should().Be(ExitCode.InvalidArguments);

        // The message has to say what a good one looks like. Somebody meeting
        // this is guessing at a format they have never seen.
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_unknown_layer_is_told_which_ones_exist()
    {
        var result = SpecialistScaffold.Draft("wizard.gandalf");

        result.Error.Should().Contain("skill");
        result.Error.Should().Contain("language");
    }

    [Fact]
    public void A_title_and_summary_given_are_the_ones_used()
    {
        var draft = SpecialistScaffold.Draft(
            "skill.deploy-checklist",
            title: "Deploying to production",
            summary: "The steps, in order, with the checks between them").Value!;

        var document = Parse(draft);

        document.Title.Should().Be("Deploying to production");
        document.Summary.Should().Be("The steps, in order, with the checks between them");
    }

    [Fact]
    public void An_identifier_is_lowercased_because_it_is_an_address()
    {
        var draft = SpecialistScaffold.Draft("Skill.Deploy-Checklist").Value!;

        draft.Id.Should().Be("skill.deploy-checklist");
        draft.FileName.Should().Be("deploy-checklist.md");
    }
}

/// <summary>
/// The modes the launcher offers are modes the library answers to.
/// </summary>
/// <remarks>
/// A screen offering "investigate" when nothing activates on it would be a
/// choice that silently does nothing — the same shape as a command palette that
/// lists commands and runs none of them, which this launcher shipped.
/// </remarks>
public sealed class LaunchModeSeamTests
{
    [Fact]
    public async Task Every_mode_the_launcher_offers_is_one_a_specialist_answers_to()
    {
        var catalogue = await new SpecialistLibrary().LoadAsync(workspaceRoot: null);

        // Matched by identifier rather than by an activation list. A 'modes:'
        // list means the opposite — it restricts a specialist to the modes it
        // applies in — and asking for a mode nothing answers to falls back to
        // the default without saying so, which is why this is worth a test.
        var known = catalogue.Specialists.Values
            .Where(s => s.Kind == SpecialistKind.Mode)
            .Select(s => s.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        known.Should().NotBeEmpty("the library ships modes");

        // The first entry is "no mode", which is deliberately not one.
        foreach (var mode in Loadout.Tui.Terminal.LaunchOptionsDialog.Modes.Skip(1))
        {
            known.Should().Contain($"mode.{mode}",
                $"the launcher offers '{mode}', so 'mode.{mode}' has to exist");
        }
    }
}
