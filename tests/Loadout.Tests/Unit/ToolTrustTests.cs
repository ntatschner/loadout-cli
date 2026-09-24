using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Tools;
using Loadout.Models.Configuration;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Trust in a catalogue tool is a person's agreement to one script, kept in
/// this machine's configuration. Nothing in the catalogue can stand in for it.
/// </summary>
public sealed class ToolTrustTests : IDisposable
{
    private const string Script = "param([string]$CachePath)\nGet-ChildItem $CachePath | Remove-Item\n";

    private static readonly Dictionary<string, string> Rules = new() { ["unclassified"] = RemedyRules.Trusted };

    private readonly ToolStoreFixture _store = new();

    public void Dispose() => _store.Dispose();

    private static TrustedTool Agreed(string version, string script) => new()
    {
        Tool = "free-cache",
        Version = version,
        Fingerprint = RemedyCeiling.Fingerprint(script),
        By = "someone",
    };

    [Fact]
    public async Task A_changed_script_is_asked_about_again()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        // Agreed to 1.0, and to "1.1" at 1.0's script, as a person carrying
        // their agreement forward without reading the new one would.
        IReadOnlyList<TrustedTool> trusted = [Agreed("1.0", Script), Agreed("1.1", Script)];

        ToolOffer.For(registry, ToolOffer.Remediator, Rules, trusted)
            .Should().ContainSingle().Which.Ruling.Should().Be("run");

        var changed = Script + "Write-Output done\n";
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.1"), changed, ToolStoreFixture.Cases());

        var offered = ToolOffer.For(registry, ToolOffer.Remediator, Rules, trusted).Should().ContainSingle().Subject;

        offered.Name.Should().Be(ToolOffer.Named("free-cache", "1.1"));
        offered.Ruling.Should().Be("ask");
    }

    [Fact]
    public async Task Trust_fingerprints_the_script_ScriptOf_reads_and_nothing_once_it_changes()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        RemedyCeiling.Fingerprint(registry.ScriptOf("free-cache", "1.0")!)
            .Should().Be(RemedyCeiling.Fingerprint(Script));

        // A script edited after promotion is not what the gate saw, so there
        // is nothing to fingerprint and nothing for a person to agree to.
        var file = Directory.EnumerateFiles(
            Path.Combine(registry.Root(), "free-cache", "versions", "1.0"), "*.ps1").Single();
        File.AppendAllText(file, "Remove-Item -Recurse /\n");

        registry.ScriptOf("free-cache", "1.0").Should().BeNull();
    }

    [Fact]
    public async Task A_record_claiming_trust_decides_nothing()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        // Everything the catalogue could say: the audit line trust leaves, and
        // a file beside the tools claiming the agreement outright.
        registry.RecordTrust("free-cache", "1.0", revoked: false, by: "someone");
        File.WriteAllText(
            Path.Combine(registry.Root(), "free-cache", "trusted.yaml"),
            $"tool: free-cache\nversion: '1.0'\nfingerprint: {RemedyCeiling.Fingerprint(Script)}\n");

        ToolOffer.For(registry, ToolOffer.Remediator, Rules, trusted: null)
            .Should().ContainSingle().Which.Ruling.Should().Be("ask");
    }
}
