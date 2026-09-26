using System.Text.Json;
using System.Text.Json.Serialization;
using Loadout.Models.Ideas;
using Loadout.Models.Results;

namespace Loadout.Core.Ideas;

/// <summary>What an agent answered to one round, as <see cref="IdeaSchema.Version1"/> has it.</summary>
public sealed class IdeaReply
{
    [JsonPropertyName("contract")]
    public string Contract { get; set; } = string.Empty;

    [JsonPropertyName("understanding")]
    public string Understanding { get; set; } = string.Empty;

    [JsonPropertyName("questions")]
    public List<ReplyQuestion> Questions { get; set; } = [];

    [JsonPropertyName("plan")]
    public ReplyPlan? Plan { get; set; }

    public sealed class ReplyQuestion
    {
        [JsonPropertyName("question")]
        public string Question { get; set; } = string.Empty;

        [JsonPropertyName("why")]
        public string Why { get; set; } = string.Empty;

        [JsonPropertyName("options")]
        public List<string> Options { get; set; } = [];

        [JsonPropertyName("recommendation")]
        public string Recommendation { get; set; } = string.Empty;
    }

    public sealed class ReplyPlan
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("layers")]
        public List<ReplyLayer> Layers { get; set; } = [];

        [JsonPropertyName("additions")]
        public List<ReplyAddition> Additions { get; set; } = [];

        [JsonPropertyName("project")]
        public ReplyProject Project { get; set; } = new();
    }

    public sealed class ReplyLayer
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("purpose")]
        public string Purpose { get; set; } = string.Empty;

        [JsonPropertyName("options")]
        public List<ReplyOption> Options { get; set; } = [];
    }

    public sealed class ReplyOption
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("detail")]
        public string Detail { get; set; } = string.Empty;

        [JsonPropertyName("pros")]
        public List<string> Pros { get; set; } = [];

        [JsonPropertyName("cons")]
        public List<string> Cons { get; set; } = [];

        [JsonPropertyName("recommended")]
        public bool Recommended { get; set; }
    }

    public sealed class ReplyAddition
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("why")]
        public string Why { get; set; } = string.Empty;
    }

    public sealed class ReplyProject
    {
        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("is_new")]
        public bool IsNew { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads an answer, or says plainly why the text is not one.</summary>
    public static OperationResult<IdeaReply> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return OperationResult<IdeaReply>.Fail(
                $"The agent ended its turn without an {IdeaSchema.Version} answer, so nothing was recorded.");
        }

        IdeaReply? reply;

        try
        {
            reply = JsonSerializer.Deserialize<IdeaReply>(json, Options);
        }
        catch (JsonException ex)
        {
            return OperationResult<IdeaReply>.Fail($"The agent's answer is not valid JSON: {ex.Message}");
        }

        if (reply is null)
        {
            return OperationResult<IdeaReply>.Fail("The agent's answer is empty.");
        }

        if (!string.Equals(reply.Contract, IdeaSchema.Version, StringComparison.Ordinal))
        {
            return OperationResult<IdeaReply>.Fail(
                $"The agent's answer names contract '{reply.Contract}', not {IdeaSchema.Version}, "
                + "so it was not read.");
        }

        // Nothing at all is not an answer. A round that neither asks nor
        // proposes would leave the idea where it was with the round spent, and
        // the person told it worked.
        if (reply.Questions.Count == 0 && reply.Plan is null)
        {
            return OperationResult<IdeaReply>.Fail(
                "The agent neither asked anything nor proposed a plan, so there was nothing to record.");
        }

        if (reply.Plan is { Layers.Count: 0 })
        {
            return OperationResult<IdeaReply>.Fail(
                "The agent proposed a plan with no layers in it, so it was not recorded.");
        }

        return OperationResult<IdeaReply>.Ok(reply);
    }
}
