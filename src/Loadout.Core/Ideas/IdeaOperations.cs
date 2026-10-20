using System.Text.Json;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Ideas;
using Loadout.Models.Results;

namespace Loadout.Core.Ideas;

/// <summary>One change to an idea, as a batch carries it.</summary>
/// <remarks>
/// <para>
/// The changes a person makes to an idea between rounds: <c>capture</c> drops
/// a new one in, <c>answer</c>, <c>choose</c> and <c>judge</c> say what they
/// make of what the agent asked and proposed, <c>dismiss</c> clears a failed
/// round, and <c>remove</c> forgets the idea.
/// </para>
/// <para>
/// Refining is not one of them. A round takes minutes and starts an agent, and
/// inside a batch it would hold an idea's changes open for all that time and
/// make all or nothing mean nothing.
/// </para>
/// </remarks>
/// <param name="Op">capture, answer, choose, judge, dismiss or remove.</param>
/// <param name="Project">The list the idea is on, or null for the workspace-wide one.</param>
/// <param name="Id">The idea. For capture, an id to use, or null for one made from the text.</param>
/// <param name="Text">For capture, the idea in the person's words.</param>
/// <param name="Title">For capture, a title, or null for the text's first line.</param>
/// <param name="Question">For answer, the question's id.</param>
/// <param name="Answer">For answer, the answer.</param>
/// <param name="Layer">For choose, the layer's id.</param>
/// <param name="Option">For choose, the option, whole or as its letter.</param>
/// <param name="Piece">For judge, a layer's or an addition's id, or <c>plan</c> for the whole of it.</param>
/// <param name="Verdict">For judge, keep, drop or improve.</param>
/// <param name="Request">For judge, what to improve.</param>
/// <param name="By">For capture, who is saying so.</param>
/// <param name="Revision">The revision of the idea's record this was made from, or null to not check.</param>
public sealed record IdeaOperation(
    string Op,
    string? Project = null,
    string? Id = null,
    string? Text = null,
    string? Title = null,
    string? Question = null,
    string? Answer = null,
    string? Layer = null,
    string? Option = null,
    string? Piece = null,
    IdeaVerdict? Verdict = null,
    string? Request = null,
    string By = "",
    string? Revision = null)
{
    public const string Capture = "capture";
    public const string Answering = "answer";
    public const string Choose = "choose";
    public const string Judge = "judge";
    public const string Dismiss = "dismiss";
    public const string Remove = "remove";

    /// <summary>Every op a batch takes.</summary>
    public static IReadOnlyList<string> Ops => [Capture, Answering, Choose, Judge, Dismiss, Remove];

    /// <summary>The ops that change an idea's record and nothing else, which are made together per idea.</summary>
    public static IReadOnlyList<string> Edits => [Answering, Choose, Judge, Dismiss];

}

/// <summary>What became of one operation in a batch of idea changes.</summary>
/// <param name="Index">Where it was in the batch, from zero.</param>
/// <param name="Op">What it was.</param>
/// <param name="Project">The list the idea is on, or null for the workspace-wide one.</param>
/// <param name="Id">The idea.</param>
/// <param name="Error">Why it was not applied, or null when it was.</param>
/// <param name="Code">The exit code a command making this change alone would have ended with.</param>
/// <param name="Record">The idea's record as it now stands; null when it was not applied, or was removed.</param>
/// <param name="Revision">The record's revision now, or null when there is none.</param>
/// <param name="HeldBack">Whether it was refused only because another change to the same idea was.</param>
public sealed record IdeaOutcome(
    int Index,
    string Op,
    string? Project,
    string Id,
    string? Error,
    ExitCode Code,
    IdeaRecord? Record,
    string? Revision,
    bool HeldBack = false)
{
    public bool Applied => Error is null;
}

/// <summary>What a batch of idea changes did: one outcome for every operation.</summary>
public sealed record IdeaBatch(IReadOnlyList<IdeaOutcome> Outcomes)
{
    public bool Applied => Outcomes.All(one => one.Applied);
}

/// <summary>An idea's record, with the revision a change made from it should name.</summary>
public sealed record IdeaRead(IdeaPlace Place, IdeaRecord Record, string? Revision);

/// <summary>An idea's record as it stands on disk: enough to tell, cheaply, that it changed.</summary>
public sealed record IdeaStamp(IdeaPlace Place, string Stamp);

/// <summary>
/// Reads a batch of idea changes from the JSON every surface takes it in,
/// as <see cref="TaskOperations"/> does for tasks.
/// </summary>
public static class IdeaOperations
{
    /// <summary>The longest batch taken in one go.</summary>
    public const int Most = TaskOperations.Most;

    private static readonly string[] Known =
        ["op", "project", "id", "text", "title", "question", "answer", "layer", "option", "piece", "verdict", "request", "by", "revision"];

