using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// One remedy read back as JSON from the built command line.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class RemedyShowContractTests
{
    /// <remarks>
    /// The record names its script file and <c>--script</c> adds the script
    /// itself. Both went out as <c>"script"</c>, so a reader got whichever of
    /// the two its parser kept and had no way to ask for the other.
    /// </remarks>
    [BuiltCliFact]
    public async Task Shown_as_JSON_it_gives_the_script_file_and_the_script_as_two_keys()
    {
        using var loadout = new LoadoutProcess();
        var shelf = Path.Combine(State(loadout.Home), "teams", "work", "system-watch", "remedies");
        Directory.CreateDirectory(shelf);

        await File.WriteAllTextAsync(Path.Combine(shelf, "free-disk.yaml"), """
            name: free-disk
            kind: script
            what: Clears the build cache.
            script: free-disk.ps1
            """);
        await File.WriteAllTextAsync(Path.Combine(shelf, "free-disk.ps1"), "Remove-Item cache -Recurse");

        var run = await loadout.RunAsync("team", "remedy", "show", "free-disk", "--team", "system-watch", "--script", "--json");

        run.ExitCode.Should().Be(0, run.StandardOutput + run.StandardError);

        using var shown = JsonDocument.Parse(run.StandardOutput);
        var keys = shown.RootElement.EnumerateObject().Select(property => property.Name).ToList();

        keys.Should().OnlyHaveUniqueItems();
        shown.RootElement.GetProperty("script").GetString().Should().Be("free-disk.ps1");
        shown.RootElement.GetProperty("scriptText").GetString().Should().Be("Remove-Item cache -Recurse");
    }

    private static string State(string home)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(home, "Local", "Loadout");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", "Loadout", "state");
        }

        return Path.Combine(home, "data", "loadout");
    }
}
