using System.Globalization;
using System.Text;
using System.Text.Json;
using Loadout.Core.Tasks;
using Loadout.Models.Results;

namespace Loadout.Core.Ask;

/// <summary>What the agent made of a request it was asked.</summary>
/// <param name="Answer">A reply in words, when it answered from what it could read.</param>
/// <param name="Request">The request as one Loadout runs itself, when it could place it.</param>
/// <param name="CostUsd">What the turn cost, as the agent reported it.</param>
/// <param name="SessionId">The agent's conversation, for carrying it on with the next request.</param>
public sealed record AskAgentTurn(string? Answer, AskRequest? Request, decimal CostUsd, string? SessionId);

/// <summary>What the agent is told about this machine before it is asked.</summary>
/// <param name="Vocabulary">The projects and teams it may name, and no others.</param>
/// <param name="Today">Today, for periods like "last tuesday".</param>
/// <param name="Project">The project its tools answer about.</param>
public sealed record AskAgentContext(AskVocabulary Vocabulary, DateOnly Today, string Project);

/// <summary>
/// Asks an agent to place a request Loadout could not, or to answer it from
/// what it can read.
/// </summary>
/// <remarks>
/// Only ever run when somebody chooses it. The agent is given tools that read
/// and nothing else, and its answer is a request in the same shape the parser
/// produces, so whatever it proposes goes through the same plan card and the
/// same approval as one Loadout placed itself. It never acts.
/// </remarks>
public interface IAskAgent
{
    Task<OperationResult<AskAgentTurn>> AskAsync(
        string query,
        AskAgentContext context,
        string? resumeSessionId,
        CancellationToken ct = default);
}

/// <summary>The shape the agent answers in, what it is told, and how its answer is read.</summary>
/// <remarks>
/// Read strictly. A project or team the agent names that is not on this
/// machine, a shape that is not one of the four, or a date that does not parse
/// is refused rather than guessed at, because whatever comes back may become a
/// plan somebody approves.
/// </remarks>
public static class AskAgentSchema
{
    /// <summary>The name the shape goes by when the agent is asked to give it again.</summary>
    public const string Contract = "ask/1";

