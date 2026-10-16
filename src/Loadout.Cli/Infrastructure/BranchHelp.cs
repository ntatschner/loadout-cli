using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// Help that lists a branch's own command's options, for a branch that runs a
/// command when named on its own.
/// </summary>
/// <remarks>
/// 'loadout daemon' and 'loadout dashboard' are each a command and a group at
/// once. Left to itself the help for a group lists only the group's options,
/// which for these is nothing but --help, so 'loadout daemon --help' stopped
/// mentioning --port the day the daemon became a group. The options listed
/// here are the ones the command really takes when the group is named alone.
/// </remarks>
internal sealed class BranchHelp : HelpProvider
{
    public BranchHelp(ICommandAppSettings settings)
        : base(settings)
    {
    }

    /// <inheritdoc />
    public override IEnumerable<IRenderable> GetOptions(ICommandModel model, ICommandInfo? command) =>
        base.GetOptions(model, command is { IsBranch: true, DefaultCommand: { } runs } ? runs : command);
}
