using FluentAssertions;
using Loadout.Core.Delivery;
using Loadout.Core.Git;
using Loadout.Models.Delivery;
using Loadout.Models.Diagnostics;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>The doctor's word on the CLI each waiting branch's host needs.</summary>
public sealed class DeliveryDiagnosticTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly LandingService _landings;

    public DeliveryDiagnosticTests() => _landings = new LandingService(_space.Paths, _space.Yaml, TimeProvider.System);

    public void Dispose() => _space.Dispose();

    private DeliveryDiagnosticContributor With(IRepositoryHost host) =>
        new(_landings, new FakeProjects("website", _space.Scratch("website")), new FixedHosts(host));

    private async Task WaitingAsync() =>
        await _landings.RecordAsync("website", [new BranchLanding { Branch = "teams/r1/implementer-2", State = LandingState.Pending }]);

    [Fact]
    public async Task Nothing_waiting_means_nothing_is_said_and_nothing_is_run()
    {
        var gh = new StubProcessLauncher(string.Empty);

        (await With(new GhRepositoryHost(gh, new StubResolver("gh"))).ContributeAsync()).Should().BeEmpty();
        gh.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_signed_in_host_is_said_once()
    {
        await WaitingAsync();
        var gh = new StubProcessLauncher(string.Empty);

        var check = (await With(new GhRepositoryHost(gh, new StubResolver("gh"))).ContributeAsync()).Should().ContainSingle().Subject;

        check.Severity.Should().Be(DiagnosticSeverity.Info);
        check.Name.Should().Be("github");
        gh.Requests.Should().ContainSingle().Which.Arguments.Should().Equal("auth", "status", "--active");
    }

    [Fact]
    public async Task A_signed_out_host_is_a_warning_naming_how_to_sign_in()
    {
        await WaitingAsync();
        var glab = new StubProcessLauncher(string.Empty, exitCode: 1);

        var check = (await With(new GlabRepositoryHost(glab, new StubResolver("glab"))).ContributeAsync()).Should().ContainSingle().Subject;

        check.Severity.Should().Be(DiagnosticSeverity.Warning);
        check.Detail.Should().Contain("glab auth login").And.Contain("website");
    }

    [Fact]
    public async Task Local_git_needs_no_cli_and_is_not_mentioned()
    {
        await WaitingAsync();

        (await With(new LocalRepositoryHost()).ContributeAsync()).Should().BeEmpty();
    }
}
