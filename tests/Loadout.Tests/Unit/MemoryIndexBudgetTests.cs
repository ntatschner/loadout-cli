using System.Text;
using FluentAssertions;
using Loadout.Core.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The memory index a session is given is fitted to a budget: the newest topics
/// keep their description, and every other topic is still named.
/// </summary>
public sealed class MemoryIndexBudgetTests : IDisposable
{
    private const string Separator = "\n\n";
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-budget-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    /// <summary>An index of <paramref name="count"/> topics; topic-N was written N days after the start.</summary>
    private static MemoryIndexBudget.Section Index(int count, int descriptionLength = 150)
    {
        var text = new StringBuilder("# Project memory index\n\nDurable facts about this project.\n\n");

        for (var n = 0; n < count; n++)
        {
            text.Append($"- [topic-{n:D3}](topic-{n:D3}.md) - {Description(n, descriptionLength)}\n");
        }

        return new MemoryIndexBudget.Section(text.ToString(), file => Start.AddDays(int.Parse(file[6..9])));
    }

    private static string Description(int n, int length) =>
        $"what topic {n:D3} holds " + new string('x', Math.Max(0, length - 20));

    [Fact]
    public void An_index_within_its_budget_is_given_whole()
    {
        var fitted = MemoryIndexBudget.Fit([Index(5)], MemoryIndexBudget.DefaultBytes, Separator);

        fitted.Described.Should().Be(5);
        fitted.NamedOnly.Should().Be(0);
        fitted.Text.Should().Contain(Description(0, 150));
        fitted.Text.Should().NotContain("By name only");
    }

    [Fact]
    public void Over_budget_the_newest_keep_their_description_and_every_topic_is_still_named()
    {
        var fitted = MemoryIndexBudget.Fit([Index(200)], 16 * 1024, Separator);

        fitted.Bytes.Should().BeLessThanOrEqualTo(16 * 1024);
        fitted.Total.Should().Be(200);
        fitted.Described.Should().BeGreaterThan(0).And.BeLessThan(200);

        // topic-199 is the newest and topic-000 the oldest.
        fitted.Text.Should().Contain(Description(199, 150));
        fitted.Text.Should().NotContain(Description(0, 150));

        for (var n = 0; n < 200; n++)
        {
            fitted.Text.Should().Contain($"topic-{n:D3}");
        }

        fitted.Text.Should().StartWith("# Project memory index");
    }

    [Fact]
    public void What_is_described_is_exactly_the_newest_not_whichever_is_short()
    {
        // The newest topic's description cannot fit, and an older one's could.
        // Skipping ahead to it would describe topics by length rather than by age.
        var text = "- [old](old.md) - tiny-description\n- [new](new.md) - " + new string('y', 400) + "\n";
        var section = new MemoryIndexBudget.Section(text, file => file == "new.md" ? Start.AddDays(1) : Start);

        var fitted = MemoryIndexBudget.Fit([section], 300, Separator);

        fitted.Described.Should().Be(0);
        fitted.Text.Should().NotContain("tiny-description");
        fitted.Text.Should().StartWith("By name only").And.Contain("old, new");
    }

    [Fact]
    public void When_names_alone_overflow_every_topic_is_still_named_and_the_size_is_reported()
    {
        var fitted = MemoryIndexBudget.Fit([Index(200)], 512, Separator);

        fitted.Described.Should().Be(0);
        fitted.Bytes.Should().BeGreaterThan(512);
        fitted.Text.Should().Contain("topic-000").And.Contain("topic-199");
    }

    [Fact]
    public void Each_scope_keeps_its_own_heading_and_names_its_own_topics()
    {
        var project = Index(100);
        var machine = new MemoryIndexBudget.Section(
            "## Also true of this machine only\n\n- [restart-manager](restart-manager.md) - " + new string('z', 300) + "\n",
            _ => Start.AddYears(-5));

        var fitted = MemoryIndexBudget.Fit([project, machine], 8 * 1024, Separator);

        var machinePart = fitted.Text[fitted.Text.IndexOf("## Also true of this machine only", StringComparison.Ordinal)..];
        machinePart.Should().Contain("By name only").And.Contain("restart-manager");
        machinePart.Should().NotContain("topic-");
    }

    [Fact]
    public async Task A_session_is_given_a_budgeted_index_and_the_audit_says_how_many_are_named_only()
    {
        var workspace = Path.Combine(_root, "workspace");
        var directory = Path.Combine(workspace, "projects", "demo", "memory");
        Directory.CreateDirectory(directory);

        for (var n = 0; n < 150; n++)
        {
            var path = Path.Combine(directory, $"topic-{n:D3}.md");
            await File.WriteAllTextAsync(path,
                $"---\nname: topic-{n:D3}\ndescription: {Description(n, 180)}\nmetadata:\n  type: lesson\n---\n\n"
                + $"- The build for topic {n:D3} requires the pinned SDK, because a newer one rewrites the lock files.\n");
            File.SetLastWriteTimeUtc(path, Start.AddDays(n));
        }

        var memory = new MemoryService(TimeProvider.System, machineRoot: null);
        (await memory.RebuildIndexAsync(workspace, "demo")).Succeeded.Should().BeTrue();

        // The file on disk keeps every description; only what a session is given is fitted.
        (await File.ReadAllTextAsync(Path.Combine(directory, "MEMORY.md"))).Should().Contain(Description(0, 180));

        var index = (await memory.ReadIndexAsync(workspace, "demo")).Value!;

        Encoding.UTF8.GetByteCount(index).Should().BeLessThanOrEqualTo(MemoryIndexBudget.DefaultBytes);
        index.Should().Contain(Description(149, 180));
        index.Should().NotContain(Description(0, 180));
        index.Should().Contain("topic-000");

        var audit = (await memory.AuditAsync(workspace, "demo")).Value!;

        audit.Findings.Should().Contain(f => f.Kind == "index-budgeted");
        audit.Findings.Should().NotContain(f => f.Kind == "index-oversized");
    }
}
