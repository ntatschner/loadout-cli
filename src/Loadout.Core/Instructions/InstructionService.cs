using Loadout.Models;
using Loadout.Models.Agents;
using Loadout.Models.Configuration;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Loadout.Models.Results;

namespace Loadout.Core.Instructions;

/// <summary>What a caller knows about the work being started.</summary>
/// <param name="Manifest">The project, or null when running without a workspace.</param>
/// <param name="RepositoryPath">Where the code is on this machine, or null.</param>
/// <param name="WorkspacePath">The workspace clone, or null.</param>
/// <param name="AgentName">Agent being launched.</param>
/// <param name="Agent">The agent as detected, for capability gating, or null.</param>
/// <param name="ProfileName">Profile chosen, or null for the project's own settings.</param>
/// <param name="Task">What the user said they were doing, or null.</param>
/// <param name="Explicit">Specialists named on the command line.</param>
/// <param name="Excluded">Specialists ruled out on the command line.</param>
/// <param name="Mode">Posture named on the command line.</param>
public sealed record InstructionRequest(
    ProjectManifest? Manifest = null,
    string? RepositoryPath = null,
    string? WorkspacePath = null,
    string AgentName = "claude",
    AgentDescriptor? Agent = null,
    string? ProfileName = null,
    string? Task = null,
    IReadOnlyList<string>? Explicit = null,
    IReadOnlyList<string>? Excluded = null,
    string? Mode = null);

/// <summary>Works out what an agent should be told, and why.</summary>
public interface IInstructionService
{
    /// <summary>Loads the library for a project, so it can be listed or inspected.</summary>
    Task<SpecialistCatalogue> LibraryAsync(
        string? workspacePath,
        string? slug = null,
        CancellationToken ct = default);

    /// <summary>Resolves the effective instructions for one piece of work.</summary>
    Task<OperationResult<EffectiveInstructions>> ResolveAsync(
        InstructionRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// What a repository appears to be built from, whatever the task.
    /// </summary>
    /// <remarks>
    /// A different question from what a task should load, and worth answering
    /// separately. "This project uses C#, .NET and PostgreSQL" is a stable fact
    /// about the project that belongs on a project screen; "this task needs the
    /// PostgreSQL specialist" is a judgement about one piece of work. Conflating
    /// the two is exactly how every project technology ends up in every prompt.
    /// </remarks>
    Task<IReadOnlyList<SpecialistSelection>> DetectAsync(
        string? repositoryPath,
        string? workspacePath,
        string? slug = null,
        CancellationToken ct = default);
}

/// <summary>
/// Gathers everything the resolver needs and hands back the answer.
/// </summary>
/// <remarks>
/// <para>
/// One service used by the launch path, the explain command and the launcher
/// screen alike. That is deliberate: three callers each assembling their own
/// inputs would drift, and the first symptom would be an explanation that did
/// not match what the agent was actually given — which is worse than no
/// explanation, because it would be believed.
/// </para>
/// </remarks>
public sealed class InstructionService : IInstructionService
{
    private readonly ISpecialistLibrary _library;
    private readonly ISpecialistResolver _resolver;
    private readonly IRepositoryEvidenceReader _evidence;
    private readonly Configuration.IConfigurationService _configuration;

    public InstructionService(
        ISpecialistLibrary library,
        ISpecialistResolver resolver,
        IRepositoryEvidenceReader evidence,
        Configuration.IConfigurationService configuration)
    {
        _library = library;
        _resolver = resolver;
        _evidence = evidence;
        _configuration = configuration;
    }

    /// <summary>
    /// The budget in force.
    /// </summary>
    /// <remarks>
    /// Read per resolution rather than captured at construction. Changing the
    /// setting and having it not take effect until something was restarted
    /// would be a confusing way for a ceiling to behave, and reading a small
    /// YAML file is not a cost worth optimising against that.
    /// </remarks>
    private async Task<InstructionContextSettings> SettingsAsync(CancellationToken ct)
    {
        var config = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

        // An unreadable config is reported elsewhere; here it means the default
        // ceiling rather than no ceiling, because no ceiling is the riskier
        // reading of a file nobody could parse.
        return config.Succeeded ? config.Value!.InstructionContext : new InstructionContextSettings();
    }

    /// <inheritdoc />
    public Task<SpecialistCatalogue> LibraryAsync(
        string? workspacePath,
        string? slug = null,
        CancellationToken ct = default) =>
        _library.LoadAsync(workspacePath, slug, ct);

