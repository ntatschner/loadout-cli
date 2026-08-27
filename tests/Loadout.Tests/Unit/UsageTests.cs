using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Usage;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The arithmetic behind <c>loadout usage</c>, and the guards that stop it
/// answering confidently when it should not answer at all.
/// </summary>
/// <remarks>
/// Both defects these cover were made for real while building the feature, on
/// real transcripts, and both produced totals that looked entirely reasonable.
/// Counting Claude's lines rather than its messages inflated the figures by
/// three quarters; adding up Codex's running totals gave eighty-four times what
/// had been spent. Neither looked wrong on screen, which is the whole problem
/// with a number nobody can check.
/// </remarks>
public sealed class UsageTests
{
    [Fact]
    public void Cache_lifetimes_are_priced_apart_because_they_are_billed_apart()
    {
        // Same number of written tokens, different lifetimes.
        var fiveMinutes = new UsageTotals(0, CacheWrite5m: 1_000, 0, 0, 0, 0);
        var oneHour = new UsageTotals(0, 0, CacheWrite1h: 1_000, 0, 0, 0);

        fiveMinutes.BilledInputEquivalent.Should().Be(1_250);
        oneHour.BilledInputEquivalent.Should().Be(2_000);

        // Collapsing the two would make the longer-lived write look cheaper
        // than it is, and so make the saving look larger than it was.
        oneHour.BilledInputEquivalent.Should().BeGreaterThan(fiveMinutes.BilledInputEquivalent);
    }

    [Fact]
    public void The_saving_is_measured_against_having_sent_everything_afresh()
    {
        var totals = new UsageTotals(
            Input: 1_000,
            CacheWrite5m: 0,
            CacheWrite1h: 0,
            CacheRead: 9_000,
            Output: 500,
            Thinking: 0);

        // 1,000 at full price and 9,000 at a tenth is 1,900, against 10,000
        // had none of it been cached.
        totals.BilledInputEquivalent.Should().Be(1_900);
        totals.UncachedInputEquivalent.Should().Be(10_000);
        totals.SavedFraction.Should().BeApproximately(0.81, 0.0001);
    }

    [Fact]
    public void Thinking_is_part_of_the_output_rather_than_an_addition_to_it()
    {
        var totals = new UsageTotals(0, 0, 0, 0, Output: 500, Thinking: 200);

        // Counting it separately would report seven hundred tokens produced
        // where five hundred were.
        totals.Total.Should().Be(500);
    }

    [Fact]
    public void Nothing_in_means_no_share_to_report_rather_than_zero_percent()
    {
        // Zero would read as "the cache never helped", which is a different
        // claim from "there is nothing to divide".
        UsageTotals.Zero.CacheHitFraction.Should().BeNull();
        UsageTotals.Zero.SavedFraction.Should().BeNull();
    }

    [Fact]
    public void Totals_add_up_field_by_field()
    {
        var a = new UsageTotals(1, 2, 3, 4, 5, 6);
        var b = new UsageTotals(10, 20, 30, 40, 50, 60);

        (a + b).Should().Be(new UsageTotals(11, 22, 33, 44, 55, 66));
    }

    [Fact]
    public void A_reader_that_understood_everything_says_so()
    {
        var integrity = new UsageIntegrity(FilesRead: 3, RecordsCounted: 100, RecordsRepeated: 80);

        // Repeats are expected: both agents write the same accounting more
        // than once, so discarding them is the normal path and not a fault.
        integrity.IsComplete.Should().BeTrue();
        integrity.Caveat.Should().BeNull();
    }

    [Fact]
    public void A_record_it_could_not_read_makes_the_total_incomplete()
    {
        var integrity = new UsageIntegrity(FilesRead: 3, RecordsCounted: 100, RecordsUnrecognised: 7);

        integrity.IsComplete.Should().BeFalse();

        // The message has to point at the cause, because the reader cannot fix
        // it and the person reading can only act on knowing what happened.
        integrity.Caveat.Should().Contain("incomplete");
        integrity.Caveat.Should().Contain("7");
        integrity.Caveat.Should().Contain("transcript format");
    }

