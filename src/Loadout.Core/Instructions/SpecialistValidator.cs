using Loadout.Models.Agents;
using Loadout.Models.Instructions;

namespace Loadout.Core.Instructions;

/// <summary>
/// Checks a loaded specialist library for the faults that would otherwise show
/// up as an agent quietly being told the wrong thing.
/// </summary>
/// <remarks>
/// Runs on every load rather than only when asked. The cost is a pass over a
/// few dozen small records, and a library that is broken is broken at launch
/// time, not at the moment somebody remembers to validate it.
/// </remarks>
internal static class SpecialistValidator
{
    /// <summary>Everything wrong with a library, worst first.</summary>
    public static IReadOnlyList<RuleFinding> Validate(
        IReadOnlyDictionary<string, SpecialistDocument> specialists)
    {
        ArgumentNullException.ThrowIfNull(specialists);

        var findings = new List<RuleFinding>();

        foreach (var specialist in specialists.Values.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            CheckIdMatchesKind(specialist, findings);
            CheckRequires(specialist, specialists, findings);
            CheckActivation(specialist, findings);
            CheckCapabilities(specialist, findings);
            CheckAccompanies(specialist, specialists, findings);
            CheckStyle(specialist, findings);
        }

        findings.AddRange(FindCycles(specialists));

        return findings;
    }

    /// <summary>
    /// The id's first segment has to agree with the declared kind.
    /// </summary>
    /// <remarks>
    /// Both are written by hand and both are used: the kind decides where the
    /// specialist composes, the id is what somebody types. A file whose id says
    /// <c>database.postgresql</c> while its kind says <c>language</c> would
    /// compose in the wrong place and read correctly in every listing, which is
    /// the sort of fault that survives for months.
    /// </remarks>
    private static void CheckIdMatchesKind(SpecialistDocument specialist, List<RuleFinding> findings)
    {
        var dot = specialist.Id.IndexOf('.', StringComparison.Ordinal);

        if (dot <= 0)
        {
            findings.Add(new RuleFinding(
                specialist.Id, RuleFindingSeverity.Error, "specialist-id",
                $"'{specialist.Id}' is not a dotted id such as 'language.csharp'."));

            return;
        }

        var prefix = specialist.Id[..dot];
        var expected = specialist.Kind.ToString().ToLowerInvariant();

        // Skills are addressed as skill.<name>, which matches; the other kinds
        // all use their own name as the prefix too.
        if (!string.Equals(prefix, expected, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new RuleFinding(
                specialist.Id, RuleFindingSeverity.Error, "specialist-id-kind",
                $"'{specialist.Id}' declares kind '{expected}', so its id should begin "
                + $"'{expected}.' rather than '{prefix}.'."));
        }
    }

    /// <summary>Everything a specialist requires has to exist.</summary>
    private static void CheckRequires(
        SpecialistDocument specialist,
        IReadOnlyDictionary<string, SpecialistDocument> specialists,
        List<RuleFinding> findings)
    {
        foreach (var required in specialist.Activation.RequiresList)
        {
            if (!specialists.ContainsKey(required))
            {
                findings.Add(new RuleFinding(
                    specialist.Id, RuleFindingSeverity.Error, "specialist-missing-requirement",
                    $"'{specialist.Id}' requires '{required}', which is not in the library."));
            }
            else if (string.Equals(required, specialist.Id, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new RuleFinding(
                    specialist.Id, RuleFindingSeverity.Error, "specialist-self-requirement",
                    $"'{specialist.Id}' requires itself."));
            }
        }
    }