    /// <inheritdoc />
    public async Task<OperationResult<EffectiveInstructions>> ResolveAsync(
        InstructionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var catalogue = await _library
            .LoadAsync(request.WorkspacePath, request.Manifest?.Slug, ct)
            .ConfigureAwait(false);

        var evidence = RepositoryEvidence.None;

        if (request.RepositoryPath is { Length: > 0 } path)
        {
            var read = await _evidence.ReadAsync(path, ct).ConfigureAwait(false);

            // A repository that cannot be scanned costs one signal, not the
            // launch. Everything the task and the user said still applies.
            evidence = read.Succeeded ? read.Value! : RepositoryEvidence.None;
        }

        var settings = await SettingsAsync(ct).ConfigureAwait(false);

        if (!settings.Specialists)
        {
            // Switched off. An empty set rather than a failure: the launch
            // proceeds exactly as it did before this feature existed, which is
            // the whole point of having the switch.
            return OperationResult<EffectiveInstructions>.Ok(new EffectiveInstructions(
                SpecialistResolver.DefaultMode,
                [],
                [],
                [],
                new InstructionContextBudget(0, 0, settings.MaxTokens, settings.WarnAtPercent)));
        }

        var preferences = Preferences(request);

        var specialistRequest = new SpecialistRequest(
            catalogue,
            request.Mode is { Length: > 0 } ? request.Mode : NullIfEmpty(preferences.Mode),
            request.Task,
            request.Explicit,
            Combine(request.Excluded, preferences.Excluded),
            preferences.Preferred,
            evidence,
            request.Agent,
            settings.MaxTokens,
            settings.WarnAtPercent);

        var unknown = SpecialistResolver.UnknownExplicit(specialistRequest);

        if (unknown.Count > 0)
        {
            // Refused rather than resolved around. Somebody who asked for a
            // specialist by name and did not get it must be told, not left
            // believing the session has guidance it has not.
            return OperationResult<EffectiveInstructions>.Fail(
                $"No specialist named {string.Join(", ", unknown.Select(u => $"'{u}'"))}. "
                + "Run 'loadout instructions list' to see what there is.",
                ExitCode.InvalidArguments);
        }

        return OperationResult<EffectiveInstructions>.Ok(_resolver.Resolve(specialistRequest));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SpecialistSelection>> DetectAsync(
        string? repositoryPath,
        string? workspacePath,
        string? slug = null,
        CancellationToken ct = default)
    {
        if (repositoryPath is not { Length: > 0 })
        {
            return [];
        }

        var read = await _evidence.ReadAsync(repositoryPath, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return [];
        }

        var evidence = read.Value!;
        var catalogue = await _library.LoadAsync(workspacePath, slug, ct).ConfigureAwait(false);

        var detected = new List<SpecialistSelection>();

        foreach (var specialist in catalogue.All)
        {
            // Foundation and modes are not detected; they are chosen. Skills
            // describe a procedure rather than a technology, so a repository
            // cannot evidence one.
            if (specialist.Kind is SpecialistKind.Foundation
                or SpecialistKind.Mode
                or SpecialistKind.Skill
                or SpecialistKind.Function)
            {
                continue;
            }

            var dependency = specialist.Activation.DependencyList.FirstOrDefault(token =>
                evidence.Dependencies.Any(line =>
                    line.Contains(token, StringComparison.OrdinalIgnoreCase)));

            if (dependency is { Length: > 0 })
            {
                detected.Add(new SpecialistSelection(
                    specialist, SpecialistTrigger.Dependency,
                    $"{dependency} dependency declared", 60));

                continue;
            }

            var glob = specialist.Activation.GlobList.FirstOrDefault(pattern =>
                evidence.Paths.Any(path => RuleService.Matches(pattern, path)));

            if (glob is { Length: > 0 })
            {
                detected.Add(new SpecialistSelection(
                    specialist, SpecialistTrigger.RepositoryEvidence,
                    $"files match {glob}", 35));
            }
        }

        return detected
            .OrderBy(s => (int)s.Specialist.Kind)
            .ThenBy(s => s.Specialist.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The preferences in force: the profile's when it has any, otherwise the
    /// project's.
    /// </summary>
    /// <remarks>
    /// Replaced rather than merged. A profile exists to narrow a project to one
    /// kind of work, and a profile that could only ever add to the project's
    /// list could not narrow anything.
    /// </remarks>
    private static SpecialistPreferences Preferences(InstructionRequest request)
    {
        if (request.Manifest is not { } manifest)
        {
            return new SpecialistPreferences();
        }

        if (request.ProfileName is { Length: > 0 } name
            && manifest.Profiles.TryGetValue(name, out var profile)
            && !profile.Specialists.IsEmpty)
        {
            return profile.Specialists;
        }

        return manifest.Specialists;
    }

    /// <summary>Exclusions from the command line and from the project both count.</summary>
    private static IReadOnlyList<string> Combine(
        IReadOnlyList<string>? first,
        IReadOnlyList<string>? second)
    {
        if (first is null or { Count: 0 })
        {
            return second ?? [];
        }

        if (second is null or { Count: 0 })
        {
            return first;
        }

        return first.Concat(second).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
