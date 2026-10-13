using System.Globalization;
using Loadout.Agents.Ideas;
using Loadout.Core.Voice;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Voice;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Voice;

/// <summary>What to draft a guide for.</summary>
/// <param name="Id">The guide's id.</param>
/// <param name="About">What the person wants it to learn, in their words.</param>
/// <param name="AgentName">The agent to ask, or null for the default.</param>
/// <param name="Model">The model to ask for, or null.</param>
public sealed record VoiceGuideDraftRequest(string Id, string About, string? AgentName = null, string? Model = null);

/// <summary>A drafted guide, not yet saved.</summary>
/// <param name="Guide">The definition, with the version it would be saved as.</param>
/// <param name="Unreadable">Sources it names that Loadout cannot gather yet.</param>
/// <param name="CostUsd">What drafting it cost.</param>
public sealed record VoiceGuideDraft(VoiceGuide Guide, IReadOnlyList<string> Unreadable, decimal CostUsd);

/// <summary>Drafts a guide definition for the person to review.</summary>
public interface IVoiceGuideDrafter
{
    /// <summary>Asks for a draft. Nothing is saved: the person reviews it first.</summary>
    Task<OperationResult<VoiceGuideDraft>> DraftAsync(VoiceGuideDraftRequest request, CancellationToken ct = default);
}

/// <summary>
/// One round with no tools, in a directory of its own, answered in the
/// guide contract and read strictly.
/// </summary>
internal sealed class VoiceGuideDrafter : IVoiceGuideDrafter
{
    private readonly VoiceStore _store;
    private readonly IDetachedLauncher _launcher;
    private readonly IPlatformPaths _paths;
    private readonly TimeProvider _time;

    public VoiceGuideDrafter(VoiceStore store, IDetachedLauncher launcher, IPlatformPaths paths, TimeProvider time)
    {
        _store = store;
        _launcher = launcher;
        _paths = paths;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<OperationResult<VoiceGuideDraft>> DraftAsync(VoiceGuideDraftRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = request.Id.Trim().ToLowerInvariant();

        if (VoiceGuideDrafting.Refusal(id) is { } refusal)
        {
            return OperationResult<VoiceGuideDraft>.Fail(refusal, ExitCode.InvalidArguments);
        }

        if (string.IsNullOrWhiteSpace(request.About))
        {
            return OperationResult<VoiceGuideDraft>.Fail(
                "Say what the guide should learn, so there is something to draft from.", ExitCode.InvalidArguments);
        }

        if (_store.GuidesDirectory is null)
        {
            return OperationResult<VoiceGuideDraft>.Fail(
                "There is no workspace on this machine, so there is nowhere to keep a guide. Run 'loadout setup' first.",
                ExitCode.ConfigurationInvalid);
        }

        var saved = await _store.SavedGuidesAsync(ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            return OperationResult<VoiceGuideDraft>.Fail(saved.Error!, saved.ExitCode);
        }

        // The next version free, so a draft can never replace a definition a
        // profile was learned against.
        var version = saved.Value!.Where(g => g.Id == id).Select(g => g.Version).DefaultIfEmpty(0).Max() + 1;

        var directory = Path.Combine(
            _paths.Paths.State,
            "voice",
            "rounds",
            $"guide-{id}-{_time.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}");

        Directory.CreateDirectory(directory);

        var asked = await SchemaRound.AskAsync(
            _launcher,
            new DetachedLaunchRequest(directory, $"Drafting the {id} guide", request.AgentName, request.Model),
            [],
            VoiceGuideDrafting.Schema,
            VoiceGuideDrafting.Version,
            VoiceGuideDrafting.Prompt(id, request.About),
            json => VoiceGuideDrafting.Read(json, id, version),
            null,
            ct).ConfigureAwait(false);

        if (asked.Failed)
        {
            return OperationResult<VoiceGuideDraft>.Fail(asked.Error!, asked.ExitCode);
        }

        if (asked.Value!.Answer.Failed)
        {
            return OperationResult<VoiceGuideDraft>.Fail(
                $"The draft could not be used: {asked.Value.Answer.Error}", ExitCode.GeneralFailure);
        }

        var guide = asked.Value.Answer.Value!;

        return OperationResult<VoiceGuideDraft>.Ok(
            new VoiceGuideDraft(guide, VoiceGuideDrafting.Unreadable(guide), asked.Value.CostUsd));
    }
}