    [Fact]
    public void A_transcript_it_could_not_open_makes_the_total_incomplete()
    {
        var integrity = new UsageIntegrity(FilesRead: 3, FilesSkipped: 1, RecordsCounted: 100);

        integrity.IsComplete.Should().BeFalse();
        integrity.Caveat.Should().Contain("could not be read");
    }

    [Fact]
    public void Integrity_adds_up_so_one_bad_agent_still_shows_in_the_total()
    {
        var claude = new UsageIntegrity(FilesRead: 10, RecordsCounted: 500);
        var codex = new UsageIntegrity(FilesSkipped: 2, RecordsUnrecognised: 3);

        var both = claude + codex;

        both.FilesRead.Should().Be(10);
        both.FilesSkipped.Should().Be(2);
        both.RecordsUnrecognised.Should().Be(3);

        // A clean read of one agent must not vouch for the other.
        both.IsComplete.Should().BeFalse();
    }

    /// <summary>
    /// The field names both readers depend on, spelled out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Neither transcript format is a published contract. Claude Code's own
    /// documentation says the entries are internal and change between versions,
    /// so a rename is a question of when rather than whether.
    /// </para>
    /// <para>
    /// This is what turns that from a silent wrong answer into a visible one.
    /// If a name here stops matching what the agent writes, the reader reports
    /// records it could not understand and the report says it is incomplete —
    /// rather than counting zero and printing a smaller number that looks just
    /// as convincing as the right one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("input_tokens")]
    [InlineData("cache_read_input_tokens")]
    [InlineData("output_tokens")]
    [InlineData("cache_creation")]
    [InlineData("ephemeral_5m_input_tokens")]
    [InlineData("ephemeral_1h_input_tokens")]
    [InlineData("output_tokens_details")]
    [InlineData("thinking_tokens")]
    public void Claude_accounting_field_names_are_pinned(string field)
    {
        var source = ReadSource("ClaudeUsageHistory.cs");

        source.Should().Contain(
            $"\"{field}\"",
            $"the reader depends on {field}, and a rename that nobody noticed would "
            + "silently count zero for it");
    }

    [Theory]
    [InlineData("token_count")]
    [InlineData("total_token_usage")]
    [InlineData("input_tokens")]
    [InlineData("cached_input_tokens")]
    [InlineData("cache_write_input_tokens")]
    [InlineData("output_tokens")]
    [InlineData("reasoning_output_tokens")]
    public void Codex_accounting_field_names_are_pinned(string field)
    {
        var source = ReadSource("CodexUsageHistory.cs");

        source.Should().Contain($"\"{field}\"");
    }

    /// <summary>
    /// A sample of the shape Claude Code actually writes, so the parsing rules
    /// are exercised against the real thing rather than against a description
    /// of it.
    /// </summary>
    [Fact]
    public void The_real_usage_shape_carries_everything_the_reader_reads()
    {
        // Copied from a transcript on disk, trimmed to the accounting.
        const string Line = """
            {"input_tokens":10,"cache_creation_input_tokens":10816,
             "cache_read_input_tokens":28842,"output_tokens":40,
             "output_tokens_details":{"thinking_tokens":33},
             "cache_creation":{"ephemeral_1h_input_tokens":10816,
                               "ephemeral_5m_input_tokens":0}}
            """;

        using var document = JsonDocument.Parse(Line);
        var usage = document.RootElement;

        // The split must add up to the flat figure, or one of the two is being
        // read wrongly and the totals would disagree with themselves.
        var creation = usage.GetProperty("cache_creation");

        var split = creation.GetProperty("ephemeral_5m_input_tokens").GetInt64()
            + creation.GetProperty("ephemeral_1h_input_tokens").GetInt64();

        split.Should().Be(usage.GetProperty("cache_creation_input_tokens").GetInt64());

        // Thinking is inside the output, not beside it.
        usage.GetProperty("output_tokens_details").GetProperty("thinking_tokens").GetInt64()
            .Should().BeLessThanOrEqualTo(usage.GetProperty("output_tokens").GetInt64());
    }

    /// <summary>Reads a reader's source, so the pinned names are checked where they are used.</summary>
    private static string ReadSource(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository's src directory has to be findable from the tests");

        var path = Path.Combine(root!.FullName, "src", "Loadout.Core", "Usage", file);

        File.Exists(path).Should().BeTrue($"{file} is what these names are pinned against");

        return File.ReadAllText(path);
    }
}
