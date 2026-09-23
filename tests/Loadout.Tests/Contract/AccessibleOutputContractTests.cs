using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// What a person who asked for accessible output actually receives, from the
/// built command line rather than from a console a test made.
/// </summary>
/// <remarks>
/// <para>
/// What these can and cannot settle is worth being plain about. A contract
/// test captures the output, which means the output is redirected, which means
/// Spectre has already dropped colour and box drawing before anything here
/// looks at it. So the folding itself is proven in the unit test, where the
/// console can be told it has a terminal; what is proven here is the part a
/// pipe cannot flatten - that the flag is accepted everywhere, that a person
/// is told the profile took, and that nothing was put in front of a document
/// somebody is parsing.
/// </para>
/// <para>
/// The first of those is the one a new command breaks: it declares its own
/// settings, forgets the shared ones, and the flag becomes an error on that
/// command alone.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class AccessibleOutputContractTests
{
    /// <summary>Commands that draw something and need nothing set up first.</summary>
    public static TheoryData<string> Drawing =>
        ["team list", "commands", "config list", "specialists"];

    [BuiltCliTheory]
    [MemberData(nameof(Drawing))]
    public async Task The_flag_is_accepted_and_nothing_is_escaped(string command)
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync([.. command.Split(' '), "--accessible"]);

        var everything = run.StandardOutput + run.StandardError;

        everything.Should().NotContain(
            "Unexpected option", "the shared options have to work on every command");

        everything.Should().NotContain(
            "", "an escape sequence is read aloud or swallows the line around it");
    }

    [BuiltCliFact]
    public async Task It_says_which_profile_is_on()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("team", "list", "--accessible=dyslexia");

        run.StandardOutput.Should().StartWith("Accessible output is on (dyslexia)");
    }

    [BuiltCliFact]
    public async Task Nobody_who_asked_for_nothing_is_told_about_it()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("team", "list");

        run.StandardOutput.Should().NotContain("Accessible output");
    }

    [BuiltCliFact]
    public async Task Machine_readable_output_is_not_interrupted_by_the_notice()
    {
        // The notice is for a person. A document with a sentence in front of
        // it is not a document, and a script reading it gets a parse error
        // rather than an answer.
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("team", "list", "--accessible", "--json");

        run.Json().TryGetProperty("teams", out _).Should().BeTrue();
    }
}
