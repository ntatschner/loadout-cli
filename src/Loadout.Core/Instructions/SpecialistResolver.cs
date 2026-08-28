using Loadout.Models.Agents;
using Loadout.Models.Instructions;

namespace Loadout.Core.Instructions;

/// <summary>Everything the resolver is allowed to consider.</summary>
/// <param name="Catalogue">The library to choose from.</param>
/// <param name="Mode">The posture asked for, or null for the default.</param>
/// <param name="Task">What the user said they were doing, or null.</param>
/// <param name="Explicit">Specialists named by the user, which are never dropped.</param>
/// <param name="Excluded">Specialists the user or profile has ruled out.</param>
/// <param name="Preferred">Specialists the project or profile expects to be relevant.</param>
/// <param name="Evidence">What the repository looks like.</param>
/// <param name="Agent">
/// The agent being launched, so a specialist requiring a capability it lacks is
/// left out with a reason rather than loaded into something that cannot use it.
/// </param>
/// <param name="TokenBudget">The ceiling on estimated tokens, or 0 for none.</param>
/// <param name="WarnAtPercent">Share of the budget worth mentioning.</param>
public sealed record SpecialistRequest(
    SpecialistCatalogue Catalogue,
    string? Mode = null,
    string? Task = null,
    IReadOnlyList<string>? Explicit = null,
    IReadOnlyList<string>? Excluded = null,
    IReadOnlyList<string>? Preferred = null,
    RepositoryEvidence? Evidence = null,
    AgentDescriptor? Agent = null,
    int TokenBudget = 0,
    int WarnAtPercent = 80);

/// <summary>Chooses the specialists for one task.</summary>
public interface ISpecialistResolver
{
    EffectiveInstructions Resolve(SpecialistRequest request);
}

/// <summary>
/// Works out the smallest useful expert context for a task.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic and explainable, which are the same requirement twice: the
/// same inputs give the same answer, and every part of that answer carries the
/// reason it is there. Nothing here consults a model. If semantic selection
/// earns its place later it can supply candidates through the same evidence
/// path, without any of this being replaced.
/// </para>
/// <para>
/// The governing idea is that evidence is not authority. A repository holding
/// one SQL file must not make every task a database task, and a project that
/// prefers the PostgreSQL specialist must not have it loaded while somebody
/// fixes a null reference. So repository evidence and project preference both
/// raise candidates and neither carries enough weight on its own to survive a
/// tight budget, whereas what the user actually asked for does.
/// </para>
/// </remarks>
internal sealed class SpecialistResolver : ISpecialistResolver
{
    /// <summary>The posture used when none is chosen.</summary>
    public const string DefaultMode = "implement";

    /// <summary>
    /// How many files of a kind before it says anything about the project.
    /// </summary>
    /// <remarks>
    /// One file is an accident; a handful is a fact. This is the specific guard
    /// against the failure the brief names — a stray <c>.sql</c> file turning
    /// every task into database work.
    /// </remarks>
    private const int MeaningfulFileCount = 3;

    private static readonly int[] Confidence =
    [
        /* Foundation         */ 100,
        /* Mode               */ 100,
        /* Explicit           */ 100,
        /* Required           */ 90,
        /* TaskSemantics      */ 80,
        /* Dependency         */ 60,
        /* ProjectPreference  */ 45,
        /* RepositoryEvidence */ 35,
    ];

