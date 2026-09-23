using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Tools;
using Loadout.Models.Tools;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the catalogue must not take on trust from files an agent can write: a
/// draft's claim to have passed verify, a version's own fingerprint, a draft
/// somewhere else on the disk, and a second writer.
/// </summary>
public sealed class ToolRegistrySafetyTests : IDisposable
{
    private const string Script = "param([string]$CachePath)\nGet-ChildItem $CachePath | Remove-Item\n";

    private static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ToolStoreFixture _store = new();

    public void Dispose() => _store.Dispose();

    [Theory]
    [InlineData("drafts")]
    [InlineData("inbox")]
    [InlineData("verified")]
    [InlineData("versions")]
    public async Task A_tool_named_after_the_catalogues_own_directories_is_refused(string name)
    {
        var (registry, _) = _store.Registry();
        var cases = ToolStoreFixture.Cases();
        var manifest = ToolStoreFixture.Manifest(name, "1.0");
        var draft = _store.Draft(manifest, Script, cases);

        var verified = await registry.VerifyAsync(draft, ToolStoreFixture.Agreed(manifest, Script, cases));
        var promoted = registry.Promote(draft, new("lesson", "inbox/test"));

        verified.Failed.Should().BeTrue();
        promoted.Failed.Should().BeTrue();
        File.Exists(Path.Combine(registry.Root(), name, "tool.yaml")).Should().BeFalse();
    }

    [Fact]
    public void A_draft_claiming_verified_without_a_verify_record_is_refused()
    {
        var (registry, _) = _store.Registry();
        var cases = ToolStoreFixture.Cases();
        var manifest = ToolStoreFixture.Manifest("free-cache", "1.0");
        var draft = _store.Draft(manifest, Script, cases);

        // Written by hand, with every fingerprint right, and never verified.
        manifest.Status = ToolVersionStatus.Verified;
        manifest.Tests = new ToolTestRecord
        {
            RanAt = DateTimeOffset.UtcNow,
            Passed = 4,
            Fingerprint = RemedyCeiling.Fingerprint(Script),
            CasesFingerprint = ToolRegistry.CasesFingerprint(Path.Combine(draft, "cases")),
        };
        File.WriteAllText(Path.Combine(draft, "manifest.yaml"), Writer.Serialize(manifest));

        var promoted = registry.Promote(draft, new("lesson", "inbox/test"));

        promoted.Failed.Should().BeTrue();
        Directory.Exists(Path.Combine(registry.Root(), "free-cache", "versions", "1.0")).Should().BeFalse();
    }

    [Fact]
    public async Task A_rejected_draft_edited_to_verified_is_refused()
    {
        var (registry, _) = _store.Registry(exit: 1);
        var cases = ToolStoreFixture.Cases();
        var manifest = ToolStoreFixture.Manifest("free-cache", "1.0");
        var draft = _store.Draft(manifest, Script, cases);

        var verified = await registry.VerifyAsync(draft, ToolStoreFixture.Agreed(manifest, Script, cases));
        verified.Value!.Gate!.Passed.Should().BeFalse();

        var file = Path.Combine(draft, "manifest.yaml");
        var rejected = Yaml.Deserialize<ToolVersion>(File.ReadAllText(file));
        rejected.Status.Should().Be(ToolVersionStatus.Rejected);
        rejected.Status = ToolVersionStatus.Verified;
        File.WriteAllText(file, Writer.Serialize(rejected));

        var promoted = registry.Promote(draft, new("lesson", "inbox/test"));

        promoted.Failed.Should().BeTrue();
        promoted.Error.Should().Contain("verify");
    }

