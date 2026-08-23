using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The classifier decides what is allowed to accumulate in memory, so its
/// judgement is pinned here rather than left to whatever the regular expressions
/// happen to do after the next edit.
/// </summary>
public sealed class MemoryFactClassifierTests
{
    [Theory]
    [InlineData("The launcher never deletes tracked files, because removing them rewrites the repository.")]
    [InlineData("Context compilation must run before the agent starts, or the profile is ignored.")]
    [InlineData("The convention is that a project rule of the same name overrides the workspace one.")]
    [InlineData("Backups live under the platform state directory, not the cache, so they survive cleanup.")]
    public void Keeps_a_standing_claim(string fact) =>
        MemoryFactClassifier.Classify(fact).Should().Be(FactVerdict.Durable);

    [Theory]
    [InlineData("Added a check to the migration service so it captures a backup first.")]
    [InlineData("Updated the context compiler to include rules and memory in the output.")]
    [InlineData("We renamed the architecture test namespace to stop it shadowing the framework type.")]
    [InlineData("The service now supports reading the index without loading every topic.")]
    public void Rejects_an_account_of_a_change(string fact) =>
        // The repository history already holds these, and they read as present
        // tense forever.
        MemoryFactClassifier.Classify(fact).Should().Be(FactVerdict.ChangeLog);

    [Theory]
    [InlineData("The highest migration number so far is 0052, next is 0053 when you add one.")]
    [InlineData("We are currently using the preview SDK until the release build lands.")]
    [InlineData("For now the Linux packaging step is skipped because the runner is unavailable.")]
    public void Rejects_a_fact_that_is_only_true_today(string fact) =>
        MemoryFactClassifier.Classify(fact).Should().Be(FactVerdict.TimeSensitive);

    [Theory]
    [InlineData("Let me check whether the analyzer is what makes the first build slow.")]
    [InlineData("npm ERR! code ELIFECYCLE and then a long stack that nobody needs again")]
    public void Rejects_session_chatter_and_tool_output(string fact) =>
        MemoryFactClassifier.Classify(fact).Should().Be(FactVerdict.Noise);

    [Fact]
    public void Rejects_something_too_short_to_be_a_fact() =>
        MemoryFactClassifier.Classify("Use spaces.").Should().Be(FactVerdict.TooShort);

    [Fact]
    public void Rejects_a_well_formed_line_that_claims_nothing() =>
        MemoryFactClassifier
            .Classify("Some general thoughts about the shape of the project directory tree here")
            .Should().Be(FactVerdict.NoAssertion);

    [Fact]
    public void Every_verdict_can_explain_itself() =>
        // The explanation is what the person reads; a verdict with no usable
        // sentence behind it is a finding nobody can act on.
        Enum.GetValues<FactVerdict>()
            .Should().OnlyContain(v => MemoryFactClassifier.Explain(v).Length > 20);
}

/// <summary>
/// Covers the instruction-layer defects that cost tokens without being visible:
/// a rule that believes it is scoped and is not, instructions written twice, and
/// imports whose size appears in nobody's budget.
/// </summary>
public sealed class RuleAuditorTests
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "loadout-audit-" + Guid.NewGuid().ToString("N"));

    private static RuleDocument Rule(
        string name,
        string body,
        bool alwaysApply = false,
        params string[] globs) =>
        new(name, $"/w/rules/{name}.md", "a rule", globs, alwaysApply, body, body.Length);

    [Fact]
    public void Reports_a_rule_that_declares_globs_and_also_always_applies()
    {
        var audit = RuleAuditor.Audit(
            [Rule("db", "Body text.", alwaysApply: true, "src/Data/**")],
            [],
            "starstats");

        // The listing shows globs, so the author believes it is scoped. It is
        // not: it loads every session and the globs do nothing.
        audit.Findings.Should().Contain(f => f.Kind == "always-with-globs");
    }

    [Fact]
    public void A_universal_glob_alongside_always_apply_is_not_a_contradiction() =>
        RuleAuditor
            .Audit([Rule("all", "Body.", alwaysApply: true, "**/*")], [], "starstats")
            .Findings.Should().NotContain(f => f.Kind == "always-with-globs");

    [Fact]
    public void Reports_the_same_instruction_written_in_two_rules()
    {
        const string line = "- Never commit an agent configuration file into an application repository.";

        var audit = RuleAuditor.Audit(
            [Rule("a", line, globs: "**/*.cs"), Rule("b", line, globs: "**/*.md")],
            [],
            "starstats");

        audit.Findings.Should().ContainSingle(f => f.Kind == "duplicate");
    }

    [Fact]
    public void Reports_an_instruction_a_rule_repeats_from_the_core_context()
    {
        var core = Path.Combine(_root, "instructions.md");
        Directory.CreateDirectory(_root);
        File.WriteAllText(core, "# Project\n\n- Always run the full test suite before claiming a fix.\n");

        try
        {
            var audit = RuleAuditor.Audit(
                [Rule("t", "- Always run the full test suite before claiming a fix.", globs: "**/*.cs")],
                [core],
                "starstats");

            // Paid for once in every session and again when the rule loads.
            audit.Findings.Should().Contain(f => f.Kind == "duplicates-core");
        }
        finally
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Reports_two_rules_claiming_the_same_paths() =>
        RuleAuditor.Audit(
            [Rule("a", "Body.", globs: "src/Data/**"), Rule("b", "Body.", globs: "src/Data/**")],
            [],
            "starstats")
            .Findings.Should().Contain(f => f.Kind == "overlapping-globs");

    [Fact]
    public void Reports_a_manifest_entry_pointing_at_a_file_that_does_not_exist()
    {
        var audit = RuleAuditor.Audit([], [Path.Combine(_root, "absent.md")], "starstats");

        audit.Findings.Should().Contain(f => f.Kind == "core-missing");
        audit.Verdict.Should().Be("ACTION REQUIRED");
    }

    [Fact]
    public void Follows_an_import_and_counts_what_it_adds()
    {
        Directory.CreateDirectory(_root);

        var imported = Path.Combine(_root, "standards.md");
        var core = Path.Combine(_root, "instructions.md");

        File.WriteAllText(imported, new string('x', 4096));
        File.WriteAllText(core, "# Project\n\n@standards.md\n");

        try
        {
            // An import's cost appears nowhere in the importing file's own size,
            // which is the usual reason a budget turns out to be several times
            // what somebody thought.
            RuleAuditor.Audit([], [core], "starstats")
                .Findings.Should().Contain(f => f.Kind == "import");
        }
        finally
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Reports_an_import_that_resolves_to_nothing()
    {
        Directory.CreateDirectory(_root);
        var core = Path.Combine(_root, "instructions.md");
        File.WriteAllText(core, "# Project\n\n@import missing.md\n");

        try
        {
            RuleAuditor.Audit([], [core], "starstats")
                .Findings.Should().Contain(f => f.Kind == "import-missing");
        }
        finally
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void A_clean_rule_set_reports_nothing() =>
        RuleAuditor.Audit(
            [Rule("data", "- Migrations are never edited once merged.", globs: "src/Data/**")],
            [],
            "starstats")
            .Verdict.Should().Be("HEALTHY");
}