    /// <inheritdoc />
    public EffectiveInstructions Resolve(SpecialistRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var catalogue = request.Catalogue;
        var evidence = request.Evidence ?? RepositoryEvidence.None;
        var excluded = new HashSet<string>(
            request.Excluded ?? [], StringComparer.OrdinalIgnoreCase);

        var candidates = new Dictionary<string, SpecialistSelection>(StringComparer.OrdinalIgnoreCase);
        var omitted = new List<SpecialistSelection>();

        var mode = ChooseMode(catalogue, request.Mode);

        AddFoundation(catalogue, candidates);
        AddMode(catalogue, mode, candidates);
        AddExplicit(catalogue, request, candidates);
        AddTaskMatches(catalogue, request.Task, mode, candidates);
        AddDependencyMatches(catalogue, evidence, mode, candidates);
        AddPreferences(catalogue, request, evidence, mode, candidates);
        AddRepositoryMatches(catalogue, evidence, mode, candidates);

        // Requirements last, so anything pulled in by a selected specialist is
        // itself considered — but only one level of chasing per pass, repeated
        // until nothing new appears, which keeps a shallow graph shallow.
        AddRequirements(catalogue, candidates);

        RemoveExcluded(candidates, excluded, omitted);
        RemoveUnsupported(candidates, request.Agent, omitted);

        var ordered = Order(candidates.Values);
        var conflicts = FindConflicts(ordered);

        var kept = ApplyBudget(ordered, request, omitted, out var budget);

        return new EffectiveInstructions(
            mode, kept, Order(omitted).ToList(), conflicts, budget, evidence.Truncated);
    }

    /// <summary>The mode asked for, or the default when it was not named or does not exist.</summary>
    private static string ChooseMode(SpecialistCatalogue catalogue, string? asked)
    {
        if (asked is { Length: > 0 }
            && catalogue.Find(ModeId(asked)) is not null)
        {
            return asked.Trim().ToLowerInvariant();
        }

        return DefaultMode;
    }

    private static string ModeId(string mode) => $"mode.{mode.Trim().ToLowerInvariant()}";

    /// <summary>Foundation loads whatever the task; that is what makes it foundation.</summary>
    private static void AddFoundation(
        SpecialistCatalogue catalogue,
        Dictionary<string, SpecialistSelection> candidates)
    {
        foreach (var specialist in catalogue.OfKind(SpecialistKind.Foundation)
            .Where(s => s.Activation.Always))
        {
            Offer(candidates, specialist, SpecialistTrigger.Foundation, "always applies");
        }
    }

    private static void AddMode(
        SpecialistCatalogue catalogue,
        string mode,
        Dictionary<string, SpecialistSelection> candidates)
    {
        if (catalogue.Find(ModeId(mode)) is { } document)
        {
            Offer(candidates, document, SpecialistTrigger.Mode, $"{mode} mode");
        }
    }

    /// <summary>
    /// What the user named.
    /// </summary>
    /// <remarks>
    /// An id that does not exist is not handled here. It is a mistake in what
    /// somebody typed, and the right answer is to refuse the command and say
    /// which specialists there are — which the caller does, because it has a
    /// way to fail and an exit code to fail with. Silently resolving without it
    /// would leave somebody believing they were getting guidance they were not.
    /// </remarks>
    private static void AddExplicit(
        SpecialistCatalogue catalogue,
        SpecialistRequest request,
        Dictionary<string, SpecialistSelection> candidates)
    {
        foreach (var id in request.Explicit ?? [])
        {
            if (catalogue.Find(id) is { } document)
            {
                Offer(candidates, document, SpecialistTrigger.Explicit, "selected by you");
            }
        }
    }

    /// <summary>
    /// The explicitly named specialists that do not exist, for a caller that
    /// wants to refuse rather than proceed.
    /// </summary>
    public static IReadOnlyList<string> UnknownExplicit(SpecialistRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (request.Explicit ?? [])
            .Where(id => request.Catalogue.Find(id) is null)
            .ToList();
    }

    /// <summary>
    /// What the task says. The strongest signal available, because it is the
    /// only one that reflects what somebody is actually trying to do.
    /// </summary>
    private static void AddTaskMatches(
        SpecialistCatalogue catalogue,
        string? task,
        string mode,
        Dictionary<string, SpecialistSelection> candidates)
    {
        if (string.IsNullOrWhiteSpace(task))
        {
            return;
        }

        var haystack = Normalise(task);

        foreach (var specialist in catalogue.All)
        {
            if (!AppliesToMode(specialist, mode))
            {
                continue;
            }

            var matched = specialist.Activation.TaskPhraseList
                .FirstOrDefault(phrase => haystack.Contains(
                    Normalise(phrase),
                    StringComparison.Ordinal));

            if (matched is { Length: > 0 })
            {
                Offer(
                    candidates,
                    specialist,
                    SpecialistTrigger.TaskSemantics,
                    $"task mentions \"{matched}\"");
            }
        }
    }

