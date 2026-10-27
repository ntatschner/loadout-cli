using Loadout.Models.Configuration;

namespace Loadout.Core.Teams;

/// <summary>
/// How long a run waits for a person, as this machine has it set.
/// </summary>
/// <remarks>
/// <para>
/// Two waits, for the reason <see cref="NodePermissions.PersonPatience"/>
/// gives: a permission stops a node mid-turn for a one-word answer, and a
/// question has to be read and thought about first.
/// </para>
/// <para>
/// A link address is somebody saying they answer away from this machine, by
/// phone, from a notice. Five minutes is about how long a notice takes to be
/// noticed, so with one set the permission wait is longer by default. Longer
/// still costs nothing but time: a waiting node spends nothing.
/// </para>
/// </remarks>
public static class TeamWaits
{
    /// <summary>The permission wait when a link address is set and no wait is.</summary>
    public static readonly TimeSpan PermissionAway = TimeSpan.FromMinutes(30);

    /// <summary>How long a node stopped mid-turn waits to be told whether it may.</summary>
    public static TimeSpan Permission(MachineTeams? teams) =>
        teams?.PermissionWaitMinutes is { } minutes
            ? TimeSpan.FromMinutes(minutes)
            : string.IsNullOrWhiteSpace(teams?.LinkAddress) ? NodePermissions.Patience : PermissionAway;

    /// <summary>How long a question, gate or brief put to a person waits.</summary>
    public static TimeSpan Question(MachineTeams? teams) =>
        teams?.QuestionWaitMinutes is { } minutes
            ? TimeSpan.FromMinutes(minutes)
            : NodePermissions.PersonPatience;

    /// <summary>A wait as somebody would say it: "5 minutes", "1 hour", "90 minutes".</summary>
    public static string Said(TimeSpan wait) =>
        wait.TotalMinutes % 60 == 0 && wait.TotalMinutes >= 60
            ? wait.TotalHours == 1 ? "1 hour" : $"{wait.TotalHours:0} hours"
            : wait.TotalMinutes == 1 ? "1 minute" : $"{wait.TotalMinutes:0} minutes";
}
