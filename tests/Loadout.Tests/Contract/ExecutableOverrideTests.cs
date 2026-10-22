using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Pointing the contract tests at a binary built elsewhere, which is how CI
/// tests the published single-file build of each runtime.
/// </summary>
public sealed class ExecutableOverrideTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_named_leaves_the_tests_on_their_own_build(string? path)
    {
        LoadoutProcess.Named(path).Should().BeNull();
    }

    [Fact]
    public void A_relative_path_is_made_absolute()
    {
        var named = LoadoutProcess.Named(Path.Combine("artefact", "loadout"));

        named.Should().Be(Path.GetFullPath(Path.Combine("artefact", "loadout")));
    }

    [Fact]
    public void A_path_to_nothing_is_kept_so_the_tests_fail_rather_than_skip()
    {
        var missing = Path.Combine(Path.GetTempPath(), "loadout-" + Guid.NewGuid().ToString("N"), "loadout");

        LoadoutProcess.Named(missing).Should().Be(missing);
    }

    /// <summary>
    /// Run only where the variable is set, which is CI's artefact job. There it
    /// is the proof that the rest of the contract tests ran the published
    /// binary and not a build of their own.
    /// </summary>
    [ArtefactFact]
    public void When_the_variable_is_set_every_contract_test_runs_that_binary()
    {
        var named = Environment.GetEnvironmentVariable(LoadoutProcess.ExecutableVariable)!;

        LoadoutProcess.Executable.Should().Be(Path.GetFullPath(named));
        File.Exists(LoadoutProcess.Executable).Should().BeTrue();
    }

    private sealed class ArtefactFactAttribute : FactAttribute
    {
        public ArtefactFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(LoadoutProcess.ExecutableVariable)))
            {
                Skip = $"Runs where {LoadoutProcess.ExecutableVariable} names a published binary, as CI's artefact job does.";
            }
        }
    }
}
