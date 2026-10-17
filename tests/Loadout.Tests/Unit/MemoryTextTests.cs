using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading a topic as it is stored and replacing it whole, as an editor in a
/// page does: what is refused, and what happens when somebody else wrote it
/// first.
/// </summary>
public sealed class MemoryTextTests : IDisposable
{
    private const string Description = "why installers fail with 1603 over a running app";

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "loadout-memtext-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryService _memory = new(TimeProvider.System);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task Reading_gives_every_byte_and_a_version_of_them()
    {
        var path = await WriteAsync();

        var read = await _memory.ReadTextAsync(_workspace, "demo", "restart-manager");

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Text.Should().Be(await File.ReadAllTextAsync(path));
        read.Value.Version.Should().Be(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant());
        read.Value.Topic.Description.Should().Be(Description);
    }

    [Fact]
    public async Task A_replacement_from_the_current_version_is_written_and_reindexed()
    {
        var path = await WriteAsync();
        var read = (await _memory.ReadTextAsync(_workspace, "demo", "restart-manager")).Value!;
        var edited = read.Text.Replace(Description, "why an MSI upgrade fails with 1603 here", StringComparison.Ordinal)
            .ReplaceLineEndings("\r\n") + "Related: [[windows-sandbox]]\r\n";

        var replaced = await _memory.ReplaceTextAsync(_workspace, "demo", "restart-manager", MemoryScope.Project, edited, read.Version);

        replaced.Succeeded.Should().BeTrue(replaced.Error);
        replaced.Value!.Conflicted.Should().BeFalse();
        (await File.ReadAllTextAsync(path)).Should().Be(edited.ReplaceLineEndings("\n"), "the workspace keeps LF whatever the browser sent");
        replaced.Value.Topic.Version.Should().NotBe(read.Version);
        replaced.Value.Topic.Topic.Links.Should().Contain("windows-sandbox");
        (await File.ReadAllTextAsync(Path.Combine(_workspace, "projects", "demo", "memory", "MEMORY.md")))
            .Should().Contain("why an MSI upgrade fails with 1603 here");
    }

    [Fact]
    public async Task A_topic_changed_since_it_was_read_is_left_alone_and_what_changed_it_comes_back()
    {
        var path = await WriteAsync();
        var read = (await _memory.ReadTextAsync(_workspace, "demo", "restart-manager")).Value!;

        // Another session adding to it in the meantime.
        await _memory.AppendAsync(_workspace, "demo", "restart-manager", ["Close the launcher before upgrading."]);
        var theirs = await File.ReadAllTextAsync(path);

        var replaced = await _memory.ReplaceTextAsync(
            _workspace, "demo", "restart-manager", MemoryScope.Project, read.Text + "- Mine.\n", read.Version);

        replaced.Succeeded.Should().BeTrue(replaced.Error);
        replaced.Value!.Conflicted.Should().BeTrue();
        replaced.Value.Topic.Text.Should().Be(theirs);
        (await File.ReadAllTextAsync(path)).Should().Be(theirs);
    }

    [Fact]
    public async Task A_credential_anywhere_in_the_text_is_refused_and_not_repeated()
    {
        var path = await WriteAsync();
        var before = await File.ReadAllTextAsync(path);
        var read = (await _memory.ReadTextAsync(_workspace, "demo", "restart-manager")).Value!;

        var replaced = await _memory.ReplaceTextAsync(
            _workspace, "demo", "restart-manager", MemoryScope.Project,
            read.Text + "\nThe release token is ghp_abcdefghijklmnopqrstuvwxyz0123, in prose.\n", read.Version);

        replaced.Succeeded.Should().BeFalse();
        replaced.ExitCode.Should().Be(ExitCode.PolicyViolation);
        replaced.Error.Should().NotContain("ghp_abcdefghijklmnopqrstuvwxyz0123");
        (await File.ReadAllTextAsync(path)).Should().Be(before);
    }

    [Fact]
    public async Task A_new_description_has_to_be_one_a_session_can_decide_by()
    {
        await WriteAsync();
        var read = (await _memory.ReadTextAsync(_workspace, "demo", "restart-manager")).Value!;

        var replaced = await _memory.ReplaceTextAsync(
            _workspace, "demo", "restart-manager", MemoryScope.Project,
            read.Text.Replace(Description, "notes", StringComparison.Ordinal), read.Version);

        replaced.Succeeded.Should().BeFalse();
        replaced.ExitCode.Should().Be(ExitCode.InvalidArguments);
    }

    [Fact]
    public async Task An_unchanged_description_is_not_judged_again()
    {
        // A topic whose description predates the rule, or came from another tool.
        var directory = Path.Combine(_workspace, "projects", "demo", "memory");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "notes.md"), "---\nname: notes\ndescription: notes\n---\n\n- One.\n");
        var read = (await _memory.ReadTextAsync(_workspace, "demo", "notes")).Value!;

        var replaced = await _memory.ReplaceTextAsync(
            _workspace, "demo", "notes", MemoryScope.Project, read.Text + "- Two.\n", read.Version);

        replaced.Succeeded.Should().BeTrue(replaced.Error);
    }

    [Theory]
    [InlineData("- No frontmatter at all.\n")]
    [InlineData("---\nname: something-else\ndescription: " + Description + "\n---\n\n- Renamed.\n")]
    public async Task Text_that_is_not_this_topic_is_refused(string text)
    {
        var path = await WriteAsync();
        var before = await File.ReadAllTextAsync(path);
        var read = (await _memory.ReadTextAsync(_workspace, "demo", "restart-manager")).Value!;

        var replaced = await _memory.ReplaceTextAsync(_workspace, "demo", "restart-manager", MemoryScope.Project, text, read.Version);

        replaced.Succeeded.Should().BeFalse();
        (await File.ReadAllTextAsync(path)).Should().Be(before);
    }

    [Fact]
    public async Task A_topic_that_does_not_exist_is_neither_read_nor_made()
    {
        (await _memory.ReadTextAsync(_workspace, "demo", "nothing")).Succeeded.Should().BeFalse();

        var made = await _memory.ReplaceTextAsync(
            _workspace, "demo", "nothing", MemoryScope.Project, "---\nname: nothing\ndescription: x\n---\n", "0");

        made.Succeeded.Should().BeFalse();
        File.Exists(Path.Combine(_workspace, "projects", "demo", "memory", "nothing.md")).Should().BeFalse();
    }

    private async Task<string> WriteAsync()
    {
        var written = await _memory.WriteAsync(
            _workspace, "demo", "restart-manager", Description, MemoryKind.Lesson,
            ["Restart Manager is disabled by policy on this machine."]);
        written.Succeeded.Should().BeTrue(written.Error);

        return written.Value!.Path;
    }
}
