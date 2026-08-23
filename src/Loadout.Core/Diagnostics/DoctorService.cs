using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Policies;
using Loadout.Core.Security;
using Loadout.Core.Workspace;
using Loadout.Models.Diagnostics;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Diagnostics;

/// <summary>Builds the full diagnostic report (spec section 60).</summary>
public interface IDoctorService
{
    Task<OperationResult<DiagnosticReport>> RunAsync(CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class DoctorService : IDoctorService
{
    private readonly IPlatformPaths _paths;
    private readonly IPlatformCapabilities _capabilities;
    private readonly IPathSemantics _pathSemantics;
    private readonly IConfigurationService _configuration;
    private readonly IWorkspaceManager _workspace;
    private readonly IGitManager _git;
    private readonly ISecretProvider _secrets;
    private readonly IPolicyService _policies;
    private readonly IEnumerable<IDiagnosticContributor> _contributors;

    public DoctorService(
        IPlatformPaths paths,
        IPlatformCapabilities capabilities,
        IPathSemantics pathSemantics,
        IConfigurationService configuration,
        IWorkspaceManager workspace,
        IGitManager git,
        ISecretProvider secrets,
        IPolicyService policies,
        IEnumerable<IDiagnosticContributor> contributors)
    {
        _paths = paths;
        _capabilities = capabilities;
        _pathSemantics = pathSemantics;
        _configuration = configuration;
        _workspace = workspace;
        _git = git;
        _secrets = secrets;
        _policies = policies;
        _contributors = contributors;
    }

    /// <inheritdoc />
    public async Task<OperationResult<DiagnosticReport>> RunAsync(CancellationToken ct = default)
    {
        var checks = new List<DiagnosticCheck>();

        AddPlatformChecks(checks);
        var config = await AddConfigurationChecksAsync(checks, ct).ConfigureAwait(false);
        await AddGitChecksAsync(checks, ct).ConfigureAwait(false);
        await AddWorkspaceChecksAsync(checks, config, ct).ConfigureAwait(false);
        await AddDiscoveryChecksAsync(checks, ct).ConfigureAwait(false);
        await AddSecretChecksAsync(checks, ct).ConfigureAwait(false);
        await AddPolicyChecksAsync(checks, ct).ConfigureAwait(false);
        AddCapabilityChecks(checks);
        await AddContributedChecksAsync(checks, ct).ConfigureAwait(false);

        return OperationResult<DiagnosticReport>.Ok(new DiagnosticReport(checks));
    }

    private void AddPlatformChecks(List<DiagnosticCheck> checks)
    {
        var host = _paths.Host;

        checks.Add(DiagnosticCheck.Ok(
            "Platform",
            $"{host.OperatingSystem} {host.Architecture}",
            $"{host.OperatingSystemDescription} ({host.RuntimeIdentifier})"));

        checks.Add(DiagnosticCheck.Ok("Platform", "Machine", host.MachineName));
    }

    private async Task<Models.Configuration.LauncherConfig?> AddConfigurationChecksAsync(
        List<DiagnosticCheck> checks,
        CancellationToken ct)
    {
        var configResult = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

        if (configResult.Failed)
        {
            checks.Add(DiagnosticCheck.Error("Launcher", "Configuration", configResult.Error!));
            return null;
        }

        checks.Add(DiagnosticCheck.Ok("Launcher", "Configuration", _paths.Paths.ConfigFile));
        checks.Add(DiagnosticCheck.Ok("Launcher", "State", _paths.Paths.State));
        checks.Add(DiagnosticCheck.Ok("Launcher", "Logs", _paths.Paths.Logs));

        var machineResult = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);
        checks.Add(machineResult.Succeeded
            ? DiagnosticCheck.Ok("Launcher", "Machine configuration", _paths.Paths.MachinesFile)
            : DiagnosticCheck.Error("Launcher", "Machine configuration", machineResult.Error!));

        return configResult.Value;
    }

    private async Task AddGitChecksAsync(List<DiagnosticCheck> checks, CancellationToken ct)
    {
        var versionResult = await _git.GetVersionAsync(ct).ConfigureAwait(false);

        if (versionResult.Failed)
        {
            // Without git nothing else works, so this is the one check that is
            // unambiguously an error rather than a warning.
            checks.Add(DiagnosticCheck.Error("Git", "Installed", versionResult.Error!));
            return;
        }

        checks.Add(DiagnosticCheck.Ok("Git", "Installed", versionResult.Value!));

        var helperResult = await _git.GetConfigValueAsync("credential.helper", null, ct)
            .ConfigureAwait(false);

        checks.Add(helperResult.Value is { Length: > 0 }
            ? DiagnosticCheck.Ok("Git", "Credential helper", helperResult.Value)
            // Not an error: SSH key authentication needs no helper at all.
            : DiagnosticCheck.Ok("Git", "Credential helper",
                "none configured (fine when using SSH keys)"));

        var excludesResult = await _git.GetConfigValueAsync("core.excludesFile", null, ct)
            .ConfigureAwait(false);

        checks.Add(excludesResult.Value is { Length: > 0 }
            ? DiagnosticCheck.Ok("Git", "Global exclude file", excludesResult.Value)
            : DiagnosticCheck.Warn("Git", "Global exclude file",
                "not configured; agent files are not globally ignored (spec section 50)"));
    }

    private async Task AddWorkspaceChecksAsync(
        List<DiagnosticCheck> checks,
        Models.Configuration.LauncherConfig? config,
        CancellationToken ct)
    {
        if (config is null)
        {
            return;
        }

        if (!_workspace.IsConfigured(config))
        {
            // Running without central storage is an offered choice, not a
            // fault, so it is reported as information.
            checks.Add(DiagnosticCheck.Ok("Workspace", "Central workspace",
                "not configured; running with local state only"));
            return;
        }

        checks.Add(DiagnosticCheck.Ok("Workspace", "Remote",
            SecretRedactor.Redact(config.Workspace.Remote)));

        if (!_workspace.IsCloned())
        {
            checks.Add(DiagnosticCheck.Warn("Workspace", "Local clone",
                $"not cloned yet at '{_workspace.LocalPath}'; run: loadout workspace sync"));
            return;
        }

        checks.Add(DiagnosticCheck.Ok("Workspace", "Local clone", _workspace.LocalPath));

        var manifestResult = await _workspace.ReadManifestAsync(ct).ConfigureAwait(false);

        if (manifestResult.Failed)
        {
            checks.Add(DiagnosticCheck.Error("Workspace", "Schema", manifestResult.Error!));
        }
        else if (manifestResult.Value!.WorkspaceSchema > WorkspaceManager.SupportedSchemaVersion)
        {
            // Refusing loudly beats misreading a newer layout (spec section 91).
            checks.Add(DiagnosticCheck.Error("Workspace", "Schema",
                $"workspace schema {manifestResult.Value.WorkspaceSchema} is newer than this launcher "
                + $"supports ({WorkspaceManager.SupportedSchemaVersion}); update loadout"));
        }
        else
        {
            checks.Add(DiagnosticCheck.Ok("Workspace", "Schema",
                $"version {manifestResult.Value.WorkspaceSchema}"));
        }

        var registryResult = await _workspace.ReadRegistryAsync(ct).ConfigureAwait(false);
        checks.Add(registryResult.Succeeded
            ? DiagnosticCheck.Ok("Workspace", "Registry",
                $"{registryResult.Value!.Projects.Count} project(s)")
            : DiagnosticCheck.Error("Workspace", "Registry", registryResult.Error!));
    }

    private async Task AddDiscoveryChecksAsync(List<DiagnosticCheck> checks, CancellationToken ct)
    {
        var machineResult = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);

        if (machineResult.Failed)
        {
            return;
        }

        var roots = machineResult.Value!.DiscoveryRoots;

        if (roots.Count == 0)
        {
            checks.Add(DiagnosticCheck.Warn("Discovery", "Roots",
                "none configured; project discovery will find nothing"));
            return;
        }

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                checks.Add(DiagnosticCheck.Warn("Discovery", root, "does not exist"));
                continue;
            }

