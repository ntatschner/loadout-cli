using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Security;
using Loadout.Models;
using Loadout.Models.Configuration;
using Loadout.Platform.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Lists every setting and its current value (spec section 77).</summary>
[Description("List configuration settings and their current values.")]
public sealed class ConfigListCommand : AsyncCommand<GlobalSettings>
{
    private readonly IConfigurationService _configuration;
    private readonly IPlatformPaths _paths;
    private readonly IAnsiConsole _console;

    public ConfigListCommand(
        IConfigurationService configuration,
        IPlatformPaths paths,
        IAnsiConsole console)
    {
        _configuration = configuration;
        _paths = paths;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var config = await _configuration.LoadConfigAsync().ConfigureAwait(false);
        if (config.Failed)
        {
            return output.Fail(config);
        }

        var machine = await _configuration.LoadMachineAsync().ConfigureAwait(false);
        if (machine.Failed)
        {
            return output.Fail(machine);
        }

        var values = ConfigKeys.All
            .Select(e => new
            {
                key = e.Key,
                // A workspace URL can carry an embedded credential, so values
                // are redacted even when the user asked to see them.
                value = SecretRedactor.Redact(e.Read(config.Value!, machine.Value!)),
                machineLocal = e.IsMachineLocal,
                description = e.Description,
            })
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(new { settings = values });
            return CommandOutput.Success();
        }

        // Said before the values, because "where does this live" is the first
        // question anybody has and the answer was previously only available by
        // running an unrelated command and reading its output carefully.
        output.WriteLine($"[dim]Shared    {Markup.Escape(_paths.Paths.Config)}"
            + $"{Path.DirectorySeparatorChar}config.yaml[/]");

        output.WriteLine($"[dim]Machine   {Markup.Escape(_paths.Paths.State)}"
            + $"{Path.DirectorySeparatorChar}machines.yaml[/]");

        output.WriteBlankLine();

        var table = new Table().Border(TableBorder.Simple).BorderColor(Color.Grey);
        table.AddColumn("Setting");
        table.AddColumn("Value");
        table.AddColumn(string.Empty);

        foreach (var value in values)
        {
            table.AddRow(
                Markup.Escape(value.key),
                value.value.Length == 0 ? "[dim](unset)[/]" : Markup.Escape(value.value),
                value.machineLocal ? "[dim]this machine[/]" : string.Empty);
        }

        output.Write(table);

        output.WriteLine("[dim]What one means:[/] loadout config get <setting> --explain");
        output.WriteLine("[dim]Change one:[/]     loadout config set <setting> <value>");
        output.WriteLine("[dim]Edit the file:[/]  loadout config edit");

        return CommandOutput.Success();
    }
}

/// <summary>Reads one setting (spec section 77).</summary>
[Description("Print one configuration value.")]
public sealed class ConfigGetCommand : AsyncCommand<ConfigGetCommand.Settings>
{
    private readonly IConfigurationService _configuration;
    private readonly IPlatformPaths _paths;
    private readonly IAnsiConsole _console;

    public ConfigGetCommand(
        IConfigurationService configuration,
        IPlatformPaths paths,
        IAnsiConsole console)
    {
        _configuration = configuration;
        _paths = paths;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Setting name, for example default-agent.")]
        public string Key { get; init; } = string.Empty;

        [CommandOption("--explain")]
        [Description("Say what the setting does, which file holds it and what it accepts.")]
        public bool Explain { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var entry = ConfigKeys.Find(settings.Key);
        if (entry is null)
        {
            return output.Fail(UnknownKeyMessage(settings.Key), ExitCode.InvalidArguments);
        }

        var config = await _configuration.LoadConfigAsync().ConfigureAwait(false);
        var machine = await _configuration.LoadMachineAsync().ConfigureAwait(false);

        if (config.Failed || machine.Failed)
        {
            return output.Fail(config.Failed ? config : machine);
        }

        var value = SecretRedactor.Redact(entry.Read(config.Value!, machine.Value!));

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                key = entry.Key,
                value,
                description = entry.Description,
                scope = entry.IsMachineLocal ? "machine" : "shared",
                file = FileFor(entry),
            });

            return CommandOutput.Success();
        }

        if (!settings.Explain)
        {
            // Written raw so it can be captured by a script without markup.
            // The explanation is behind a flag for exactly this reason: adding
            // it here would break every $(loadout config get ...) in existence.
            Console.Out.WriteLine(value);

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(entry.Key)}[/]");
        output.WriteLine(value.Length == 0
            ? "  [dim](unset)[/]"
            : $"  {Markup.Escape(value)}");

        output.WriteBlankLine();
        output.WriteLine($"  {Markup.Escape(entry.Description)}");

        output.WriteLine(entry.IsMachineLocal
            ? "  [dim]Machine-local: it describes this machine's layout and never travels "
              + "to another one.[/]"
            : "  [dim]Shared: it travels with the workspace to every machine you use.[/]");

        output.WriteLine($"  [dim]{Markup.Escape(FileFor(entry))}[/]");

        return CommandOutput.Success();
    }

    private string FileFor(ConfigKeys.Entry entry) => Path.Combine(
        entry.IsMachineLocal ? _paths.Paths.State : _paths.Paths.Config,
        entry.IsMachineLocal ? "machines.yaml" : "config.yaml");

    internal static string UnknownKeyMessage(string key) =>
        $"'{key}' is not a known setting. Available: "
        + string.Join(", ", ConfigKeys.All.Select(e => e.Key));
}

