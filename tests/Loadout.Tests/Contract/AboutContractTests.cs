using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// <c>loadout about --json</c>, which the launcher window and Studio's page
/// read the same model as.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class AboutContractTests
{
    [BuiltCliFact]
    public async Task About_json_has_the_four_sections()
    {
        using var loadout = new LoadoutProcess();

        // Not forwarded to a daemon of this machine, and with the source off,
        // so nothing here can reach the network.
        loadout.Environment["LOADOUT_LOCAL"] = "1";

        var off = await loadout.RunAsync("config", "set", "updates-source", "off");
        off.ExitCode.Should().Be(0, off.StandardError);

        var run = await loadout.RunAsync("about", "--json");
        run.ExitCode.Should().Be(0, run.StandardError);

        var about = run.Json();

        about.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        about.GetProperty("features").GetArrayLength().Should().BePositive();
        about.GetProperty("guides").GetArrayLength().Should().BePositive();

        var timeline = about.GetProperty("timeline");
        timeline.GetProperty("releases").GetArrayLength().Should().Be(0);
        timeline.GetProperty("empty").GetString().Should().Be("Off");
    }
}
