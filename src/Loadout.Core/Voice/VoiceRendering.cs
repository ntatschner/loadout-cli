using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Loadout.Core.Instructions;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Voice;

namespace Loadout.Core.Voice;

/// <summary>What rendering a profile did to its specialist file.</summary>
public enum VoiceRenderState
{
    /// <summary>Written, new or changed.</summary>
    Written,

    /// <summary>Already said exactly this, so left alone.</summary>
    Unchanged,

    /// <summary>The profile has no rules any more, so the rendered file was taken away.</summary>
    Removed,

    /// <summary>Nothing to render and nothing rendered before.</summary>
    Nothing,

    /// <summary>
    /// The file was changed by hand, or was never rendered, so it was left as it is.
    /// </summary>
    HandEdited,
}

/// <summary>What rendering did, and where.</summary>
/// <param name="Id">The specialist's id, such as <c>style.personal.voice</c>.</param>
/// <param name="Path">The file.</param>
/// <param name="State">What happened to it.</param>
/// <param name="EstimatedTokens">What it costs on every launch the style is in force, or zero when there is none.</param>
public sealed record VoiceRendered(string Id, string Path, VoiceRenderState State, int EstimatedTokens);

/// <summary>
/// Turns a voice profile into the style specialist a launch loads.
/// </summary>
/// <remarks>
/// <para>
/// The profile is the source of truth and the specialist is made from it, so
/// loading and budgeting go through the machinery every other specialist
/// already goes through. It is a style part, <c>style.&lt;name&gt;.voice</c>,
/// so it is in force exactly where its style is, and layers the way styles do.
/// </para>
/// <para>
/// The guard is a hash. Each rendered file carries the hash of what was
/// rendered, in a frontmatter key the library ignores. A file whose text no
/// longer matches its hash was edited by hand, and one with no hash was never
/// rendered at all; either is left alone unless the person says to replace
/// it, because silently overwriting somebody's edit is how they find out the
/// file was generated.
/// </para>
/// <para>
/// Only statements are rendered. Citations quote the person's writing and say
/// where it came from, and none of that is guidance: it would cost tokens on
/// every launch and put provenance into a file that travels with the
/// workspace.
/// </para>
/// </remarks>
public static partial class VoiceRendering
{
    /// <summary>The frontmatter key holding the hash of what was rendered.</summary>
    public const string StampKey = "voice_rendered";

    [GeneratedRegex(@"^voice_rendered:[ \t]*(?<hash>[0-9a-f]{64})[ \t]*\r?\n", RegexOptions.Multiline)]
    private static partial Regex Stamp();

    /// <summary>The id a style's voice is rendered under.</summary>
    public static string IdOf(string name) => $"style.{name}.{CodingStyles.VoiceSegment}";

    /// <summary>The specialist text for a profile, stamped with its hash.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="layer">Its layer.</param>
    /// <param name="name">Its style's name.</param>
    /// <param name="guide">The guide, for the surfaces a rule with none applies to.</param>
    public static string Render(VoiceProfile profile, StyleLayer layer, string name, VoiceGuide guide) =>
        Render([(guide, profile)], layer, name);

    /// <summary>The specialist text for every guide's profile at a layer, stamped with its hash.</summary>
    /// <param name="learned">Each guide with its profile, in the order they are to be read.</param>
    /// <param name="layer">Their layer.</param>
    /// <param name="name">Their style's name.</param>
    public static string Render(IReadOnlyList<(VoiceGuide Guide, VoiceProfile Profile)> learned, StyleLayer layer, string name)
    {
        ArgumentNullException.ThrowIfNull(learned);

        var text = new StringBuilder();

        text.Append("---\n");
        text.Append(CultureInfo.InvariantCulture, $"id: {IdOf(name)}\n");
        text.Append("kind: style\n");
        text.Append(CultureInfo.InvariantCulture, $"title: {Title(layer, name)}\n");
        text.Append(CultureInfo.InvariantCulture,
            $"summary: How {Whose(layer, name)} docs, commit messages and pull request descriptions are written.\n");
        text.Append("# Rendered by 'loadout voice' from a learned profile. Change the profile, not this file:\n");
        text.Append("# an edit here is refused on the next render rather than overwritten.\n");
        text.Append("---\n\n");

        text.Append("## Rules\n\n");
        text.Append(CultureInfo.InvariantCulture,
            $"Learned from {Whose(layer, name)} own writing and accepted rule by rule ({string.Join(", ", learned.Select(l => l.Profile.Guide))}).\n\n");

        foreach (var (guide, profile) in learned)
        {
            foreach (var rule in profile.Rules)
            {
                text.Append("- ").Append(rule.Statement.Trim());

                if (Limited(rule, guide) is { } surfaces)
                {
                    text.Append(CultureInfo.InvariantCulture, $" *({surfaces} only)*");
                }

                text.Append('\n');
            }
        }

        text.Append("\n## Before you commit or open a pull request\n\n");
        text.Append("Check what you wrote, fix what is found once, and leave it there:\n\n");
        text.Append("- A commit message: `loadout voice check --message \"<message>\"`\n");
        text.Append("- A pull request description: `loadout voice check --surface pr --stdin`\n");
        text.Append("- Documentation you changed: `loadout voice check <files>`\n\n");
        text.Append(
            "Run it once more after fixing. If anything is still reported, commit anyway and list what remains "
            + "in your reply, never in the commit message or the pull request. A newly learned rule should not "
            + "hold up work, and the reply is where somebody can judge it.\n");

        var unstamped = text.ToString();
        var hash = VoiceStore.Hash(unstamped);

        return Insert(unstamped, hash);
    }

