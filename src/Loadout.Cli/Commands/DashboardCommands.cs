using System.Collections;
using System.ComponentModel;
using System.Reflection;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Agents;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Studio's view of the command line: every command with what it takes, read
/// off the same settings types the parser reads, and a way to run one.
/// </summary>
/// <remarks>
/// <para>
/// Read by reflection rather than written out, for the reason the catalogue
/// is built as commands are registered: a second list of options kept by hand
/// drifts, and a form offering an option the command does not have is a
/// button that fails only when somebody presses it.
/// </para>
/// <para>
/// Running one starts this launcher again with the words after it, so the
/// page does exactly what typing them would. It runs in the home directory,
/// not the daemon's: a command left to find its project from the directory
/// it was started in would otherwise act on whichever repository the daemon
/// happened to be started from, which is nothing the page could show.
/// </para>
/// </remarks>
internal sealed class DashboardCommands : IStudioCommands
{
    private readonly IProcessLauncher _processes;
    private readonly Lazy<IReadOnlyList<StudioCommand>> _catalogue;

    internal DashboardCommands(IProcessLauncher processes)
    {
        ArgumentNullException.ThrowIfNull(processes);

        _processes = processes;
        _catalogue = new(() => Build(Catalogue.Commands, Catalogue.TypeOf));
    }

    /// <inheritdoc />
    IReadOnlyList<StudioCommand> IStudioCommands.Catalogue => _catalogue.Value;

    /// <inheritdoc />
    public Task<OperationResult<IPipedProcess>> StartAsync(IReadOnlyList<string> words, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(words);

        if (LauncherInvocation.Parts() is not ({ Length: > 0 } command, var prefix))
        {
            return Task.FromResult(OperationResult<IPipedProcess>.Fail(
                "This launcher cannot say how it was started, so it cannot run a command for the page.",
                ExitCode.GeneralFailure));
        }

        return _processes.StartPipedAsync(
            new ProcessRequest(
                command,
                [.. prefix, .. words],
                WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            ct);
    }

    /// <summary>Every command in the catalogue as a form sees it.</summary>
    /// <param name="entries">The catalogue.</param>
    /// <param name="typeOf">The command type behind a path, or null for a branch that only groups.</param>
    internal static IReadOnlyList<StudioCommand> Build(IReadOnlyList<CatalogueEntry> entries, Func<string, Type?> typeOf)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(typeOf);

        return [.. entries.Select(entry =>
        {
            var type = typeOf(entry.Path);
            var settings = type is null ? null : SettingsOf(type);

            // A command nobody has placed is treated as one that changes
            // things, so a watch-only page can never run something the list
            // has not been told reads. The coverage test fails on it anyway.
            var reach = StudioCoverage.Reach.GetValueOrDefault(entry.Path)
                ?? new StudioReach(StudioCoverage.CommandsPage, StudioEffect.Changes);

            return new StudioCommand(
                entry.Path,
                entry.Description,
                entry.Category,
                entry.Example,
                Runnable: type is not null,
                reach,
                settings is null ? [] : Arguments(settings),
                settings is null ? [] : Options(settings));
        })];
    }

    /// <summary>The settings type a command is declared with.</summary>
    private static Type? SettingsOf(Type command)
    {
        for (var type = command; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericArguments() is [var settings]
                && typeof(CommandSettings).IsAssignableFrom(settings))
            {
                return settings;
            }
        }

        return null;
    }

    private static string DescriptionOf(PropertyInfo property) =>
        property.GetCustomAttribute<DescriptionAttribute>(inherit: true)?.Description ?? string.Empty;

    private static bool Many(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    private static List<StudioArgument> Arguments(Type settings) =>
        [.. settings.GetProperties()
            .Select(property => (property, argument: property.GetCustomAttribute<CommandArgumentAttribute>(inherit: true)))
            .Where(one => one.argument is not null)
            .OrderBy(one => one.argument!.Position)
            .Select(one => new StudioArgument(
                one.argument!.ValueName.Trim('<', '>', '[', ']'),
                one.argument.Position,
                one.argument.IsRequired,
                Many(one.property.PropertyType),
                DescriptionOf(one.property)))];

    private static List<StudioOption> Options(Type settings)
    {
        var options = new List<StudioOption>();

        foreach (var property in settings.GetProperties())
        {
            if (property.GetCustomAttribute<CommandOptionAttribute>(inherit: true) is not { IsHidden: false } option
                || option.LongNames.Count == 0)
            {
                continue;
            }

            var type = property.PropertyType;
            var underlying = Nullable.GetUnderlyingType(type) ?? type;

            var kind =
                underlying == typeof(bool) ? "flag"
                : underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(FlagValue<>) ? "optional"
                : Many(underlying) ? "values"
                : option.ValueIsOptional ? "optional"
                : "value";

            var choices = underlying.IsEnum
                ? Enum.GetNames(underlying).Select(name => name.ToLowerInvariant()).ToList()
                : [];

            options.Add(new StudioOption(
                "--" + option.LongNames[0],
                option.ShortNames.Count > 0 ? "-" + option.ShortNames[0] : null,
                kind,
                option.ValueName,
                DescriptionOf(property),
                option.IsRequired,
                Global: property.DeclaringType == typeof(GlobalSettings),
                choices));
        }

        return options;
    }
}
