using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Platform.Common;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A relative path handed to a command the daemon runs for somebody else is
/// theirs, not the daemon's.
/// </summary>
public sealed class CallerPathsTests : IDisposable
{
    private readonly string _caller = Directory.CreateTempSubdirectory("loadout-caller-").FullName;

    public void Dispose() => Directory.Delete(_caller, recursive: true);

    [Fact]
    public void A_relative_repo_is_the_callers_directory()
    {
        var settings = new DocsAuditCommand.Settings { Repo = "." };

        var directory = Intercepted(settings);

        settings.Repo.Should().Be(Path.GetFullPath(".", directory));
    }

    [Fact]
    public void Each_relative_file_is_found_from_the_callers_directory_and_a_whole_one_is_left_alone()
    {
        var whole = Path.Combine(Path.GetTempPath(), "elsewhere", "guide.md");
        var settings = new VoiceCheckSettings { Files = [Path.Combine("docs", "guide.md"), whole] };

        var directory = Intercepted(settings);

        settings.Files.Should().Equal(Path.Combine(directory, "docs", "guide.md"), whole);
    }

    [Fact]
    public void A_value_that_is_not_a_path_is_left_as_it_was()
    {
        var settings = new DocsAuditCommand.Settings { Agent = "claude" };

        _ = Intercepted(settings);

        settings.Agent.Should().Be("claude");
    }

    [Fact]
    public void Run_from_the_command_line_every_value_stays_as_it_was_typed()
    {
        var settings = new DocsAuditCommand.Settings { Repo = "." };

        new CallerPaths().Intercept(null!, settings);

        settings.Repo.Should().Be(".");
    }

    /// <summary>Intercepts <paramref name="settings"/> inside an invocation run from the caller's directory, and says which directory that was.</summary>
    private string Intercepted(Spectre.Console.Cli.CommandSettings settings)
    {
        var invocation = new Invocation(TextWriter.Null, TextWriter.Null, TextReader.Null, _caller);

        using (invocation.Enter())
        {
            new CallerPaths().Intercept(null!, settings);
        }

        return invocation.WorkingDirectory;
    }
}