    /// <summary>
    /// Whether a rendered file has been changed since it was rendered, or was
    /// never rendered at all.
    /// </summary>
    public static bool IsHandEdited(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var match = Stamp().Match(text);

        return !match.Success
            || !string.Equals(match.Groups["hash"].Value, VoiceStore.Hash(text.Remove(match.Index, match.Length)), StringComparison.Ordinal);
    }

    /// <summary>
    /// Renders a profile into its specialist file, guarded against hand edits.
    /// </summary>
    /// <param name="path">The specialist file.</param>
    /// <param name="profile">The profile, or null when there is none.</param>
    /// <param name="layer">Its layer.</param>
    /// <param name="name">Its style's name.</param>
    /// <param name="guide">The guide.</param>
    /// <param name="force">Replace a hand-edited file anyway.</param>
    /// <param name="dryRun">Say what would happen and change nothing.</param>
    /// <param name="ct">Cancellation.</param>
    public static Task<OperationResult<VoiceRendered>> WriteAsync(
        string path,
        VoiceProfile? profile,
        StyleLayer layer,
        string name,
        VoiceGuide guide,
        bool force = false,
        bool dryRun = false,
        CancellationToken ct = default) =>
        WriteAsync(path, profile is { Rules.Count: > 0 } ? [(guide, profile)] : [], layer, name, force, dryRun, ct);

    /// <summary>
    /// Renders every guide's profile at a layer into its specialist file,
    /// guarded against hand edits.
    /// </summary>
    /// <param name="path">The specialist file.</param>
    /// <param name="learned">Each guide with rules, and its profile.</param>
    /// <param name="layer">Their layer.</param>
    /// <param name="name">Their style's name.</param>
    /// <param name="force">Replace a hand-edited file anyway.</param>
    /// <param name="dryRun">Say what would happen and change nothing.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<OperationResult<VoiceRendered>> WriteAsync(
        string path,
        IReadOnlyList<(VoiceGuide Guide, VoiceProfile Profile)> learned,
        StyleLayer layer,
        string name,
        bool force = false,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(learned);

        learned = [.. learned.Where(l => l.Profile.Rules.Count > 0)];

        var id = IdOf(name);

        try
        {
            var existing = File.Exists(path)
                ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false)
                : null;

            if (existing is not null && !force && IsHandEdited(existing))
            {
                return OperationResult<VoiceRendered>.Ok(new VoiceRendered(id, path, VoiceRenderState.HandEdited, 0));
            }

            if (learned.Count == 0)
            {
                if (existing is null)
                {
                    return OperationResult<VoiceRendered>.Ok(new VoiceRendered(id, path, VoiceRenderState.Nothing, 0));
                }

                if (!dryRun)
                {
                    File.Delete(path);
                }

                return OperationResult<VoiceRendered>.Ok(new VoiceRendered(id, path, VoiceRenderState.Removed, 0));
            }

            var text = Render(learned, layer, name);
            var tokens = Tokens(text);

            if (existing is not null && string.Equals(
                    existing.Replace("\r\n", "\n", StringComparison.Ordinal), text, StringComparison.Ordinal))
            {
                return OperationResult<VoiceRendered>.Ok(new VoiceRendered(id, path, VoiceRenderState.Unchanged, tokens));
            }

            if (!dryRun)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

                var temporary = path + ".tmp";

                await File.WriteAllTextAsync(temporary, text, ct).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: true);
            }

            return OperationResult<VoiceRendered>.Ok(new VoiceRendered(id, path, VoiceRenderState.Written, tokens));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<VoiceRendered>.Fail($"Could not render '{path}': {ex.Message}", ExitCode.GeneralFailure);
        }
    }

    /// <summary>
    /// What the library will count it as: the body's bytes over four, the same
    /// estimate every specialist is budgeted by.
    /// </summary>
    private static int Tokens(string text)
    {
        var body = text[(text.IndexOf("\n---\n", StringComparison.Ordinal) + 5)..].Trim('\r', '\n');

        return (int)Math.Ceiling(Encoding.UTF8.GetByteCount(body) / 4.0);
    }

    private static string Insert(string unstamped, string hash)
    {
        // After the summary, inside the frontmatter, where the library ignores it.
        var at = unstamped.IndexOf("\n# Rendered by", StringComparison.Ordinal) + 1;

        return unstamped.Insert(at, $"{StampKey}: {hash}\n");
    }

    private static string? Limited(VoiceRule rule, VoiceGuide guide)
    {
        var surfaces = rule.Surfaces.Where(guide.Surfaces.Contains).Distinct().ToList();

        if (surfaces.Count == 0 || surfaces.Count >= guide.Surfaces.Count)
        {
            return null;
        }

        return string.Join(" and ", surfaces.Select(s => s switch
        {
            VoiceGuides.Commit => "commit messages",
            VoiceGuides.PullRequest => "pull requests",
            VoiceGuides.Document => "docs",
            _ => s,
        }));
    }

    private static string Title(StyleLayer layer, string name) => layer switch
    {
        StyleLayer.Personal => "Personal voice",
        StyleLayer.Codebase => "Codebase voice",
        _ => $"{char.ToUpperInvariant(name[0])}{name[1..]} voice",
    };

    private static string Whose(StyleLayer layer, string name) => layer switch
    {
        StyleLayer.Personal => "your",
        StyleLayer.Codebase => "this codebase's",
        _ => $"the '{name}' style's",
    };
}
