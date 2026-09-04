using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Packs;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Packs;

/// <summary>Named sets of specialists fetched from a Git remote.</summary>
public interface IPackService
{
    /// <summary>Where every declared pack stands on this machine.</summary>
    Task<OperationResult<IReadOnlyList<PackStanding>>> StandingAsync(CancellationToken ct = default);

    /// <summary>
    /// Declares a pack and pins it to whatever its ref points at now.
    /// </summary>
    /// <remarks>
    /// Fetching is not approving. This brings the content onto the machine and
    /// writes down which commit it is, so somebody can read it — nothing is
    /// loaded until they say so.
    /// </remarks>
    Task<OperationResult<SpecialistPack>> AddAsync(
        string name,
        string remote,
        string reference = "main",
        CancellationToken ct = default);

    /// <summary>Records that somebody on this machine has read a pack's pinned commit.</summary>
    Task<OperationResult> ApproveAsync(string name, string approvedBy, CancellationToken ct = default);

    /// <summary>Moves a pack's pin to whatever its ref points at now, losing its approval.</summary>
    Task<OperationResult<SpecialistPack>> UpdateAsync(string name, CancellationToken ct = default);

    /// <summary>Stops declaring a pack, and forgets any approval of it.</summary>
    Task<OperationResult> RemoveAsync(string name, CancellationToken ct = default);

    /// <summary>Where a pack's files are on this machine, or null when it is not fetched.</summary>
    string? DirectoryFor(string name);
}

