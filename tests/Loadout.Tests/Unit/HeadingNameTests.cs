using Loadout.Core.Instructions;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Covers the one derivation two tools share.
/// <para>
/// The splitter names rule files after headings and the compressor names memory
/// topics after them. Each used to do its own, and the two disagreed: the same
/// heading produced a readable name in one place and a hundred-character one in
/// the other. These cases are taken from a real instruction file, which is
/// where the awkward ones come from.
/// </para>
/// </summary>
public sealed class HeadingNameTests
{
    [Theory]
    [InlineData("Code conventions", "code-conventions")]
    [InlineData("Build / Test", "build-test")]
    [InlineData("Working rules (read first)", "working-rules")]
    [InlineData("Merit awards & their limits (`crates/core/src/recognition/store.rs`)",
        "merit-awards-their-limits")]
    [InlineData("Steward admin override (panel) — `crates/server/src/admin.rs`",
        "steward-admin-override")]
    public void A_heading_becomes_a_readable_name(string heading, string expected) =>
        HeadingName.From(heading).Should().Be(expected);

    [Fact]
    public void A_heading_naming_six_files_still_fits_a_filename()
    {
        var name = HeadingName.From(
            "Component modularization & first-run setup (`crates/core/src/modules`, "
            + "`server/src/{modules,module_gate,sweeps,modules_panel,setup}.rs`)");

        name.Should().Be("component-modularization-first-run-setup");
        name.Length.Should().BeLessThanOrEqualTo(HeadingName.MaximumLength);
    }

    [Fact]
    public void A_name_is_cut_at_a_word_rather_than_mid_syllable()
    {
        var name = HeadingName.From(
            "An extremely long heading about provisioning and reconciliation and everything else");

        name.Length.Should().BeLessThanOrEqualTo(HeadingName.MaximumLength);
        name.Should().NotEndWith("-");

        // Cutting mid-word would leave a fragment that reads as a typo.
        name.Split('-').Should().OnlyContain(part => part.Length > 1 || part == "a");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("`` () --")]
    public void A_heading_with_nothing_in_it_falls_back(string? heading) =>
        HeadingName.From(heading, "notes").Should().Be("notes");

    [Fact]
    public void Colliding_names_are_numbered_rather_than_lost()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        HeadingName.Unique("deploy", used).Should().Be("deploy");
        HeadingName.Unique("deploy", used).Should().Be("deploy-2");
        HeadingName.Unique("deploy", used).Should().Be("deploy-3");
    }

    [Fact]
    public void Two_headings_that_shorten_alike_both_survive()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var first = HeadingName.Unique(HeadingName.From("Deploy (`server/src/a.rs`)"), used);
        var second = HeadingName.Unique(HeadingName.From("Deploy (`server/src/b.rs`)"), used);

        // Dropping the second used to leave a whole section unrouted: invisible
        // in the map, and so left in the always-loaded core unannounced.
        first.Should().NotBe(second);
    }

    [Fact]
    public void The_paths_a_heading_names_are_read_out_of_it()
    {
        var paths = HeadingName.PathsIn(
            "Component modularization & first-run setup (`crates/core/src/modules`, "
            + "`server/src/{modules,module_gate}.rs`)");

        paths.Should().Equal("crates/core/src/modules", "server/src/{modules,module_gate}.rs");
    }

    [Theory]
    // Headings backtick more than paths. Turning a type name or a flag into a
    // glob would scope a rule to files that do not exist, which is worse than
    // leaving it unscoped: it silently never loads.
    [InlineData("Using `--no-verify` carefully")]
    [InlineData("The `MemoryTopic` record")]
    [InlineData("Run `cargo test` first")]
    public void Backticked_text_that_is_not_a_path_is_not_treated_as_one(string heading) =>
        HeadingName.PathsIn(heading).Should().BeEmpty();

    [Fact]
    public void A_heading_with_no_backticks_yields_no_paths() =>
        HeadingName.PathsIn("Code conventions").Should().BeEmpty();
}
