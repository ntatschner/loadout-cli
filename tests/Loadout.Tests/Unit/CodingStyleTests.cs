using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Coding styles: which are in force, in what order, and which of their parts
/// a launch pays for.
/// </summary>
/// <remarks>
/// Written against a real workspace on disk rather than records built in
/// memory, so the frontmatter a person would write is what is being tested —
/// including <c>accompanies</c>, which nothing else in the library uses.
/// </remarks>
public sealed class CodingStyleTests : IDisposable
{
    private const string Slug = "demo";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "loadout-styles-" + Guid.NewGuid().ToString("N"));

    public CodingStyleTests()
    {
        Write("global/specialists/style/personal/personal.md", Style(
            "style.personal", "- Prefer early returns."));

        Write("global/specialists/style/personal/personal.csharp.md", Style(
            "style.personal.csharp", "- Use file-scoped namespaces.", "accompanies:\n  - language.csharp"));

        Write("global/specialists/style/work/work.md", Style(
            "style.work", "- One exit per method."));

        Write("global/specialists/style/work/work.pattern.repository.md", Style(
            "style.work.pattern.repository",
            "Repositories return entities, never queries.\n\n```csharp\npublic Task<Order?> FindAsync(int id);\n```",
            "task_phrases:\n  - 'repository'"));

        Write("global/specialists/style/loose/loose.md", Style(
            "style.loose", "- Anything goes."));

        Write("global/specialists/style/loose/loose.pattern.repository.md", Style(
            "style.loose.pattern.repository",
            "Return whatever.\n\n```csharp\nIQueryable<Order> All();\n```",
            "task_phrases:\n  - 'repository'"));

        Write($"projects/{Slug}/specialists/style/codebase/codebase.md", Style(
            "style.codebase", "- Results, not exceptions, for expected failures."));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Style(string id, string body, string activation = "") =>
        $"---\nid: {id}\nkind: style\ntitle: {id}\nsummary: A part of a style.\n"
        + (activation.Length > 0 ? activation + "\n" : string.Empty)
        + $"---\n\n{body}\n";

    private static RepositoryEvidence CSharp() => new(
        Paths: ["src/Orders.cs", "src/Customers.cs", "src/Program.cs"],
        Extensions: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [".cs"] = 40 },
        Dependencies: [],
        Truncated: false);

    private async Task<SpecialistCatalogue> LibraryAsync(string? slug = Slug) =>
        await new SpecialistLibrary().LoadAsync(_root, slug);

    private async Task<EffectiveInstructions> ResolveAsync(
        string task = "Fix the null reference.",
        string? style = null,
        RepositoryEvidence? evidence = null,
        IReadOnlyList<string>? explicitly = null,
        IReadOnlyList<string>? excluded = null,
        int budget = 0) =>
        new SpecialistResolver().Resolve(new SpecialistRequest(
            await LibraryAsync(),
            Task: task,
            Explicit: explicitly,
            Excluded: excluded,
            Evidence: evidence ?? CSharp(),
            TokenBudget: budget,
            Style: style));

    private static List<string> Styles(EffectiveInstructions result) =>
        result.Selected
            .Where(s => s.Specialist.Kind == SpecialistKind.Style)
            .Select(s => s.Specialist.Id)
            .ToList();

    // ------------------------------------------------------------- in force

    [Fact]
    public async Task Without_a_choice_the_personal_and_codebase_styles_are_in_force()
    {
        var result = await ResolveAsync();

        Styles(result).Should().Contain(["style.personal", "style.codebase"]);
        Styles(result).Should().NotContain(["style.work", "style.loose"],
            "a named style is in force only where it was chosen");
    }

    [Fact]
    public async Task The_layers_compose_personal_then_named_then_codebase()
    {
        var result = await ResolveAsync(style: "work");

        // Not alphabetical, and not by where the files came from: the more
        // specific layer is read last, so it wins where two disagree.
        Styles(result).Should().ContainInOrder(
            "style.personal", "style.personal.csharp", "style.work", "style.codebase");
    }

    [Fact]
    public async Task The_core_of_each_style_says_why_it_is_there()
    {
        var result = await ResolveAsync(style: "work");

        var reasons = result.Selected
            .Where(s => s.Trigger == SpecialistTrigger.Style)
            .ToDictionary(s => s.Specialist.Id, s => s.Reason);

        reasons["style.personal"].Should().Be("your personal style");
        reasons["style.work"].Should().Be("the 'work' style chosen for this work");
        reasons["style.codebase"].Should().Be("this codebase's style");
    }

    [Fact]
    public async Task Choosing_personal_as_the_named_style_adds_nothing()
    {
        var result = await ResolveAsync(style: "personal");

        Styles(result).Where(id => id == "style.personal").Should().ContainSingle();
    }

    // ------------------------------------------------------- language files

    [Fact]
    public async Task A_language_file_follows_its_language_in()
    {
        var result = await ResolveAsync();

        var file = result.Selected.Single(s => s.Specialist.Id == "style.personal.csharp");

        file.Trigger.Should().Be(SpecialistTrigger.Accompanies);
        file.Reason.Should().Be("accompanies language.csharp");
    }

    [Fact]
    public async Task A_language_file_costs_nothing_where_the_language_is_absent()
    {
        var result = await ResolveAsync(evidence: RepositoryEvidence.None);

        Styles(result).Should().NotContain("style.personal.csharp");
    }

    [Fact]
    public async Task Excluding_the_language_takes_its_style_file_with_it()
    {
        var result = await ResolveAsync(excluded: ["language.csharp"]);

        Styles(result).Should().NotContain("style.personal.csharp");
    }

    [Fact]
    public async Task Dropping_the_language_for_budget_takes_its_style_file_with_it()
    {
        // One token: everything negotiable goes. C# is the weakest evidence and
        // goes first; its style file must not be left behind holding rules for
        // a language the session was not told about.
        var result = await ResolveAsync(budget: 1);

        result.Selected.Select(s => s.Specialist.Id).Should().NotContain("language.csharp");
        Styles(result).Should().NotContain("style.personal.csharp");
        Styles(result).Should().Contain("style.personal",
            "a core is never dropped for budget, which is why the validator keeps it small");
    }

    // ------------------------------------------------------------- patterns

    [Fact]
    public async Task A_pattern_loads_when_the_task_points_at_it()
    {
        var result = await ResolveAsync("Add a repository for invoices.", style: "work");

        Styles(result).Should().Contain("style.work.pattern.repository");
    }

    [Fact]
    public async Task A_pattern_costs_nothing_on_unrelated_work()
    {
        var result = await ResolveAsync("Fix the null reference.", style: "work");

        Styles(result).Should().NotContain("style.work.pattern.repository");
    }

    [Fact]
    public async Task A_style_not_in_force_never_answers_a_task()
    {
        var result = await ResolveAsync("Add a repository for invoices.", style: "work");

        Styles(result).Should().NotContain("style.loose.pattern.repository",
            "somebody else's pattern turning up because a word matched would be the wrong way round");
    }

    [Fact]
    public async Task A_part_of_another_style_can_still_be_named()
    {
        var result = await ResolveAsync(explicitly: ["style.loose.pattern.repository"]);

        result.Selected.Single(s => s.Specialist.Id == "style.loose.pattern.repository")
            .Trigger.Should().Be(SpecialistTrigger.Explicit);
    }

    // ------------------------------------------------------------ the parts

    [Theory]
    [InlineData("style.work", StylePart.Core)]
    [InlineData("style.work.csharp", StylePart.Language)]
    [InlineData("style.work.pattern.repository", StylePart.Pattern)]
    [InlineData("style.work.pattern", null)]
    [InlineData("style.work.csharp.extra", null)]
    [InlineData("language.csharp", null)]
    public void An_id_says_which_part_of_a_style_it_is(string id, StylePart? expected) =>
        CodingStyles.PartOf(id).Should().Be(expected);

    [Fact]
    public async Task A_library_groups_its_styles_by_name()
    {
        var styles = CodingStyles.All(await LibraryAsync());

        styles.Select(s => s.Name).Should().Equal("codebase", "loose", "personal", "work");

        var personal = styles.Single(s => s.Name == "personal");

        personal.Core!.Id.Should().Be("style.personal");
        personal.Languages.Select(s => s.Id).Should().Equal("style.personal.csharp");
        personal.Patterns.Should().BeEmpty();
    }

    // ------------------------------------------------------------ validation

    [Fact]
    public async Task A_well_formed_style_library_has_nothing_wrong_with_it()
    {
        var catalogue = await LibraryAsync();

        catalogue.Findings
            .Where(f => f.Rule is { } rule && rule.StartsWith("style.", StringComparison.Ordinal))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task A_malformed_style_id_is_an_error()
    {
        Write("global/specialists/style/bad.md", Style("style.work.csharp.extra", "- Rule."));

        var catalogue = await LibraryAsync();

        catalogue.Findings.Should().Contain(f =>
            f.Kind == "style-id" && f.Severity == RuleFindingSeverity.Error);
    }

    [Fact]
    public async Task A_core_over_the_ceiling_is_reported()
    {
        var rules = string.Join('\n', Enumerable.Repeat("- A rule that goes on for a while, and then some more.", 90));

        Write("global/specialists/style/big/big.md", Style("style.big", rules));

        var catalogue = await LibraryAsync();

        catalogue.Findings.Should().Contain(f => f.Rule == "style.big" && f.Kind == "style-core-too-large");
    }

    [Fact]
    public async Task A_pattern_without_example_code_is_reported()
    {
        Write("global/specialists/style/work/work.pattern.bare.md", Style(
            "style.work.pattern.bare", "Just a rule.", "task_phrases:\n  - 'bare'"));

        var catalogue = await LibraryAsync();

        catalogue.Findings.Should().Contain(f =>
            f.Rule == "style.work.pattern.bare" && f.Kind == "style-pattern-example");
    }

    [Fact]
    public async Task A_language_file_that_accompanies_nothing_is_reported()
    {
        Write("global/specialists/style/work/work.go.md", Style("style.work.go", "- Rule."));

        var catalogue = await LibraryAsync();

        catalogue.Findings.Should().Contain(f =>
            f.Rule == "style.work.go" && f.Kind == "style-language-unattached");
    }

    [Fact]
    public async Task A_codebase_style_outside_a_project_is_reported()
    {
        Write("global/specialists/style/codebase/codebase.csharp.md", Style(
            "style.codebase.csharp", "- Rule.", "accompanies:\n  - language.csharp"));

        var catalogue = await LibraryAsync();

        catalogue.Findings.Should().Contain(f =>
            f.Rule == "style.codebase.csharp" && f.Kind == "style-codebase-scope");
        catalogue.Findings.Should().NotContain(f =>
            f.Rule == "style.codebase" && f.Kind == "style-codebase-scope",
            "the project's own codebase style is where one belongs");
    }

    [Fact]
    public async Task A_style_core_is_not_reported_as_unreachable()
    {
        var catalogue = await LibraryAsync();

        catalogue.Findings.Should().NotContain(f =>
            f.Rule == "style.personal" && f.Kind == "specialist-unreachable");
    }

    // ------------------------------------------------------------ scaffolding

    [Fact]
    public void A_drafted_language_file_accompanies_its_language()
    {
        var draft = SpecialistScaffold.Draft("style.work.csharp").Value!;

        draft.Content.Should().Contain("accompanies:\n  - language.csharp".ReplaceLineEndings());
        SpecialistScaffold.DirectoryFor(draft).Should().Be(Path.Combine("style", "work"));
    }

    [Fact]
    public void A_drafted_pattern_is_found_by_its_name_and_carries_an_example()
    {
        var draft = SpecialistScaffold.Draft("style.work.pattern.repository").Value!;

        draft.Content.Should().Contain("task_phrases:");
        draft.Content.Should().Contain("'repository'");
        draft.Content.Should().Contain("```");
        draft.Content.Should().Contain("## When not to use this");
        draft.Content.Should().Contain("title: Work pattern: Repository");
    }

    [Fact]
    public void Only_a_pattern_is_drafted_with_an_example()
    {
        // A core or language file is paid for on every launch that loads it,
        // so an example block there would be context spent on illustration.
        SpecialistScaffold.Draft("style.work").Value!.Content.Should().NotContain("```");
        SpecialistScaffold.Draft("style.work.csharp").Value!.Content.Should().NotContain("```");
    }

    [Fact]
    public void A_drafted_part_that_is_no_part_of_a_style_is_refused() =>
        SpecialistScaffold.Draft("style.work.csharp.extra").Failed.Should().BeTrue();

    [Fact]
    public async Task A_drafted_style_loads_without_findings()
    {
        foreach (var id in new[] { "style.drafted", "style.drafted.csharp", "style.drafted.pattern.repository" })
        {
            var draft = SpecialistScaffold.Draft(id).Value!;

            Write(
                Path.Combine("global", "specialists", SpecialistScaffold.DirectoryFor(draft), draft.FileName),
                draft.Content);
        }

        var catalogue = await LibraryAsync();

        catalogue.Findings
            .Where(f => f.Rule is { } rule && rule.StartsWith("style.drafted", StringComparison.Ordinal))
            .Should().BeEmpty("a draft is written already valid");
    }

    // ------------------------------------------------------------ preferences

    [Fact]
    public void A_profile_that_only_picks_a_style_leaves_the_project_selection_alone()
    {
        var onlyStyle = new SpecialistPreferences { Style = "work" };

        onlyStyle.IsEmpty().Should().BeFalse();
        onlyStyle.HasSelection().Should().BeFalse();

        new SpecialistPreferences { Mode = "review" }.HasSelection().Should().BeTrue();
    }
}
