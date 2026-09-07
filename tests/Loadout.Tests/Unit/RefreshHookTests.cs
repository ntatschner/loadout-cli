using System.Text.Json.Nodes;
using FluentAssertions;
using Loadout.Core.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The after-edit hook: what it reads from Claude, what it says back, and how
/// it is installed without disturbing anything else in the settings file.
/// </summary>
public sealed class RefreshHookTests : IDisposable
{
    private readonly string _root;

    public RefreshHookTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-hook-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a run over a temp directory.
        }
    }

    [Fact]
    public void The_file_and_working_directory_are_read_from_the_payload_and_nothing_else_breaks_it()
    {
        var input = RefreshHook.Parse(
            """{"session_id":"s","cwd":"D:/git/repo","hook_event_name":"PostToolUse","tool_name":"Edit","tool_input":{"file_path":"D:/git/repo/src/A.cs","old_string":"a","new_string":"b"},"tool_response":{}}""");

        input.Should().Be(new RefreshHookInput("D:/git/repo/src/A.cs", "D:/git/repo"));

        // A tool that names no file, an empty payload, and a payload that is
        // not JSON at all: none of these are the agent's problem.
        RefreshHook.Parse("""{"tool_name":"Bash","tool_input":{"command":"ls"}}""")
            .Should().Be(new RefreshHookInput(null, null));
        RefreshHook.Parse(string.Empty).Should().Be(new RefreshHookInput(null, null));
        RefreshHook.Parse("not json {").Should().Be(new RefreshHookInput(null, null));
        RefreshHook.Parse("[1,2]").Should().Be(new RefreshHookInput(null, null));
    }

    [Fact]
    public void Nothing_is_said_unless_the_map_changed()
    {
        var quiet = new SymbolRefresh([new SymbolRefreshedFile("src/A.cs", 3, 4)], 10, "abc", false);

        RefreshHook.Context(quiet).Should().BeNull();
        RefreshHook.Output(quiet).Should().BeNull();

        var changed = quiet with
        {
            MapChanges =
            [
                new SymbolMapChange("src", "- `src` — 1 type(s): A", "- `src` — 2 type(s): A, B"),
                new SymbolMapChange("old", "- `old` — 1 type(s): Gone", null),
            ],
        };

        var context = RefreshHook.Context(changed)!;

        context.Should().StartWith("The map of the code changed with that edit:");
        context.Should().Contain("\n- `src` — 2 type(s): A, B");
        context.Should().Contain("\n`old` no longer holds any types.");
        context.Should().NotContain("\r", "it is read through a JSON string, not a console");

        var output = JsonNode.Parse(RefreshHook.Output(changed)!)!.AsObject();
        var specific = output["hookSpecificOutput"]!.AsObject();

        specific["hookEventName"]!.GetValue<string>().Should().Be("PostToolUse");
        specific["additionalContext"]!.GetValue<string>().Should().Be(context);
    }

    [Fact]
    public async Task Installing_adds_the_hook_beside_whatever_is_there_and_is_idempotent()
    {
        var path = Path.Combine(_root, "settings.json");

        await File.WriteAllTextAsync(
            path,
            """
            {
              "permissions": { "allow": ["Bash(git status)"] },
              "hooks": {
                "PostToolUse": [
                  { "matcher": "Bash", "hooks": [ { "type": "command", "command": "echo other" } ] }
                ],
                "Stop": [ { "hooks": [ { "type": "command", "command": "echo bye" } ] } ]
              }
            }
            """);

        var first = await RefreshHookInstaller.InstallAsync(path, @"C:\tools\loadout.exe");

        first.Succeeded.Should().BeTrue(first.Error);
        first.Value!.AlreadyThere.Should().BeFalse();
        first.Value!.Command.Should().Be("\"C:\\tools\\loadout.exe\" docs refresh --hook");

        var again = await RefreshHookInstaller.InstallAsync(path, @"C:\tools\loadout.exe");

        again.Value!.AlreadyThere.Should().BeTrue();

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();

        // Everything that was there is still there, and ours is beside it once.
        root["permissions"]!["allow"]![0]!.GetValue<string>().Should().Be("Bash(git status)");
        root["hooks"]!["Stop"].Should().NotBeNull();

        var post = root["hooks"]!["PostToolUse"]!.AsArray();

        post.Should().HaveCount(2);
        post[0]!["hooks"]![0]!["command"]!.GetValue<string>().Should().Be("echo other");
        post[1]!["matcher"]!.GetValue<string>().Should().Be("Edit|Write|MultiEdit");

        (await RefreshHookInstaller.IsInstalledAsync(path)).Value.Should().BeTrue();
    }

    [Fact]
    public void A_hosted_launcher_names_its_assembly_and_a_shipped_one_names_only_itself()
    {
        // The first install of this hook, from a development build, wrote
        // "dotnet.exe docs refresh --hook": the host with nothing to run.
        RefreshHookInstaller.CommandFor(@"C:\Program Files\dotnet\dotnet.exe", @"D:\build\loadout.dll")
            .Should().Be("\"C:\\Program Files\\dotnet\\dotnet.exe\" \"D:\\build\\loadout.dll\" docs refresh --hook");

        RefreshHookInstaller.CommandFor(@"C:\tools\loadout.exe")
            .Should().Be("\"C:\\tools\\loadout.exe\" docs refresh --hook");

        // The project named on the command, so the hook need not work it out
        // from the directory on every edit.
        RefreshHookInstaller.CommandFor(@"C:\tools\loadout.exe", null, "starstats")
            .Should().Be("\"C:\\tools\\loadout.exe\" docs refresh --hook --project starstats");
    }

    [Fact]
    public async Task The_installed_entry_carries_a_timeout_and_survives_odd_neighbours()
    {
        var path = Path.Combine(_root, "settings.json");

        // Somebody else's hook with a command that is not a string. Asking it
        // for one would throw out of the middle of the install.
        await File.WriteAllTextAsync(
            path,
            """
            {
              "hooks": {
                "PostToolUse": [
                  { "matcher": "Bash", "hooks": [ { "type": "command", "command": null }, { "type": "command", "command": { "odd": true } } ] }
                ]
              }
            }
            """);

        var installed = await RefreshHookInstaller.InstallAsync(path, @"C:\tools\loadout.exe", null, "starstats");

        installed.Succeeded.Should().BeTrue(installed.Error);

        var post = JsonNode.Parse(await File.ReadAllTextAsync(path))!["hooks"]!["PostToolUse"]!.AsArray();

        post.Should().HaveCount(2);

        var ours = post[1]!["hooks"]![0]!.AsObject();

        ours["command"]!.GetValue<string>().Should().EndWith("--hook --project starstats");
        ours["timeout"]!.GetValue<int>().Should().Be(RefreshHookInstaller.TimeoutSeconds);

        (await RefreshHookInstaller.UninstallAsync(path)).Value.Should().BeTrue();
        post = JsonNode.Parse(await File.ReadAllTextAsync(path))!["hooks"]!["PostToolUse"]!.AsArray();
        post.Should().HaveCount(1, "the odd neighbour is left exactly as it was");
    }

    [Fact]
    public async Task A_moved_launcher_is_updated_in_place_rather_than_installed_twice()
    {
        var path = Path.Combine(_root, "settings.json");

        await RefreshHookInstaller.InstallAsync(path, @"C:\old\loadout.exe");

        var moved = await RefreshHookInstaller.InstallAsync(path, @"C:\new\loadout.exe");

        moved.Value!.AlreadyThere.Should().BeFalse();

        var post = JsonNode.Parse(await File.ReadAllTextAsync(path))!["hooks"]!["PostToolUse"]!.AsArray();

        post.Should().HaveCount(1);
        post[0]!["hooks"]![0]!["command"]!.GetValue<string>().Should().Contain(@"C:\new\loadout.exe");

        // An entry written before the timeout existed is brought up to date,
        // even when its command already matches, rather than reported as
        // already there.
        post[0]!["hooks"]![0]!.AsObject().Remove("timeout");
        await File.WriteAllTextAsync(path, post.Parent!.Parent!.ToJsonString());

        var same = await RefreshHookInstaller.InstallAsync(path, @"C:\new\loadout.exe");

        same.Value!.AlreadyThere.Should().BeFalse();

        post = JsonNode.Parse(await File.ReadAllTextAsync(path))!["hooks"]!["PostToolUse"]!.AsArray();
        post[0]!["hooks"]![0]!["timeout"]!.GetValue<int>().Should().Be(RefreshHookInstaller.TimeoutSeconds);
    }

    [Fact]
    public async Task Uninstalling_removes_only_ours_and_tidies_what_it_emptied()
    {
        var path = Path.Combine(_root, "settings.json");

        await File.WriteAllTextAsync(
            path,
            """
            {
              "theme": "dark",
              "hooks": {
                "PostToolUse": [
                  { "matcher": "Bash", "hooks": [ { "type": "command", "command": "echo other" } ] }
                ]
              }
            }
            """);

        await RefreshHookInstaller.InstallAsync(path, @"C:\tools\loadout.exe");

        (await RefreshHookInstaller.UninstallAsync(path)).Value.Should().BeTrue();
        (await RefreshHookInstaller.UninstallAsync(path)).Value.Should().BeFalse("it is already gone");

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();

        root["theme"]!.GetValue<string>().Should().Be("dark");
        root["hooks"]!["PostToolUse"]!.AsArray().Should().HaveCount(1);
        (await RefreshHookInstaller.IsInstalledAsync(path)).Value.Should().BeFalse();

        // A file that held nothing but ours is left with no empty scaffolding.
        var alone = Path.Combine(_root, "alone.json");

        await RefreshHookInstaller.InstallAsync(alone, @"C:\tools\loadout.exe");
        await RefreshHookInstaller.UninstallAsync(alone);

        JsonNode.Parse(await File.ReadAllTextAsync(alone))!.AsObject().Should().BeEmpty();

        (await RefreshHookInstaller.UninstallAsync(Path.Combine(_root, "missing.json")))
            .Value.Should().BeFalse();
    }

    [Fact]
    public async Task A_settings_file_that_is_not_json_is_left_alone()
    {
        var path = Path.Combine(_root, "settings.json");

        await File.WriteAllTextAsync(path, "{ not json");

        var result = await RefreshHookInstaller.InstallAsync(path, @"C:\tools\loadout.exe");

        result.Failed.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("{ not json");
    }
}