    /// <summary>
    /// Whether what a repository is made of is enough, by itself, to load a
    /// specialist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the single most important rule in the resolver, and the one that
    /// decides whether the feature is worth having. Languages and frameworks
    /// describe what the code <em>is</em>: knowing a repository is C# on .NET
    /// is useful for every task in it, so the files are enough.
    /// </para>
    /// <para>
    /// Everything else describes what somebody is <em>doing</em>. A repository
    /// that talks to PostgreSQL, deploys to Kubernetes and runs on Azure
    /// contains all three whatever the task, so letting the files activate them
    /// would put all three in front of somebody fixing a null reference — which
    /// is precisely the "one enormous prompt" this exists to avoid. Those kinds
    /// need the task to point at them, or the user to ask.
    /// </para>
    /// </remarks>
    private static bool IsStructural(SpecialistKind kind) =>
        kind is SpecialistKind.Language or SpecialistKind.Framework;

    /// <summary>A declared dependency is a deliberate choice, so it counts for more than a file name.</summary>
    private static void AddDependencyMatches(
        SpecialistCatalogue catalogue,
        RepositoryEvidence evidence,
        string mode,
        Dictionary<string, SpecialistSelection> candidates)
    {
        if (evidence.Dependencies.Count == 0)
        {
            return;
        }

        foreach (var specialist in catalogue.All)
        {
            if (!AppliesToMode(specialist, mode) || !IsStructural(specialist.Kind))
            {
                continue;
            }

            foreach (var token in specialist.Activation.DependencyList)
            {
                if (evidence.Dependencies.Any(line =>
                    line.Contains(token, StringComparison.OrdinalIgnoreCase)))
                {
                    Offer(
                        candidates,
                        specialist,
                        SpecialistTrigger.Dependency,
                        $"{token} dependency declared");

                    break;
                }
            }
        }
    }

    /// <summary>
    /// What the project expects to be relevant.
    /// </summary>
    /// <remarks>
    /// Preference is not instruction. A preferred specialist is offered only
    /// when something else about the task or the repository already points that
    /// way, which is the difference between "this project often needs the
    /// database specialist" and "put the database specialist in front of
    /// everybody forever".
    /// </remarks>
    private static void AddPreferences(
        SpecialistCatalogue catalogue,
        SpecialistRequest request,
        RepositoryEvidence evidence,
        string mode,
        Dictionary<string, SpecialistSelection> candidates)
    {
        foreach (var id in request.Preferred ?? [])
        {
            if (catalogue.Find(id) is not { } specialist || !AppliesToMode(specialist, mode))
            {
                continue;
            }

            // Already reached by stronger evidence. The preference does not
            // change the reason — that would replace a true explanation with a
            // vaguer one — but it does mean the project expects this specialist,
            // so it should not be the first thing dropped for budget.
            if (candidates.TryGetValue(specialist.Id, out var existing))
            {
                candidates[specialist.Id] = existing with
                {
                    Confidence = Math.Min(99, existing.Confidence + 10),
                };

                continue;
            }

            // A preference activates on its own only for the kinds that
            // describe what the code is. Preferring the PostgreSQL specialist
            // says this project often needs it, not that everybody fixing a
            // null reference should be handed it.
            if (IsStructural(specialist.Kind) && HasRepositorySupport(specialist, evidence))
            {
                Offer(
                    candidates,
                    specialist,
                    SpecialistTrigger.ProjectPreference,
                    "preferred by the project, and the repository supports it");
            }
        }
    }

