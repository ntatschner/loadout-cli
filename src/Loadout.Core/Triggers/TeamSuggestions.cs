using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Loadout.Models.Teams;
using Loadout.Models.Triggers;

namespace Loadout.Core.Triggers;

/// <summary>One suggestion a team makes, and where it stands on this machine.</summary>
/// <param name="Team">The team that suggests it.</param>
/// <param name="Suggested">What it suggests.</param>
/// <param name="Accepted">The rule accepted from it, or null when nobody has.</param>
/// <param name="Changed">Whether the team's suggestion has changed since it was accepted.</param>
public sealed record TeamSuggestion(string Team, TeamTrigger Suggested, TriggerRule? Accepted, bool Changed)
{
    /// <summary>The name the rule it becomes is given.</summary>
    public string RuleId => TeamSuggestions.RuleId(Team, Suggested.Name);
}

/// <summary>Trigger rules that teams suggest for themselves, and taking one as a rule.</summary>
/// <remarks>
/// A suggestion only becomes a rule when somebody accepts it, and then it is
/// an ordinary rule: checked the same way, capped, changed through the
/// daemon. What it keeps from the team is a fingerprint, so a team author
/// changing the suggestion later is shown and never taken: nothing a team
/// writes can widen what a machine's triggers do without its owner seeing it.
/// </remarks>
public static class TeamSuggestions
{
    /// <summary>The rule a suggestion becomes, named team-suggestion.</summary>
    public static string RuleId(string team, string name) => $"{team}-{name}".ToLowerInvariant();

    /// <summary>
    /// The rule accepting a suggestion makes, watching <paramref name="projects"/>,
    /// before any budget or cap the person accepting it settles.
    /// </summary>
    public static TriggerRule Rule(string team, TeamTrigger suggested, IEnumerable<string> projects)
    {
        ArgumentNullException.ThrowIfNull(suggested);
        ArgumentNullException.ThrowIfNull(projects);

        return new TriggerRule
        {
            Id = RuleId(team, suggested.Name),
            On = suggested.On.Trim().ToLowerInvariant(),
            Projects = [.. projects],
            Where = new Dictionary<string, string>(suggested.Where, StringComparer.OrdinalIgnoreCase),
            Action = new TriggerAction
            {
                Kind = TriggerActionKinds.Team,
                Name = team,
                Goal = suggested.Goal.Trim(),
                Autonomy = suggested.Autonomy.Trim().ToLowerInvariant(),
            },
            BudgetUsd = suggested.BudgetUsd,
            CapUsd = suggested.CapUsd,
            Debounce = TeamDuration.Parse(suggested.Debounce) ?? TimeSpan.Zero,
            SuggestedBy = team,
            Suggestion = Print(suggested),
        };
    }

    /// <summary>
    /// Every suggestion the given teams make, with the rule accepted from each
    /// and whether the team has changed it since.
    /// </summary>
    public static IReadOnlyList<TeamSuggestion> Of(IEnumerable<TeamDefinition> teams, IEnumerable<TriggerRule> rules)
    {
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(rules);

        var accepted = rules
            .Where(one => one.SuggestedBy.Length > 0)
            .ToDictionary(one => one.Id, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. teams
                .Where(team => !team.Template)
                .SelectMany(team => team.Triggers
                    .Where(one => one.Name.Length > 0)
                    .Select(one =>
                    {
                        accepted.TryGetValue(RuleId(team.Name, one.Name), out var rule);

                        return new TeamSuggestion(
                            team.Name, one, rule, rule is not null && !string.Equals(rule.Suggestion, Print(one), StringComparison.Ordinal));
                    }))
                .OrderBy(one => one.RuleId, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The suggestion's name and a short hash of everything it says, so any
    /// change to it, a figure included, reads as a change.
    /// </summary>
    public static string Print(TeamTrigger suggested)
    {
        ArgumentNullException.ThrowIfNull(suggested);

        var said = string.Join(
            "\n",
            suggested.On,
            string.Join(";", suggested.Where.OrderBy(one => one.Key, StringComparer.Ordinal).Select(one => $"{one.Key}={one.Value}")),
            suggested.Goal,
            suggested.Autonomy,
            suggested.BudgetUsd?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            suggested.CapUsd?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            suggested.Debounce);

        return $"{suggested.Name}@{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(said)))[..12]}";
    }
}
