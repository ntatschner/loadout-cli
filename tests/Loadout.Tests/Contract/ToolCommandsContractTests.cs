using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Tests.Unit;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Loadout.Tests.Contract;

/// <summary>
/// The shapes <c>loadout tools ... --json</c> prints, which scripts and the
/// MCP tools' callers read, and that <c>--dry-run</c> changes nothing.
/// </summary>
/// <remarks>
/// The catalogue is built in a temporary directory by the registry itself,
/// promoted through the gate, and copied to where the built launcher keeps it,
/// which the launcher names in <c>tools search --json</c>.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class ToolCommandsContractTests
{
    private const string Script = "param([string]$CachePath)\nGet-ChildItem $CachePath | Remove-Item\n";

    private static async Task<(LoadoutProcess Loadout, string Root)> CatalogueAsync()
    {
        var loadout = new LoadoutProcess();
        var root = (await loadout.RunAsync("tools", "search", "x", "--json")).Json().GetProperty("root").GetString()!;

        using var store = new ToolStoreFixture();
        var (registry, _) = store.Registry();
        await store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        Copy(registry.Root(), root);

        return (loadout, root);
    }

    private static void Copy(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        Directory.CreateDirectory(to);

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
        }
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => !file.EndsWith("registry.lock", StringComparison.Ordinal))
            .ToDictionary(file => Path.GetRelativePath(root, file), File.ReadAllText, StringComparer.Ordinal);

    [BuiltCliFact]
    public async Task Search_json_names_the_query_the_root_and_each_tool()
    {
        var (loadout, root) = await CatalogueAsync();
        using var _ = loadout;

        var run = await loadout.RunAsync("tools", "search", "cache", "--json");

        run.ExitCode.Should().Be(0, run.StandardError);
        var json = run.Json();
        json.GetProperty("query").GetString().Should().Be("cache");
        json.GetProperty("root").GetString().Should().Be(root);
        var tool = json.GetProperty("tools").EnumerateArray().Should().ContainSingle().Subject;
        tool.GetProperty("name").GetString().Should().Be("free-cache");
        tool.GetProperty("active").GetString().Should().Be("1.0");
        tool.GetProperty("lifecycle").GetString().Should().Be("active");
    }

    [BuiltCliFact]
    public async Task Health_json_measures_each_active_tool_on_four_dimensions()
    {
        var (loadout, _) = await CatalogueAsync();
        using var _ = loadout;

        var run = await loadout.RunAsync("tools", "health", "free-cache", "--json");

        run.ExitCode.Should().Be(0, run.StandardError);
        var tool = run.Json().GetProperty("tools").EnumerateArray().Should().ContainSingle().Subject;
        tool.GetProperty("name").GetString().Should().Be("free-cache");
        tool.GetProperty("version").GetString().Should().Be("1.0");
        tool.GetProperty("performance").GetProperty("cases").GetInt32().Should().Be(4);
        tool.GetProperty("usability").GetProperty("uses").GetInt32().Should().Be(0);
        tool.GetProperty("maintainability").GetProperty("scriptLines").GetInt32().Should().Be(2);
        tool.GetProperty("relevance").GetProperty("daysIdle").GetInt32().Should().Be(0, "it was promoted just now and never used");
        tool.GetProperty("crossed").GetArrayLength().Should().Be(0);

        (await loadout.RunAsync("tools", "health", "no-such-tool", "--json")).ExitCode.Should().NotBe(0);
    }

    [BuiltCliFact]
    public async Task Show_json_carries_versions_and_trust()
    {
        var (loadout, _) = await CatalogueAsync();
        using var _ = loadout;

        var run = await loadout.RunAsync("tools", "show", "free-cache", "--json");

        run.ExitCode.Should().Be(0, run.StandardError);
        var json = run.Json();
        json.GetProperty("name").GetString().Should().Be("free-cache");
        json.GetProperty("version").GetString().Should().Be("1.0");
        json.GetProperty("versions").GetProperty("1.0").GetString().Should().Be("known-good");
        json.GetProperty("trusted").GetBoolean().Should().BeFalse();
        json.GetProperty("manifest").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [BuiltCliFact]
    public async Task Submit_json_names_the_inbox_item()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(
            "tools", "submit", "--kind", "idea", "--text", "A tool that clears a named cache directory.", "--json");

        run.ExitCode.Should().Be(0, run.StandardError);
        var json = run.Json();
        json.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("kind").GetString().Should().Be("idea");
        json.GetProperty("overlapping").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [BuiltCliFact]
    public async Task Used_json_says_what_was_recorded()
    {
        var (loadout, _) = await CatalogueAsync();
        using var _ = loadout;

        var run = await loadout.RunAsync("tools", "used", "free-cache@1.0", "--outcome", "ok", "--json");

        run.ExitCode.Should().Be(0, run.StandardError);
        var json = run.Json();
        json.GetProperty("tool").GetString().Should().Be("free-cache");
        json.GetProperty("version").GetString().Should().Be("1.0");
        json.GetProperty("outcome").GetString().Should().Be("ok");
        json.GetProperty("recorded").GetBoolean().Should().BeTrue();
    }

    [BuiltCliFact]
    public async Task Dry_run_changes_nothing()
    {
        var (loadout, root) = await CatalogueAsync();
        using var _ = loadout;
        var before = Snapshot(root);

        string[][] asked =
        [
            ["tools", "submit", "--kind", "idea", "--text", "Something new.", "--dry-run"],
            ["tools", "used", "free-cache@1.0", "--outcome", "failed", "--dry-run"],
            ["tools", "trust", "free-cache@1.0", "--dry-run"],
            ["tools", "deprecate", "free-cache", "--reason", "Replaced.", "--dry-run"],
            ["tools", "retire", "free-cache", "--dry-run"],
        ];

        foreach (var one in asked)
        {
            var run = await loadout.RunAsync(one);
            (run.StandardOutput + run.StandardError).Should().Contain("Dry run", string.Join(' ', one));
        }

        Snapshot(root).Should().BeEquivalentTo(before);
        (await loadout.RunAsync("tools", "show", "free-cache", "--json")).Json()
            .GetProperty("trusted").GetBoolean().Should().BeFalse("a dry-run trust wrote no agreement");
    }

    [BuiltCliFact]
    public async Task Dry_run_on_a_missing_draft_says_so()
    {
        var (loadout, root) = await CatalogueAsync();
        using var _ = loadout;
        var missing = Path.Combine(root, "drafts", "no-such-tool", "1");

        string[][] asked =
        [
            ["tools", "verify", missing, "--dry-run"],
            ["tools", "promote", missing, "--because", "lesson", "--source", "inbox/x", "--dry-run"],
        ];

        foreach (var one in asked)
        {
            var run = await loadout.RunAsync(one);

            run.ExitCode.Should().NotBe(0, string.Join(' ', one));
            (run.StandardOutput + run.StandardError).Should().Contain("no readable manifest.yaml", string.Join(' ', one));
            (run.StandardOutput + run.StandardError).Should().NotContain("would be", string.Join(' ', one));
        }
    }

    [BuiltCliFact]
    public async Task Trust_fingerprints_the_script_on_disk_not_the_manifest_field()
    {
        var (loadout, root) = await CatalogueAsync();
        using var _ = loadout;

        // The manifest sits beside the script and anything that can write one
        // can write the other: a fingerprint read from it would be whatever the
        // writer wanted agreed to.
        var manifest = Path.Combine(root, "free-cache", "versions", "1.0", "manifest.yaml");
        var yaml = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties().Build().Deserialize<Loadout.Models.Tools.ToolVersion>(File.ReadAllText(manifest));
        var forged = RemedyCeiling.Fingerprint("Remove-Item -Recurse /\n");
        yaml.Fingerprint = forged;
        File.WriteAllText(manifest, new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull).Build().Serialize(yaml));

        var run = await loadout.RunAsync("tools", "trust", "free-cache@1.0", "--by", "tester", "--json");

        run.ExitCode.Should().Be(0, run.StandardError);
        var fingerprint = run.Json().GetProperty("fingerprint").GetString();
        fingerprint.Should().Be(RemedyCeiling.Fingerprint(Script));
        fingerprint.Should().NotBe(forged);
    }
}