    /// <summary>
    /// What the files say. The weakest signal, and the one most likely to be
    /// wrong about what somebody is doing right now.
    /// </summary>
    private static void AddRepositoryMatches(
        SpecialistCatalogue catalogue,
        RepositoryEvidence evidence,
        string mode,
        Dictionary<string, SpecialistSelection> candidates)
    {
        if (evidence.Paths.Count == 0)
        {
            return;
        }

        foreach (var specialist in catalogue.All)
        {
            if (!AppliesToMode(specialist, mode)
                || candidates.ContainsKey(specialist.Id)
                || !IsStructural(specialist.Kind))
            {
                continue;
            }

            // Languages have to clear a count, because one file of a kind says
            // nothing about what a repository is written in. A framework is
            // matched on presence, since a framework's own files are not
            // scattered about by accident.
            if (specialist.Kind == SpecialistKind.Language)
            {
                var (extension, count) = BestExtension(specialist, evidence);

                if (count >= MeaningfulFileCount)
                {
                    Offer(
                        candidates,
                        specialist,
                        SpecialistTrigger.RepositoryEvidence,
                        $"{count} {extension} files");
                }

                continue;
            }

            var hit = specialist.Activation.GlobList
                .FirstOrDefault(glob => evidence.Paths.Any(path => RuleService.Matches(glob, path)));

            if (hit is { Length: > 0 })
            {
                Offer(
                    candidates,
                    specialist,
                    SpecialistTrigger.RepositoryEvidence,
                    $"repository contains {hit}");
            }
        }
    }

    /// <summary>The extension this specialist claims that the repository has most of.</summary>
    private static (string Extension, int Count) BestExtension(
        SpecialistDocument specialist,
        RepositoryEvidence evidence)
    {
        var best = (Extension: string.Empty, Count: 0);

        foreach (var glob in specialist.Activation.GlobList)
        {
            var star = glob.LastIndexOf('*');

            if (star < 0 || star + 1 >= glob.Length)
            {
                continue;
            }

            var extension = glob[(star + 1)..];

            if (!extension.StartsWith('.'))
            {
                continue;
            }

            var count = evidence.Count(extension);

            if (count > best.Count)
            {
                best = (extension, count);
            }
        }

        return best;
    }

