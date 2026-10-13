using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Loadout.Core.Instructions;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>A profile in force, with the layer it came from.</summary>
/// <param name="Layer">Which layer it is.</param>
/// <param name="Name">The style it belongs to: <c>personal</c>, a named style, or <c>codebase</c>.</param>
/// <param name="Path">Where it is kept.</param>
/// <param name="Profile">What it holds, or null when there is nothing there yet.</param>
public sealed record VoiceLayer(StyleLayer Layer, string Name, string Path, VoiceProfile? Profile);

/// <summary>A rule in force, with the layer that set it.</summary>
public sealed record ResolvedVoiceRule(VoiceRule Rule, StyleLayer Layer, string Name);

/// <summary>
/// Where voice profiles and marks are kept, and how the layers combine.
/// </summary>
/// <remarks>
/// <para>
/// Three places, for three different kinds of thing. The personal profile is
/// in the user configuration directory, beside <c>config.yaml</c>, because it
/// is about this person wherever they work. Named and codebase profiles are in
/// the workspace beside the project's other configuration, which is where a
/// named style's specialists and a project's docs policy already live, so
/// they travel with the workspace and are committed with it.
/// </para>
/// <para>
/// Marks are local state and stay on this machine. Which of somebody's
/// commits they wrote by hand is provenance, and provenance does not go into
/// anybody's history.
/// </para>
/// <para>
/// The layers combine the way the style layers do: personal, then named, then
/// codebase, a later layer replacing an earlier one's rule of the same id.
/// </para>
/// </remarks>
public sealed class VoiceStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _config;
    private readonly string _state;
    private readonly string? _workspace;

    /// <param name="configRoot">The user configuration directory.</param>
    /// <param name="stateRoot">The machine-local state directory.</param>
    /// <param name="workspaceRoot">The workspace clone, or null when there is none.</param>
    public VoiceStore(string configRoot, string stateRoot, string? workspaceRoot)
    {
        _config = configRoot;
        _state = stateRoot;
        _workspace = workspaceRoot;
    }

    /// <summary>The local mark list.</summary>
    public string MarksPath => Path.Combine(_state, "voice", "marks.json");

    /// <summary>
    /// Where a layer's profile for a guide is kept, or null when it would be in
    /// a workspace this machine does not have.
    /// </summary>
    public string? PathOf(StyleLayer layer, string name, string guide, string? slug) => layer switch
    {
        StyleLayer.Personal => Path.Combine(_config, "voice", $"{guide}.json"),
        StyleLayer.Named when _workspace is not null => Path.Combine(_workspace, "voice", name, $"{guide}.json"),
        StyleLayer.Codebase when _workspace is not null && slug is { Length: > 0 } =>
            Path.Combine(_workspace, "projects", slug, "voice", $"{guide}.json"),
        _ => null,
    };

    /// <summary>
    /// Where a layer's voice is rendered as a style specialist, or null when it
    /// would be in a workspace this machine does not have.
    /// </summary>
    /// <remarks>
    /// Beside the style's other parts: the workspace's own specialists for the
    /// personal and named styles, the project's for its codebase style. The
    /// personal profile itself stays in user configuration; what is rendered
    /// from it is guidance, not provenance, so it can travel with the workspace
    /// the way <c>style.personal</c> already does.
    /// </remarks>
    public string? SpecialistPath(StyleLayer layer, string name, string? slug) => layer switch
    {
        StyleLayer.Personal or StyleLayer.Named when _workspace is not null =>
            Path.Combine(_workspace, "global", "specialists", $"{VoiceRendering.IdOf(name)}.md"),
        StyleLayer.Codebase when _workspace is not null && slug is { Length: > 0 } =>
            Path.Combine(_workspace, "projects", slug, "specialists", $"{VoiceRendering.IdOf(name)}.md"),
        _ => null,
    };

    /// <summary>
    /// Renders one layer's profile into its style specialist, guarded against
    /// hand edits.
    /// </summary>
    public async Task<OperationResult<VoiceRendered>> RenderAsync(
        StyleLayer layer,
        string name,
        VoiceGuide guide,
        string? slug,
        bool force = false,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(guide);

        if (PathOf(layer, name, guide.Id, slug) is not { } source || SpecialistPath(layer, name, slug) is not { } target)
        {
            return OperationResult<VoiceRendered>.Fail(
                "There is no workspace on this machine, so there is nowhere to render the voice into. Run 'loadout setup' first.",
                ExitCode.ConfigurationInvalid);
        }

        var profile = await ReadProfileAsync(source, ct).ConfigureAwait(false);

        return profile.Failed
            ? OperationResult<VoiceRendered>.Fail(profile.Error!, profile.ExitCode)
            : await VoiceRendering.WriteAsync(target, profile.Value, layer, name, guide, force, dryRun, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The profiles in force for a guide, least specific first, including
    /// layers with nothing in them yet so a caller can say where one would go.
    /// </summary>
    /// <param name="guide">The guide's id.</param>
    /// <param name="chosen">The named style chosen for this work, or null.</param>
    /// <param name="slug">The project, or null outside one.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<OperationResult<IReadOnlyList<VoiceLayer>>> LayersAsync(
        string guide,
        string? chosen,
        string? slug,
        CancellationToken ct = default)
    {
        var layers = new List<VoiceLayer>();

        foreach (var (name, layer) in CodingStyles.InForce(chosen))
        {
            if (PathOf(layer, name, guide, slug) is not { } path)
            {
                continue;
            }

            var read = await ReadProfileAsync(path, ct).ConfigureAwait(false);

            if (read.Failed)
            {
                return OperationResult<IReadOnlyList<VoiceLayer>>.Fail(read.Error!, read.ExitCode);
            }

            layers.Add(new VoiceLayer(layer, name, path, read.Value));
        }

        return OperationResult<IReadOnlyList<VoiceLayer>>.Ok(layers);
    }

    /// <summary>The rules in force: each layer's, a later layer replacing an earlier one's rule by id.</summary>
    public static IReadOnlyList<ResolvedVoiceRule> Resolve(IEnumerable<VoiceLayer> layers)
    {
        var rules = new Dictionary<string, ResolvedVoiceRule>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var layer in layers)
        {
            foreach (var rule in layer.Profile?.Rules ?? [])
            {
                if (!rules.ContainsKey(rule.Id))
                {
                    order.Add(rule.Id);
                }

                rules[rule.Id] = new ResolvedVoiceRule(rule, layer.Layer, layer.Name);
            }
        }

        return order.Select(id => rules[id]).ToList();
    }

    /// <summary>A profile, or null when there is none at that path.</summary>
    public static async Task<OperationResult<VoiceProfile?>> ReadProfileAsync(string path, CancellationToken ct = default) =>
        await ReadAsync<VoiceProfile>(path, ct).ConfigureAwait(false);

    /// <summary>Writes a profile.</summary>
    public static Task<OperationResult> WriteProfileAsync(string path, VoiceProfile profile, CancellationToken ct = default) =>
        WriteAsync(path, profile, ct);

    /// <summary>The local mark list, empty when nothing has been marked.</summary>
    public async Task<OperationResult<VoiceMarkList>> ReadMarksAsync(CancellationToken ct = default)
    {
        var read = await ReadAsync<VoiceMarkList>(MarksPath, ct).ConfigureAwait(false);

        return read.Failed
            ? OperationResult<VoiceMarkList>.Fail(read.Error!, read.ExitCode)
            : OperationResult<VoiceMarkList>.Ok(read.Value ?? new VoiceMarkList());
    }

    /// <summary>Writes the local mark list.</summary>
    public Task<OperationResult> WriteMarksAsync(VoiceMarkList marks, CancellationToken ct = default) =>
        WriteAsync(MarksPath, marks, ct);

    /// <summary>
    /// The local file for one person's running of a guide for one profile:
    /// its cap, carried proposals, rejections and run history.
    /// </summary>
    /// <remarks>
    /// Keyed by profile, and kept in machine state even for a profile that is
    /// shared, because what one person spent and what they have not yet
    /// reviewed is nobody else's business and nothing reads it but them.
    /// </remarks>
    public string StatePath(StyleLayer layer, string name, string guide, string? slug)
    {
        var key = layer switch
        {
            StyleLayer.Personal => "personal",
            StyleLayer.Named => $"named-{name}",
            _ => $"codebase-{slug}",
        };

        return Path.Combine(_state, "voice", "learn", $"{key}-{guide}.json");
    }

    /// <summary>The local learn state, empty before the first run.</summary>
    public static async Task<OperationResult<VoiceLearnState>> ReadStateAsync(string path, CancellationToken ct = default)
    {
        var read = await ReadAsync<VoiceLearnState>(path, ct).ConfigureAwait(false);

        return read.Failed
            ? OperationResult<VoiceLearnState>.Fail(read.Error!, read.ExitCode)
            : OperationResult<VoiceLearnState>.Ok(read.Value ?? new VoiceLearnState());
    }

    /// <summary>Writes the local learn state.</summary>
    public static Task<OperationResult> WriteStateAsync(string path, VoiceLearnState state, CancellationToken ct = default) =>
        WriteAsync(path, state, ct);

    /// <summary>
    /// The hash a mark or citation is pinned to: SHA-256 of the text with its
    /// line endings made the same, so a checkout on another platform does not
    /// lapse every mark.
    /// </summary>
    public static string Hash(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }

    private static async Task<OperationResult<T?>> ReadAsync<T>(string path, CancellationToken ct)
        where T : class
    {
        if (!File.Exists(path))
        {
            return OperationResult<T?>.Ok(null);
        }

        try
        {
            await using var stream = File.OpenRead(path);

            return OperationResult<T?>.Ok(
                await JsonSerializer.DeserializeAsync<T>(stream, Options, ct).ConfigureAwait(false));
        }
        catch (JsonException ex)
        {
            return OperationResult<T?>.Fail($"'{path}' is not a readable voice file: {ex.Message}", ExitCode.InvalidArguments);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<T?>.Fail($"Could not read '{path}': {ex.Message}");
        }
    }

    private static async Task<OperationResult> WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Written beside and moved into place, so an interrupted write
            // cannot leave half a profile behind.
            var temporary = path + ".tmp";

            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Options) + "\n", ct)
                .ConfigureAwait(false);

            File.Move(temporary, path, overwrite: true);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not write '{path}': {ex.Message}");
        }
    }
}