    /// <summary>
    /// A specialist nothing can ever activate is a specialist nobody will ever
    /// see.
    /// </summary>
    /// <remarks>
    /// Reported as a warning rather than an error because it is still
    /// reachable by name. But it is almost always a mistake — somebody wrote
    /// the guidance and forgot to say when it applies — and it is invisible
    /// otherwise, since nothing fails and nothing appears.
    /// </remarks>
    private static void CheckActivation(SpecialistDocument specialist, List<RuleFinding> findings)
    {
        var activation = specialist.Activation;

        if (activation.Always
            || activation.GlobList.Count > 0
            || activation.DependencyList.Count > 0
            || activation.TaskPhraseList.Count > 0
            || activation.AccompaniesList.Count > 0)
        {
            return;
        }

        // Modes are chosen rather than detected, so having no evidence is
        // correct for them. So are roles: the team that uses one names it.
        // So is a style's core, which is in force because the style is.
        if (specialist.Kind is SpecialistKind.Mode or SpecialistKind.Role
            || (specialist.Kind is SpecialistKind.Style
                && CodingStyles.PartOf(specialist.Id) == StylePart.Core))
        {
            return;
        }

        findings.Add(new RuleFinding(
            specialist.Id, RuleFindingSeverity.Warning, "specialist-unreachable",
            $"'{specialist.Id}' declares no evidence, so it will only ever load when "
            + "named explicitly."));
    }

    /// <summary>
    /// What a specialist accompanies has to exist, or it can never load.
    /// </summary>
    /// <remarks>
    /// A warning rather than an error: the library is still sound, and the
    /// usual cause is a style's file for a language this machine has no
    /// specialist for yet. But nothing else would ever say that the file is
    /// dead weight.
    /// </remarks>
    private static void CheckAccompanies(
        SpecialistDocument specialist,
        IReadOnlyDictionary<string, SpecialistDocument> specialists,
        List<RuleFinding> findings)
    {
        foreach (var leader in specialist.Activation.AccompaniesList)
        {
            if (!specialists.ContainsKey(leader))
            {
                findings.Add(new RuleFinding(
                    specialist.Id, RuleFindingSeverity.Warning, "specialist-accompanies-missing",
                    $"'{specialist.Id}' accompanies '{leader}', which is not in the library, "
                    + "so it will only load when named."));
            }
        }
    }

    /// <summary>
    /// A style's parts have to be addressable as parts, and its core small.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The id is the only thing that says which part of which style a file is,
    /// so a malformed one is an error: <c>style.work.csharp.extra</c> belongs to
    /// no part and would never load.
    /// </para>
    /// <para>
    /// The rest are warnings about a style that works but costs more than it
    /// should or loads less than its author thinks. A core is paid for on every
    /// launch and never dropped for budget. A language file with nothing to
    /// accompany is a file nothing loads. A pattern without example code is a
    /// rule wearing a pattern's name, and belongs in the core or a language
    /// file where it is cheaper to find.
    /// </para>
    /// </remarks>
    private static void CheckStyle(SpecialistDocument specialist, List<RuleFinding> findings)
    {
        if (specialist.Kind != SpecialistKind.Style)
        {
            return;
        }

        switch (CodingStyles.PartOf(specialist.Id))
        {
            case null:
                findings.Add(new RuleFinding(
                    specialist.Id, RuleFindingSeverity.Error, "style-id",
                    $"'{specialist.Id}' is not a part of a style. Use 'style.<name>' for the core, "
                    + "'style.<name>.<language>' for a language file, or "
                    + "'style.<name>.pattern.<pattern>' for a pattern."));
                break;

            case StylePart.Core:
                if (specialist.EstimatedTokens > CodingStyles.CoreTokenCeiling)
                {
                    findings.Add(new RuleFinding(
                        specialist.Id, RuleFindingSeverity.Warning, "style-core-too-large",
                        $"'{specialist.Id}' is about {specialist.EstimatedTokens:N0} tokens, over the "
                        + $"{CodingStyles.CoreTokenCeiling:N0} a core should cost. It loads on every "
                        + "launch; move what applies to one language into a language file, and "
                        + "examples into patterns."));
                }

                break;

            case StylePart.Language:
                if (specialist.Activation.AccompaniesList.Count == 0)
                {
                    findings.Add(new RuleFinding(
                        specialist.Id, RuleFindingSeverity.Warning, "style-language-unattached",
                        $"'{specialist.Id}' is a language file that accompanies nothing. Add "
                        + "'accompanies: [language.<name>]' so it loads with its language."));
                }

                break;

            case StylePart.Pattern:
                if (!specialist.Body.Contains("```", StringComparison.Ordinal))
                {
                    findings.Add(new RuleFinding(
                        specialist.Id, RuleFindingSeverity.Warning, "style-pattern-example",
                        $"'{specialist.Id}' is a pattern with no example code. A pattern is a rule "
                        + "plus what following it looks like; without the example it is a rule, "
                        + "and belongs in the core or a language file."));
                }

                break;
        }

        if (string.Equals(CodingStyles.NameOf(specialist.Id), CodingStyles.Codebase, StringComparison.Ordinal)
            && specialist.Origin != SpecialistOrigin.Project)
        {
            findings.Add(new RuleFinding(
                specialist.Id, RuleFindingSeverity.Warning, "style-codebase-scope",
                $"'{specialist.Id}' is a codebase style outside a project, so it applies to every "
                + "project. Move it under the project it describes, or make it part of your "
                + "personal style."));
        }
    }

