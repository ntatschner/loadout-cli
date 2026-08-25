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

/// <summary>
/// The settings <c>loadout config</c> can read and write (spec section 77).
/// <para>
/// A registry rather than a switch in each command, so list, get and set can
/// never disagree about which keys exist. Keys are hyphenated because that is
/// what people type; the YAML underneath keeps its own naming.
/// </para>
/// </summary>
internal static class ConfigKeys
{
    /// <summary>
    /// One setting. <c>Sample</c> is a valid value, needed only for settings
    /// whose value has a shape rather than being free text: a test infers a
    /// sample from the current value, which keeps a new setting covered the
    /// moment it is added, and that inference cannot work for a setting that
    /// parses what it is given.
    /// </summary>
    internal sealed record Entry(
        string Key,
        string Description,
        Func<LauncherConfig, MachineConfig, string?> Read,
        Action<LauncherConfig, MachineConfig, string> Write,
        bool IsMachineLocal,
        string? Sample = null);

    /// <summary>Renders the agent-to-profile map as one settable string.</summary>
    private static string? FormatProfiles(Dictionary<string, string> profiles) =>
        profiles.Count == 0
            ? null
            : string.Join(";", profiles.Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>
    /// Replaces the map from "claude=Agents;codex=Codex". Replaces rather than
    /// merges, so that removing an entry is possible at all: with a merge the
    /// only way to unset one would be to edit the YAML by hand.
    /// </summary>
    private static void WriteProfiles(Dictionary<string, string> profiles, string value)
    {
        profiles.Clear();

        foreach (var pair in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);

            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                throw new FormatException(
                    $"'{pair}' is not an agent and a profile. Write them as claude=Agents, "
                    + "separated by semicolons.");
            }

            profiles[parts[0]] = parts[1];
        }
    }

    internal static IReadOnlyList<Entry> All =>
    [
        new("workspace-remote", "Git URL of the central workspace",
            (c, _) => c.Workspace.Remote,
            (c, _, v) => c.Workspace.Remote = v, false),

        new("workspace-branch", "Branch of the central workspace",
            (c, _) => c.Workspace.Branch,
            (c, _, v) => c.Workspace.Branch = v, false),

        new("default-agent", "Agent launched when a project names none",
            (c, _) => c.DefaultAgent,
            (c, _, v) => c.DefaultAgent = v, false),

        new("editor-command", "Editor opened by 'loadout code': code, code-insiders, codium, cursor",
            (c, _) => c.Editor.Command,
            (c, _, v) => c.Editor.Command = v, false),

        // One key rather than one per agent, because the set of agents is not
        // fixed and a key list that has to be regenerated when somebody adds a
        // custom agent is a key list that will be wrong.
        new("editor-profiles", "Editor profile per agent, as claude=Agents;codex=Codex",
            (c, _) => FormatProfiles(c.Editor.Profiles),
            (c, _, v) => WriteProfiles(c.Editor.Profiles, v), false,
            Sample: "claude=Agents;codex=Codex"),

        new("sync-launch", "Sync policy at launch: auto, prompt or never",
            (c, _) => c.Sync.Launch,
            (c, _, v) => c.Sync.Launch = v, false),

        new("sync-exit", "Sync policy at exit: prompt, always or never",
            (c, _) => c.Sync.Exit,
            (c, _, v) => c.Sync.Exit = v, false),

        new("sync-timeout", "Seconds a launch-time fetch may block before going offline",
            (c, _) => c.Sync.NetworkTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (c, _, v) => c.Sync.NetworkTimeoutSeconds = int.Parse(v, System.Globalization.CultureInfo.InvariantCulture),
            false),

        new("secrets-provider", "native, environment, 1password, bitwarden, vault or custom",
            (c, _) => c.Secrets.Provider,
            (c, _, v) => c.Secrets.Provider = v, false),

        new("terminal", "Preferred terminal, or 'current' to reuse this one",
            (c, _) => c.Terminal.Preferred,
            (c, _, v) => c.Terminal.Preferred = v, false),

        new("updates-source", "Release feed URL",
            (c, _) => c.Updates.Source,
            (c, _, v) => c.Updates.Source = v, false),

        new("statusline-project", "Show the project slug in the agent status line",
            (c, _) => Boolean(c.Statusline.ShowProject),
            (c, _, v) => c.Statusline.ShowProject = Flag(v), false),

        new("statusline-directory", "Show the working directory in the agent status line",
            (c, _) => Boolean(c.Statusline.ShowDirectory),
            (c, _, v) => c.Statusline.ShowDirectory = Flag(v), false),

        new("statusline-git", "Show the branch and whether the tree is dirty",
            (c, _) => Boolean(c.Statusline.ShowGit),
            (c, _, v) => c.Statusline.ShowGit = Flag(v), false),

        new("statusline-model", "Show the model name in the agent status line",
            (c, _) => Boolean(c.Statusline.ShowModel),
            (c, _, v) => c.Statusline.ShowModel = Flag(v), false),

        new("statusline-context", "Show how much of the context window is spent",
            (c, _) => Boolean(c.Statusline.ShowContext),
            (c, _, v) => c.Statusline.ShowContext = Flag(v), false),

        new("statusline-colour", "Colour the agent status line with ANSI escapes",
            (c, _) => Boolean(c.Statusline.Colour),
            (c, _, v) => c.Statusline.Colour = Flag(v), false),

        new("statusline-separator", "Text drawn between status line segments",
            (c, _) => c.Statusline.Separator,
            (c, _, v) => c.Statusline.Separator = v, false),

        // Machine-local from here down: these describe this machine's layout and
        // must never travel to another one (spec section 15).
        new("clone-root", "Where new clones are placed on this machine",
            (_, m) => m.DefaultCloneRoot,
            (_, m, v) => m.DefaultCloneRoot = v, true),

        new("discovery-roots", "Comma-separated directories scanned for repositories",
            (_, m) => string.Join(", ", m.DiscoveryRoots),
            (_, m, v) => m.DiscoveryRoots = v
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            true),

        new("agent-search-paths", "Comma-separated extra directories searched for agent executables",
            (c, _) => string.Join(", ", c.AgentSearchPaths),
            (c, _, v) => c.AgentSearchPaths = v
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            false),
    ];

    /// <summary>How a flag is shown, in the spelling the setter accepts back.</summary>
    private static string Boolean(bool value) => value ? "true" : "false";

    /// <summary>
    /// Reads a flag generously. Somebody turning a segment off will type
    /// whichever of these came to mind, and refusing all but one spelling
    /// would be pedantry rather than validation.
    /// </summary>
    private static bool Flag(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "on" or "1" => true,
            "false" or "no" or "off" or "0" => false,
            _ => throw new FormatException(
                $"'{value}' is not a yes or no. Use true or false."),
        };

    internal static Entry? Find(string key) =>
        All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
}

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
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
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
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
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
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        var entry = ConfigKeys.Find(settings.Key);
        if (entry is null)
        {
            return output.Fail(
                ConfigGetCommand.UnknownKeyMessage(settings.Key), ExitCode.InvalidArguments);
        }

        var config = await _configuration.LoadConfigAsync().ConfigureAwait(false);
        var machine = await _configuration.LoadMachineAsync().ConfigureAwait(false);

        if (config.Failed || machine.Failed)
        {
            return output.Fail(config.Failed ? config : machine);
        }

        try
        {
            entry.Write(config.Value!, machine.Value!, settings.Value);
        }
        catch (FormatException)
        {
            // Only the numeric settings can fail here, and naming the setting
            // is more use than a bare parse error.
            return output.Fail(
                $"'{settings.Value}' is not valid for {entry.Key}. {entry.Description}.",
                ExitCode.InvalidArguments);
        }

        var save = entry.IsMachineLocal
            ? await _configuration.SaveMachineAsync(machine.Value!).ConfigureAwait(false)
            : await _configuration.SaveConfigAsync(config.Value!).ConfigureAwait(false);

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
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
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

        var result = await _launcher.OpenInFileManagerAsync(path).ConfigureAwait(false);

        if (result.Failed)
        {
            // Falling back to printing the path keeps the command useful on a
            // headless machine, where there is nothing to open it with.
            output.WriteLine($"[yellow]Could not open an editor:[/] {Markup.Escape(result.Error!)}");
            Console.Out.WriteLine(path);
        }

        return CommandOutput.Success();
    }
}
