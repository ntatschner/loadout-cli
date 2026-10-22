using Loadout.Core.Teams.Daemon;

namespace Loadout.Core.Triggers;

/// <summary>Turns what <c>/api/trigger</c> was sent into an event for the rules.</summary>
/// <remarks>
/// <para>
/// Before triggers, a webhook started its named team directly, which made it
/// the one way into a run that no cap could see. As an event it meets the same
/// rules, ledger and journal as everything else, and a rule decides what it
/// starts: <c>on: webhook</c>, conditioned on the team it names, with a goal
/// that can quote the webhook's own as <c>{goal}</c>.
/// </para>
/// <para>
/// Each request is its own subject. Two webhooks asking for the same team on
/// the same project a second apart are two requests, and coalescing them would
/// drop one that its sender was told had been accepted.
/// </para>
/// </remarks>
public static class WebhookEvents
{
    /// <summary>What its events name as their source.</summary>
    public const string Name = "webhook";

    /// <summary>The event for one request.</summary>
    public static TriggerEvent From(TriggerRequest asking, string project, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(asking);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);

        var id = $"webhook:{Guid.NewGuid():N}";

        return new TriggerEvent(
            id,
            now,
            Name,
            TriggerEventKinds.Webhook,
            id,
            project,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["team"] = asking.Team,
                ["project"] = project,
                ["goal"] = asking.Goal,
            },
            []);
    }
}