/// <inheritdoc />
internal sealed class PackService : IPackService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IGitManager _git;
    private readonly IPlatformPaths _paths;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;

    public PackService(
        IWorkspaceManager workspace,
        IGitManager git,
        IPlatformPaths paths,
        YamlStore yaml,
        TimeProvider time)
    {
        _workspace = workspace;
        _git = git;
        _paths = paths;
        _yaml = yaml;
        _time = time;
    }

    /// <summary>The shared half: which packs the team uses.</summary>
    private string DeclarationPath => Path.Combine(_workspace.LocalPath, "packs.yaml");

    /// <summary>The local half: what this machine has agreed to run.</summary>
    private string ApprovalPath => Path.Combine(_paths.Paths.State, "pack-approvals.yaml");

    /// <inheritdoc />
    public string? DirectoryFor(string name)
    {
        if (PackNames.Rejection(name) is not null)
        {
            return null;
        }

        var directory = Path.Combine(_paths.Paths.State, "packs", name.Trim());

        return Directory.Exists(directory) ? directory : null;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<PackStanding>>> StandingAsync(
        CancellationToken ct = default)
    {
        var declared = await DeclaredAsync(ct).ConfigureAwait(false);

        if (declared.Failed)
        {
            return OperationResult<IReadOnlyList<PackStanding>>.Fail(
                declared.Error!, declared.ExitCode);
        }

        var approvals = await ApprovedAsync(ct).ConfigureAwait(false);

        return OperationResult<IReadOnlyList<PackStanding>>.Ok(
            PackGate.Standing(declared.Value!.Packs, approvals.Approvals));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SpecialistPack>> AddAsync(
        string name,
        string remote,
        string reference = "main",
        CancellationToken ct = default)
    {
        if (PackNames.Rejection(name) is { } rejected)
        {
            return OperationResult<SpecialistPack>.Fail(rejected, ExitCode.InvalidArguments);
        }

        if (string.IsNullOrWhiteSpace(remote))
        {
            return OperationResult<SpecialistPack>.Fail(
                "A pack needs a remote to come from.", ExitCode.InvalidArguments);
        }

        // A remote can carry a credential in its userinfo, and this one is
        // about to be written into packs.yaml — which lives in the workspace,
        // gets committed, and travels to everybody on the team. The workspace
        // secret gate would catch it before a push; refusing here means it
        // never reaches the file at all.
        //
        // The pattern is named, never the value: a refusal that quoted what it
        // found would put the credential into terminal scrollback.
        var patterns = Security.SecretScanner.Match(remote);

        if (patterns.Count > 0)
        {
            return OperationResult<SpecialistPack>.Fail(
                $"That remote carries something shaped like a credential "
                + $"({string.Join(", ", patterns)}). Use a remote that authenticates through "
                + "your Git configuration instead of one with the secret in the URL.",
                ExitCode.PolicyViolation);
        }

        var trimmed = name.Trim();

        var declared = await DeclaredAsync(ct).ConfigureAwait(false);

        if (declared.Failed)
        {
            return OperationResult<SpecialistPack>.Fail(declared.Error!, declared.ExitCode);
        }

        if (declared.Value!.Packs.Any(pack =>
            string.Equals(pack.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<SpecialistPack>.Fail(
                $"'{trimmed}' is already declared. Use 'pack update' to move its pin.",
                ExitCode.InvalidArguments);
        }

        var fetched = await FetchAsync(trimmed, remote, reference, ct).ConfigureAwait(false);

        if (fetched.Failed)
        {
            return OperationResult<SpecialistPack>.Fail(fetched.Error!, fetched.ExitCode);
        }

        var pack = new SpecialistPack
        {
            Name = trimmed,
            Remote = remote.Trim(),
            Ref = string.IsNullOrWhiteSpace(reference) ? "main" : reference.Trim(),
            Commit = fetched.Value!,
        };

        declared.Value.Packs.Add(pack);

        var written = await _yaml
            .SaveAsync(DeclarationPath, declared.Value, true, ct)
            .ConfigureAwait(false);

        return written.Succeeded
            ? OperationResult<SpecialistPack>.Ok(pack)
            : OperationResult<SpecialistPack>.Fail(written.Error!, written.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult> ApproveAsync(
        string name,
        string approvedBy,
        CancellationToken ct = default)
    {
        var standing = await StandingAsync(ct).ConfigureAwait(false);

        if (standing.Failed)
        {
            return OperationResult.Fail(standing.Error!, standing.ExitCode);
        }

        var found = standing.Value!.FirstOrDefault(entry =>
            string.Equals(entry.Pack.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (found is null)
        {
            return OperationResult.Fail(
                $"'{name.Trim()}' is not a declared pack.", ExitCode.InvalidArguments);
        }

        if (found.Pack.Commit.Length == 0)
        {
            return OperationResult.Fail(
                $"'{found.Pack.Name}' is pinned to no commit, so there is nothing to approve.",
                ExitCode.InvalidArguments);
        }

        // Written to the machine, never to the workspace. Approving is taking
        // responsibility for what an agent will be told, and that cannot be
        // done on somebody else's behalf by committing a file.
        var approvals = await ApprovedAsync(ct).ConfigureAwait(false);

        approvals.Approvals.RemoveAll(approval =>
            string.Equals(approval.Name, found.Pack.Name, StringComparison.OrdinalIgnoreCase));

        approvals.Approvals.Add(new PackApproval
        {
            Name = found.Pack.Name,
            Commit = found.Pack.Commit,
            ApprovedBy = approvedBy.Trim(),
            ApprovedUtc = _time.GetUtcNow(),
        });

        return await _yaml.SaveAsync(ApprovalPath, approvals, true, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<SpecialistPack>> UpdateAsync(
        string name,
        CancellationToken ct = default)
    {
        var declared = await DeclaredAsync(ct).ConfigureAwait(false);

        if (declared.Failed)
        {
            return OperationResult<SpecialistPack>.Fail(declared.Error!, declared.ExitCode);
        }

        var pack = declared.Value!.Packs.FirstOrDefault(entry =>
            string.Equals(entry.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (pack is null)
        {
            return OperationResult<SpecialistPack>.Fail(
                $"'{name.Trim()}' is not a declared pack.", ExitCode.InvalidArguments);
        }

        var fetched = await FetchAsync(pack.Name, pack.Remote, pack.Ref, ct).ConfigureAwait(false);

        if (fetched.Failed)
        {
            return OperationResult<SpecialistPack>.Fail(fetched.Error!, fetched.ExitCode);
        }

        // The approval is not carried over, and nothing here needs to remove
        // it: the gate compares commits, so a moved pin is unapproved by
        // arithmetic rather than by bookkeeping somebody could forget.
        pack.Commit = fetched.Value!;

        var written = await _yaml
            .SaveAsync(DeclarationPath, declared.Value, true, ct)
            .ConfigureAwait(false);

        return written.Succeeded
            ? OperationResult<SpecialistPack>.Ok(pack)
            : OperationResult<SpecialistPack>.Fail(written.Error!, written.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveAsync(string name, CancellationToken ct = default)
    {
        var trimmed = name.Trim();

        var declared = await DeclaredAsync(ct).ConfigureAwait(false);

        if (declared.Failed)
        {
            return OperationResult.Fail(declared.Error!, declared.ExitCode);
        }

        if (declared.Value!.Packs.RemoveAll(pack =>
            string.Equals(pack.Name, trimmed, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return OperationResult.Fail(
                $"'{trimmed}' is not a declared pack.", ExitCode.InvalidArguments);
        }

        var written = await _yaml
            .SaveAsync(DeclarationPath, declared.Value, true, ct)
            .ConfigureAwait(false);

        if (written.Failed)
        {
            return written;
        }

        // The approval goes too. Leaving it would mean re-declaring the same
        // pack later silently reactivated it at a commit nobody looked at
        // again.
        var approvals = await ApprovedAsync(ct).ConfigureAwait(false);

        if (approvals.Approvals.RemoveAll(approval =>
            string.Equals(approval.Name, trimmed, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            await _yaml.SaveAsync(ApprovalPath, approvals, true, ct).ConfigureAwait(false);
        }

        return OperationResult.Ok();
    }

    /// <summary>Brings a pack onto this machine and says which commit arrived.</summary>
    private async Task<OperationResult<string>> FetchAsync(
        string name,
        string remote,
        string reference,
        CancellationToken ct)
    {
        var directory = Path.Combine(_paths.Paths.State, "packs", name);

        if (!Directory.Exists(Path.Combine(directory, ".git")))
        {
            var cloned = await _git
                .CloneAsync(remote, directory, reference, ct)
                .ConfigureAwait(false);

            if (cloned.Failed)
            {
                return OperationResult<string>.Fail(cloned.Error!, cloned.ExitCode);
            }
        }
        else
        {
            // Bounded, because a pack comes from a remote nobody here controls
            // and a fetch that never returns would hold whatever asked for it.
            var fetched = await _git
                .FetchAsync(directory, TimeSpan.FromMinutes(2), ct)
                .ConfigureAwait(false);

            if (fetched.Failed)
            {
                return OperationResult<string>.Fail(fetched.Error!, fetched.ExitCode);
            }

            // Fetching alone was the whole of this once, and 'pack update'
            // silently did nothing: fetch moves the remote-tracking refs and
            // never touches HEAD, so the commit read below was the one already
            // pinned and the files on disk never changed. It looked like it
            // worked — it printed the same commit it started with.
            var pulled = await _git
                .PullFastForwardAsync(directory, ct)
                .ConfigureAwait(false);

            if (pulled.Failed)
            {
                return OperationResult<string>.Fail(pulled.Error!, pulled.ExitCode);
            }
        }

        var state = await _git.GetStateAsync(directory, ct).ConfigureAwait(false);

        if (state.Failed || state.Value!.HeadCommit is not { Length: > 0 } commit)
        {
            return OperationResult<string>.Fail(
                $"'{name}' was fetched but its commit could not be read, so there is nothing "
                + "to pin it to.",
                ExitCode.RepositoryUnavailable);
        }

        return OperationResult<string>.Ok(commit);
    }

    private async Task<OperationResult<SpecialistPackFile>> DeclaredAsync(CancellationToken ct)
    {
        if (!_workspace.IsAvailable())
        {
            return OperationResult<SpecialistPackFile>.Fail(
                "There is no workspace on this machine, so there is nowhere to declare a pack.",
                ExitCode.WorkspaceSyncFailed);
        }

        return await _yaml
            .LoadAsync(DeclarationPath, () => new SpecialistPackFile(), ct)
            .ConfigureAwait(false);
    }

    private async Task<PackApprovalFile> ApprovedAsync(CancellationToken ct)
    {
        var loaded = await _yaml
            .LoadAsync(ApprovalPath, () => new PackApprovalFile(), ct)
            .ConfigureAwait(false);

        // An unreadable approval file means nothing is approved, which is the
        // safe direction: it stops packs loading rather than starting them.
        return loaded.Succeeded ? loaded.Value! : new PackApprovalFile();
    }
}