    /// <summary>Capability requirements have to name capabilities that exist.</summary>
    private static void CheckCapabilities(SpecialistDocument specialist, List<RuleFinding> findings)
    {
        foreach (var capability in specialist.Activation.CapabilityList)
        {
            if (!KnownCapabilities.Contains(capability, StringComparer.Ordinal))
            {
                findings.Add(new RuleFinding(
                    specialist.Id, RuleFindingSeverity.Warning, "specialist-unknown-capability",
                    $"'{specialist.Id}' requires capability '{capability}', which no adapter "
                    + "reports. It will never be loaded."));
            }
        }
    }

    /// <summary>
    /// The capability keys adapters actually probe for.
    /// </summary>
    /// <remarks>
    /// Read from the shared constants rather than listed again here, so a
    /// capability added to an adapter is immediately spellable in a specialist
    /// without this file needing to hear about it.
    /// </remarks>
    private static readonly IReadOnlyList<string> KnownCapabilities =
        typeof(AgentCapabilities)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

    /// <summary>
    /// Finds requirement cycles.
    /// </summary>
    /// <remarks>
    /// A cycle would make composition non-terminating, so this has to be an
    /// error rather than a warning. Depth-first with a colour marking, which
    /// reports the specialists involved rather than merely that a cycle exists:
    /// "there is a cycle somewhere in fifty-two files" is not actionable.
    /// </remarks>
    private static IEnumerable<RuleFinding> FindCycles(
        IReadOnlyDictionary<string, SpecialistDocument> specialists)
    {
        var findings = new List<RuleFinding>();
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        const int Visiting = 1;
        const int Done = 2;

        foreach (var id in specialists.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            Walk(id, []);
        }

        return findings;

        void Walk(string id, List<string> trail)
        {
            if (state.TryGetValue(id, out var seen))
            {
                if (seen == Visiting)
                {
                    var start = trail.FindIndex(t =>
                        string.Equals(t, id, StringComparison.OrdinalIgnoreCase));

                    var cycle = start >= 0 ? trail[start..] : trail;
                    var members = string.Join(" -> ", cycle.Append(id));

                    // One finding per cycle, not one per way of entering it.
                    if (reported.Add(string.Join("|", cycle.Order(StringComparer.Ordinal))))
                    {
                        findings.Add(new RuleFinding(
                            id, RuleFindingSeverity.Error, "specialist-cycle",
                            $"Specialists require each other in a loop: {members}."));
                    }
                }

                return;
            }

            if (!specialists.TryGetValue(id, out var specialist))
            {
                // Reported already by the requirement check.
                return;
            }

            state[id] = Visiting;
            trail.Add(id);

            foreach (var required in specialist.Activation.RequiresList)
            {
                Walk(required, trail);
            }

            trail.RemoveAt(trail.Count - 1);
            state[id] = Done;
        }
    }
}
