using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The office page's engine - what blocks, finding a way, where a visitor
/// stands, turning a floor - run under node against floors the planner
/// generated, at all four turns.
/// </summary>
/// <remarks>
/// <para>
/// The planner's own tests prove every desk and spot on a floor can be walked
/// to. That is the server's idea of walking. The people are moved by the page,
/// with its own idea of what is in the way, and nothing proved the two agree
/// until this ran: 12,112 scenes and half a million paths, the first time.
/// </para>
/// <para>
/// Node, because the page is JavaScript and the alternative was a second copy
/// of the engine in C# for the test to check, which would drift from the one
/// that ships. A machine without node fails here, by name, rather than
/// skipping: a test that quietly does not run looks exactly like one that
/// passed.
/// </para>
/// </remarks>
public sealed class OfficeEngineTests : IDisposable
{
    private readonly string _scenes = Path.Combine(Path.GetTempPath(), "loadout-engine-" + Guid.NewGuid().ToString("N")[..8] + ".jsonl");

    public void Dispose()
    {
        File.Delete(_scenes);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task The_page_can_walk_everywhere_the_planner_puts_somebody_on_every_floor_at_every_turn()
    {
        WriteScenes(seeds: 20);

        var (code, output) = await NodeAsync(
            Path.Combine(Repository(), "tests", "Loadout.Tests", "Office", "engine-check.mjs"),
            _scenes,
            Path.Combine(Repository(), "src", "Loadout.Core", "Teams", "Daemon", "dashboard.html"));

        code.Should().Be(0, output);
        output.Should().Contain("reaches everything");
    }

    /// <summary>Floors for every team size, the lobby and roof at every size, and both basements.</summary>
    private void WriteScenes(int seeds)
    {
        var kit = OfficeKit.Kit();
        var rules = OfficeRules.Default;

        using var file = new StreamWriter(_scenes);

        void Write(string name, OfficeScene scene) => file.WriteLine(JsonSerializer.Serialize(new { name, scene }));

        for (var team = 1; team <= 30; team++)
        {
            for (var seed = 0; seed < seeds; seed++)
            {
                Write($"floor team {team} seed {seed}", FloorPlanner.Plan(kit, rules, $"run-{seed}", team).Scene);
            }
        }

        for (var seats = 0; seats <= 48; seats += 4)
        {
            Write($"lobby {seats}", FloorPlanner.Lobby(kit, rules, seats).Scene);
            Write($"roof {seats}", FloorPlanner.Roof(kit, rules, seats).Scene);
        }

        Write("basement 1", FloorPlanner.Basement(kit, rules, 1).Scene);
        Write("basement 2", FloorPlanner.Basement(kit, rules, 2).Scene);
    }

    private static async Task<(int Code, string Output)> NodeAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process? node;

        try
        {
            node = Process.Start(start);
        }
        catch (Win32Exception)
        {
            node = null;
        }

        node.Should().NotBeNull("node has to be installed to test the office page's engine; this fails rather than skips, so it is never mistaken for a pass");

        using (node)
        {
            var output = node!.StandardOutput.ReadToEndAsync();
            var error = node.StandardError.ReadToEndAsync();

            await node.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5));

            return (node.ExitCode, await output + await error);
        }
    }

    private static string Repository()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository has to be findable from the tests");

        return root!.FullName;
    }
}
