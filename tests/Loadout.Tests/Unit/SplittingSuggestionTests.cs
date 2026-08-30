using FluentAssertions;
using Loadout.Cli.Commands;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Pointing at the other tool when this one cannot help.
/// </summary>
/// <remarks>
/// There are two ways to shrink an always-loaded instruction file and they suit
/// different files. Compressing moves standing facts into memory and needs
/// facts; splitting scopes sections to the paths they concern and needs
/// sections. Run against a real 67KB file of prose, compressing examined
/// forty-nine list items, rejected forty-six of them as making no standing
/// claim, and reported that nothing was worth moving — true, and silent about
/// the tool that would have worked.
/// </remarks>
public sealed class SplittingSuggestionTests : IDisposable
{
    private readonly string _root;

    public SplittingSuggestionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-suggest-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temp directory that outlives the run is not a failed test.
        }
    }

    private string Write(int headings, int paddingBytes)
    {
        var path = Path.Combine(_root, "instructions.md");
        var text = new System.Text.StringBuilder();

        for (var i = 0; i < headings; i++)
        {
            text.AppendLine($"## Section {i}");
            text.AppendLine();
            text.AppendLine("Prose that makes no standing claim anybody could rely on.");
            text.AppendLine();
        }

        text.Append(new string('x', paddingBytes));

        File.WriteAllText(path, text.ToString());

        return path;
    }

    [Fact]
    public void A_large_file_under_many_headings_is_pointed_at_splitting()
    {
        var path = Write(headings: 19, paddingBytes: 60 * 1024);

        var suggestion = MemoryCompressCommand.SplittingSuggestion(path, new FileInfo(path).Length);

        suggestion.Should().NotBeNull();
        suggestion.Should().Contain("rules split");
        suggestion.Should().Contain("19 headings");

        // The path goes in the message. Telling somebody to run a command
        // against a file in the workspace without saying which file leaves them
        // to go and find it.
        suggestion.Should().Contain(path);
    }

    [Fact]
    public void A_small_file_is_left_alone()
    {
        var path = Write(headings: 19, paddingBytes: 0);

        // Small files are not the problem whatever shape they are in, and a
        // tool that recommends another tool every time it finds nothing is one
        // nobody reads the output of.
        MemoryCompressCommand.SplittingSuggestion(path, new FileInfo(path).Length)
            .Should().BeNull();
    }

    [Fact]
    public void A_large_file_with_nothing_to_split_on_is_left_alone()
    {
        var path = Write(headings: 1, paddingBytes: 60 * 1024);

        // Splitting needs sections. One heading is a document with a title, and
        // pointing at a tool that cannot help it would be worse than silence.
        MemoryCompressCommand.SplittingSuggestion(path, new FileInfo(path).Length)
            .Should().BeNull();
    }

    [Fact]
    public void A_file_that_is_not_there_says_nothing()
    {
        MemoryCompressCommand
            .SplittingSuggestion(Path.Combine(_root, "gone.md"), 60 * 1024)
            .Should().BeNull();
    }
}
