namespace Loadout.Core.Security;

/// <summary>A file about to be committed that looks like it holds a credential.</summary>
/// <param name="Path">Where it is, relative to the workspace.</param>
/// <param name="Pattern">
/// The name of the pattern that matched, never the text that matched it. A
/// finding that quoted the value would copy the credential into the console, the
/// scrollback and whatever is capturing them.
/// </param>
public sealed record SecretFinding(string Path, string Pattern);

/// <summary>
/// Checks what is about to be committed to the workspace.
/// </summary>
/// <remarks>
/// <para>
/// Memory has been screened at the point of writing since it existed, on the
/// reasoning that a credential committed is a credential disclosed and an audit
/// finding afterwards does not undo it. Everything else in the workspace was
/// not: handoffs, project instructions, context notes, profiles and MCP server
/// definitions are all committed by the same exit policy, and it pushes without
/// asking when <c>sync_exit</c> is <c>always</c>.
/// </para>
/// <para>
/// So the check moves to the last place everything passes through. It catches
/// what an agent wrote directly as well as what went through this launcher,
/// which the write-time checks by their nature cannot.
/// </para>
/// <para>
/// Text only. A workspace holds instructions, memory and configuration; a file
/// with a zero byte near the top of it is not one of those, and running a
/// credential pattern across a binary produces noise rather than findings.
/// </para>
/// </remarks>
internal static class WorkspaceSecrets
{
    /// <summary>How much of a file to look at before deciding it is not text.</summary>
    private const int SniffBytes = 8000;

    /// <summary>
    /// Beyond this a file is not the sort of thing a workspace holds, and
    /// reading it on every save would cost more than it protects.
    /// </summary>
    private const long LargestFile = 8 * 1024 * 1024;

    /// <summary>
    /// The files among these that look like they carry a credential.
    /// </summary>
    /// <param name="root">The workspace clone.</param>
    /// <param name="paths">Changed paths, relative to the root.</param>
    /// <param name="ct">Cancellation token.</param>
    public static IReadOnlyList<SecretFinding> Scan(
        string root,
        IReadOnlyList<string> paths,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var findings = new List<SecretFinding>();

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();

            var file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

            string text;

            try
            {
                // A deleted file is a change with nothing to read, which is the
                // ordinary shape of a cleanup rather than anything suspicious.
                if (!File.Exists(file) || new FileInfo(file).Length > LargestFile)
                {
                    continue;
                }

                if (LooksBinary(file))
                {
                    continue;
                }

                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or PathTooLongException
                or ArgumentException)
            {
                // Unreadable here means unreadable to the scan, not safe. It is
                // still going to be committed, so say so rather than pass it.
                //
                // The list is wide because the alternative is worse than a false
                // finding: an unexpected path shape throwing out of here would
                // fail the save with a stack trace instead of a sentence, on the
                // way out of a session that has already done its work.
                findings.Add(new SecretFinding(path, "unreadable"));

                continue;
            }

            foreach (var pattern in SecretScanner.Match(text))
            {
                findings.Add(new SecretFinding(path, pattern));
            }
        }

        return findings;
    }

    /// <summary>
    /// What to tell somebody whose save was refused.
    /// </summary>
    /// <remarks>
    /// Names the file and the pattern and stops there. Somebody who has to be
    /// told which line it was on can open the file; nobody needs the value
    /// repeated to them in a terminal.
    /// </remarks>
    public static string Explain(IReadOnlyList<SecretFinding> findings)
    {
        var listed = findings
            .Select(finding => $"  {finding.Path} — {finding.Pattern}")
            .Distinct(StringComparer.Ordinal);

        return "The workspace was not saved. These changes look like they carry credentials, "
            + "and the workspace is a Git repository that gets pushed:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, listed)
            + Environment.NewLine
            + "Take the value out and put it in the credential store with 'loadout secret set', "
            + "then save again.";
    }

    private static bool LooksBinary(string file)
    {
        using var stream = File.OpenRead(file);

        Span<byte> head = stackalloc byte[SniffBytes];

        var read = stream.Read(head);

        return head[..read].IndexOf((byte)0) >= 0;
    }
}
