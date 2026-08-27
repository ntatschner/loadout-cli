using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The specialist commands as somebody actually meets them.
/// </summary>
/// <remarks>
/// Run against the real binary and a throwaway home. The point of doing it this
/// way is that the built-in library travels inside the assembly: a packaging
/// mistake would leave every unit test passing against an in-process load and
/// the shipped binary with no specialists at all.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class InstructionsContractTests
{
    [BuiltCliFact]
    public async Task The_library_is_there_in_a_built_binary()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("instructions", "list", "--json");

        run.ExitCode.Should().Be(0);

        using var document = JsonDocument.Parse(run.StandardOutput);

        document.RootElement.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
    }

    [BuiltCliFact]
    public async Task The_shipped_library_validates()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("instructions", "validate", "--json");

        using var document = JsonDocument.Parse(run.StandardOutput);

        document.RootElement.GetProperty("errors").GetInt32().Should().Be(0);

        // Clean means zero, so the exit code has to agree with the report.
        run.ExitCode.Should().Be(0);
    }

    [BuiltCliFact]
    public async Task Listing_can_be_narrowed_to_one_kind()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("instructions", "list", "--kind", "database", "--json");

        using var document = JsonDocument.Parse(run.StandardOutput);

        var kinds = document.RootElement.GetProperty("specialists")
            .EnumerateArray()
            .Select(s => s.GetProperty("kind").GetString())
            .Distinct()
            .ToList();

        kinds.Should().ContainSingle().Which.Should().Be("database");
    }

    [BuiltCliFact]
    public async Task A_kind_that_does_not_exist_is_refused_with_the_ones_that_do()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("instructions", "list", "--kind", "wizard");

        run.ExitCode.Should().Be(2, "an unusable argument is an argument error");

        // Names the valid kinds, because the person reading this is fixing a
        // typo and the list is the thing they need.
        (run.StandardOutput + run.StandardError).Should().Contain("language");
    }

    [BuiltCliFact]
    public async Task Showing_a_specialist_gives_its_guidance_and_its_activation()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("instructions", "show", "database.postgresql", "--json");

        run.ExitCode.Should().Be(0);

        using var document = JsonDocument.Parse(run.StandardOutput);

        var root = document.RootElement;

        root.GetProperty("id").GetString().Should().Be("database.postgresql");
        root.GetProperty("body").GetString().Should().NotBeNullOrWhiteSpace();

        var activation = root.GetProperty("activation");

        activation.GetProperty("dependencies").EnumerateArray()
            .Select(d => d.GetString())
            .Should().Contain("Npgsql");
    }

    [BuiltCliFact]
    public async Task Showing_something_that_does_not_exist_fails_and_says_where_to_look()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("instructions", "show", "language.cobol");

        run.ExitCode.Should().Be(3, "asking for something absent is a not-found");

        (run.StandardOutput + run.StandardError).Should().Contain("instructions list");
    }

    [BuiltCliFact]
    public async Task Explain_answers_with_the_reason_for_every_specialist()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(
            "instructions", "explain", "Why is this PostgreSQL query so slow?",
            "--mode", "investigate", "--json");

        run.ExitCode.Should().Be(0);

        using var document = JsonDocument.Parse(run.StandardOutput);

        var root = document.RootElement;

        root.GetProperty("mode").GetString().Should().Be("investigate");

        var selected = root.GetProperty("selected").EnumerateArray().ToList();

        selected.Should().NotBeEmpty();

        // Every selection carries why it is there. That is the whole contract:
        // an instruction set nobody can account for is one nobody can correct.
        foreach (var selection in selected)
        {
            selection.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
            selection.GetProperty("trigger").GetString().Should().NotBeNullOrWhiteSpace();
        }

        var ids = selected.Select(s => s.GetProperty("id").GetString()).ToList();

        ids.Should().Contain("database.postgresql");
        ids.Should().Contain("function.performance");
    }

    [BuiltCliFact]
    public async Task Explain_reports_the_context_it_would_cost()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("instructions", "explain", "fix the tests", "--json");

        using var document = JsonDocument.Parse(run.StandardOutput);

        var context = document.RootElement.GetProperty("context");

        context.GetProperty("estimatedTokens").GetInt32().Should().BeGreaterThan(0);
        context.GetProperty("bytes").GetInt64().Should().BeGreaterThan(0);
        context.GetProperty("overBudget").GetBoolean().Should().BeFalse();
    }

    [BuiltCliFact]
    public async Task A_specialist_can_be_demanded_and_ruled_out()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(
            "instructions", "explain", "tidy up",
            "--specialist", "function.security",
            "--without", "function.testing",
            "--json");

        using var document = JsonDocument.Parse(run.StandardOutput);

        var ids = document.RootElement.GetProperty("selected").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString())
            .ToList();

        ids.Should().Contain("function.security");
        ids.Should().NotContain("function.testing");
    }

    [BuiltCliFact]
    public async Task Demanding_a_specialist_that_does_not_exist_stops_rather_than_proceeding()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(
            "instructions", "explain", "tidy up", "--specialist", "language.cobol");

        // Quietly resolving without it would leave somebody believing the
        // session had guidance it did not.
        run.ExitCode.Should().Be(2);

        (run.StandardOutput + run.StandardError).Should().Contain("cobol");
    }

    [BuiltCliFact]
    public async Task Every_instruction_command_offers_json()
    {
        using var loadout = new LoadoutProcess();

        foreach (var command in new[] { "list", "validate", "explain" })
        {
            var run = await loadout.RunAsync("instructions", command, "--json");

            var act = () => JsonDocument.Parse(run.StandardOutput);

            act.Should().NotThrow($"'instructions {command} --json' has to emit a document");
        }
    }
}
