using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Agents;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which specialists a task actually gets, and why.
/// </summary>
/// <remarks>
/// <para>
/// Written against the built-in library rather than fixtures. The activation
/// data is the feature — the guidance is only words — so a test that invented
/// its own evidence would prove the resolver worked and say nothing about
/// whether the library it ships with does.
/// </para>
/// <para>
/// The four journeys below are the ones the brief sets out, and the negative
/// assertions in them matter more than the positive ones. Loading the right
/// specialist is easy; not loading the other forty is the whole point.
/// </para>
/// </remarks>
public sealed class SpecialistResolverTests
{
    private static SpecialistCatalogue? _catalogue;

    private static async Task<SpecialistCatalogue> LibraryAsync() =>
        _catalogue ??= await new SpecialistLibrary().LoadAsync(workspaceRoot: null);

    /// <summary>A repository that genuinely contains all of it, which is the hard case.</summary>
    private static RepositoryEvidence Everything() => new(
        Paths:
        [
            "src/Api/Program.cs", "src/Api/Api.csproj", "src/Api/Orders.cs",
            "src/Api/Customers.cs", "db/schema.sql", "db/seed.sql",
            "k8s/deployment.yaml", "infra/main.tf", "Dockerfile",
        ],
        Extensions: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = 174, [".sql"] = 2, [".yaml"] = 1, [".tf"] = 1,
        },
        Dependencies:
        [
            "<PackageReference Include=\"Microsoft.AspNetCore.OpenApi\" />",
            "<PackageReference Include=\"Microsoft.EntityFrameworkCore\" />",
            "<PackageReference Include=\"Npgsql.EntityFrameworkCore.PostgreSQL\" />",
            "<PackageReference Include=\"Azure.Identity\" />",
        ],
        Truncated: false);

    private static async Task<EffectiveInstructions> ResolveAsync(
        string task,
        string? mode = null,
        IReadOnlyList<string>? explicitly = null,
        IReadOnlyList<string>? excluded = null,
        IReadOnlyList<string>? preferred = null,
        RepositoryEvidence? evidence = null,
        AgentDescriptor? agent = null,
        int budget = 0)
    {
        return new SpecialistResolver().Resolve(new SpecialistRequest(
            await LibraryAsync(),
            mode,
            task,
            explicitly,
            excluded,
            preferred,
            evidence ?? Everything(),
            agent,
            budget));
    }

    private static IEnumerable<string> Ids(EffectiveInstructions result) =>
        result.Selected.Select(s => s.Specialist.Id);

    // ------------------------------------------------------- the four journeys

    [Fact]
    public async Task A_plain_language_bug_does_not_drag_in_the_whole_technology_stack()
    {
        var result = await ResolveAsync("Fix this null reference exception.");

        Ids(result).Should().Contain("language.csharp", "the repository is 174 C# files");
        Ids(result).Should().Contain("function.debugging");

        // The heart of the feature. This repository really does use PostgreSQL,
        // Kubernetes, Terraform, Docker and Azure, and none of them has
        // anything to do with a null reference.
        Ids(result).Should().NotContain("database.postgresql");
        Ids(result).Should().NotContain("platform.kubernetes");
        Ids(result).Should().NotContain("platform.docker");
        Ids(result).Should().NotContain("cloud.azure");
        Ids(result).Should().NotContain("function.security");
    }

    [Fact]
    public async Task A_database_performance_task_gathers_exactly_the_right_layers()
    {
        var result = await ResolveAsync(
            "Why is this EF Core PostgreSQL query taking 12 seconds?",
            mode: "investigate");

        result.Mode.Should().Be("investigate");

        Ids(result).Should().Contain("language.csharp");
        Ids(result).Should().Contain("framework.ef-core");
        Ids(result).Should().Contain("database.postgresql");
        Ids(result).Should().Contain("function.performance");
        Ids(result).Should().Contain("skill.query-optimisation");

        // The generic database specialist comes too. It is reachable here both
        // as PostgreSQL's declared requirement and from the word "query" in the
        // task, and the reported reason is the task, because that is what the
        // user actually said.
        Ids(result).Should().Contain("function.database");
    }

    [Fact]
    public async Task A_specialist_brings_what_it_declares_it_needs()
    {
        // A neutral task, so nothing here is reachable by wording. PostgreSQL
        // deliberately does not repeat the generic database guidance, so
        // loading it alone would leave a gap the user cannot see.
        var result = await ResolveAsync(
            "have a look at this",
            explicitly: ["database.postgresql"],
            evidence: RepositoryEvidence.None);

        var brought = result.Selected.Single(s => s.Specialist.Id == "function.database");

        brought.Trigger.Should().Be(SpecialistTrigger.Required);
        brought.Reason.Should().Contain("database.postgresql");
    }

    [Fact]
    public async Task A_security_review_loads_the_review_posture_and_the_security_layers()
    {
        var result = await ResolveAsync(
            "Review the authentication changes in this PR.",
            mode: "review");

        result.Mode.Should().Be("review");

        Ids(result).Should().Contain("mode.review");
        Ids(result).Should().Contain("function.security");
        Ids(result).Should().Contain("function.code-review");
        Ids(result).Should().Contain("skill.secure-code-review");

        // Still knows what the code is written in.
        Ids(result).Should().Contain("language.csharp");
    }

    [Fact]
    public async Task A_flaky_test_does_not_assume_concurrency_without_evidence()
    {
        var result = await ResolveAsync(
            "This test fails around one run in twenty in GitHub Actions.",
            mode: "investigate");

        Ids(result).Should().Contain("function.testing");
        Ids(result).Should().Contain("function.devops");
        Ids(result).Should().Contain("skill.flaky-test-investigation");

        // Flakiness is often concurrency and is not evidence of it. The brief
        // names this one specifically, and it is the difference between a
        // resolver and a set of associations.
        Ids(result).Should().NotContain("function.concurrency");
    }

    // ------------------------------------------------------------- activation

    [Fact]
    public async Task Foundation_and_a_mode_are_always_present()
    {
        var result = await ResolveAsync("anything at all");

        result.Selected.Where(s => s.Specialist.Kind == SpecialistKind.Foundation)
            .Should().NotBeEmpty();

        result.Selected.Should().ContainSingle(s => s.Specialist.Kind == SpecialistKind.Mode);
    }

    [Fact]
    public async Task An_unrecognised_mode_falls_back_rather_than_leaving_no_posture()
    {
        var result = await ResolveAsync("do a thing", mode: "sideways");

        result.Mode.Should().Be(SpecialistResolver.DefaultMode);
    }

    [Fact]
    public async Task One_stray_file_of_a_language_is_not_evidence_of_that_language()
    {
        var evidence = new RepositoryEvidence(
            ["notes/one-off.sql", "src/main.py", "src/app.py", "src/util.py"],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [".sql"] = 1, [".py"] = 3,
            },
            [],
            false);

        var result = await ResolveAsync("tidy up the helpers", evidence: evidence);

        Ids(result).Should().Contain("language.python");

        // The brief names this exactly: one .sql file must not make every task
        // a database task.
        Ids(result).Should().NotContain("language.sql");
    }

    [Fact]
    public async Task What_the_user_asked_for_is_loaded_whatever_the_repository_says()
    {
        var result = await ResolveAsync(
            "fix a typo",
            explicitly: ["function.security"]);

        var chosen = result.Selected.Single(s => s.Specialist.Id == "function.security");

        chosen.Trigger.Should().Be(SpecialistTrigger.Explicit);
        chosen.Reason.Should().Contain("selected by you");
    }

    [Fact]
    public async Task A_specialist_named_that_does_not_exist_is_reported_to_the_caller()
    {
        var request = new SpecialistRequest(
            await LibraryAsync(),
            Explicit: ["function.security", "language.cobol"]);

        // Not resolved around quietly. Somebody who asked for guidance and did
        // not get it should be told, and the caller has an exit code to say it
        // with.
        SpecialistResolver.UnknownExplicit(request).Should().ContainSingle()
            .Which.Should().Be("language.cobol");
    }

    [Fact]
    public async Task A_preference_alone_does_not_load_a_database_specialist()
    {
        var result = await ResolveAsync(
            "Fix this null reference exception.",
            preferred: ["database.postgresql", "language.csharp"]);

        // Preferred means likely relevant, not always on. The repository does
        // use PostgreSQL and the project does prefer it, and this task still
        // has nothing to do with it.
        Ids(result).Should().NotContain("database.postgresql");
        Ids(result).Should().Contain("language.csharp");
    }

    [Fact]
    public async Task A_preference_protects_a_specialist_from_being_dropped_first()
    {
        var plain = await ResolveAsync("Optimise the PostgreSQL query.");
        var preferred = await ResolveAsync(
            "Optimise the PostgreSQL query.",
            preferred: ["database.postgresql"]);

        var before = plain.Selected.Single(s => s.Specialist.Id == "database.postgresql");
        var after = preferred.Selected.Single(s => s.Specialist.Id == "database.postgresql");

        after.Confidence.Should().BeGreaterThan(before.Confidence);

        // The reason still says how it was actually reached, rather than being
        // replaced by the vaguer "the project prefers it".
        after.Trigger.Should().Be(before.Trigger);
    }

    [Fact]
    public async Task An_exclusion_beats_inference_but_not_an_explicit_request()
    {
        var inferred = await ResolveAsync(
            "Fix the C# null reference.",
            excluded: ["language.csharp"]);

        Ids(inferred).Should().NotContain("language.csharp");
        inferred.Omitted.Should().Contain(s => s.Specialist.Id == "language.csharp");

        var asked = await ResolveAsync(
            "Fix the C# null reference.",
            explicitly: ["language.csharp"],
            excluded: ["language.csharp"]);

        // Asking for a thing and excluding it in the same breath is a
        // contradiction. Honouring the explicit request is the safer way round:
        // the user gets what they named, and can see it in the explanation.
        Ids(asked).Should().Contain("language.csharp");
    }

    [Fact]
    public async Task A_specialist_the_agent_cannot_use_is_left_out_with_a_reason()
    {
        var catalogue = await LibraryAsync();

        var needsSkills = new SpecialistDocument(
            "function.exotic", SpecialistKind.Function, "Exotic", "Needs a capability.",
            new SpecialistActivation(
                TaskPhrases: ["exotic"],
                Capabilities: [AgentCapabilities.SessionResume]),
            "Guidance.", 10);

        var extended = new Dictionary<string, SpecialistDocument>(
            catalogue.Specialists, StringComparer.OrdinalIgnoreCase)
        {
            ["function.exotic"] = needsSkills,
        };

        var agent = new AgentDescriptor(
            "plain", "Plain Agent", true, "/bin/plain", "1.0",
            new Dictionary<string, bool> { [AgentCapabilities.SessionResume] = false });

        var result = new SpecialistResolver().Resolve(new SpecialistRequest(
            new SpecialistCatalogue(extended, []),
            Task: "do the exotic thing",
            Evidence: RepositoryEvidence.None,
            Agent: agent));

        Ids(result).Should().NotContain("function.exotic");

        result.Omitted.Should().Contain(s =>
            s.Specialist.Id == "function.exotic" && s.Reason.Contains("Plain Agent"));
    }

    [Fact]
    public async Task The_reason_reported_is_the_strongest_one_that_applied()
    {
        // C# is reachable here both from 174 .cs files and from the task naming
        // it. The task is what the user actually said, so that is what the
        // explanation has to show — anything else is misleading in exactly the
        // case where somebody is checking whether they were understood.
        var result = await ResolveAsync("Fix the C# nullable warning.");

        result.Selected.Single(s => s.Specialist.Id == "language.csharp")
            .Trigger.Should().Be(SpecialistTrigger.TaskSemantics);
    }

    [Fact]
    public async Task Resolution_is_deterministic_and_ordered_by_layer()
    {
        var first = await ResolveAsync("Optimise the PostgreSQL query.", mode: "investigate");
        var second = await ResolveAsync("Optimise the PostgreSQL query.", mode: "investigate");

        Ids(first).Should().Equal(Ids(second));

        // Composition order is general to specific, so the agent reads the
        // narrowest guidance last.
        var kinds = first.Selected.Select(s => (int)s.Specialist.Kind).ToList();

        kinds.Should().BeInAscendingOrder();
    }

    // ---------------------------------------------------------------- budget

    [Fact]
    public async Task Under_budget_nothing_is_dropped()
    {
        var result = await ResolveAsync(
            "Optimise the PostgreSQL query.", mode: "investigate", budget: 100_000);

        result.Budget.IsOverBudget.Should().BeFalse();
        result.DroppedForBudget.Should().BeEmpty();
    }

    [Fact]
    public async Task Over_budget_the_weakest_evidence_goes_first()
    {
        var generous = await ResolveAsync(
            "Optimise the PostgreSQL query.", mode: "investigate", budget: 100_000);

        // Deliberately far too small, so almost everything negotiable has to go.
        var tight = await ResolveAsync(
            "Optimise the PostgreSQL query.", mode: "investigate", budget: 900);

        tight.Selected.Count.Should().BeLessThan(generous.Selected.Count);
        tight.DroppedForBudget.Should().NotBeEmpty();

        var dropped = tight.Omitted.Select(s => s.Specialist.Id).ToList();
        var kept = Ids(tight).ToList();

        // Whatever else went, the safety rules and the posture stayed.
        kept.Should().Contain("foundation.change-safety");
        kept.Should().Contain("mode.investigate");
        dropped.Should().NotContain("foundation.change-safety");
    }

    [Fact]
    public async Task An_explicit_choice_survives_a_budget_that_drops_everything_else()
    {
        var result = await ResolveAsync(
            "Optimise the PostgreSQL query.",
            mode: "investigate",
            explicitly: ["function.security"],
            budget: 900);

        // Being quietly denied something you asked for is the worst available
        // outcome: the session proceeds and you believe it has guidance it does
        // not have.
        Ids(result).Should().Contain("function.security");
    }

    [Fact]
    public async Task Nothing_is_ever_cut_in_half_to_fit()
    {
        var result = await ResolveAsync(
            "Optimise the PostgreSQL query.", mode: "investigate", budget: 900);

        // Whole specialists only. Half a specialist reads as complete guidance
        // while missing the caveat that made it safe, and nothing on the page
        // says so.
        foreach (var selection in result.Selected)
        {
            var full = (await LibraryAsync()).Find(selection.Specialist.Id);

            selection.Specialist.Bytes.Should().Be(full!.Bytes);
        }
    }

    [Fact]
    public async Task Dropping_for_budget_is_reported_rather_than_silent()
    {
        var result = await ResolveAsync(
            "Optimise the PostgreSQL query.", mode: "investigate", budget: 900);

        result.DroppedForBudget.Should().NotBeEmpty();

        foreach (var dropped in result.DroppedForBudget)
        {
            // The reason keeps both halves: how it was reached, and why it went.
            dropped.Reason.Should().Contain("budget");
        }
    }

    [Fact]
    public async Task The_budget_reports_what_was_spent_against_what_was_allowed()
    {
        var result = await ResolveAsync("Fix the null reference.", budget: 12_000);

        result.Budget.TokenBudget.Should().Be(12_000);
        result.Budget.EstimatedTokens.Should().BeGreaterThan(0);
        result.Budget.Bytes.Should().Be(result.Selected.Sum(s => s.Specialist.Bytes));
        result.Budget.UsedFraction.Should().NotBeNull();
    }

    [Fact]
    public async Task No_budget_means_no_ceiling_rather_than_a_ceiling_of_zero()
    {
        var result = await ResolveAsync("Optimise the PostgreSQL query.", budget: 0);

        result.Budget.IsOverBudget.Should().BeFalse();
        result.Budget.UsedFraction.Should().BeNull();
        result.DroppedForBudget.Should().BeEmpty();
    }

    // ------------------------------------------------------------- normalise

    [Theory]
    [InlineData("the capital city", "api", false)]
    [InlineData("rapid prototyping", "api", false)]
    [InlineData("review the api contract", "api", true)]
    [InlineData("fix the C# warning", "c#", true)]
    [InlineData("upgrade to .NET 10", ".net", true)]
    [InlineData("classic problem", "ci", false)]
    public void Task_phrases_match_whole_words_rather_than_substrings(
        string task, string phrase, bool expected)
    {
        // "api" sits inside "capital", "rapid" and "therapist"; "ci" sits
        // inside "classic" and "precision". A raw substring test would have
        // loaded those specialists with a reason claiming the task asked for
        // them.
        var haystack = SpecialistResolver.Normalise(task);
        var needle = SpecialistResolver.Normalise(phrase);

        haystack.Contains(needle, StringComparison.Ordinal).Should().Be(expected);
    }
}
