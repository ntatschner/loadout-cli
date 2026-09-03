using FluentAssertions;
using Loadout.Core.Security;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the workspace refuses to carry.
/// </summary>
/// <remarks>
/// <para>
/// The scanner existed and was wired to three call sites, all of them memory.
/// Handoffs, project instructions, context notes, profiles and MCP definitions
/// were committed and pushed unexamined by the same exit policy — and so was
/// anything an agent wrote into the workspace directly, which no write-time
/// check can see. This is the gate at the end.
/// </para>
/// <para>
/// The values below are synthetic and are the ones the scanner's own tests use.
/// A test fixture holding a real credential would be the disclosure it exists to
/// prevent.
/// </para>
/// </remarks>
public sealed class WorkspaceSecretGateTests : IDisposable
{
    private const string GitHubTokenShape = "ghp_abcdefghijklmnopqrstuvwxyz0123";
    private const string AnthropicKeyShape = "sk-ant-api03-abcdefghijklmnopqrstuvwxyz012345";

    private readonly string _root;

    public WorkspaceSecretGateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-gate-" + Guid.NewGuid().ToString("N"));

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    [Fact]
    public void A_handoff_carrying_a_credential_is_found()
    {
        // The route named in the finding: an agent writes what it worked out,
        // the exit policy commits it, and under "always" pushes it.
        Write("projects/starstats/handoffs/2026-02-01.md",
            $"The deploy token is {GitHubTokenShape} and it works.");

        var findings = Scan("projects/starstats/handoffs/2026-02-01.md");

        findings.Should().ContainSingle()
            .Which.Pattern.Should().Be("GitHub token");
    }

    [Theory]
    [InlineData("projects/starstats/instructions.md")]
    [InlineData("projects/starstats/notes/context.md")]
    [InlineData("projects/starstats/profiles/narrow.yaml")]
    [InlineData("projects/starstats/mcp/servers.json")]
    public void Everything_else_the_exit_policy_commits_is_covered_too(string path)
    {
        // Memory was the only thing screened. Each of these is committed by the
        // same policy and none of them was.
        Write(path, $"key: {AnthropicKeyShape}");

        Scan(path).Should().ContainSingle().Which.Path.Should().Be(path);
    }

    [Fact]
    public void A_clean_change_passes()
    {
        Write("projects/starstats/handoffs/2026-02-01.md",
            "The upload retries twice and then gives up. The retry count is in config.");

        Scan("projects/starstats/handoffs/2026-02-01.md").Should().BeEmpty();
    }

    [Fact]
    public void The_finding_names_the_pattern_and_never_the_value()
    {
        Write("notes.md", $"token {GitHubTokenShape}");

        var findings = Scan("notes.md");

        var explained = WorkspaceSecrets.Explain(findings);

        // The whole point of the scanner reporting pattern names. A gate that
        // printed the credential to explain itself would put it in the console,
        // the scrollback and whatever is capturing them.
        explained.Should().NotContain(GitHubTokenShape);
        explained.Should().Contain("GitHub token");
        explained.Should().Contain("notes.md");
    }

    [Fact]
    public void A_deleted_file_is_a_change_with_nothing_to_read()
    {
        // Cleanups show up in the same list as edits. Treating a missing file as
        // suspicious would refuse every tidy-up.
        Scan("projects/starstats/handoffs/gone.md").Should().BeEmpty();
    }

    [Fact]
    public void A_binary_file_is_not_run_through_credential_patterns()
    {
        // An image in a workspace is unusual but not wrong, and matching text
        // patterns across bytes produces noise rather than findings.
        //
        // The bytes deliberately carry something a pattern would match, after a
        // zero byte. A fixture of inert bytes would pass whether or not binaries
        // are skipped, which is a test that agrees with itself.
        var path = Path.Combine(_root, "docs", "diagram.png");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.WriteAllBytes(path,
        [
            0x89, 0x50, 0x4E, 0x47, 0x00,
            .. System.Text.Encoding.ASCII.GetBytes(GitHubTokenShape),
        ]);

        Scan("docs/diagram.png").Should().BeEmpty();
    }

    // The same property on both platforms rather than one of them, because
    // making a file unreadable is the one thing here that has no cross-platform
    // spelling: Windows does it by holding the file open, Unix by mode bits.
    // Asserting it on Windows alone would leave two of the three release
    // platforms claiming a guarantee nothing had checked.

    [Platform.WindowsFact]
    public void A_file_held_open_is_reported_rather_than_passed()
    {
        // It is about to be committed whether or not the scan could open it.
        // Silence here would be the scan saying "clean" about something it never
        // looked at, which is the one answer it must not give.
        var path = Path.Combine(_root, "locked.md");

        File.WriteAllText(path, "content");

        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Scan("locked.md").Should().ContainSingle().Which.Pattern.Should().Be("unreadable");
    }

    [Platform.UnixFact]
    public void A_file_with_no_read_permission_is_reported_rather_than_passed()
    {
        // Guarded as well as attributed. The attribute skips this when the run
        // is on Windows; the platform analyser needs to see the check itself
        // before it will accept a call that does not exist there.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_root, "locked.md");

        File.WriteAllText(path, "content");
        File.SetUnixFileMode(path, UnixFileMode.None);

        Scan("locked.md").Should().ContainSingle().Which.Pattern.Should().Be("unreadable");
    }

    [Fact]
    public void Every_changed_file_is_looked_at_rather_than_only_the_first()
    {
        Write("one.md", "nothing here");
        Write("two.md", $"key {AnthropicKeyShape}");

        Scan("one.md", "two.md").Should().ContainSingle()
            .Which.Path.Should().Be("two.md");
    }

    private IReadOnlyList<SecretFinding> Scan(params string[] paths) =>
        WorkspaceSecrets.Scan(_root, paths);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
