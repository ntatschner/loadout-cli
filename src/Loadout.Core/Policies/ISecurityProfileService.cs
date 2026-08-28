using Loadout.Core.Configuration;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Policies;
using Loadout.Models.Projects;
using Loadout.Models.Results;

namespace Loadout.Core.Policies;

/// <summary>What an environment selection resolved to.</summary>
/// <param name="Name">Environment that was selected, or null for the project default.</param>
/// <param name="Profile">The security profile that applies.</param>
/// <param name="ProfileName">Name of that profile, for display.</param>
/// <param name="Environment">
/// The project's bindings with the environment's overrides applied.
/// </param>
public sealed record ResolvedEnvironment(
    string? Name,
    SecurityProfile Profile,
    string ProfileName,
    IReadOnlyDictionary<string, EnvironmentBinding> Environment);

/// <summary>
/// Resolves environment and security profiles (spec sections 57 and 58).
/// </summary>
public interface ISecurityProfileService
{
    /// <summary>
    /// Works out which security profile and environment bindings apply.
    /// <para>
    /// Naming an environment the project does not define is an error rather
    /// than a silent fall back to the default: someone who typed
    /// <c>--environment prod</c> meaning <c>production</c> must not quietly get
    /// development's permissions.
    /// </para>
    /// </summary>
    Task<OperationResult<ResolvedEnvironment>> ResolveAsync(
        ProjectManifest manifest,
        string? environmentName,
        CancellationToken ct = default);

    /// <summary>Security profiles available, from the workspace or the built-in set.</summary>
    Task<OperationResult<IReadOnlyDictionary<string, SecurityProfile>>> ListProfilesAsync(
        CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class SecurityProfileService : ISecurityProfileService
{
    private const string ProfilesFileName = "security-profiles.yaml";

    private readonly IWorkspaceManager _workspace;
    private readonly YamlStore _yaml;

    public SecurityProfileService(IWorkspaceManager workspace, YamlStore yaml)
    {
        _workspace = workspace;
        _yaml = yaml;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyDictionary<string, SecurityProfile>>>
        ListProfilesAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(_workspace.LocalPath, "policies", ProfilesFileName);

        var loaded = await _yaml
            .LoadAsync<Dictionary<string, SecurityProfile>>(path, () => [], ct)
            .ConfigureAwait(false);

        if (loaded.Failed)
        {
            return OperationResult<IReadOnlyDictionary<string, SecurityProfile>>.Fail(
                loaded.Error!, loaded.ExitCode);
        }

        // Workspace profiles layer over the built-ins rather than replacing
        // them, so defining one custom profile does not remove the three the
        // spec names.
        var profiles = new Dictionary<string, SecurityProfile>(
            SecurityProfile.CreateDefaults(), StringComparer.OrdinalIgnoreCase);

        foreach (var (name, profile) in loaded.Value!)
        {
            profiles[name] = profile;
        }

        return OperationResult<IReadOnlyDictionary<string, SecurityProfile>>.Ok(profiles);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ResolvedEnvironment>> ResolveAsync(
        ProjectManifest manifest,
        string? environmentName,
        CancellationToken ct = default)
    {
        var profilesResult = await ListProfilesAsync(ct).ConfigureAwait(false);
        if (profilesResult.Failed)
        {
            return OperationResult<ResolvedEnvironment>.Fail(
                profilesResult.Error!, profilesResult.ExitCode);
        }

        var profiles = profilesResult.Value!;

        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return OperationResult<ResolvedEnvironment>.Ok(new ResolvedEnvironment(
                null,
                profiles["normal"],
                "normal",
                manifest.Environment));
        }

        if (!manifest.Environments.TryGetValue(environmentName, out var environment))
        {
            var available = manifest.Environments.Count == 0
                ? "the project defines none"
                : string.Join(", ", manifest.Environments.Keys);

            return OperationResult<ResolvedEnvironment>.Fail(
                $"'{manifest.Slug}' has no environment named '{environmentName}'. Available: {available}.",
                ExitCode.InvalidArguments);
        }

        var profileName = environment.SecurityProfile ?? "normal";

        if (!profiles.TryGetValue(profileName, out var profile))
        {
            // Falling back to a permissive default here would be the worst
            // possible failure: an environment meant to be locked down would
            // silently run wide open.
            return OperationResult<ResolvedEnvironment>.Fail(
                $"Environment '{environmentName}' names security profile '{profileName}', which does "
                + $"not exist. Available: {string.Join(", ", profiles.Keys)}.",
                ExitCode.ConfigurationInvalid);
        }

        // The environment's bindings layer over the project's, so production
        // can point one variable at production without restating the rest.
        var merged = new Dictionary<string, EnvironmentBinding>(
            manifest.Environment, StringComparer.Ordinal);

        foreach (var (name, binding) in environment.Environment)
        {
            merged[name] = binding;
        }

        return OperationResult<ResolvedEnvironment>.Ok(
            new ResolvedEnvironment(environmentName, profile, profileName, merged));
    }
}
