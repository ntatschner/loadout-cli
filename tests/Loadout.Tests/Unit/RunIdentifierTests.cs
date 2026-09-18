using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Platform;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A run identifier becomes a directory, so it has to be one.
/// </summary>
/// <remarks>
/// <para>
/// Found on a bug hunt rather than reasoned about. <c>team message
/// '..\probe' --message x</c> created a directory outside the runs root, wrote
/// a file in it and reported success — and the same thing was reachable from
/// the dashboard, which can be bound to a network, because an action's run
/// identifier is unescaped on the way through.
/// </para>
/// <para>
/// Nothing downstream was at fault. Everything that takes a run identifier
/// joins it to a path, so the check belongs where the joining happens rather
/// than at each of the places that later write a file.
/// </para>
/// </remarks>
public sealed class RunIdentifierTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "loadout-runid-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException)
        {
        }
    }

    [Theory]
    [InlineData("20260918-1436-ed59")]
    [InlineData("20260918-1436-ED59")]
    [InlineData("a")]
    public void What_a_run_is_called_is_a_run_identifier(string id)
    {
        RunJournal.Names(id).Should().BeTrue();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../../etc")]
    [InlineData("..\\..\\Users")]
    [InlineData("C:/Users/somebody")]
    [InlineData("run/../../elsewhere")]
    [InlineData("run id with spaces")]
    [InlineData("run.id")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_that_could_be_a_path_is_not(string? id)
    {
        RunJournal.Names(id).Should().BeFalse();
    }

    [Fact]
    public void A_name_that_is_not_one_never_becomes_a_directory_outside_the_runs()
    {
        var journal = Journal();

        // Where a well-formed run goes, which is the only place anything
        // taking a run identifier should ever end up.
        var runs = Path.GetDirectoryName(journal.DirectoryOf("20260918-1436-ed59"))!;

        var where = journal.DirectoryOf("..\\..\\somewhere-else");

        Path.GetFullPath(where).Should().StartWith(Path.GetFullPath(runs));
        where.Should().NotContain("somewhere-else");
    }

    [Fact]
    public void And_is_never_the_runs_directory_itself()
    {
        // Sending it to the root would make a malformed identifier read the
        // runs directory as though it were a run.
        var journal = Journal();

        var runs = Path.GetDirectoryName(journal.DirectoryOf("20260918-1436-ed59"))!;

        Path.GetFullPath(journal.DirectoryOf(".."))
            .Should().NotBe(Path.GetFullPath(runs));
    }

    [Fact]
    public void Reading_one_fails_as_a_sentence_rather_than_reaching_anywhere()
    {
        var read = Journal().Read("../../anything");

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain("not a run identifier");
    }

    [Fact]
    public void A_run_that_is_named_properly_still_resolves_where_it_always_did()
    {
        var journal = Journal();

        journal.DirectoryOf("20260918-1436-ed59")
            .Should().EndWith(Path.Combine("teams", "runs", "20260918-1436-ed59"));
    }

    /// <summary>A journal writing somewhere that is not this machine's own.</summary>
    private RunJournal Journal()
    {
        var paths = new LinuxPaths(
            new FakeEnvironmentProvider(
                Path.Combine(_root, "home"),
                new Dictionary<string, string>
                {
                    ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                    ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                    ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                    ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
                }),
            new NoOpFilePermissions(),
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST"));

        paths.EnsureDirectoriesExist();

        return new RunJournal(paths);
    }
}
