using System.Globalization;
using Loadout.Agents.Ideas;
using Loadout.Core.Instructions;
using Loadout.Core.Voice;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Voice;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Voice;

/// <summary>What the person said to a cap question.</summary>
public enum VoiceCapChoice
{
    /// <summary>Use the estimate plus the margin.</summary>
    Accept,

    /// <summary>No. On a first or larger estimate nothing is spent; on a lower one the stored cap stays.</summary>
    Decline,

    /// <summary>Use the figure they typed.</summary>
    Typed,
}

/// <summary>An answer to a cap question.</summary>
public sealed record VoiceCapAnswer(VoiceCapChoice Choice, decimal? Usd = null);

/// <summary>What the person decided about one proposal or question.</summary>
public enum VoiceReviewChoice
{
    /// <summary>Keep it as worded.</summary>
    Accept,

    /// <summary>Keep it in their own words.</summary>
    Edit,

    /// <summary>Turn it down.</summary>
    Reject,

    /// <summary>Not now: carry it to the next run.</summary>
    Skip,
}

/// <summary>A decision about one proposal.</summary>
public sealed record VoiceReviewAnswer(VoiceReviewChoice Choice, string? Statement = null);

/// <summary>One proposal or question as it is put to the person.</summary>
/// <param name="Rule">The rule, with its citations.</param>
/// <param name="Asked">True when it is short of the evidence threshold, so it is a question rather than a proposal.</param>
/// <param name="Evidence">How much of its evidence counts.</param>
/// <param name="Replaces">The accepted rule it would change, or null when it is new.</param>
/// <param name="Number">Its place in the review, counting from one.</param>
/// <param name="Of">How many there are to review.</param>
public sealed record VoiceReviewItem(
    VoiceRule Rule,
    bool Asked,
    VoiceEvidenceCount Evidence,
    VoiceRule? Replaces,
    int Number,
    int Of);

/// <summary>
/// Everything a learn run asks the person, so the run can be driven by a
/// terminal or by a script.
/// </summary>
public interface IVoiceLearnPrompts
{
    /// <summary>Says what is happening, for somebody waiting.</summary>
    void Progress(string message);

    /// <summary>Asks about the cap before anything is spent.</summary>
    VoiceCapAnswer Cap(VoiceCapQuestion question, VoiceEstimate estimate, VoiceCap proposed, VoiceCap? stored);

    /// <summary>The cap is reached with work left: carry on past it?</summary>
    bool PastCap(VoiceCap cap, int calls, decimal spent, int remaining);

    /// <summary>One proposal or question.</summary>
    VoiceReviewAnswer Review(VoiceReviewItem item);
}

/// <summary>What to learn, for which profile.</summary>
/// <param name="Guide">The guide.</param>
/// <param name="Layer">Which layer's profile to write.</param>
/// <param name="Name">The style's name: <c>personal</c>, a named style, or <c>codebase</c>.</param>
/// <param name="Slug">The project, for a codebase profile.</param>
/// <param name="Repository">Where to find marked writing and squash merges, or null.</param>
/// <param name="Corpus">A directory of writing, for a named style, or null.</param>
/// <param name="Prompts">How to ask the person.</param>
/// <param name="PullRequests">Read merged pull requests through gh, for edit pairs to score against.</param>
/// <param name="AgentName">The agent to ask, or null for the default.</param>
/// <param name="Model">The model to ask for, or null.</param>
/// <param name="DryRun">Plan only: ask nothing, start nothing, write nothing.</param>
/// <param name="Transcripts">Read what the person typed to Claude Code, for a guide that lists them.</param>
public sealed record VoiceLearnRequest(
    VoiceGuide Guide,
    StyleLayer Layer,
    string Name,
    string? Slug,
    string? Repository,
    string? Corpus,
    IVoiceLearnPrompts Prompts,
    bool PullRequests = false,
    string? AgentName = null,
    string? Model = null,
    bool DryRun = false,
    bool Transcripts = false);

