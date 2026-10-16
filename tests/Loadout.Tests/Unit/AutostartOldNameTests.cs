using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A login entry written before the daemon moved out from under 'team'.
/// </summary>
/// <remarks>
/// Such an entry still works, because 'team daemon' is kept as a second name,
/// so nothing breaks at login. What would go wrong is quieter: the entry keeps
/// the old name for good, and the day the old name goes it stops starting
/// anything. 'show' saying so is the way out.
/// </remarks>
public sealed class AutostartOldNameTests
{
    [Theory]
    [InlineData(@"""C:\Program Files\Loadout\loadout.exe"" team daemon", true)]
    [InlineData("/usr/bin/loadout team daemon", true)]
    [InlineData(@"""C:\Program Files\Loadout\loadout.exe"" daemon", false)]
    [InlineData("/usr/bin/loadout daemon", false)]
    [InlineData("/home/team/loadout daemon", false)]
    [InlineData("", false)]
    public void Only_team_followed_by_daemon_is_the_old_name(string command, bool old)
    {
        TeamAutostartShowCommand.UsesOldName(command).Should().Be(old);
    }

    [Fact]
    public void Show_says_when_the_entry_uses_the_old_name()
    {
        var (exit, said) = Show(@"""C:\loadout.exe"" team daemon");

        exit.Should().Be(0);
        said.Should().Contain("old name");
        said.Should().Contain("loadout daemon autostart enable");
    }

    [Fact]
    public void Show_says_nothing_of_names_when_the_entry_uses_the_new_one()
    {
        var (exit, said) = Show(@"""C:\loadout.exe"" daemon");

        exit.Should().Be(0);
        said.Should().Contain("starts when you log in");
        said.Should().NotContain("old name");
    }

    [Fact]
    public void Show_still_answers_when_the_entry_cannot_be_read()
    {
        // Whether the entry is there is what was asked, and that is known.
        var (exit, said) = Show(command: null, readable: false);

        exit.Should().Be(0);
        said.Should().Contain("starts when you log in");
        said.Should().NotContain("old name");
    }

    private static (int Exit, string Said) Show(string? command, bool readable = true)
    {
        var console = new TestConsole();
        console.Profile.Width = 200;

        var services = new ServiceCollection();
        services.AddSingleton<IAnsiConsole>(console);
        services.AddSingleton<IAutostart>(new Entry(command, readable));

        var app = new CommandApp<TeamAutostartShowCommand>(new TypeRegistrar(services));

        // The app registers a console of its own over the one above, so it is
        // told which to use.
        app.Configure(config => config.ConfigureConsole(console));

        var exit = app.Run([]);

        return (exit, console.Output);
    }

    /// <summary>An installed entry that runs the command it was given.</summary>
    private sealed class Entry(string? command, bool readable) : IAutostart
    {
        public string Describe() => "the login item";

        public OperationResult<bool> IsInstalled() => OperationResult<bool>.Ok(true);

        public Task<OperationResult<string?>> CommandAsync(CancellationToken ct = default) =>
            Task.FromResult(readable
                ? OperationResult<string?>.Ok(command)
                : OperationResult<string?>.Fail("could not read it"));

        public Task<OperationResult> InstallAsync(string command, CancellationToken ct = default) =>
            throw new InvalidOperationException("show must not write");

        public Task<OperationResult> UninstallAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("show must not remove");
    }
}
