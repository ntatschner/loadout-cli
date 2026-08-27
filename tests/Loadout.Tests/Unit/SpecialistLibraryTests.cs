using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The built-in specialist library, and what happens to one that is malformed.
/// </summary>
/// <remarks>
/// The library is content rather than code, which is exactly why it needs
/// testing: nothing about a specialist with a misspelled kind or a requirement
/// that does not exist fails to compile. It fails by quietly not being there
/// when somebody needed it.
/// </remarks>
public sealed class SpecialistLibraryTests
{
    private static async Task<SpecialistCatalogue> BuiltInAsync() =>
        await new SpecialistLibrary().LoadAsync(workspaceRoot: null);

    [Fact]
    public async Task The_built_in_library_loads_from_inside_the_assembly()
    {
        var catalogue = await BuiltInAsync();

        // Embedded rather than installed beside the binary, so this also proves
        // the resources were actually included in the build. A packaging
        // mistake here would otherwise show up as an agent silently getting no
        // guidance at all.
        catalogue.All.Should().NotBeEmpty();
        catalogue.All.Should().OnlyContain(s => s.Origin == SpecialistOrigin.BuiltIn);
    }

    [Fact]
    public async Task The_built_in_library_has_no_errors_in_it()
    {
        var catalogue = await BuiltInAsync();

        var errors = catalogue.Findings
            .Where(f => f.Severity == RuleFindingSeverity.Error)
            .Select(f => $"{f.Rule}: {f.Detail}")
            .ToList();

        errors.Should().BeEmpty("a shipped library that does not validate is a shipped defect");
    }

    [Fact]
    public async Task Every_kind_the_composition_needs_is_populated()
    {
        var catalogue = await BuiltInAsync();

        foreach (var kind in Enum.GetValues<SpecialistKind>())
        {
            catalogue.OfKind(kind).Should().NotBeEmpty(
                $"the {kind} layer is part of the composition and an empty one is a gap");
        }
    }

