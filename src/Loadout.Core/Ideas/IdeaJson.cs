using Loadout.Models.Ideas;

namespace Loadout.Core.Ideas;

/// <summary>An idea as JSON, the one shape every surface writes it in.</summary>
/// <remarks>
/// Here rather than beside the commands, so the daemon's routes, the agents'
/// tools and every idea command write the same thing, and a field added here
/// reaches all of them.
/// </remarks>
public static class IdeaJson
{
    public static string Stage(IdeaStage stage) => stage switch
    {
        IdeaStage.Captured => "captured",
        IdeaStage.Answering => "answering",
        IdeaStage.Ready => "ready",
        IdeaStage.Proposed => "proposed",
        _ => "accepted",
    };

    /// <summary>A failed round, as JSON, or null when there is none.</summary>
    public static object? Failure(IdeaAttempt? failure) => failure is null ? null : new
    {
        when = failure.Utc == default ? (DateTimeOffset?)null : failure.Utc,
        agent = failure.Agent.Length > 0 ? failure.Agent : null,
        model = failure.Model.Length > 0 ? failure.Model : null,
        error = failure.Error,
    };

    public static object Of(IdeaPlace place, IdeaRecord record)
    {
        var stage = IdeaWork.StageOf(record);
        var failure = IdeaWork.FailureOf(record);

        return new
        {
            id = place.Id,
            project = place.Project,
            stage = Stage(stage),
            ask = record.Ask,
            captured = record.CapturedUtc,
            error = failure?.Error,
            failure = Failure(failure),
            lastRound = record.LastRound is not { } last ? null : new
            {
                when = last.Utc,
                agent = last.Agent.Length > 0 ? last.Agent : null,
                model = last.Model.Length > 0 ? last.Model : null,
            },
            questions = record.Rounds.SelectMany(round => round.Questions).Select(q => new
            {
                id = q.Id,
                question = q.Question,
                why = q.Why,
                options = q.Options,
                recommendation = q.Recommendation,
                answer = q.Answer.Length > 0 ? q.Answer : null,
            }),
            notes = record.Notes.Select(note => new
            {
                id = note.Id,
                text = note.Text,
                by = note.By,
                added = note.AddedUtc,
                taken = note.TakenUtc,
            }),
            request = record.Request.Length > 0 ? record.Request : null,
            plan = record.Plan is not { } plan ? null : new
            {
                revision = plan.Revision,
                proposed = plan.ProposedUtc,
                title = plan.Title,
                understanding = plan.Understanding,
                layers = plan.Layers.Select(layer => new
                {
                    id = layer.Id,
                    name = layer.Name,
                    purpose = layer.Purpose,
                    chosen = layer.Chosen,
                    verdict = layer.Verdict.ToString().ToLowerInvariant(),
                    request = layer.Request.Length > 0 ? layer.Request : null,
                    options = layer.Options.Select(option => new
                    {
                        id = option.Id,
                        title = option.Title,
                        detail = option.Detail,
                        pros = option.Pros,
                        cons = option.Cons,
                        recommended = option.Recommended,
                    }),
                }),
                additions = plan.Additions.Select(addition => new
                {
                    id = addition.Id,
                    title = addition.Title,
                    why = addition.Why,
                    verdict = addition.Verdict.ToString().ToLowerInvariant(),
                    request = addition.Request.Length > 0 ? addition.Request : null,
                }),
                placement = new
                {
                    slug = plan.Project.Slug.Length > 0 ? plan.Project.Slug : null,
                    isNew = plan.Project.IsNew,
                    name = plan.Project.Name.Length > 0 ? plan.Project.Name : null,
                    reason = plan.Project.Reason,
                },
            },
            accepted = record.Accepted is not { } accepted ? null : new
            {
                project = accepted.Project,
                plan = accepted.PlanPath,
                by = accepted.AcceptedBy,
                when = accepted.AcceptedUtc,
            },
        };
    }
}