    /// <summary>A JSON Schema for the answer: words, or a request, or both.</summary>
    public const string Json = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["answer", "request"],
          "properties": {
            "answer": { "type": ["string", "null"], "description": "A reply in plain words, when you answered from what you read. Null when you give a request instead." },
            "request": {
              "type": ["object", "null"],
              "additionalProperties": false,
              "required": ["shape", "from", "until", "project", "count", "pick", "team", "start"],
              "properties": {
                "shape": { "enum": ["spend", "tasks", "team-queue", "terminals"] },
                "from": { "type": ["string", "null"], "description": "First day, yyyy-MM-dd, for spend." },
                "until": { "type": ["string", "null"], "description": "Last day, yyyy-MM-dd, for spend." },
                "project": { "type": ["string", "null"], "description": "One project's slug, or null for all of them." },
                "count": { "type": ["integer", "null"], "minimum": 1, "maximum": 50 },
                "pick": { "enum": ["any", "easy", "needs-work-first"] },
                "team": { "type": ["string", "null"] },
                "start": { "type": "boolean" }
              }
            }
          }
        }
        """;

    /// <summary>What the agent is asked.</summary>
    public static string Prompt(string query, AskAgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var text = new StringBuilder();

        text.AppendLine("You are helping Loadout answer a request typed into its Studio palette. Its own parser could not place it.");
        text.AppendLine();
        text.AppendLine("Loadout answers four kinds of request itself, and you should turn the request into one of them whenever it fits:");
        text.AppendLine("- spend: what was spent over a period (from and until, inclusive), for one project or all of them.");
        text.AppendLine("- tasks: list or rank open tasks, for one project or all; pick 'easy' or 'needs-work-first' when asked.");
        text.AppendLine("- team-queue: queue some tasks for a team, and start it when asked (start true).");
        text.AppendLine("- terminals: open some terminals, each with a task loaded.");
        text.AppendLine();
        text.AppendLine("When it fits none of them, answer in plain words from what your tools can read, and give request as null.");
        text.AppendLine("You cannot start, queue, record or change anything, and you must not say you did. Anything that would start or change something is a request, which a person approves.");
        text.AppendLine("Use only the project slugs and team names below. If the request names one that is not here, say so in answer.");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Today is {context.Today:yyyy-MM-dd} ({context.Today:dddd}).");
        text.AppendLine("Projects: " + string.Join(", ", context.Vocabulary.Projects.Select(project => project.Slug)));
        text.AppendLine("Teams: " + (context.Vocabulary.Teams.Count == 0 ? "none" : string.Join(", ", context.Vocabulary.Teams)));
        text.AppendLine(CultureInfo.InvariantCulture, $"Your tools read about {context.Project}.");
        text.AppendLine();
        text.AppendLine("The request:");
        text.AppendLine(query);

        return text.ToString();
    }

    /// <summary>Reads the agent's answer, refusing anything that names what is not here.</summary>
    public static OperationResult<(string? Answer, AskRequest? Request)> Read(string? json, AskVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);

        if (string.IsNullOrWhiteSpace(json))
        {
            return OperationResult<(string?, AskRequest?)>.Fail("The agent gave no answer.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var answer = Text(root, "answer");
            var request = root.TryGetProperty("request", out var given) && given.ValueKind == JsonValueKind.Object
                ? given
                : (JsonElement?)null;

            if (request is null)
            {
                return answer is { Length: > 0 }
                    ? OperationResult<(string?, AskRequest?)>.Ok((answer, null))
                    : OperationResult<(string?, AskRequest?)>.Fail("The agent gave neither an answer nor a request.");
            }

            var shape = Text(request.Value, "shape") switch
            {
                "spend" => AskShape.Spend,
                "tasks" => AskShape.Tasks,
                "team-queue" => AskShape.TeamQueue,
                "terminals" => AskShape.Terminals,
                null => throw new FormatException("The request has no shape."),
                var other => throw new FormatException($"'{other}' is not a kind of request."),
            };

            var project = Text(request.Value, "project");

            if (project is not null && !vocabulary.Projects.Any(one => string.Equals(one.Slug, project, StringComparison.OrdinalIgnoreCase)))
            {
                throw new FormatException($"'{project}' is not a project on this machine.");
            }

            var team = Text(request.Value, "team");

            if (team is not null && !vocabulary.Teams.Any(one => string.Equals(one, team, StringComparison.OrdinalIgnoreCase)))
            {
                throw new FormatException($"'{team}' is not a team on this machine.");
            }

            DateSpan? period = null;

            if (Day(request.Value, "from") is { } from)
            {
                var until = Day(request.Value, "until") ?? from;

                if (until < from)
                {
                    throw new FormatException("The period ends before it starts.");
                }

                period = new DateSpan(from, until, from == until
                    ? from.ToString("ddd d MMM", CultureInfo.InvariantCulture)
                    : $"{from:ddd d MMM} to {until:ddd d MMM}");
            }

            var count = request.Value.TryGetProperty("count", out var counted) && counted.ValueKind == JsonValueKind.Number
                ? Math.Clamp(counted.GetInt32(), 1, 50)
                : (int?)null;

            var pick = Text(request.Value, "pick") switch
            {
                "easy" => TaskPick.Easy,
                "needs-work-first" => TaskPick.NeedsWorkFirst,
                _ => TaskPick.Any,
            };

            var start = request.Value.TryGetProperty("start", out var starting) && starting.ValueKind == JsonValueKind.True;

            return OperationResult<(string?, AskRequest?)>.Ok((answer, new AskRequest(
                shape,
                period,
                project is null ? null : vocabulary.Projects.First(one => string.Equals(one.Slug, project, StringComparison.OrdinalIgnoreCase)).Slug,
                count,
                pick,
                team is null ? null : vocabulary.Teams.First(one => string.Equals(one, team, StringComparison.OrdinalIgnoreCase)),
                start)));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return OperationResult<(string?, AskRequest?)>.Fail($"The agent's answer could not be used: {ex.Message}");
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text.Trim()
            : null;

    private static DateOnly? Day(JsonElement element, string name) =>
        Text(element, name) is { } text
            ? DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : throw new FormatException($"'{text}' is not a date as yyyy-MM-dd.")
            : null;
}