    [Fact]
    public async Task Ids_are_unique_and_agree_with_their_kind()
    {
        var catalogue = await BuiltInAsync();

        foreach (var specialist in catalogue.All)
        {
            specialist.Id.Should().StartWith(
                specialist.Kind.ToString().ToLowerInvariant() + ".",
                "the id is what somebody types and the kind is where it composes; "
                + "the two disagreeing is invisible in every listing");
        }

        catalogue.Specialists.Keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Foundation_is_always_on_and_nothing_else_is()
    {
        var catalogue = await BuiltInAsync();

        catalogue.OfKind(SpecialistKind.Foundation)
            .Should().OnlyContain(s => s.Activation.Always);

        // The whole design rests on this: anything marked always is paid for on
        // every launch, so a language or framework that crept in would quietly
        // undo the point of the feature.
        catalogue.All
            .Where(s => s.Kind != SpecialistKind.Foundation)
            .Should().OnlyContain(s => !s.Activation.Always);
    }

    [Fact]
    public async Task Foundation_stays_small_because_every_launch_pays_for_it()
    {
        var catalogue = await BuiltInAsync();

        var bytes = catalogue.OfKind(SpecialistKind.Foundation).Sum(s => s.Bytes);

        // Advisory rather than exact. The number matters much less than
        // something noticing when foundation starts becoming a dumping ground,
        // which is the failure mode the brief names.
        bytes.Should().BeLessThan(
            8 * 1024,
            "foundation is charged to every session whatever the task");
    }

    [Fact]
    public async Task Every_requirement_names_a_specialist_that_exists()
    {
        var catalogue = await BuiltInAsync();

        foreach (var specialist in catalogue.All)
        {
            foreach (var required in specialist.Activation.RequiresList)
            {
                catalogue.Find(required).Should().NotBeNull(
                    $"{specialist.Id} requires {required}");
            }
        }
    }

    [Fact]
    public async Task Requirements_stay_shallow_so_composition_stays_understandable()
    {
        var catalogue = await BuiltInAsync();

        foreach (var specialist in catalogue.All)
        {
            Depth(catalogue, specialist.Id, 0).Should().BeLessThanOrEqualTo(
                3,
                $"{specialist.Id} sits at the bottom of a deep chain; the design calls for "
                + "composition rather than an inheritance tree");
        }

        static int Depth(SpecialistCatalogue catalogue, string id, int sofar)
        {
            if (sofar > 10 || catalogue.Find(id) is not { } specialist)
            {
                return sofar;
            }

            var deepest = sofar;

            foreach (var required in specialist.Activation.RequiresList)
            {
                deepest = Math.Max(deepest, Depth(catalogue, required, sofar + 1));
            }

            return deepest;
        }
    }

    [Fact]
    public async Task Every_specialist_can_be_reached_by_something()
    {
        var catalogue = await BuiltInAsync();

        var unreachable = catalogue.All
            .Where(s => s.Kind is not (SpecialistKind.Mode or SpecialistKind.Foundation))
            .Where(s => s.Activation.GlobList.Count == 0
                && s.Activation.DependencyList.Count == 0
                && s.Activation.TaskPhraseList.Count == 0)
            .Select(s => s.Id)
            .ToList();

        // A specialist nothing can activate is guidance nobody will ever see,
        // and nothing fails to tell you so. This is the entire gap in the
        // proposed library: it carried prose for fifty-two specialists and not
        // one piece of evidence that would reach any of them.
        unreachable.Should().BeEmpty();
    }

    // ------------------------------------------------------------- parsing

    private static OperationOutcome Parse(string text) =>
        new(SpecialistLibrary.Parse(text, "test.md", SpecialistOrigin.Workspace));

    private sealed record OperationOutcome(Models.Results.OperationResult<SpecialistDocument> Result)
    {
        internal bool Failed => Result.Failed;

        internal string Error => Result.Error ?? string.Empty;

        internal SpecialistDocument Value => Result.Value!;
    }

    [Fact]
    public void A_file_without_frontmatter_is_not_a_specialist()
    {
        // Unlike a rule, where frontmatter is optional and its absence merely
        // means an undeclared scope. A specialist without it has no id, no kind
        // and nothing that could activate it.
        var parsed = Parse("# Just some prose\n\nWith no frontmatter at all.\n");

        parsed.Failed.Should().BeTrue();
        parsed.Error.Should().Contain("frontmatter");
    }

    [Fact]
    public void An_unknown_kind_is_refused_and_the_real_ones_are_named()
    {
        var parsed = Parse("---\nid: wizard.merlin\nkind: wizard\n---\n\nGuidance.\n");

        parsed.Failed.Should().BeTrue();
        parsed.Error.Should().Contain("wizard");

        // Listing the valid kinds, because the person reading this is fixing a
        // typo and the list is the thing they need.
        parsed.Error.Should().Contain("language");
    }

    [Fact]
    public void A_specialist_with_no_guidance_is_refused()
    {
        var parsed = Parse("---\nid: function.empty\nkind: function\n---\n\n\n");

        parsed.Failed.Should().BeTrue();
        parsed.Error.Should().Contain("no guidance");
    }

    [Fact]
    public void Activation_evidence_is_read_from_the_frontmatter()
    {
        var parsed = Parse("""
            ---
            id: database.postgresql
            kind: database
            title: PostgreSQL
            summary: Planner behaviour.
            globs:
              - '**/*.sql'
            dependencies:
              - 'Npgsql'
            task_phrases:
              - 'postgres'
            requires:
              - 'function.database'
            ---

            Guidance about plans.
            """);

        parsed.Failed.Should().BeFalse(parsed.Error);

        var activation = parsed.Value.Activation;

        activation.GlobList.Should().ContainSingle().Which.Should().Be("**/*.sql");
        activation.DependencyList.Should().Contain("Npgsql");
        activation.TaskPhraseList.Should().Contain("postgres");
        activation.RequiresList.Should().Contain("function.database");
    }

    [Fact]
    public void Size_is_measured_on_the_guidance_rather_than_the_whole_file()
    {
        // The frontmatter is not sent to the agent, so charging the budget for
        // it would overstate what a specialist costs by a fifth or more.
        var parsed = Parse("""
            ---
            id: function.testing
            kind: function
            summary: A summary long enough to matter to a byte count.
            task_phrases:
              - 'test'
            ---

            Body.
            """);

        parsed.Failed.Should().BeFalse(parsed.Error);
        parsed.Value.Bytes.Should().Be(5);
    }
}
