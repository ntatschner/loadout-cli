using System.Text;
using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Policies;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Policies;

/// <inheritdoc />
public sealed class PolicyService : IPolicyService
{
    private const string PolicyFileName = "forbidden-repository-files.yaml";
    private const string ExcludeFileName = "loadout-global-excludes";

    /// <summary>
    /// Names this file has had. A configured path ending in one of these was
    /// written by this tool, whatever it was called at the time, so repointing
    /// it is tidying rather than trampling on a choice somebody made.
    /// </summary>
    private static readonly string[] OwnExcludeFileNames =
    [
        ExcludeFileName,
        "agentctl-global-excludes",
    ];
    private const string HookFileName = "pre-commit";

    /// <summary>
    /// Marks a hook as the launcher's own. Anything without it is a hook
    /// somebody else installed, and overwriting or deleting that would be
    /// destroying work the launcher knows nothing about.
    /// </summary>
    private const string HookSignature = "# loadout-managed-hook";

    /// <summary>
    /// Signatures this launcher recognises as its own work, including the name
    /// it used to go by.
    /// <para>
    /// A hook written before the rename is still this tool's hook. Recognising
    /// only the current spelling stranded them: install refused to upgrade
    /// them because they looked like somebody else's, remove refused to delete
    /// them for the same reason, and they went on telling people to run a
    /// command that no longer exists. Same failure as the global exclude file
    /// after the rename, for the same reason.
    /// </para>
    /// </summary>
    private static readonly string[] OwnHookSignatures =
    [
        HookSignature,
        "# agentctl-managed-hook",
    ];

    private readonly IWorkspaceManager _workspace;
    private readonly IGitManager _git;
    private readonly IPlatformPaths _paths;
    private readonly IFilePermissions _permissions;
    private readonly YamlStore _yaml;

    public PolicyService(
        IWorkspaceManager workspace,
        IGitManager git,
        IPlatformPaths paths,
        IFilePermissions permissions,
        YamlStore yaml)
    {
        _workspace = workspace;
        _git = git;
        _paths = paths;
        _permissions = permissions;
        _yaml = yaml;
    }

    /// <inheritdoc />
    public async Task<OperationResult<RepositoryPolicy>> LoadPolicyAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(_workspace.LocalPath, "policies", PolicyFileName);