            // Reported because it is genuinely surprising when wrong, and it
            // decides whether two spellings of a path are one project or two
            // (spec section 84).
            var sensitivity = _pathSemantics.IsCaseInsensitive(root)
                ? "case-insensitive"
                : "case-sensitive";

            checks.Add(DiagnosticCheck.Ok("Discovery", root, sensitivity));
        }
    }

    private async Task AddSecretChecksAsync(List<DiagnosticCheck> checks, CancellationToken ct)
    {
        var availability = await _secrets.IsAvailableAsync(ct).ConfigureAwait(false);

        checks.Add(availability.Succeeded
            ? DiagnosticCheck.Ok("Secrets", "Provider", _secrets.Name)
            : DiagnosticCheck.Warn("Secrets", "Provider",
                $"{_secrets.Name} is unavailable: {availability.Error}"));
    }

    /// <summary>
    /// Reports policy compliance for the repository the user is standing in.
    /// <para>
    /// Only the current directory, deliberately: checking every registered
    /// project would turn doctor into a slow command, and this is the
    /// repository the person is most likely asking about. It also surfaces the
    /// gap in spec section 51, where the pre-commit hook is per-clone and
    /// silently absent on a fresh clone made on another machine.
    /// </para>
    /// </summary>
    private async Task AddPolicyChecksAsync(List<DiagnosticCheck> checks, CancellationToken ct)
    {
        var result = await _policies.CheckAsync(Directory.GetCurrentDirectory(), ct)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            // Not being inside a repository is the normal case for someone
            // running doctor from their home directory, not a fault.
            return;
        }

        var report = result.Value!;

        checks.Add(report.Violations.Count == 0
            ? DiagnosticCheck.Ok("Repository", "Agent files", "none tracked")
            : DiagnosticCheck.Error("Repository", "Agent files",
                $"{report.Violations.Count} tracked: "
                + string.Join(", ", report.Violations.Take(5).Select(v => v.Path))));

        if (report.Warnings.Count > 0)
        {
            checks.Add(DiagnosticCheck.Warn("Repository", "Untracked agent files",
                $"{report.Warnings.Count} present and not ignored"));
        }

        checks.Add(report.HasPreCommitHook
            ? DiagnosticCheck.Ok("Repository", "Pre-commit protection", "installed")
            : DiagnosticCheck.Warn("Repository", "Pre-commit protection",
                "not installed in this clone; hooks are per-clone, so run: loadout protect"));
    }

    private void AddCapabilityChecks(List<DiagnosticCheck> checks)
    {
        foreach (var status in _capabilities.QueryAll())
        {
            // Every capability is listed whether or not it is available. That
            // is the point of spec section 5: a gap must be visible, with its
            // reason, rather than absent from the report.
            checks.Add(status.IsSupported
                ? DiagnosticCheck.Ok("Capabilities", status.Capability.ToString(), status.Detail)
                : DiagnosticCheck.Warn("Capabilities", status.Capability.ToString(), status.Detail));
        }
    }

    private async Task AddContributedChecksAsync(List<DiagnosticCheck> checks, CancellationToken ct)
    {
        foreach (var contributor in _contributors)
        {
            try
            {
                checks.AddRange(await contributor.ContributeAsync(ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A broken contributor must not take down the whole report;
                // doctor is what people run when things are already wrong.
                checks.Add(DiagnosticCheck.Error(
                    "Diagnostics",
                    contributor.GetType().Name,
                    SecretRedactor.Redact(ex.Message)));
            }
        }
    }
}
