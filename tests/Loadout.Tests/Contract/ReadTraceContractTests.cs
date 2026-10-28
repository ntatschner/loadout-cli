using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// <c>LOADOUT_READ_TRACE=1</c> through the real binary: a summary of what the
/// process read lands under the state directory, and without the variable
/// nothing does.
/// </summary>
/// <remarks>
/// Checked from outside the process because what matters is that the ledger
/// is started by the entry point and written as the process ends, which no
/// test inside the process can see.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class ReadTraceContractTests
{
    [BuiltCliFact]
    public async Task Asked_to_the_process_writes_what_it_read_as_it_ends()
    {
        using var loadout = new LoadoutProcess();
        var project = await MemoryWriteContractTests.Registered(loadout);

        (await loadout.RunAsync(
            "memory", "write", "restart-manager", "--project", project,
            "--fact", "Restart Manager is disabled by policy on this machine.",
            "--description", "why installers fail with 1603 over a running app"))
            .ExitCode.Should().Be(0);

        Summaries(loadout).Should().BeEmpty("nothing asked for a trace yet");

        loadout.Environment["LOADOUT_READ_TRACE"] = "1";

        var shown = await loadout.RunAsync("memory", "show", "restart-manager", "--project", project, "--json");
        shown.ExitCode.Should().Be(0, shown.StandardError);

        var summary = Summaries(loadout).Should().ContainSingle().Subject;
        Path.GetFileName(summary).Should().EndWith("-memory-show.json");

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(summary));
        var callers = document.RootElement.GetProperty("sources").EnumerateArray()
            .SelectMany(source => source.GetProperty("callers").EnumerateArray())
            .Select(caller => caller.GetProperty("caller").GetString())
            .ToList();

        callers.Should().Contain(caller => caller!.StartsWith("MemoryService.", StringComparison.Ordinal),
            "showing a topic reads it");
    }

    private static List<string> Summaries(LoadoutProcess loadout) =>
        Directory.EnumerateDirectories(loadout.Home, "read-trace", SearchOption.AllDirectories)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.json"))
            .ToList();
}
