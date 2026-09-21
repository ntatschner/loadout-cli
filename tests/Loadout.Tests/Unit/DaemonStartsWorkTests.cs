using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams.Daemon;
using Loadout.Tui;
using Spectre.Console;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Starting a run answers when it has started, not when it has finished.
/// </summary>
/// <remarks>
/// <para>
/// A team run takes twenty minutes on a good day. The webhook waited for the
/// whole of it and then replied "Started", which is the one thing a 202 does
/// not mean — and whatever called it had given up long before. A GitHub webhook
/// waits ten seconds and then retries, and every retry arrived here as another
/// run of the same team on the same repository, merging into the same branch.
/// </para>
/// <para>
/// So the property worth pinning is a timing one: the handler returns while the
/// command is still running. A held command makes that a fact rather than a
/// race — nothing here waits on a clock.
/// </para>
/// </remarks>
public sealed class DaemonStartsWorkTests
{
    [Fact]
    public async Task A_triggered_run_is_answered_before_it_has_finished()
    {
        var held = new Held();
        var daemon = Daemon(held);

        var answered = await daemon.TriggeredAsync(
            new TriggerRequest("docs-crew", "check the docs", "loadout-cli"),
            Output(),
            CancellationToken.None);

        answered.Succeeded.Should().BeTrue(answered.Error);

        // Waited for rather than assumed: the run is started on a task of its
        // own, so "has it been asked for yet" is a question about scheduling
        // and asserting it straight away is a race in the test.
        await held.Asked();

        // And it is still going. If this ever waits for the run again, this is
        // the line that stops it.
        held.Finished.Should().BeFalse("answering did not wait for it");

        held.Release();
    }

    [Fact]
    public async Task So_is_one_started_from_the_page()
    {
        var held = new Held();
        var daemon = Daemon(held);

        var answered = await daemon.BeganAsync(
            new StartRequest("docs-crew", "check the docs", "loadout-cli"),
            Output(),
            CancellationToken.None);

        answered.Succeeded.Should().BeTrue(answered.Error);

        await held.Asked();

        held.Finished.Should().BeFalse();

        held.Release();
    }

    [Fact]
    public async Task A_triggered_run_with_no_project_is_refused_rather_than_started()
    {
        var held = new Held();
        var daemon = Daemon(held);

        var answered = await daemon.TriggeredAsync(
            new TriggerRequest("docs-crew", "check the docs"),
            Output(),
            CancellationToken.None);

        answered.Failed.Should().BeTrue();
        answered.Error.Should().Contain("project");

        held.Started.Should().BeFalse("nothing should have been typed");
    }

    [Fact]
    public async Task What_the_page_asked_for_becomes_the_command_line()
    {
        var held = new Held();
        var daemon = Daemon(held);

        await daemon.BeganAsync(
            new StartRequest("docs-crew", "check the docs", "loadout-cli", 3, "supervised"),
            Output(),
            CancellationToken.None);

        await held.Asked();

        held.Path.Should().Be("team run");

        held.Arguments.Should().Equal(
            "docs-crew", "check the docs",
            "--project", "loadout-cli",
            "--rounds", "3",
            "--autonomy", "supervised",
            "--non-interactive");

        held.Release();
    }

    /// <summary>
    /// Only the two things this path touches are real. The rest are not reached
    /// and standing them up would hide what the test is about.
    /// </summary>
    private static TeamDaemonCommand Daemon(ICommandCatalogue commands) =>
        new(
            secrets: null!,
            client: null!,
            configuration: null!,
            schedules: null!,
            journal: null!,
            commands,
            paths: null!,
            projects: null!,
            tasks: null!,
            git: null!,
            processes: null!,
            Quiet(),
            TimeProvider.System,
            Loadout.Cli.Infrastructure.AccessibleMode.Off,
            speech: null!,
            teams: null!,
            library: null!,
            workspace: null!);

    private static CommandOutput Output() => new(Quiet(), new GlobalSettings());

    /// <summary>A console that writes where nobody is looking.</summary>
    private static IAnsiConsole Quiet() =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null),
            Interactive = InteractionSupport.No,
        });

    /// <summary>A command that starts and does not finish until it is let go.</summary>
    private sealed class Held : ICommandCatalogue
    {
        private readonly TaskCompletionSource _asked = new();
        private readonly TaskCompletionSource _go = new();

        public bool Started { get; private set; }

        public bool Finished { get; private set; }

        public string? Path { get; private set; }

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public IReadOnlyList<CatalogueEntry> Commands => [];

        public Task Asked() => _asked.Task;

        public void Release() => _go.TrySetResult();

        public async Task<int> RunAsync(
            string path,
            IReadOnlyList<string> arguments,
            CancellationToken ct = default)
        {
            Started = true;
            Path = path;
            Arguments = arguments;

            _asked.TrySetResult();

            await _go.Task;

            Finished = true;

            return 0;
        }
    }
}
