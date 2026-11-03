using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Offers from outside the process: reading one changes nothing, accepting
/// makes one prerequisite joined to the work, a queue holds the work on it, and
/// a 'Not needed' answer is listed and undone.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class TaskOffersContractTests
{
    private const string Team = "iterating-project";

    [BuiltCliFact]
    public async Task Accepting_the_ci_offer_makes_one_prerequisite_a_queue_then_holds_the_work_on()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");
        await File.WriteAllTextAsync(Path.Combine(loadout.Home, "repository", "global.json"), "{}");

        var declared = await loadout.RunAsync(
            "task", "declare", "ship", "open", "--title", "Ship it", "--type", "release", "--done-when", "it ships", "--project", project);

        declared.ExitCode.Should().Be(0, declared.StandardError);
        string.Join(" ", declared.StandardOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Should().Contain("--accept ci", "declare prints the gap and the command, and creates nothing");

        var read = (await loadout.RunAsync("task", "offers", "ship", "--project", project, "--json")).Json();
        var offer = read.GetProperty("offers").EnumerateArray().Single();

        offer.GetProperty("check").GetString().Should().Be("ci");
        offer.GetProperty("linked").GetBoolean().Should().BeFalse();
        read.GetProperty("unseen").EnumerateArray().Select(one => one.GetProperty("check").GetString()).Should().Contain("environments");
        (await loadout.RunAsync("task", "show", "add-ci", "--project", project)).ExitCode.Should().NotBe(0, "reading made nothing");

        var accepted = await loadout.RunAsync("task", "offers", "ship", "--project", project, "--accept=ci", "--json");

        accepted.ExitCode.Should().Be(0, accepted.StandardError);

        var made = accepted.Json();

        made.GetProperty("prerequisite").GetString().Should().Be("add-ci");
        made.GetProperty("created").GetBoolean().Should().BeTrue();
        made.GetProperty("templateVersion").GetInt32().Should().BeGreaterThan(0);
        made.GetProperty("stacks").EnumerateArray().Select(one => one.GetString()).Should().Equal("dotnet");

        (await loadout.RunAsync("team", "queue", "add", Team, "ship", "--project", project)).ExitCode.Should().Be(0);

        var plan = (await loadout.RunAsync("team", "queue", "plan", Team, "--json")).Json();

        plan.GetProperty("left").EnumerateArray().Single().GetProperty("reason").GetString().Should().Contain("add-ci");

        var again = (await loadout.RunAsync("task", "offers", "ship", "--project", project, "--json")).Json();

        again.GetProperty("offers").EnumerateArray().Single().GetProperty("linked").GetBoolean().Should().BeTrue();
    }

    [BuiltCliFact]
    public async Task A_not_needed_answer_is_listed_quiets_the_offer_and_can_be_undone()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        (await loadout.RunAsync("task", "declare", "ship", "open", "--title", "Ship it", "--type", "release", "--project", project)).ExitCode.Should().Be(0);

        (await loadout.RunAsync("project", "capabilities", project, "--not-needed=ci", "--json")).ExitCode.Should().Be(0);

        var listed = (await loadout.RunAsync("project", "capabilities", project, "--json")).Json();

        listed.GetProperty("notNeeded").EnumerateArray().Single().GetProperty("check").GetString().Should().Be("ci");
        (await loadout.RunAsync("task", "offers", "ship", "--project", project, "--json")).Json()
            .GetProperty("offers").GetArrayLength().Should().Be(0);

        var undone = (await loadout.RunAsync("project", "capabilities", project, "--needed=ci", "--json")).Json();

        undone.GetProperty("touches").EnumerateArray().Select(one => one.GetString()).Should().Equal("ship");
    }

    [BuiltCliFact]
    public async Task Changing_a_type_keeps_the_link_unless_told_and_then_takes_it_off_in_the_same_command()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        (await loadout.RunAsync("idea", "add", "--text=Ship release notes", "--id", "notes", "--project", project)).ExitCode.Should().Be(0);
        (await loadout.RunAsync("idea", "type", "notes", "release", "--project", project)).ExitCode.Should().Be(0);
        (await loadout.RunAsync("task", "offers", "notes", "--project", project, "--accept=ci")).ExitCode.Should().Be(0);

        var kept = (await loadout.RunAsync("idea", "type", "notes", "bug-fix", "--project", project, "--json")).Json();

        kept.GetProperty("released").GetArrayLength().Should().Be(0);
        kept.GetProperty("stillWaiting").EnumerateArray().Select(one => one.GetString()).Should().Equal("add-ci");

        var sheet = (await loadout.RunAsync("task", "offers", "notes", "--project", project, "--json")).Json();

        sheet.GetProperty("links").EnumerateArray().Single().GetProperty("outgrown").GetBoolean().Should().BeTrue();

        var released = (await loadout.RunAsync("idea", "type", "notes", "docs", "--project", project, "--release-links", "--json")).Json();

        released.GetProperty("released").EnumerateArray().Select(one => one.GetString()).Should().Equal("add-ci");
        (await loadout.RunAsync("task", "requires", "notes", "--project", project, "--json")).Json()
            .GetProperty("requires").GetArrayLength().Should().Be(0);
    }

    [BuiltCliFact]
    public async Task An_offer_with_no_template_or_no_project_is_refused()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        (await loadout.RunAsync("task", "declare", "ship", "open", "--title", "Ship it", "--project", project)).ExitCode.Should().Be(0);

        var refused = await loadout.RunAsync("task", "offers", "ship", "--project", project, "--accept=branch-protection");

        refused.ExitCode.Should().NotBe(0);
        refused.StandardError.Should().Contain("no template");
    }
}