    /// <summary>Whether the repository says anything at all in a specialist's favour.</summary>
    private static bool HasRepositorySupport(
        SpecialistDocument specialist,
        RepositoryEvidence evidence)
    {
        if (specialist.Activation.DependencyList.Any(token =>
            evidence.Dependencies.Any(line => line.Contains(token, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        return specialist.Activation.GlobList.Any(glob =>
            evidence.Paths.Any(path => RuleService.Matches(glob, path)));
    }

    /// <summary>
    /// Pulls in what selected specialists say they need, repeatedly until
    /// nothing new arrives.
    /// </summary>
    /// <remarks>
    /// A framework specialist deliberately does not repeat its language's
    /// guidance, so loading ASP.NET Core without C# would leave a gap the user
    /// cannot see. The loop terminates because the library is validated to have
    /// no requirement cycles.
    /// </remarks>
    private static void AddRequirements(
        SpecialistCatalogue catalogue,
        Dictionary<string, SpecialistSelection> candidates)
    {
        bool added;

        do
        {
            added = false;

            foreach (var selection in candidates.Values.ToList())
            {
                foreach (var required in selection.Specialist.Activation.RequiresList)
                {
                    if (candidates.ContainsKey(required)
                        || catalogue.Find(required) is not { } document)
                    {
                        continue;
                    }

                    candidates[document.Id] = new SpecialistSelection(
                        document,
                        SpecialistTrigger.Required,
                        $"required by {selection.Specialist.Id}",
                        Confidence[(int)SpecialistTrigger.Required]);

                    added = true;
                }
            }
        }
        while (added);
    }

    /// <summary>
    /// Removes what was ruled out.
    /// </summary>
    /// <remarks>
    /// Exclusion beats every kind of inference, including a requirement: if
    /// somebody has said they do not want the AWS specialist, something else
    /// wanting it is not a reason to overrule them. It does not beat an
    /// explicit selection, because asking for a thing and excluding it in the
    /// same breath is a contradiction worth surfacing rather than silently
    /// deciding.
    /// </remarks>
    private static void RemoveExcluded(
        Dictionary<string, SpecialistSelection> candidates,
        HashSet<string> excluded,
        List<SpecialistSelection> omitted)
    {
        foreach (var id in excluded)
        {
            if (!candidates.TryGetValue(id, out var selection))
            {
                continue;
            }

            if (selection.Trigger is SpecialistTrigger.Explicit)
            {
                continue;
            }

            candidates.Remove(id);

            omitted.Add(selection with { Reason = $"{selection.Reason}, but excluded" });
        }
    }

    /// <summary>Leaves out anything the chosen agent could not act on.</summary>
    private static void RemoveUnsupported(
        Dictionary<string, SpecialistSelection> candidates,
        AgentDescriptor? agent,
        List<SpecialistSelection> omitted)
    {
        if (agent is null)
        {
            return;
        }

        foreach (var selection in candidates.Values.ToList())
        {
            var missing = selection.Specialist.Activation.CapabilityList
                .FirstOrDefault(capability => !agent.Supports(capability));

            if (missing is { Length: > 0 })
            {
                candidates.Remove(selection.Specialist.Id);

                omitted.Add(selection with
                {
                    Reason = $"{selection.Reason}, but {agent.DisplayName} does not support {missing}",
                });
            }
        }
    }

    /// <summary>
    /// Puts selections into composition order: by layer, then by id.
    /// </summary>
    /// <remarks>
    /// Sorted rather than left in discovery order so that the same inputs
    /// always compose the same file. An agent handed the same guidance in a
    /// different order every launch would make every compiled context look
    /// changed, and nothing that changes every time gets read twice.
    /// </remarks>
    private static IEnumerable<SpecialistSelection> Order(IEnumerable<SpecialistSelection> selections) =>
        selections
            .OrderBy(s => (int)s.Specialist.Kind)
            .ThenBy(s => s.Specialist.Id, StringComparer.Ordinal);

    /// <summary>
    /// Finds specialists that claim the same subject.
    /// </summary>
    /// <remarks>
    /// Overlap is expected and mostly harmless: C# and .NET will both mention
    /// async, and saying a thing twice is a small waste rather than a fault.
    /// What matters is a narrower specialist deliberately contradicting a wider
    /// one, which is declared rather than guessed — a specialist says what it
    /// overrides, and this reports that it happened so the compiled context can
    /// show it.
    /// </remarks>
    private static IReadOnlyList<InstructionConflict> FindConflicts(
        IEnumerable<SpecialistSelection> selections)
    {
        var conflicts = new List<InstructionConflict>();
        var byId = selections.ToDictionary(s => s.Specialist.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var selection in byId.Values.OrderBy(s => s.Specialist.Id, StringComparer.Ordinal))
        {
            foreach (var required in selection.Specialist.Activation.RequiresList)
            {
                if (!byId.TryGetValue(required, out var wider))
                {
                    continue;
                }

                // The narrower specialist composes later, so where the two
                // disagree the narrower one is what the agent reads last.
                if (selection.Specialist.Kind > wider.Specialist.Kind)
                {
                    conflicts.Add(new InstructionConflict(
                        wider.Specialist.Title,
                        selection.Specialist.Id,
                        wider.Specialist.Id,
                        "narrower scope composes last"));
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Cuts the candidate set down to what fits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whole specialists are dropped, never parts of them. Half a specialist is
    /// worse than none: it reads as complete guidance while missing the caveat
    /// that made it safe, and nothing on the page says so.
    /// </para>
    /// <para>
    /// What goes first is the weakest evidence — a specialist inferred from
    /// file extensions before one the task asked for. Foundation, mode and
    /// anything named explicitly are never dropped, so the result of asking for
    /// something is always either getting it or being told why not.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<SpecialistSelection> ApplyBudget(
        IEnumerable<SpecialistSelection> ordered,
        SpecialistRequest request,
        List<SpecialistSelection> omitted,
        out InstructionContextBudget budget)
    {
        var kept = ordered.ToList();

        if (request.TokenBudget > 0)
        {
            // Weakest evidence first, and within a tier the largest first, so
            // one bulky low-confidence specialist goes before three small ones.
            var negotiable = kept
                .Where(s => s.IsNegotiable)
                .OrderByDescending(s => s.Confidence)
                .ThenBy(s => s.Specialist.Bytes)
                .ToList();

            while (Tokens(kept) > request.TokenBudget && negotiable.Count > 0)
            {
                var drop = negotiable[^1];

                negotiable.RemoveAt(negotiable.Count - 1);
                kept.Remove(drop);

                omitted.Add(drop with
                {
                    Reason = $"{drop.Reason}, but dropped to stay inside the context budget",
                });
            }
        }

        budget = new InstructionContextBudget(
            kept.Sum(s => s.Specialist.Bytes),
            Tokens(kept),
            request.TokenBudget,
            request.WarnAtPercent);

        return kept;
    }

    private static int Tokens(IEnumerable<SpecialistSelection> selections) =>
        selections.Sum(s => s.Specialist.EstimatedTokens);

    /// <summary>
    /// Lowercases and pads a string so phrases match whole words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A raw substring test is wrong here in a way that is easy to miss: the
    /// API specialist declares the phrase "api", and "api" appears inside
    /// "capital", "rapid" and "therapist". Every task containing one of those
    /// words would have loaded the API specialist, with a reason claiming the
    /// task had asked for it.
    /// </para>
    /// <para>
    /// Punctuation that carries meaning inside a token is kept, because
    /// dropping it would flatten <c>c#</c> to <c>c</c> and <c>.net</c> to
    /// <c>net</c> — turning two precise phrases into two very loose ones.
    /// Everything else becomes a space, and the whole is padded, so a phrase
    /// only matches on word boundaries at both ends.
    /// </para>
    /// </remarks>
    internal static string Normalise(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length + 2);

        builder.Append(' ');

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));

                continue;
            }

            var after = i + 1 < text.Length ? text[i + 1] : ' ';
            var before = builder[^1];

            // Punctuation is kept only where it is genuinely inside a token.
            // A hash or plus closes one, as in "c#" and "c++"; a dot, dash or
            // underscore has to have a word on both sides, as in "ef-core".
            // Keeping a trailing dot unconditionally was the mistake here: it
            // made "exception." a different word from "exception", so a task
            // ending in one matched nothing at all.
            var inside = c switch
            {
                '#' or '+' => char.IsLetterOrDigit(before),
                '.' or '-' or '_' => char.IsLetterOrDigit(before) && char.IsLetterOrDigit(after),
                _ => false,
            };

            if (inside)
            {
                builder.Append(c);
            }
            else if (before != ' ')
            {
                builder.Append(' ');
            }
        }

        if (builder[^1] != ' ')
        {
            builder.Append(' ');
        }

        return builder.ToString();
    }

    private static bool AppliesToMode(SpecialistDocument specialist, string mode)
    {
        var modes = specialist.Activation.ModeList;

        return modes.Count == 0 || modes.Contains(mode, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Records a candidate, keeping the strongest reason when several apply.
    /// </summary>
    /// <remarks>
    /// A specialist reached by both the task and the file listing was reached
    /// because of the task, and that is what should be reported. Showing the
    /// weaker reason would make the explanation misleading in exactly the case
    /// where somebody is checking whether the resolver understood them.
    /// </remarks>
    private static void Offer(
        Dictionary<string, SpecialistSelection> candidates,
        SpecialistDocument specialist,
        SpecialistTrigger trigger,
        string reason)
    {
        if (candidates.TryGetValue(specialist.Id, out var existing) && existing.Trigger <= trigger)
        {
            return;
        }

        candidates[specialist.Id] = new SpecialistSelection(
            specialist,
            trigger,
            reason,
            Confidence[(int)trigger]);
    }
}