    [Fact]
    public async Task A_token_inside_a_url_never_reaches_the_refusal_text()
    {
        var token = "ghp_" + new string('b', 36);
        var script = Script + "git clone https://x-access-token:" + token + "@github.com/someone/thing\n";
        var (registry, _) = _store.Registry();
        var cases = ToolStoreFixture.Cases();
        var manifest = ToolStoreFixture.Manifest("free-cache", "1.0");
        var draft = _store.Draft(manifest, script, cases);
        await registry.VerifyAsync(draft, ToolStoreFixture.Agreed(manifest, script, cases));

        var promoted = registry.Promote(draft, new("lesson", "inbox/test"));

        promoted.Failed.Should().BeTrue();
        promoted.Error.Should().Contain("GitHub token");
        promoted.Error.Should().NotContain(token);
        File.ReadAllText(Path.Combine(registry.Root(), "audit.jsonl")).Should().NotContain(token);
        registry.Audit().Should().NotContain(one => (one.Note ?? string.Empty).Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public void A_password_in_a_url_is_not_quoted_even_when_no_secret_pattern_knows_it()
    {
        var found = ToolGenericity.Check("git clone https://someone:hunter2hunter2@github.com/someone/thing");

        found.Should().Contain(one => one.StartsWith("a repository URL", StringComparison.Ordinal));
        found.Should().NotContain(one => one.Contains("hunter2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_draft_outside_the_drafts_directory_is_refused()
    {
        var (registry, _) = _store.Registry();
        var cases = ToolStoreFixture.Cases();
        var manifest = ToolStoreFixture.Manifest("free-cache", "1.0");
        var outside = Path.Combine(_store.Paths.Paths.State, "elsewhere", "free-cache");
        ToolStoreFixture.Write(outside, manifest, Script, cases);
        var before = File.ReadAllText(Path.Combine(outside, "manifest.yaml"));

        // Climbing out of drafts by name is the same as starting outside it.
        var climbing = Path.Combine(_store.Drafts, "..", "elsewhere", "free-cache");

        foreach (var draft in new[] { outside, climbing })
        {
            var verified = await registry.VerifyAsync(draft, ToolStoreFixture.Agreed(manifest, Script, cases));
            verified.Failed.Should().BeTrue();
            verified.Error.Should().Contain("drafts");
            registry.Promote(draft, new("lesson", "inbox/test")).Failed.Should().BeTrue();
        }

        File.ReadAllText(Path.Combine(outside, "manifest.yaml")).Should().Be(before);
    }

    [Fact]
    public async Task Rewriting_script_and_manifest_fingerprint_together_reads_as_tampered()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        var directory = Path.Combine(registry.Root(), "free-cache", "versions", "1.0");
        var changed = Script + "Remove-Item -Recurse /\n";
        File.WriteAllText(Path.Combine(directory, "free-cache.v1.0.ps1"), changed);

        var file = Path.Combine(directory, "manifest.yaml");
        var manifest = Yaml.Deserialize<ToolVersion>(File.ReadAllText(file));
        manifest.Fingerprint = RemedyCeiling.Fingerprint(changed);
        File.WriteAllText(file, Writer.Serialize(manifest));

        var shown = registry.Show("free-cache").Value!;

        shown.Versions["1.0"].Should().Be(ToolVersionStatus.Tampered);
        shown.Active.Should().BeNull();
    }

    [Fact]
    public async Task Appending_a_forged_promote_entry_leaves_the_version_tampered()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        var directory = Path.Combine(registry.Root(), "free-cache", "versions", "1.0");
        var changed = Script + "Remove-Item -Recurse /\n";
        File.WriteAllText(Path.Combine(directory, "free-cache.v1.0.ps1"), changed);

        // The audit log is appended to, so a later promote line for the same
        // version is exactly what somebody rewriting the script would add.
        var audit = Path.Combine(registry.Root(), "audit.jsonl");
        var promote = File.ReadAllLines(audit).Single(line => line.Contains("\"promote\"", StringComparison.Ordinal));
        var forged = promote.Replace(
            RemedyCeiling.Fingerprint(Script), RemedyCeiling.Fingerprint(changed), StringComparison.OrdinalIgnoreCase);
        forged.Should().NotBe(promote);
        File.AppendAllText(audit, forged + "\n");

        registry.Standing("free-cache", "1.0").Should().Be(ToolVersionStatus.Tampered);
    }

    [Fact]
    public async Task A_second_writer_is_refused_while_the_lock_is_held()
    {
        var (registry, _) = _store.Registry();
        registry.LockWait = TimeSpan.FromMilliseconds(100);
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        using (new FileStream(Path.Combine(registry.Root(), "registry.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var used = registry.RecordUsage(new ToolUsage { Tool = "free-cache", Version = "1.0", Outcome = ToolOutcome.Ok });

            used.Failed.Should().BeTrue();
            used.Error.Should().Contain("lock");
        }

        registry.RecordUsage(new ToolUsage { Tool = "free-cache", Version = "1.0", Outcome = ToolOutcome.Ok })
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Promotions_at_once_lose_no_known_good_version()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        var drafts = new List<string>();

        for (var minor = 1; minor <= 12; minor++)
        {
            var manifest = ToolStoreFixture.Manifest("free-cache", "1." + minor);
            var script = Script + "# " + minor + "\n";
            var cases = ToolStoreFixture.Cases();
            var draft = _store.Draft(manifest, script, cases);
            (await registry.VerifyAsync(draft, ToolStoreFixture.Agreed(manifest, script, cases))).Value!.Gate!.Passed.Should().BeTrue();
            drafts.Add(draft);
        }

        var results = new System.Collections.Concurrent.ConcurrentBag<bool>();
        using var start = new ManualResetEventSlim();
        var threads = drafts.Select(one => new Thread(() =>
        {
            start.Wait();
            results.Add(registry.Promote(one, new("lesson", "inbox/test")).Succeeded);
        })).ToList();

        threads.ForEach(one => one.Start());
        start.Set();
        threads.ForEach(one => one.Join());

        results.Should().OnlyContain(one => one);
        registry.Show("free-cache").Value!.Record.KnownGood.Should().HaveCount(13);
    }
}