/// <summary>Writes one setting (spec section 77).</summary>
[Description("Set one configuration value.")]
public sealed class ConfigSetCommand : AsyncCommand<ConfigSetCommand.Settings>
{
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public ConfigSetCommand(IConfigurationService configuration, IAnsiConsole console)
    {
        _configuration = configuration;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<key>")]
        [Description("Setting name, for example default-agent.")]
        public string Key { get; init; } = string.Empty;

        [CommandArgument(1, "<value>")]
        [Description("New value.")]
        public string Value { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var entry = ConfigKeys.Find(settings.Key);
        if (entry is null)
        {
            return output.Fail(
                ConfigGetCommand.UnknownKeyMessage(settings.Key), ExitCode.InvalidArguments);
        }

        // Read and written under one lock rather than loaded, changed and
        // saved. Two of these running at once — a person at a prompt while a
        // session writes its own setting — would otherwise each read the same
        // starting file and write their change over the other's, leaving a
        // valid file, two successful commands and one setting silently gone.
        //
        // The other side of the pair is still loaded plainly, because it is
        // only read: a key writes to the machine file or the launcher file,
        // never both.
        FormatException? invalid = null;

        // Carried out rather than thrown through the store: a write that throws
        // inside the lock is a failure nobody gets to name. Only the numeric
        // settings can fail here, and naming the setting is more use than a
        // bare parse error.
        void Apply(Action write)
        {
            try
            {
                write();
            }
            catch (FormatException ex)
            {
                invalid = ex;
            }
        }

        Loadout.Models.Results.OperationResult save;

        if (entry.IsMachineLocal)
        {
            var other = await _configuration.LoadConfigAsync().ConfigureAwait(false);

            if (other.Failed)
            {
                return output.Fail(other);
            }

            save = await _configuration.UpdateMachineAsync(
                machine => Apply(() => entry.Write(other.Value!, machine, settings.Value)))
                .ConfigureAwait(false);
        }
        else
        {
            var other = await _configuration.LoadMachineAsync().ConfigureAwait(false);

            if (other.Failed)
            {
                return output.Fail(other);
            }

            save = await _configuration.UpdateConfigAsync(
                config => Apply(() => entry.Write(config, other.Value!, settings.Value)))
                .ConfigureAwait(false);
        }

        if (invalid is not null)
        {
            return output.Fail(
                $"'{settings.Value}' is not valid for {entry.Key}. {entry.Description}.",
                ExitCode.InvalidArguments);
        }

        if (save.Failed)
        {
            return output.Fail(save);
        }

        output.WriteLine($"[green]Set[/] {Markup.Escape(entry.Key)} "
            + $"[dim]= {Markup.Escape(SecretRedactor.Redact(settings.Value))}[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Opens the config file in the platform's editor (spec section 77).</summary>
[Description("Print the path of the configuration file, or open it.")]
public sealed class ConfigEditCommand : AsyncCommand<ConfigEditCommand.Settings>
{
    private readonly IPlatformPaths _paths;
    private readonly IApplicationLauncher _launcher;
    private readonly IAnsiConsole _console;

    public ConfigEditCommand(
        IPlatformPaths paths,
        IApplicationLauncher launcher,
        IAnsiConsole console)
    {
        _paths = paths;
        _launcher = launcher;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--machine")]
        [Description("Open this machine's local configuration instead.")]
        public bool Machine { get; init; }

        [CommandOption("--path-only")]
        [Description("Print the path and do not open anything.")]
        public bool PathOnly { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var path = settings.Machine ? _paths.Paths.MachinesFile : _paths.Paths.ConfigFile;

        if (settings.PathOnly || output.IsJson)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { path });
            }
            else
            {
                Console.Out.WriteLine(path);
            }

            return CommandOutput.Success();
        }

        if (!File.Exists(path))
        {
            return output.Fail(
                $"'{path}' does not exist yet. Run: loadout setup",
                ExitCode.ConfigurationInvalid);
        }

        if (!output.CanOpenAWindow)
        {
            // Nobody is watching, so the path is the whole of the useful
            // answer. Handing the file to the desktop here is how a suite run
            // put a "choose an application" dialog in front of somebody
            // several times a day.
            Console.Out.WriteLine(path);

            return CommandOutput.Success();
        }

        var result = await _launcher.OpenInFileManagerAsync(path).ConfigureAwait(false);

        if (result.Failed)
        {
            // Falling back to printing the path keeps the command useful on a
            // headless machine, where there is nothing to open it with.
            output.WriteLine($"[yellow]Could not open an editor:[/] {Loadout.Tui.Shown.Safely(result.Error!)}");
            Console.Out.WriteLine(path);
        }

        return CommandOutput.Success();
    }
}
