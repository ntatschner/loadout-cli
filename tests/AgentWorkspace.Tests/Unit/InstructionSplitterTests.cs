using AgentWorkspace.Core.Instructions;
using AgentWorkspace.Models;
using AgentWorkspace.Models.Instructions;
using FluentAssertions;
using Xunit;

namespace AgentWorkspace.Tests.Unit;

/// <summary>
/// The splitter rewrites a file somebody may have spent a year on, so what is
/// pinned here is mostly the safety: content moves verbatim, every line is
/// accounted for, and anything that cannot be accounted for stops the operation
/// rather than being written anyway.
/// </summary>
public sealed class InstructionSplitterTests : IDisposable
{
    private const string Source = """
# Project instructions

This file says how to work on the project.

## Conventions

- Use British spelling in user-facing text.
- Prefer explicit names over short ones.

## Database

- Migrations are never edited once merged.
- The connection string comes from the environment, never from a file.

## Frontend

- Components live under src/Web/Components.
""";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "agentctl-split-" + Guid.NewGuid().ToString("N"));

    private readonly InstructionSplitter _splitter = new();

    public InstructionSplitterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    private string WriteSource(string text = Source)
    {
        var path = Path.Combine(_root, "instructions.md");
        File.WriteAllText(path, text);

        return path;
    }

    private static SplitMap Map() => new()
    {
        Rules =
        [
            new RuleTarget { Name = "database", Description = "db work", Globs = ["src/Data/**"] },
            new RuleTarget { Name = "frontend", Description = "ui work", Globs = ["src/Web/**"] },
        ],
        Sections =
        [
            new SectionRoute { Pattern = "Database", Rule = "database" },
            new SectionRoute { Pattern = "Frontend", Rule = "frontend" },
        ],
    };

    [Fact]
    public async Task Moves_the_routed_sections_and_leaves_the_rest()
    {
        var plan = await _splitter.PlanAsync(WriteSource(), Map());

        plan.Succeeded.Should().BeTrue();
        plan.Value!.Rules.Select(r => r.Name).Should().BeEquivalentTo("database", "frontend");

        plan.Value.Core.Should().Contain("Use British spelling");
        plan.Value.Core.Should().NotContain("Migrations are never edited");

        plan.Value.Rules
            .Single(r => r.Name == "database").Body
            .Should().Contain("Migrations are never edited once merged.");
    }

    [Fact]
    public async Task Content_moves_verbatim()
    {
        var plan = await _splitter.PlanAsync(WriteSource(), Map());

        // The splitter's entire claim is that the result says what the source
        // said. Rewording, even to tidy, would break that.
        plan.Value!.Rules
            .Single(r => r.Name == "database").Body
            .Should().Contain("The connection string comes from the environment, never from a file.");
    }

    [Fact]
    public async Task Every_line_is_accounted_for()
    {
        var plan = await _splitter.PlanAsync(WriteSource(), Map());

        plan.Value!.IsLossless.Should().BeTrue();
        plan.Value.MissingLines.Should().BeEmpty();
    }

    [Fact]
    public async Task The_core_keeps_an_index_of_what_moved()
    {
        var plan = await _splitter.PlanAsync(WriteSource(), Map());

        // Without this the instructions simply appear to have been deleted.
        plan.Value!.Core.Should().Contain("database").And.Contain("frontend");
    }

    [Fact]
    public async Task A_rule_with_no_globs_is_refused()
    {
        var map = new SplitMap
        {
            Rules = [new RuleTarget { Name = "misc", Description = "bits" }],
            Sections = [new SectionRoute { Pattern = "Database", Rule = "misc" }],
        };

        var plan = await _splitter.PlanAsync(WriteSource(), map);

        // Splitting into an unscoped rule moves text around without making
        // anything cheaper: it still loads every session.
        plan.Failed.Should().BeTrue();
        plan.ExitCode.Should().Be(ExitCode.ConfigurationInvalid);
        plan.Error.Should().Contain("misc");
    }

    [Fact]
    public async Task Applying_writes_the_rules_and_takes_their_content_out_of_the_source()
    {
        var source = WriteSource();
        var rules = Path.Combine(_root, "rules");

        var plan = await _splitter.PlanAsync(source, Map());
        var applied = await _splitter.ApplyAsync(plan.Value!, rules);

        applied.Succeeded.Should().BeTrue();
        File.Exists(Path.Combine(rules, "database.md")).Should().BeTrue();
        File.ReadAllText(Path.Combine(rules, "database.md")).Should().Contain("globs: src/Data/**");
        File.ReadAllText(source).Should().NotContain("Migrations are never edited");
    }

    [Fact]
    public async Task On_a_real_sized_file_the_always_loaded_cost_falls()
    {
        // The toy fixture above is small enough that the index the splitter
        // writes offsets what it moves. That is not a defect, but it is not the
        // case anybody runs this for either, so the saving is measured on a
        // file the size of one worth splitting.
        var padding = string.Join("\n", Enumerable.Range(0, 200)
            .Select(i => $"- The migration runner refuses to reorder step {i} once it has run."));

        var plan = await _splitter.PlanAsync(
            WriteSource(Source.Replace(
                "- Migrations are never edited once merged.",
                "- Migrations are never edited once merged.\n" + padding)),
            Map());

        plan.Value!.MovedBytes.Should().BeGreaterThan(plan.Value.CoreBytes * 5);
        plan.Value.IsLossless.Should().BeTrue();
    }

    [Fact]
    public async Task Splitting_twice_is_refused()
    {
        var source = WriteSource();

        var plan = await _splitter.PlanAsync(source, Map());
        await _splitter.ApplyAsync(plan.Value!, Path.Combine(_root, "rules"));

        var second = await _splitter.PlanAsync(source, Map());

        // The second run would rebuild the rules from a file whose content has
        // already moved out of it, quietly replacing them with fragments.
        second.Failed.Should().BeTrue();
        second.Error.Should().Contain("already been split");
    }

    [Fact]
    public async Task A_plan_that_would_lose_a_line_cannot_be_applied()
    {
        var plan = await _splitter.PlanAsync(WriteSource(), Map());

        var damaged = plan.Value! with { MissingLines = ["- Migrations are never edited once merged."] };

        var applied = await _splitter.ApplyAsync(damaged, Path.Combine(_root, "rules"));

        applied.Failed.Should().BeTrue();
        applied.ExitCode.Should().Be(ExitCode.PolicyViolation);
        Directory.Exists(Path.Combine(_root, "rules")).Should().BeFalse();
    }

    [Fact]
    public async Task A_bullet_can_be_routed_out_of_a_section_that_stays()
    {
        var map = new SplitMap
        {
            Rules =
            [
                new RuleTarget { Name = "naming", Description = "naming", Globs = ["**/*.cs"] },
            ],
            Bullets =
            [
                new BulletRoute
                {
                    Section = "Conventions",
                    Contains = "explicit names",
                    Rule = "naming",
                },
            ],
        };

        var plan = await _splitter.PlanAsync(WriteSource(), map);

        plan.Value!.Core.Should().Contain("Use British spelling");
        plan.Value.Core.Should().NotContain("Prefer explicit names");
        plan.Value.Rules.Single().Body.Should().Contain("Prefer explicit names over short ones.");
        plan.Value.IsLossless.Should().BeTrue();
    }

    [Fact]
    public async Task A_suggested_map_lists_every_section_and_refuses_to_run_as_written()
    {
        var source = WriteSource();

        var map = await _splitter.SuggestMapAsync(source);

        map.Value!.Rules.Select(r => r.Name)
            .Should().BeEquivalentTo("conventions", "database", "frontend");

        // Deliberately not applicable as generated: the globs are the decision
        // the tool cannot make, so it stops until a person has made it.
        var plan = await _splitter.PlanAsync(source, map.Value);
        plan.Failed.Should().BeTrue();
    }

    [Fact]
    public async Task A_file_something_else_already_split_is_refused()
    {
        // The shape these projects arrive in: a short core that points at rule
        // files, written by whatever organised them before the launcher
        // existed. Splitting it again would rebuild those rules out of this
        // summary, replacing real instructions with a list of their own names.
        var source = WriteSource("""
# Project instructions

## Subsystem notes (path-scoped)

Detail moved out of this file into `.claude/rules/`.

- `.claude/rules/backend.md` - server rules
- `.claude/rules/database.md` - schema rules
- `.claude/rules/frontend.md` - UI rules
""");

        var plan = await _splitter.PlanAsync(source, Map());

        plan.Failed.Should().BeTrue();
        plan.Error.Should().Contain("already split");
    }

    [Fact]
    public async Task An_index_of_rules_is_recognised_without_a_heading_to_announce_it()
    {
        var source = WriteSource("""
# Project instructions

Read `rules/backend.md`, `rules/database.md` and `rules/frontend.md` as needed.
""");

        // Recognised by what the file does rather than by who wrote it: no
        // marker and no known heading, but it is plainly an index.
        (await _splitter.PlanAsync(source, Map())).Failed.Should().BeTrue();
    }

    [Fact]
    public async Task A_file_that_mentions_one_rule_in_passing_is_not_treated_as_split()
    {
        var source = WriteSource(Source + "\n\nSee `rules/naming.md` for the naming convention.\n");

        // One reference is a cross-reference, not an index. Refusing here would
        // block the split that this file actually needs.
        (await _splitter.PlanAsync(source, Map())).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Database", "Database", true)]
    [InlineData("database", "Database", true)]
    [InlineData("Database*", "Database and migrations", true)]
    [InlineData("*migrations", "Database and migrations", true)]
    [InlineData("Frontend", "Database", false)]
    public void Heading_patterns_allow_a_wildcard(string pattern, string title, bool expected) =>
        // So a map does not break the first time somebody rewords a heading.
        InstructionSplitter.Matches(pattern, title).Should().Be(expected);
}
