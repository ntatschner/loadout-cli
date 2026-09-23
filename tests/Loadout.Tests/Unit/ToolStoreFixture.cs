using Loadout.Core.Tools;
using Loadout.Models.Configuration;
using Loadout.Models.Platform;
using Loadout.Models.Tools;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Loadout.Tests.Unit;

/// <summary>
/// A tool catalogue in a temporary state directory, and drafts to put in it.
/// </summary>
/// <remarks>
/// Every run goes through <see cref="StubProcessLauncher" />, so nothing here
/// needs pwsh on the machine running the tests.
/// </remarks>
internal sealed class ToolStoreFixture : IDisposable
{
    private static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private readonly string _root;

    public ToolStoreFixture(params string[] known)
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-tools-" + Guid.NewGuid().ToString("N"));

        Paths = new LinuxPaths(
            new FakeEnvironmentProvider(
                Path.Combine(_root, "home"),
                new Dictionary<string, string>
                {
                    ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                    ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                    ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                    ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
                }),
            new NoOpFilePermissions(),
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST"));

        Paths.EnsureDirectoriesExist();
        Known = known;
    }

    public IPlatformPaths Paths { get; }

    public IReadOnlyList<string> Known { get; }

    /// <summary>A registry whose every run exits with <paramref name="exit" />.</summary>
    public (ToolRegistry Registry, StubProcessLauncher Launcher) Registry(int exit = 0)
    {
        var launcher = new StubProcessLauncher(string.Empty, exit);

        return (new ToolRegistry(Paths, new ToolHarness(launcher), TimeProvider.System, () => Known), launcher);
    }

    /// <summary>A manifest with every field filled.</summary>
    public static ToolVersion Manifest(string name, string version) => new()
    {
        Name = name,
        Version = version,
        Script = "tool.ps1",
        Purpose = "Clears a named cache directory of files older than a given age.",
        Inputs = [new ToolInput { Name = "CachePath", Type = "path", Required = true, Describe = "The directory." }],
        Outputs = new ToolOutputs { Stdout = "freed <n> files", Exit = new Dictionary<int, string> { [0] = "success" } },
        Dependencies = ["pwsh>=7"],
        Constraints = ["Deletes only below CachePath."],
        ErrorBehaviour = "Exits non-zero with one line on stderr.",
        Examples = [new ToolExample { Command = "pwsh -File tool.ps1 -CachePath ./cache", Expect = "exit 0" }],
        Origin = "A build cache kept filling the disk.",
    };

    /// <summary>One case of each class, each expecting <paramref name="exit" />.</summary>
    public static List<ToolCase> Cases(int exit = 0, string prefix = "") =>
    [
        .. ToolCaseClass.All.Select(one => new ToolCase
        {
            Name = prefix + one,
            Class = one,
            Args = new Dictionary<string, string> { ["CachePath"] = "{tmp}/cache" },
            Expect = new ToolCaseExpect { Exit = exit },
        }),
    ];

    /// <summary>Writes a draft directory and returns where it is.</summary>
    public string Draft(ToolVersion manifest, string script, IEnumerable<ToolCase> cases)
    {
        var directory = Path.Combine(_root, "drafts", manifest.Name, manifest.Version + "-" + Guid.NewGuid().ToString("N")[..6]);

        Directory.CreateDirectory(Path.Combine(directory, "cases"));
        File.WriteAllText(Path.Combine(directory, "manifest.yaml"), Writer.Serialize(manifest));
        File.WriteAllText(Path.Combine(directory, manifest.Script), script);

        foreach (var one in cases)
        {
            File.WriteAllText(Path.Combine(directory, "cases", one.Name + ".yaml"), Writer.Serialize(one));
        }

        return directory;
    }

    /// <summary>A person's agreement to run this draft's harness, on a machine that lets tool-test run.</summary>
    public static ToolTestConsent Agreed(ToolVersion manifest, string script, IReadOnlyList<ToolCase> cases) =>
        new(
            "trusted",
            [
                new TrustedRemedy
                {
                    Remedy = ToolHarness.Named(manifest),
                    Fingerprint = ToolHarness.Fingerprint(script, cases),
                },
            ]);

    /// <summary>Writes, verifies and promotes a version, failing the test if any step does.</summary>
    public async Task<string> PromoteAsync(ToolRegistry registry, ToolVersion manifest, string script, List<ToolCase> cases)
    {
        var draft = Draft(manifest, script, cases);
        var verified = await registry.VerifyAsync(draft, Agreed(manifest, script, cases));

        if (verified.Failed || verified.Value!.Gate is not { Passed: true })
        {
            throw new InvalidOperationException("Verify did not pass: " + (verified.Error ?? verified.Value!.Because));
        }

        var promoted = registry.Promote(draft, new ToolPromotionRequest("lesson", "inbox/test", Summary: "Clears a cache.", Capabilities: ["cache", "disk"]));

        if (promoted.Failed)
        {
            throw new InvalidOperationException("Promote did not pass: " + promoted.Error);
        }

        return draft;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
