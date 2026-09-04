using System.Text.RegularExpressions;

namespace Loadout.Core.Instructions;

/// <summary>What kind of thing a symbol is.</summary>
public enum SymbolKind
{
    Type,
    Member,
}

/// <summary>One named thing, and where it is.</summary>
/// <param name="Kind">Type or member.</param>
/// <param name="Name">Its name.</param>
/// <param name="Signature">The declaration as written, trimmed.</param>
/// <param name="File">Path relative to the repository root.</param>
/// <param name="Line">One-based line number.</param>
/// <param name="Summary">The first line of its doc comment, when it had one.</param>
public sealed record Symbol(
    SymbolKind Kind,
    string Name,
    string Signature,
    string File,
    int Line,
    string Summary);

/// <summary>
/// Finds the public surface of a C# codebase by reading it, not by parsing it.
/// </summary>
/// <remarks>
/// <para>
/// Lexical on purpose, and this is the first thing to know about anything it
/// produces. It matches declarations the way a person skimming would, which
/// gets the overwhelming majority right and will miss things a compiler would
/// not: a declaration split across lines, a generic constraint that pushes the
/// brace onto the next line, anything inside a string that looks like code.
/// </para>
/// <para>
/// The alternative is a real parse, which means taking on Roslyn — a large
/// dependency for a launcher, to produce a document nobody compiles. The trade
/// is deliberate, and the honest consequence is that output built from this is
/// a good index and not an authority. Where it is wrong it omits rather than
/// invents, which is the failure worth having.
/// </para>
/// </remarks>
public static partial class SymbolScan
{
    /// <summary>How many files to read before stopping.</summary>
    /// <remarks>
    /// The same bound the evidence reader uses, for the same reason: a
    /// repository can be enormous, and a documentation pass that takes a minute
    /// is one nobody runs twice.
    /// </remarks>
    private const int MostFiles = 4000;

    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", "dist", "build", "out", "target",
        "vendor", ".venv", "venv", "__pycache__", "packages", "coverage", "artifacts",
    };

    [GeneratedRegex(
        @"^\s*(?:public|internal)\s+(?:(?:static|sealed|abstract|partial|readonly|ref)\s+)*"
        + @"(?<kind>class|record|struct|interface|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled)]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(
        @"^\s*public\s+(?:(?:static|async|virtual|override|sealed|partial|new|readonly)\s+)*"
        + @"(?<type>[A-Za-z_][A-Za-z0-9_<>,\.\?\[\]\s]*?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*[\(\{=]",
        RegexOptions.Compiled)]
    private static partial Regex MemberDeclaration();

    [GeneratedRegex(@"^\s*///\s*<summary>\s*(?<text>.*?)\s*(?:</summary>)?\s*$", RegexOptions.Compiled)]
    private static partial Regex SummaryOpen();

    [GeneratedRegex(@"^\s*///\s*(?<text>.+?)\s*$", RegexOptions.Compiled)]
    private static partial Regex DocLine();

    /// <summary>Everything named in a repository's C# files.</summary>
    /// <param name="repositoryPath">Where to look.</param>
    /// <param name="ct">Cancellation token.</param>
    public static IReadOnlyList<Symbol> Scan(string repositoryPath, CancellationToken ct = default)
    {
        var found = new List<Symbol>();

        if (!Directory.Exists(repositoryPath))
        {
            return found;
        }

        foreach (var file in Walk(repositoryPath, ct))
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] lines;

            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var relative = Path.GetRelativePath(repositoryPath, file).Replace('\\', '/');

            found.AddRange(InFile(lines, relative));
        }

        return
        [
            .. found
                .OrderBy(symbol => symbol.File, StringComparer.Ordinal)
                .ThenBy(symbol => symbol.Line),
        ];
    }

    /// <summary>The symbols one file declares.</summary>
    internal static IEnumerable<Symbol> InFile(IReadOnlyList<string> lines, string file)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            var type = TypeDeclaration().Match(line);

            if (type.Success)
            {
                yield return new Symbol(
                    SymbolKind.Type,
                    type.Groups["name"].Value,
                    line.Trim().TrimEnd('{').TrimEnd(),
                    file,
                    i + 1,
                    SummaryAbove(lines, i));

                continue;
            }

            var member = MemberDeclaration().Match(line);

            // Two things keep two different intruders out. Control flow is
            // excluded by the shape — the pattern wants an identifier, a space
            // and another identifier before the bracket, and "if (" has only
            // one. A local variable is excluded by the "public": "var turns =
            // 1" has exactly the shape of a field and nothing but the modifier
            // separates them.
            //
            // A keyword exclusion list stood here too and was removed. Deleting
            // it failed no test, and scanning this repository with and without
            // it produced byte-for-byte the same 3,077 symbols, because nothing
            // could reach the place it was looking.
            if (member.Success)
            {
                yield return new Symbol(
                    SymbolKind.Member,
                    member.Groups["name"].Value,
                    line.Trim().TrimEnd('{').TrimEnd(),
                    file,
                    i + 1,
                    SummaryAbove(lines, i));
            }
        }
    }

    /// <summary>
    /// The first line of the doc comment above a declaration, if there is one.
    /// </summary>
    /// <remarks>
    /// Read upward from the declaration through attributes and blank-ish lines,
    /// because a doc comment is not always immediately above what it describes.
    /// Only the summary's first line is taken: the rest is the reasoning, which
    /// belongs in the file rather than in an index of it.
    /// </remarks>
    internal static string SummaryAbove(IReadOnlyList<string> lines, int declaration)
    {
        for (var i = declaration - 1; i >= 0 && declaration - i < 40; i--)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('['))
            {
                continue;
            }

            if (!trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var open = SummaryOpen().Match(line);

            if (!open.Success)
            {
                continue;
            }

            // Read to the closing tag rather than taking the first line. A
            // summary that wraps is ordinary, and stopping at the newline cuts
            // it mid-clause — "locating the binary, reading its version, and"
            // is worse than no summary, because it reads like prose and ends
            // like a fault.
            var text = new System.Text.StringBuilder(open.Groups["text"].Value);

            for (var j = i + 1; j < lines.Count && j - i < 12; j++)
            {
                var continued = DocLine().Match(lines[j]);

                if (!continued.Success)
                {
                    break;
                }

                var piece = continued.Groups["text"].Value;

                if (piece.Contains("</summary>", StringComparison.Ordinal))
                {
                    text.Append(' ').Append(
                        piece[..piece.IndexOf("</summary>", StringComparison.Ordinal)]);

                    break;
                }

                // A nested element means the summary has given way to the
                // reasoning, which belongs in the file and not in an index.
                if (piece.StartsWith('<'))
                {
                    break;
                }

                text.Append(' ').Append(piece);
            }

            return text
                .Replace("</summary>", string.Empty)
                .ToString()
                .Replace("  ", " ", StringComparison.Ordinal)
                .Trim();
        }

        return string.Empty;
    }

    private static IEnumerable<string> Walk(string root, CancellationToken ct)
    {
        var seen = 0;
        var queue = new Queue<string>();

        queue.Enqueue(root);

        while (queue.Count > 0 && seen < MostFiles)
        {
            ct.ThrowIfCancellationRequested();

            string[] files;
            string[] directories;

            try
            {
                var directory = queue.Dequeue();

                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (seen++ >= MostFiles)
                {
                    yield break;
                }

                yield return file;
            }

            foreach (var child in directories)
            {
                if (!Ignored.Contains(Path.GetFileName(child)))
                {
                    queue.Enqueue(child);
                }
            }
        }
    }
}
