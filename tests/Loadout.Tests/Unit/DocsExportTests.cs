using FluentAssertions;
using Loadout.Core.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Four documents from one scan, and the one that must never claim to be done.
/// </summary>
/// <remarks>
/// They are not equally derivable. The reference and the machine index fall out
/// of the code and need nobody. The technical guide is the prose already in the
/// doc comments, arranged. The user guide is barely derivable at all, because
/// what somebody wants to do is not in the source — so it is emitted as a
/// scaffold that says so, rather than as something that reads like
/// documentation and teaches nothing.
/// </remarks>
public sealed class DocsExportTests
{
    private static readonly string[] Source =
    [
        "namespace Demo;",
        string.Empty,
        "/// <summary>Holds a widget.</summary>",
        "public sealed class Widget",
        "{",
        "    /// <summary>",
        "    /// Turns the widget, which is the whole point of having one",
        "    /// and takes a moment.",
        "    /// </summary>",
        "    /// <remarks>Not to be done twice.</remarks>",
        "    public void Turn()",
        "    {",
        "        var turns = 1;",
        "        if (true)",
        "        {",
        "        }",
        "    }",
        "}",
    ];

    /// <summary>The shape most of this codebase actually uses.</summary>
    private static readonly string[] WithPara =
    [
        "/// <summary>",
        "/// Holds a gadget.",
        "/// <para>",
        "/// And explains at length why it holds one, which is the reasoning",
        "/// and belongs in the file rather than in an index of it.",
        "/// </para>",
        "/// </summary>",
        "public sealed class Gadget",
        "{",
        "}",
    ];

    private static IReadOnlyList<Symbol> Scanned() =>
        [.. SymbolScan.InFile(Source, "src/Demo/Widget.cs")];

    [Fact]
    public void A_type_and_its_members_are_found_with_where_they_are()
    {
        var symbols = Scanned();

        symbols.Should().Contain(s => s.Name == "Widget" && s.Kind == SymbolKind.Type);
        symbols.Should().Contain(s => s.Name == "Turn" && s.Kind == SymbolKind.Member);

        symbols.Single(s => s.Name == "Widget").Line.Should().Be(4);
    }

    [Fact]
    public void Control_flow_is_not_mistaken_for_a_member()
    {
        // Two different things keep two different intruders out, and it is
        // worth being exact about which does what.
        //
        // "if (true)" is excluded by the shape: the pattern wants an
        // identifier, a space and another identifier before the bracket, and
        // control flow has only one. A local variable is excluded by the
        // "public" instead — "var turns = 1" has that shape exactly, and
        // without the modifier it is indistinguishable from a field.
        //
        // A keyword exclusion list was written for this as well and removed:
        // no mutation failed for it, and scanning this repository with and
        // without gave byte-for-byte the same symbols, because nothing can
        // reach the place it was looking.
        var found = Scanned();

        found.Should().NotContain(s =>
            s.Name == "true" || s.Signature.Contains("if (", StringComparison.Ordinal));

        found.Should().NotContain(s => s.Name == "turns");
    }

    [Fact]
    public void A_summary_that_wraps_is_read_whole()
    {
        var turn = Scanned().Single(s => s.Name == "Turn");

        // Stopping at the newline cut it mid-clause, which is worse than no
        // summary: it reads like prose and ends like a fault.
        turn.Summary.Should().Be(
            "Turns the widget, which is the whole point of having one and takes a moment.");
    }

    [Fact]
    public void The_reasoning_under_a_summary_is_left_in_the_file()
    {
        // The remarks are where the why lives, and it belongs in the source
        // rather than in an index of it.
        Scanned().Single(s => s.Name == "Turn").Summary
            .Should().NotContain("twice");
    }

    [Fact]
    public void A_summary_stops_where_the_reasoning_starts()
    {
        var gadget = SymbolScan.InFile(WithPara, "src/Demo/Gadget.cs")
            .Single(symbol => symbol.Name == "Gadget");

        // The shape most of this codebase uses: a sentence, then a para
        // carrying the why. Folding the para in would put a paragraph of
        // reasoning into every line of an index, which is how an index stops
        // being scannable.
        gadget.Summary.Should().Be("Holds a gadget.");
        gadget.Summary.Should().NotContain("belongs in the file");
    }

    [Fact]
    public void A_symbol_with_no_doc_comment_has_no_summary()
    {
        var symbols = SymbolScan.InFile(
            ["public sealed class Bare", "{", "}"], "src/Bare.cs").ToList();

        symbols.Single(s => s.Name == "Bare").Summary.Should().BeEmpty();
    }

    [Fact]
    public void The_user_guide_says_it_is_a_scaffold_before_anything_else()
    {
        var written = DocsExport.Write(DocsExportType.UserGuide, Scanned(), "Demo");

        // The whole safety of this type. A generated guide that did not say so
        // looks finished enough that nobody writes the real one.
        written.Should().Contain("scaffold, not a document");
        written.Should().Contain("TODO");
    }

    [Fact]
    public void The_reference_says_it_is_an_index_rather_than_an_authority()
    {
        var written = DocsExport.Write(DocsExportType.Reference, Scanned(), "Demo");

        // Derived from a lexical scan, so it omits rather than invents — and a
        // reader deciding how much to trust it needs to know which.
        written.Should().Contain("index rather than an authority");
        written.Should().Contain("src/Demo/Widget.cs:4");
    }

    [Fact]
    public void The_machine_index_is_one_line_per_symbol()
    {
        var written = DocsExport.Write(DocsExportType.MachineIndex, Scanned(), "Demo");

        var rows = written
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains('\t', StringComparison.Ordinal))
            .ToList();

        // Not read by a person, so the grouping and prose that help elsewhere
        // are only tokens here.
        rows.Should().HaveCount(Scanned().Count);
        rows.Should().OnlyContain(line => line.Split('\t').Length == 4);
    }

    [Fact]
    public void A_module_nobody_has_written_about_says_so()
    {
        var written = DocsExport.Write(
            DocsExportType.Technical,
            [.. SymbolScan.InFile(["public sealed class Bare", "{", "}"], "src/Bare.cs")],
            "Demo");

        // An empty section reads as a module that does nothing. This reads as
        // one nobody has written about, which is the true and more useful
        // thing.
        written.Should().Contain("No type in this module carries a summary");
    }

    [Theory]
    [InlineData("reference", DocsExportType.Reference)]
    [InlineData("technical", DocsExportType.Technical)]
    [InlineData("user-guide", DocsExportType.UserGuide)]
    [InlineData("machine-index", DocsExportType.MachineIndex)]
    [InlineData("", DocsExportType.Reference)]
    public void Every_type_can_be_asked_for_by_name(string given, DocsExportType expected)
    {
        Loadout.Cli.Commands.DocsExportCommand.TryReadType(given, out var type).Should().BeTrue();
        type.Should().Be(expected);
    }

    [Fact]
    public void A_type_nobody_offers_is_refused_rather_than_defaulted()
    {
        // Quietly falling back to the reference would hand somebody a document
        // they did not ask for and might not notice was the wrong one.
        Loadout.Cli.Commands.DocsExportCommand.TryReadType("nonsense", out _)
            .Should().BeFalse();
    }
}