        // Defaults apply when the workspace has no policy of its own, so the
        // check is useful from the first run rather than only after somebody
        // has written a policy file.
        return await _yaml.LoadAsync(path, RepositoryPolicy.CreateDefault, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<PolicyReport>> CheckAsync(
        string repositoryPath,
        CancellationToken ct = default)
    {
        var rootResult = await _git.FindRepositoryRootAsync(repositoryPath, ct).ConfigureAwait(false);
        if (rootResult.Failed)
        {
            return OperationResult<PolicyReport>.Fail(rootResult.Error!, rootResult.ExitCode);
        }

        var root = rootResult.Value!;

        var policyResult = await LoadPolicyAsync(ct).ConfigureAwait(false);
        if (policyResult.Failed)
        {
            return OperationResult<PolicyReport>.Fail(policyResult.Error!, policyResult.ExitCode);
        }

        var policy = policyResult.Value!;
        var findings = new List<PolicyFinding>();

        foreach (var (fileSet, kind) in new[]
        {
            (GitFileSet.Tracked, PolicyFindingKind.Tracked),
            (GitFileSet.UntrackedAndVisible, PolicyFindingKind.UntrackedAndVisible),
            (GitFileSet.Ignored, PolicyFindingKind.Ignored),
        })
        {
            var listed = await _git.ListFilesAsync(root, policy.Forbidden, fileSet, ct)
                .ConfigureAwait(false);

            if (listed.Failed)
            {
                return OperationResult<PolicyReport>.Fail(listed.Error!, listed.ExitCode);
            }

            foreach (var path in listed.Value!)
            {
                if (await IsAllowedAsync(root, path, policy, ct).ConfigureAwait(false))
                {
                    continue;
                }

                findings.Add(new PolicyFinding(path, kind, MatchedPattern(path, policy)));
            }
        }

        var excludesResult = await _git.GetConfigValueAsync("core.excludesFile", null, ct)
            .ConfigureAwait(false);

        return OperationResult<PolicyReport>.Ok(new PolicyReport(
            root,
            findings,
            !string.IsNullOrWhiteSpace(excludesResult.Value),
            HasManagedHook(root),

            // Ours, but written by an older version: protected in practice,
            // and still worth replacing.
            HasManagedHook(root) && !HasCurrentHook(root)));
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> InstallGlobalExcludesAsync(CancellationToken ct = default)
    {
        var policyResult = await LoadPolicyAsync(ct).ConfigureAwait(false);
        if (policyResult.Failed)
        {
            return OperationResult<string>.Fail(policyResult.Error!, policyResult.ExitCode);
        }

        // Kept in the launcher's own config directory rather than overwriting
        // whatever the user already has at ~/.config/git/ignore, which may well
        // hold rules of their own.
        var excludePath = Path.Combine(_paths.Paths.Config, ExcludeFileName);

        var builder = new StringBuilder()
            .AppendLine("# Written by loadout. Agent tooling files are kept out of application")
            .AppendLine("# repositories (spec sections 9 and 50). Edit the workspace policy")
            .AppendLine("# instead of this file; it is regenerated.")
            .AppendLine();

        foreach (var pattern in policyResult.Value!.Forbidden)
        {
            builder.AppendLine(pattern);
        }

        foreach (var pattern in policyResult.Value.Allowed)
        {
            // A leading bang re-includes a path an earlier rule excluded, which
            // is how a project opts into versioning something like AGENTS.md.
            builder.AppendLine("!" + pattern);
        }

        try
        {
            Directory.CreateDirectory(_paths.Paths.Config);
            await File.WriteAllTextAsync(excludePath, builder.ToString(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<string>.Fail($"Could not write '{excludePath}': {ex.Message}");
        }

        var existing = await _git.GetConfigValueAsync("core.excludesFile", null, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(existing.Value)
            && !string.Equals(existing.Value, excludePath, StringComparison.OrdinalIgnoreCase)
            && !IsOurExcludeFile(existing.Value))
        {
            // Silently replacing a configured exclude file would disable rules
            // the user relies on, so the launcher writes its file and hands
            // back the decision.
            return OperationResult<string>.Fail(
                $"core.excludesFile already points at '{existing.Value}'. The launcher's rules were "
                + $"written to '{excludePath}'; include them there, or repoint core.excludesFile "
                + "yourself.");
        }

        var setResult = await _git.SetGlobalConfigValueAsync("core.excludesFile", excludePath, ct)
            .ConfigureAwait(false);

        return setResult.Succeeded
            ? OperationResult<string>.Ok(excludePath)
            : OperationResult<string>.Fail(setResult.Error!, setResult.ExitCode);
    }

    /// <summary>
    /// Whether a configured exclude path is one this tool wrote.
    /// <para>
    /// Compared by file name rather than full path, because the directory moves
    /// when the tool is renamed and the stale value then looks exactly like
    /// somebody else's carefully chosen exclude file. Refusing to touch it
    /// would leave the protection pointing at a file that no longer exists,
    /// with no way to repair it short of editing Git's configuration by hand.
    /// </para>
    /// </summary>
    private static bool IsOurExcludeFile(string configured)
    {
        var name = Path.GetFileName(configured.Trim());

        return OwnExcludeFileNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<OperationResult> InstallHookAsync(
        string repositoryPath,
        CancellationToken ct = default)
    {
        var rootResult = await _git.FindRepositoryRootAsync(repositoryPath, ct).ConfigureAwait(false);
        if (rootResult.Failed)
        {
            return OperationResult.Fail(rootResult.Error!, rootResult.ExitCode);
        }

        var policyResult = await LoadPolicyAsync(ct).ConfigureAwait(false);
        if (policyResult.Failed)
        {
            return OperationResult.Fail(policyResult.Error!, policyResult.ExitCode);
        }

        var hookPath = HookPath(rootResult.Value!);

        if (File.Exists(hookPath) && !HasManagedHook(rootResult.Value!))
        {
            return OperationResult.Fail(
                $"A pre-commit hook already exists at '{hookPath}' and was not written by the "
                + "launcher. Merge the check in by hand rather than losing the existing hook.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
            await File.WriteAllTextAsync(hookPath, BuildHook(policyResult.Value!), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not write '{hookPath}': {ex.Message}");
        }

        // Without the executable bit git ignores the hook entirely on Unix,
        // which would look like the protection was installed when it was not.
        var executable = _permissions.MakeExecutable(hookPath);

        return executable.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(
                $"The hook was written but could not be made executable, so git will ignore it: "
                + executable.Error);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveHookAsync(
        string repositoryPath,
        CancellationToken ct = default)
    {
        var rootResult = await _git.FindRepositoryRootAsync(repositoryPath, ct).ConfigureAwait(false);
        if (rootResult.Failed)
        {
            return OperationResult.Fail(rootResult.Error!, rootResult.ExitCode);
        }

        var hookPath = HookPath(rootResult.Value!);

        if (!File.Exists(hookPath))
        {
            return OperationResult.Ok();
        }

        if (!HasManagedHook(rootResult.Value!))
        {
            return OperationResult.Fail(
                $"The pre-commit hook at '{hookPath}' was not written by the launcher, so it was "
                + "left alone.");
        }

        try
        {
            File.Delete(hookPath);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not remove '{hookPath}': {ex.Message}");
        }
    }

    private static string HookPath(string root) =>
        Path.Combine(root, ".git", "hooks", HookFileName);

    /// <summary>Whether the repository carries a hook this launcher wrote.</summary>
    /// <summary>
    /// Whether the installed hook is this version's, rather than one written
    /// under an older name.
    /// <para>
    /// Kept separate from <see cref="HasManagedHook"/> on purpose. Recognising
    /// an old hook as ours is what lets it be replaced; reporting it as current
    /// would leave it in place forever, still naming commands that no longer
    /// exist. So one question guards the overwrite and the other drives the
    /// upgrade.
    /// </para>
    /// </summary>
    private static bool HasCurrentHook(string root)
    {
        try
        {
            var path = HookPath(root);

            return File.Exists(path)
                && File.ReadAllText(path).Contains(HookSignature, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasManagedHook(string root)
    {
        var path = HookPath(root);

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var contents = File.ReadAllText(path);

            return Array.Exists(
                OwnHookSignatures,
                signature => contents.Contains(signature, StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the pre-commit script.
    /// <para>
    /// Written as POSIX shell because that is what Git runs hooks with on all
    /// three platforms, including Windows, where Git ships its own shell. The
    /// script re-derives the check from git rather than calling back into
    /// loadout, so the protection still works on a machine where the launcher
    /// has been moved or uninstalled.
    /// </para>
    /// </summary>
    private static string BuildHook(RepositoryPolicy policy)
    {
        // One quoted pattern per line inside a single-quoted heredoc. The
        // previous attempt built a multi-line printf with backslash
        // continuations, and losing them turned every pattern after the first
        // into a command of its own: the hook errored on each line, exited zero
        // and let the commit through. A heredoc needs no continuations, so
        // there is nothing to lose.
        //
        // Quoting is single-quote-escaped so a pattern containing a quote
        // cannot end the string and run as shell.
        var patterns = string.Join(
            "\n",
            policy.Forbidden.Select(p => "'" + p.Replace("'", "'\\''") + "'"));

        return $"""
            #!/bin/sh
            {HookSignature}
            # Blocks commits containing AI tooling files (spec section 51).
            # Remove with: loadout protect --remove

            staged=""

            while IFS= read -r pattern; do
              [ -n "$pattern" ] || continue

              # eval strips the surrounding quotes written above, so a pattern
              # containing a space survives as one argument.
              eval "set -- $pattern"

              match=$(git diff --cached --name-only --diff-filter=AM -- "$1")
              if [ -n "$match" ]; then
                staged="$staged$match
            "
              fi
            done <<'LOADOUT_PATTERNS'
            {patterns}
            LOADOUT_PATTERNS

            if [ -n "$staged" ]; then
              echo "Commit blocked."
              echo
              echo "AI tooling files are not allowed in this repository:"
              echo
              printf '%s' "$staged" | sed 's/^/  /'
              echo
              echo "Move them into the central workspace with:"
              echo
              echo "  loadout migrate"
              echo
              echo "To commit anyway, pass --no-verify."
              exit 1
            fi

            exit 0

            """;
    }

    /// <summary>
    /// Whether an allowed pattern exempts this path. Uses git's own matching so
    /// an exemption behaves the same way the forbidden rule does.
    /// </summary>
    private async Task<bool> IsAllowedAsync(
        string root,
        string path,
        RepositoryPolicy policy,
        CancellationToken ct)
    {
        if (policy.Allowed.Count == 0)
        {
            return false;
        }

        var allowed = await _git.ListFilesAsync(root, policy.Allowed, GitFileSet.Tracked, ct)
            .ConfigureAwait(false);

        if (allowed.Succeeded && allowed.Value!.Contains(path, StringComparer.Ordinal))
        {
            return true;
        }

        var visible = await _git.ListFilesAsync(root, policy.Allowed, GitFileSet.UntrackedAndVisible, ct)
            .ConfigureAwait(false);

        return visible.Succeeded && visible.Value!.Contains(path, StringComparer.Ordinal);
    }

    /// <summary>
    /// Names the rule a path fell foul of, so the report explains itself rather
    /// than just listing paths.
    /// </summary>
    private static string MatchedPattern(string path, RepositoryPolicy policy)
    {
        var normalised = path.Replace('\\', '/');

        foreach (var pattern in policy.Forbidden)
        {
            var prefix = pattern.TrimEnd('*', '/');

            if (normalised.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalised, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return pattern;
            }
        }

        return "policy";
    }
}
