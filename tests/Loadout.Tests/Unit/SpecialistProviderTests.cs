using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Instructions;
using Loadout.Models.Configuration;
using Loadout.Models.Instructions;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Provider neutrality, and the switch that turns the whole layer off.
/// </summary>
/// <remarks>
/// <para>
/// Loadout composes one provider-neutral payload and each adapter delivers it
/// the way its own agent expects. That is not new: the compiled context already
/// worked that way, and specialists are written into the same file, so every
/// adapter carries them without any of them being told what a specialist is.
/// </para>
/// <para>
/// The tests below cover the two things that could quietly break it: an adapter
/// growing its own idea of instructions, and the off switch not actually
/// switching anything off.
/// </para>
/// </remarks>
public sealed class SpecialistProviderTests
{
    [Fact]
    public void No_adapter_knows_what_a_specialist_is()
    {
        var adapters = Directory.EnumerateFiles(
            Path.Combine(Repository(), "src", "Loadout.Agents"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            .Where(p => p.Contains("Claude", StringComparison.Ordinal)
                || p.Contains("Codex", StringComparison.Ordinal)
                || p.Contains("Generic", StringComparison.Ordinal))
            .ToList();

        adapters.Should().NotBeEmpty("the adapters have to be findable for this to check anything");

        foreach (var adapter in adapters)
        {
            var text = File.ReadAllText(adapter);

            // The moment an adapter starts reasoning about specialists, the
            // model has stopped being provider-neutral and the next agent added
            // will have to reimplement whatever this one decided.
            text.Should().NotContain(
                "SpecialistDocument",
                $"{Path.GetFileName(adapter)} should receive a compiled payload, not compose one");

            text.Should().NotContain("EffectiveInstructions", $"{Path.GetFileName(adapter)}");
        }
    }

    [Fact]
    public void Every_adapter_receives_the_same_compiled_payload()
    {
        var agents = Path.Combine(Repository(), "src", "Loadout.Agents");

        // Each delivers it differently — a flag, a file in the working
        // directory, a placeholder in an argument list — and all three read the
        // same property. That is the whole of the provider abstraction for
        // instructions, and it already existed.
        foreach (var adapter in new[]
        {
            Path.Combine(agents, "Claude", "ClaudeAdapter.cs"),
            Path.Combine(agents, "Codex", "CodexAdapter.cs"),
            Path.Combine(agents, "Generic", "GenericAgentAdapter.cs"),
        })
        {
            File.Exists(adapter).Should().BeTrue(adapter);
            File.ReadAllText(adapter).Should().Contain("CompiledContext");
        }
    }

    [Fact]
    public async Task Turning_specialists_off_gives_an_empty_set_rather_than_a_failure()
    {
        var service = Service(new InstructionContextSettings { Specialists = false });

        var resolved = await service.ResolveAsync(new InstructionRequest(Task: "optimise the query"));

        resolved.Succeeded.Should().BeTrue("switching a feature off is not an error");
        resolved.Value!.Selected.Should().BeEmpty();

        // Not even foundation. Off means the launch composes exactly the
        // context it composed before this feature existed.
        resolved.Value.Budget.EstimatedTokens.Should().Be(0);
    }

    [Fact]
    public async Task Left_on_it_resolves_as_normal()
    {
        var service = Service(new InstructionContextSettings());

        var resolved = await service.ResolveAsync(new InstructionRequest(Task: "optimise the query"));

        resolved.Value!.Selected.Should().NotBeEmpty();
    }

    [Fact]
    public async Task The_budget_comes_from_configuration_rather_than_a_fixed_default()
    {
        var service = Service(new InstructionContextSettings { MaxTokens = 4242 });

        var resolved = await service.ResolveAsync(new InstructionRequest(Task: "fix the tests"));

        resolved.Value!.Budget.TokenBudget.Should().Be(4242);
    }

    /// <summary>An instruction service reading a configuration written to a temporary home.</summary>
    private static InstructionService Service(InstructionContextSettings settings)
    {
        var home = Path.Combine(Path.GetTempPath(), "loadout-prov-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(home);

        var permissions = new NoOpFilePermissions();
        var environment = new FakeEnvironmentProvider(home);
        // The Linux layout, deliberately, so this behaves the same on every
        // host: the test is about configuration reaching the resolver, not
        // about where a platform puts its files.
        var paths = new Loadout.Platform.Linux.LinuxPaths(
            environment,
            permissions,
            new Loadout.Models.Platform.HostPlatform(
                Loadout.Models.Platform.HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));
        var yaml = new YamlStore(permissions);
        var configuration = new ConfigurationService(paths, environment, yaml);

        configuration.SaveConfigAsync(new LauncherConfig { InstructionContext = settings })
            .GetAwaiter().GetResult();

        return new InstructionService(
            new SpecialistLibrary(),
            new SpecialistResolver(),
            new RepositoryEvidenceReader(),
            configuration);
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
