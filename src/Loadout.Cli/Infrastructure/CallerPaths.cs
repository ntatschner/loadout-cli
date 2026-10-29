using System.Reflection;
using Loadout.Platform.Common;
using Spectre.Console.Cli;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// Makes each relative path a command was given relative to the directory it
/// was run from, when it runs inside another process for somebody else.
/// </summary>
/// <remarks>
/// <para>
/// A command handed to the daemon runs in the daemon's process, whose working
/// directory is the daemon's own. Leaving out <c>--repo</c> was already right,
/// because commands fall back to <see cref="Invocation.CurrentDirectory"/>.
/// But a relative value such as <c>--repo .</c> was later made whole against
/// the process, so <c>docs audit --repo .</c> looked in the daemon's directory,
/// found no documentation and said so, which reads like a pass.
/// </para>
/// <para>
/// One rule covers every command rather than a fix at each place that reads a
/// path: a string option or argument whose value is named PATH, FILE, FILES or
/// DIRECTORY holds a path. Outside an invocation nothing changes, so the
/// command line keeps every value exactly as it was typed.
/// </para>
/// </remarks>
internal sealed class CallerPaths : ICommandInterceptor
{
    private static readonly HashSet<string> PathNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "FILE", "FILES", "DIRECTORY",
    };

    /// <inheritdoc />
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (Invocation.Current is not { } current)
        {
            return;
        }

        Resolve(settings, current.WorkingDirectory);
    }

    /// <summary>Rewrites each relative path in <paramref name="settings"/> against <paramref name="directory"/>.</summary>
    internal static void Resolve(CommandSettings settings, string directory)
    {
        foreach (var property in settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || !HoldsPath(property))
            {
                continue;
            }

            switch (property.GetValue(settings))
            {
                case string one:
                    property.SetValue(settings, Whole(one, directory));
                    break;

                case string[] many:
                    property.SetValue(settings, many.Select(one => Whole(one, directory)).ToArray());
                    break;
            }
        }
    }

    private static bool HoldsPath(PropertyInfo property) =>
        property.GetCustomAttribute<CommandOptionAttribute>()?.ValueName is { } option && PathNames.Contains(option)
        || property.GetCustomAttribute<CommandArgumentAttribute>()?.ValueName is { } argument && PathNames.Contains(argument);

    private static string Whole(string path, string directory) =>
        path.Length == 0 || Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path, directory);
}