    /// <summary>Reads a JSON array of operations.</summary>
    /// <param name="json">The array.</param>
    /// <param name="project">The list an operation naming none is on, or null for the workspace-wide one.</param>
    /// <param name="by">Who a capture naming nobody is said by.</param>
    public static OperationResult<IReadOnlyList<IdeaOperation>> Parse(string json, string? project, string by)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Refused($"That is not JSON: {ex.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Refused("A batch is a JSON array of operations, such as [{\"op\":\"answer\",\"id\":\"status-page\",\"question\":\"Q1\",\"answer\":\"Yes\"}].");
            }

            var items = document.RootElement.EnumerateArray().ToList();

            if (items.Count > Most)
            {
                return Refused($"That is {items.Count} operations, and a batch takes at most {Most}. Send it in parts.");
            }

            var operations = new List<IdeaOperation>();

            for (var index = 0; index < items.Count; index++)
            {
                var read = One(items[index], index, project, by);

                if (read.Failed)
                {
                    return Refused(read.Error!);
                }

                operations.Add(read.Value!);
            }

            return OperationResult<IReadOnlyList<IdeaOperation>>.Ok(operations);
        }
    }

    /// <summary>An outcome as every surface writes it.</summary>
    public static object Json(IdeaOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new
        {
            index = outcome.Index,
            op = outcome.Op,
            project = outcome.Project,
            id = outcome.Id,
            applied = outcome.Applied,
            heldBack = outcome.HeldBack,
            error = outcome.Error,
            code = (int)outcome.Code,
            revision = outcome.Revision,
            idea = outcome.Record is { } record ? IdeaJson.Of(new IdeaPlace(outcome.Project, outcome.Id), record) : null,
        };
    }

    /// <summary>A batch as every surface writes it.</summary>
    public static object Json(IdeaBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return new { applied = batch.Applied, results = batch.Outcomes.Select(Json) };
    }

    private static OperationResult<IdeaOperation> One(JsonElement item, int index, string? project, string by)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return OperationResult<IdeaOperation>.Fail($"Operation {index} is not an object.", ExitCode.InvalidArguments);
        }

        foreach (var property in item.EnumerateObject())
        {
            if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                return OperationResult<IdeaOperation>.Fail(
                    $"Operation {index} has '{property.Name}' as something other than text.", ExitCode.InvalidArguments);
            }

            if (!Known.Contains(property.Name))
            {
                return OperationResult<IdeaOperation>.Fail(
                    $"Operation {index} has '{property.Name}', which is not something an operation takes. "
                    + $"They are {string.Join(", ", Known)}.",
                    ExitCode.InvalidArguments);
            }
        }

        string? Text(string name) =>
            item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        bool Has(string name) => item.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

        var op = Text("op")?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!IdeaOperation.Ops.Contains(op))
        {
            return OperationResult<IdeaOperation>.Fail(
                $"Operation {index} has op '{op}'. It should be one of {string.Join(", ", IdeaOperation.Ops)}.",
                ExitCode.InvalidArguments);
        }

        IdeaVerdict? verdict = null;

        if (Text("verdict") is { Length: > 0 } said)
        {
            if (!Enum.TryParse<IdeaVerdict>(said, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed)
                || int.TryParse(said, out _) || parsed == IdeaVerdict.Undecided)
            {
                return OperationResult<IdeaOperation>.Fail(
                    $"Operation {index} has verdict '{said}'. It should be keep, drop or improve.", ExitCode.InvalidArguments);
            }

            verdict = parsed;
        }

        // What each op cannot go without, said before anything is read.
        string[] needs = op switch
        {
            IdeaOperation.Capture => ["text"],
            IdeaOperation.Answering => ["id", "question", "answer"],
            IdeaOperation.Choose => ["id", "layer", "option"],
            IdeaOperation.Judge => ["id", "piece", "verdict"],
            _ => ["id"],
        };

        if (needs.FirstOrDefault(name => !Has(name)) is { } missing)
        {
            return OperationResult<IdeaOperation>.Fail(
                $"Operation {index} is {op} without '{missing}'. It needs {string.Join(", ", needs)}.", ExitCode.InvalidArguments);
        }

        return OperationResult<IdeaOperation>.Ok(new IdeaOperation(
            op,
            Has("project") ? TaskOperations.List(Text("project")) : project,
            Text("id"),
            Text("text"),
            Text("title"),
            Text("question"),
            Text("answer"),
            Text("layer"),
            Text("option"),
            Text("piece"),
            verdict,
            Text("request"),
            Text("by") is { Length: > 0 } named ? named : by,
            Text("revision") is { Length: > 0 } revision ? revision : null));
    }

    private static OperationResult<IReadOnlyList<IdeaOperation>> Refused(string why) =>
        OperationResult<IReadOnlyList<IdeaOperation>>.Fail(why, ExitCode.InvalidArguments);
}
