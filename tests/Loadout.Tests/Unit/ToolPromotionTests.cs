using FluentAssertions;
using Loadout.Core.Tools;
using Loadout.Models.Tools;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The regression gate: a failed refinement cannot replace what worked.
/// </summary>
public sealed class ToolPromotionTests : IDisposable
{
    private const string Script = "param([string]$CachePath)\nGet-ChildItem $CachePath | Remove-Item\n";

    private readonly ToolStoreFixture _store = new();

    public void Dispose() => _store.Dispose();

    [Fact]
    public async Task A_draft_failing_a_previous_known_good_case_is_rejected_and_active_does_not_move()
    {
        var (first, _) = _store.Registry(exit: 0);
        await _store.PromoteAsync(first, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases(0, "v1-"));

        // The refinement exits 3 everywhere. Its own cases say that is right;
        // the known-good cases say 0.
        var (second, _) = _store.Registry(exit: 3);
        var next = ToolStoreFixture.Manifest("free-cache", "1.1");
        var cases = ToolStoreFixture.Cases(3, "v11-");
        var changed = Script + "exit 3\n";
        var draft = _store.Draft(next, changed, cases);

        var verified = await second.VerifyAsync(draft, ToolStoreFixture.Agreed(next, changed, cases));

        verified.Value!.Gate!.Passed.Should().BeFalse();
        verified.Value.Gate.Regressions.Should().Contain("v1-success");
        second.Promote(draft, new("idea", "inbox/refine")).Failed.Should().BeTrue();
        second.Show("free-cache").Value!.Record.Active.Should().Be("1.0");
        Directory.Exists(Path.Combine(second.Root(), "free-cache", "versions", "1.1")).Should().BeFalse();
    }

    [Fact]
    public async Task A_declared_break_with_migration_may_retire_a_case()
    {
        var active = ToolStoreFixture.Manifest("free-cache", "1.0");
        var old = new List<ToolCase> { new() { Name = "old-flag", Class = ToolCaseClass.Success } };

        Task<ToolCaseResult> Run(string script, ToolCase one, CancellationToken ct) =>
            Task.FromResult(new ToolCaseResult(one.Name, one.Class, one.Name != "old-flag", string.Empty));

        var declared = ToolStoreFixture.Manifest("free-cache", "2.0");
        declared.Compatibility = new ToolCompatibilityInfo
        {
            Breaks = true,
            Migration = "Pass -CachePath instead of -Path.",
            RetiredCases = ["old-flag"],
        };

        var allowed = await ToolPromotion.GateAsync(declared, "next.ps1", ToolStoreFixture.Cases(), active, old, Run);

        allowed.Passed.Should().BeTrue();
        allowed.Retired.Should().Equal("old-flag");

        // The same claim without a major bump is not a declared break.
        var minor = ToolStoreFixture.Manifest("free-cache", "1.1");
        minor.Compatibility = declared.Compatibility;

        var refused = await ToolPromotion.GateAsync(minor, "next.ps1", ToolStoreFixture.Cases(), active, old, Run);

        refused.Passed.Should().BeFalse();
        refused.Regressions.Should().Equal("old-flag");
    }
}
