using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// A task recording where it came from, through the real binary: what an
/// agent or a person types when they leave work for later.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class TaskSourceContractTests
{
    [BuiltCliFact]
    public async Task Task_declare_records_a_source_and_task_show_gives_it_back()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        var declared = await loadout.RunAsync(
            "task", "declare", "retry-logic", "open", "--title", "Retry the upload", "--source", "transcript:3f2a-91", "--project", project);

        declared.ExitCode.Should().Be(0, declared.StandardError);

        var shown = await loadout.RunAsync("task", "show", "retry-logic", "--project", project, "--json");

        shown.ExitCode.Should().Be(0, shown.StandardError);
        shown.Json().GetProperty("task").GetProperty("source").GetString().Should().Be("transcript:3f2a-91");
    }

    [BuiltCliFact]
    public async Task A_source_that_is_not_one_is_refused_and_nothing_is_recorded()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        var declared = await loadout.RunAsync(
            "task", "declare", "retry-logic", "open", "--title", "Retry the upload", "--source", "pr:12", "--project", project);

        declared.ExitCode.Should().Be(2);
        declared.StandardError.Should().Contain("'pr:12' is not a source");
        (await loadout.RunAsync("task", "show", "retry-logic", "--project", project)).ExitCode.Should().NotBe(0);
    }
}
