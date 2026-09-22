namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// One remedy this machine has agreed a team may run on its own.
/// </summary>
/// <param name="Team">The team whose directory it is in.</param>
/// <param name="Remedy">The remedy, by name.</param>
/// <param name="By">Who said so.</param>
/// <param name="At">When, as text, because this is read and never compared.</param>
public sealed record TrustedOnThisMachine(string Team, string Remedy, string By, string At);

/// <summary>
/// What this machine is set to, as the dashboard may read it.
/// </summary>
/// <remarks>
/// <para>
/// A projection rather than the machine's own record, because one field of it
/// must never leave this process. Where notices are sent is a webhook address,
/// and a webhook address <em>is</em> the credential: anybody holding it can post
/// into that channel as you. So this says whether one is held and never what it
/// is, which is the rule everything else here follows - the pattern or the
/// type, never the value.
/// </para>
/// <para>
/// Everything else is a preference somebody typed and would recognise on a
/// screen: which art the desks are drawn with, which address the server binds,
/// which teams something outside this machine may start.
/// </para>
/// </remarks>
/// <param name="NotifyKind">slack, discord, teams, telegram, generic, or empty for none.</param>
/// <param name="NotifyChat">The Telegram chat, meaningless for the others.</param>
/// <param name="NotifyAddressSet">Whether an address is held. Never the address.</param>
/// <param name="OfficeSet">Which set of office art the desks are drawn with.</param>
/// <param name="WaitingSet">Which set the waiting area is drawn with.</param>
/// <param name="OfficeSets">The art sets this machine actually has, so the page offers real ones.</param>
/// <param name="WebhookListen">The address the server binds. 127.0.0.1 is this machine only.</param>
/// <param name="WebhookTeams">What something outside this machine may start, by name.</param>
/// <param name="WebhookTokenSet">Whether a trigger token is held. Never the token.</param>
/// <param name="Trusted">Remedies agreed to run unattended.</param>
public sealed record MachineSettings(
    string NotifyKind,
    string NotifyChat,
    bool NotifyAddressSet,
    string OfficeSet,
    string WaitingSet,
    IReadOnlyList<string> OfficeSets,
    string WebhookListen,
    IReadOnlyList<string> WebhookTeams,
    bool WebhookTokenSet,
    IReadOnlyList<TrustedOnThisMachine> Trusted);

/// <summary>
/// Something the page asked this machine be set to.
/// </summary>
/// <remarks>
/// <para>
/// One shape for all of them, because they all end the same way the run verbs
/// do: the daemon runs the command somebody would have typed. Nothing here
/// decides what any of them mean, and nothing here writes a configuration file.
/// </para>
/// <para>
/// Two of these are not like the others and the server treats them so. Moving
/// <c>listen</c> off loopback is the setting that makes every run on this
/// machine reachable by anybody who has the address, and trusting a remedy is
/// standing permission for a script to run unattended - so that one needs the
/// second credential, the same one typing at a live node needs, rather than the
/// token that merely got somebody to the page.
/// </para>
/// </remarks>
/// <param name="What">
/// Which setting: notify, office, waiting, listen, webhook-teams, webhook or
/// remedy.
/// </param>
/// <param name="Value">
/// What to set it to, in the words the command takes. For a remedy, its name.
/// </param>
/// <param name="Url">
/// The address notices are posted to. Only ever travels this way - inwards, from
/// the person setting it - and is never sent back or written to the daemon's
/// output.
/// </param>
/// <param name="Chat">The Telegram chat, for the one kind that has one.</param>
/// <param name="Off">
/// Turn it off rather than set it: clear the notices, disable the trigger token,
/// revoke a remedy's agreement.
/// </param>
/// <param name="Team">The team a remedy belongs to, which is part of naming it.</param>
/// <param name="Grant">
/// A grant from <c>/api/attach</c>, for the changes that need the second
/// credential. Ignored by the ones that do not.
/// </param>
public sealed record SettingsChange(
    string What,
    string? Value = null,
    string? Url = null,
    string? Chat = null,
    bool Off = false,
    string? Team = null,
    string? Grant = null);