/// <summary>What a learn run came to.</summary>
/// <param name="Run">The run as recorded, or as it would be on a dry run.</param>
/// <param name="ProfilePath">Where the profile is.</param>
/// <param name="Gathered">The writing it found.</param>
/// <param name="HeldOut">Pieces of it held back from mining, to score against.</param>
/// <param name="Estimate">What mining and scoring were expected to cost.</param>
/// <param name="Dropped">Carried proposals dropped because their evidence no longer counts, by id.</param>
/// <param name="Weakened">Accepted rules whose counted evidence is now short of the threshold, by id.</param>
/// <param name="Failures">Mining and scoring rounds that came back unusable, with why.</param>
public sealed record VoiceLearnOutcome(
    VoiceRun Run,
    string ProfilePath,
    VoiceGathering Gathered,
    int HeldOut,
    VoiceEstimate Estimate,
    IReadOnlyList<string> Dropped,
    IReadOnlyList<string> Weakened,
    IReadOnlyList<string> Failures);

/// <summary>Runs a guide: gathers, mines, asks, records.</summary>
public interface IVoiceLearner
{
    /// <summary>One learn run.</summary>
    Task<OperationResult<VoiceLearnOutcome>> LearnAsync(VoiceLearnRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// The order is the spec's, and each step is there for a reason somebody
/// agreed to. The cap is settled before anything is spent, so a refusal costs
/// nothing. Proposals carried from an earlier run are checked against the
/// writing as it is now, and dropped by name when their evidence has gone.
/// Mining stops at the cap and asks; a no ends the run as partial and carries
/// what it mined to the next one rather than paying for it twice.
/// </para>
/// <para>
/// A rule with enough evidence is proposed; one without is asked about, which
/// is the interview. Either way the person decides, rule by rule, and only
/// what they accept reaches the profile.
/// </para>
/// <para>
/// A share of the writing is never mined, and a complete run scores the
/// profile against it and against any edit pairs. Scoring may ask a model
/// twice, once to rewrite held-out writing so it breaks a rule and once to
/// judge the rules no checker can read, and both calls are in the estimate
/// and under the cap. A partial or review-only run is recorded with no score,
/// and that is shown rather than hidden.
/// </para>
/// </remarks>
internal sealed class VoiceLearner : IVoiceLearner
{
    private readonly VoiceStore _store;
    private readonly VoiceEvidenceGatherer _gatherer;
    private readonly IDetachedLauncher _launcher;
    private readonly IPlatformPaths _paths;
    private readonly TimeProvider _time;

    public VoiceLearner(
        VoiceStore store,
        VoiceEvidenceGatherer gatherer,
        IDetachedLauncher launcher,
        IPlatformPaths paths,
        TimeProvider time)
    {
        _store = store;
        _gatherer = gatherer;
        _launcher = launcher;
        _paths = paths;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<OperationResult<VoiceLearnOutcome>> LearnAsync(VoiceLearnRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var guide = request.Guide;
        var prompts = request.Prompts;

        if (_store.PathOf(request.Layer, request.Name, guide.Id, request.Slug) is not { } profilePath)
        {
            return OperationResult<VoiceLearnOutcome>.Fail(
                "That profile is kept in the workspace, and there is no workspace on this machine.",
                ExitCode.InvalidArguments);
        }

        var statePath = _store.StatePath(request.Layer, request.Name, guide.Id, request.Slug);

        var profileRead = await VoiceStore.ReadProfileAsync(profilePath, ct).ConfigureAwait(false);
        var stateRead = await VoiceStore.ReadStateAsync(statePath, ct).ConfigureAwait(false);

        if (profileRead.Failed)
        {
            return OperationResult<VoiceLearnOutcome>.Fail(profileRead.Error!, profileRead.ExitCode);
        }

        if (stateRead.Failed)
        {
            return OperationResult<VoiceLearnOutcome>.Fail(stateRead.Error!, stateRead.ExitCode);
        }

        var profile = profileRead.Value ?? new VoiceProfile { Guide = guide.Reference };
        var state = stateRead.Value!;
        var now = _time.GetUtcNow();
        var runId = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        var gathered = await _gatherer
            .GatherAsync(
                new VoiceGatherRequest(request.Repository, request.Corpus, guide.Sources, request.PullRequests, request.Transcripts),
                ct)
            .ConfigureAwait(false);

        if (gathered.Failed)
        {
            return OperationResult<VoiceLearnOutcome>.Fail(gathered.Error!, gathered.ExitCode);
        }

        var (evidence, heldOut) = VoiceScoring.HoldOut(gathered.Value!.Evidence, guide);
        var holding = VoicePlanning.Holding(evidence);

        // Carried proposals first, re-checked against the writing as it is now.
        var dropped = new List<string>();
        var carried = new List<VoiceProposal>();

        foreach (var proposal in state.Carried)
        {
            var count = VoicePlanning.Count(proposal.Rule, guide, holding);

            if (proposal.Asked ? count.Citations == 0 : !count.Meets)
            {
                dropped.Add(proposal.Rule.Id);
            }
            else
            {
                carried.Add(proposal);
            }
        }

        var weakened = profile.Rules
            .Where(r => r.Citations.Any(c => c.Hash is not null))
            .Where(r => !VoicePlanning.Count(r, guide, holding).Meets)
            .Select(r => r.Id)
            .ToList();

        var batches = VoicePlanning.Batches(evidence, guide);
        var estimate = VoicePlanning.Estimate(
            batches.Count + VoiceScoring.Calls(heldOut.Count, gathered.Value.EditPairs.Count), state, guide);

        var run = new VoiceRun { Id = runId, Guide = guide.Reference, Evidence = evidence.Count, Cap = VoiceCapSetting.Kept };

        if (request.DryRun)
        {
            run.Proposed = carried.Count;
            run.Carried = carried.Count;

            return OperationResult<VoiceLearnOutcome>.Ok(
                new VoiceLearnOutcome(run, profilePath, gathered.Value, heldOut.Count, estimate, dropped, weakened, []));
        }

        // The cap, settled before anything is spent.
        var cap = state.Cap;
        var mine = batches.Count > 0;

        if (mine)
        {
            var question = VoicePlanning.Question(cap, estimate, guide);

            if (question != VoiceCapQuestion.None)
            {
                var proposed = VoicePlanning.Proposed(estimate, guide, now);
                var answer = prompts.Cap(question, estimate, proposed, cap);

                switch (answer.Choice)
                {
                    case VoiceCapChoice.Accept:
                        cap = proposed;
                        run.Cap = VoiceCapSetting.Accepted;
                        break;

                    case VoiceCapChoice.Typed when answer.Usd is > 0:
                        cap = VoicePlanning.Typed(answer.Usd.Value, estimate, now);
                        run.Cap = VoiceCapSetting.Typed;
                        break;

                    case VoiceCapChoice.Decline when question == VoiceCapQuestion.Lower:
                        run.Cap = VoiceCapSetting.Kept;
                        break;

                    default:
                        mine = false;
                        run.Cap = VoiceCapSetting.Declined;
                        break;
                }
            }
        }

        // Mining, a batch at a time, stopping at the cap to ask.
        var mined = new List<VoiceRule>();
        var failures = new List<string>();
        var stopped = false;
        var past = false;
        var known = profile.Rules;
        var directory = Path.Combine(_paths.Paths.State, "voice", "rounds", runId);

        if (mine)
        {
            Directory.CreateDirectory(directory);

            for (var index = 0; index < batches.Count; index++)
            {
                if (!past && VoicePlanning.Reached(cap!, run.Calls, run.CostUsd))
                {
                    if (!prompts.PastCap(cap!, run.Calls, run.CostUsd, batches.Count - index))
                    {
                        stopped = true;

                        break;
                    }

                    past = true;
                }

                var batch = batches[index];

                prompts.Progress(
                    $"Reading {batch.Count} piece(s) of your writing, round {index + 1} of {batches.Count}.");

                var asked = await SchemaRound.AskAsync(
                    _launcher,
                    new DetachedLaunchRequest(directory, $"Learning your {guide.Id} voice", request.AgentName, request.Model),
                    [],
                    VoiceMining.Schema,
                    VoiceMining.Version,
                    VoiceMining.Prompt(guide, batch, known),
                    json => VoiceMining.Read(json, guide, batch),
                    null,
                    ct).ConfigureAwait(false);

                if (asked.Failed)
                {
                    failures.Add(asked.Error!);

                    continue;
                }

                run.Calls++;
                run.CostUsd += asked.Value!.CostUsd;

                if (asked.Value.Answer.Failed)
                {
                    failures.Add(asked.Value.Answer.Error!);

                    continue;
                }

                mined.AddRange(asked.Value.Answer.Value!);
            }
        }

        // What is put to the person: carried proposals, then what was mined,
        // less what they already turned down in the same words. A rule mined
        // exactly as accepted only adds its citations to the accepted one.
        var fresh = new List<VoiceProposal>();
        var changed = false;

        foreach (var rule in VoiceMining.Merge(mined))
        {
            if (state.Rejected.Any(r => string.Equals(r.Id, rule.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Statement, rule.Statement, StringComparison.Ordinal)))
            {
                continue;
            }

            if (profile.Rules.FirstOrDefault(r => string.Equals(r.Id, rule.Id, StringComparison.OrdinalIgnoreCase)) is { } same
                && string.Equals(same.Statement, rule.Statement, StringComparison.Ordinal))
            {
                foreach (var citation in rule.Citations.Where(c => !same.Citations.Any(s => s.Reference == c.Reference && s.Excerpt == c.Excerpt)))
                {
                    same.Citations.Add(citation);
                    changed = true;
                }

                continue;
            }

            var count = VoicePlanning.Count(rule, guide, holding);

            if (count.Citations == 0)
            {
                continue;
            }

            carried.RemoveAll(p => string.Equals(p.Rule.Id, rule.Id, StringComparison.OrdinalIgnoreCase));
            fresh.Add(new VoiceProposal { Rule = rule, Run = runId, Asked = !count.Meets });
        }

        // A run stopped at its cap carries what it mined rather than reviewing
        // it: the person said no to more of this run. What was carried in is
        // free to review, so it is reviewed either way.
        var review = stopped ? carried : [.. carried, .. fresh];
        var left = stopped ? fresh : [];

        for (var index = 0; index < review.Count; index++)
        {
            var proposal = review[index];
            var count = VoicePlanning.Count(proposal.Rule, guide, holding);
            var replaces = profile.Rules.FirstOrDefault(r => string.Equals(r.Id, proposal.Rule.Id, StringComparison.OrdinalIgnoreCase));

            var answer = prompts.Review(new VoiceReviewItem(proposal.Rule, proposal.Asked, count, replaces, index + 1, review.Count));

            switch (answer.Choice)
            {
                case VoiceReviewChoice.Accept:
                case VoiceReviewChoice.Edit:
                    var rule = proposal.Rule;

                    if (answer.Choice == VoiceReviewChoice.Edit && answer.Statement is { Length: > 0 } edited)
                    {
                        rule.Statement = edited.Trim();
                    }

                    if (proposal.Asked)
                    {
                        rule.Citations.Add(new VoiceCitation
                        {
                            Source = "interview",
                            Reference = runId,
                            Excerpt = $"Confirmed when asked: {rule.Statement}",
                        });
                    }

                    rule.Accepted = now;
                    profile.Rules.RemoveAll(r => string.Equals(r.Id, rule.Id, StringComparison.OrdinalIgnoreCase));
                    profile.Rules.Add(rule);
                    run.Accepted++;
                    changed = true;
                    break;

                case VoiceReviewChoice.Reject:
                    state.Rejected.RemoveAll(r => string.Equals(r.Id, proposal.Rule.Id, StringComparison.OrdinalIgnoreCase));
                    state.Rejected.Add(new VoiceRejection { Id = proposal.Rule.Id, Statement = proposal.Rule.Statement });
                    run.Rejected++;
                    break;

                default:
                    left.Add(proposal);
                    break;
            }
        }

        run.Proposed = review.Count;
        run.Carried = left.Count;
        // Nothing mined is review-only, whether because there was nothing to
        // read or because the cap was declined. A run that stopped short, at
        // its cap or because a round failed, is partial: it did not read
        // everything, so it cannot count as a re-learn.
        run.Outcome = !mine ? VoiceRunOutcome.ReviewOnly
            : stopped || failures.Count > 0 ? VoiceRunOutcome.Partial
            : VoiceRunOutcome.Complete;
        // Scored only when complete. A partial run cannot count towards
        // promotion, and a score from part of the work reads like one from all
        // of it.
        if (run.Outcome == VoiceRunOutcome.Complete)
        {
            run.Score = await ScoreAsync(
                new Scoring(request, profile.Rules, heldOut, gathered.Value.EditPairs, cap!, past, directory),
                run,
                failures,
                ct).ConfigureAwait(false);
        }

        run.Finished = _time.GetUtcNow();

        if (changed)
        {
            profile.Guide = guide.Reference;
            profile.Updated = run.Finished;

            var written = await VoiceStore.WriteProfileAsync(profilePath, profile, CancellationToken.None).ConfigureAwait(false);

            if (written.Failed)
            {
                return OperationResult<VoiceLearnOutcome>.Fail(written.Error!, written.ExitCode);
            }
        }

        state.Cap = cap;
        state.Carried = left;
        state.Runs.Add(run);

        var saved = await VoiceStore.WriteStateAsync(statePath, state, CancellationToken.None).ConfigureAwait(false);

        return saved.Failed
            ? OperationResult<VoiceLearnOutcome>.Fail(saved.Error!, saved.ExitCode)
            : OperationResult<VoiceLearnOutcome>.Ok(
                new VoiceLearnOutcome(run, profilePath, gathered.Value, heldOut.Count, estimate, dropped, weakened, failures));
    }

    /// <summary>What scoring a run needs to know.</summary>
    private sealed record Scoring(
        VoiceLearnRequest Request,
        IReadOnlyList<VoiceRule> Rules,
        IReadOnlyList<VoiceEvidence> HeldOut,
        IReadOnlyList<VoiceEditPair> Pairs,
        VoiceCap Cap,
        bool Past,
        string Directory);

    /// <summary>
    /// Scores the profile, asking for variants and verdicts where they are
    /// needed and the cap allows. A round that cannot be asked or used leaves
    /// the rules it was for unscored, with why, and the run stays complete:
    /// everything was mined and reviewed.
    /// </summary>
    private async Task<VoiceScore> ScoreAsync(Scoring scoring, VoiceRun run, List<string> failures, CancellationToken ct)
    {
        var guide = scoring.Request.Guide;
        var prompts = scoring.Request.Prompts;
        var variants = new List<VoiceVariant>();
        var requests = VoiceScoring.VariantRequests(scoring.Rules, scoring.HeldOut, scoring.Pairs, guide);

        bool Affordable() => scoring.Past || !VoicePlanning.Reached(scoring.Cap, run.Calls, run.CostUsd);

        if (requests.Count > 0 && Affordable())
        {
            prompts.Progress($"Rewriting {requests.Count} piece(s) of your writing to test the rules against.");

            var asked = await AskAsync(
                scoring,
                VoiceJudging.VariantsSchema,
                VoiceJudging.VariantsVersion,
                VoiceJudging.VariantsPrompt(requests),
                json => VoiceJudging.ReadVariants(json, requests),
                run,
                failures,
                ct).ConfigureAwait(false);

            variants.AddRange(asked ?? []);
        }

        var judged = scoring.Rules.Where(r => r.Check is null).ToList();
        VoiceJudgement? judgement = null;
        string? unjudged = null;

        if (judged.Count > 0)
        {
            var samples = VoiceScoring.JudgeSamples(judged, scoring.HeldOut, scoring.Pairs, variants, guide);

            if (samples.Count == 0)
            {
                unjudged = "no held-out sample it applies to";
            }
            else if (!Affordable())
            {
                unjudged = "the cap was reached before the judged rules could be scored";
            }
            else
            {
                prompts.Progress($"Judging {judged.Count} rule(s) against {samples.Count} sample(s).");

                var broken = await AskAsync(
                    scoring,
                    VoiceJudging.JudgeSchema,
                    VoiceJudging.JudgeVersion,
                    VoiceJudging.JudgePrompt(judged, samples),
                    json => VoiceJudging.ReadVerdicts(json, judged, samples),
                    run,
                    failures,
                    ct).ConfigureAwait(false);

                if (broken is null)
                {
                    unjudged = "the judging round could not be used";
                }
                else
                {
                    judgement = new VoiceJudgement(samples.Select(s => s.Id).ToHashSet(StringComparer.Ordinal), broken);
                }
            }
        }

        return VoiceScoring.Score(scoring.Rules, scoring.HeldOut, scoring.Pairs, guide, variants, judgement, unjudged);
    }

    /// <summary>One scoring round, counted against the run. Null when it could not be used, with why in the failures.</summary>
    private async Task<T?> AskAsync<T>(
        Scoring scoring,
        string schema,
        string contract,
        string prompt,
        Func<string?, OperationResult<T>> read,
        VoiceRun run,
        List<string> failures,
        CancellationToken ct)
        where T : class
    {
        var request = scoring.Request;

        Directory.CreateDirectory(scoring.Directory);

        var asked = await SchemaRound.AskAsync(
            _launcher,
            new DetachedLaunchRequest(scoring.Directory, $"Scoring your {request.Guide.Id} voice", request.AgentName, request.Model),
            [],
            schema,
            contract,
            prompt,
            read,
            null,
            ct).ConfigureAwait(false);

        if (asked.Failed)
        {
            failures.Add(asked.Error!);

            return null;
        }

        run.Calls++;
        run.CostUsd += asked.Value!.CostUsd;

        if (asked.Value.Answer.Failed)
        {
            failures.Add(asked.Value.Answer.Error!);

            return null;
        }

        return asked.Value.Answer.Value;
    }
}
