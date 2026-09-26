using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// What 'config set' says about a value it cannot take: before anything is
/// written as well as after, and with the setting's own reason.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class ConfigSetDryRunContractTests
{
    [BuiltCliFact]
    public async Task A_preview_refuses_what_the_real_run_would_refuse()
    {
        using var loadout = new LoadoutProcess();

        var previewed = await loadout.RunAsync("config", "set", "show-speech", "loud", "--dry-run");

        // It said "Would set show-speech" for this and exited 0, then the real
        // run refused it: the preview approved a change that could not happen.
        previewed.ExitCode.Should().NotBe(0);
        (previewed.StandardOutput + previewed.StandardError).Should().Contain("screen-reader",
            "the refusal says which words the setting takes");

        var good = await loadout.RunAsync("config", "set", "show-speech", "screen-reader", "--dry-run");

        good.ExitCode.Should().Be(0);
        good.StandardOutput.Should().Contain("Would set show-speech");
    }

    [BuiltCliFact]
    public async Task A_refusal_says_what_was_wrong_and_not_only_what_the_setting_is_for()
    {
        using var loadout = new LoadoutProcess();

        var refused = await loadout.RunAsync("config", "set", "team-remediation", "disk=sometimes");
        var said = refused.StandardOutput + refused.StandardError;

        refused.ExitCode.Should().NotBe(0);
        said.Should().Contain("For disk", "the kind whose rule could not be read is named");
        said.Should().Contain("trusted", "and the rules it could have been");
        (await loadout.RunAsync("config", "get", "team-remediation")).StandardOutput
            .Should().NotContain("sometimes", "nothing was written");
    }
}
