using System.Text.RegularExpressions;

namespace Loadout.Core.Instructions;

/// <summary>How a language writes the comment that documents a declaration.</summary>
public enum DocStyle
{
    /// <summary>C#: <c>///</c> lines carrying XML, with a summary and remarks.</summary>
    XmlSlashes,

    /// <summary>Rust: <c>///</c> lines carrying Markdown.</summary>
    Slashes,

    /// <summary>Go: <c>//</c> lines directly above, opening with the name.</summary>
    DoubleSlash,

    /// <summary>Python, Ruby, shell, PowerShell, Terraform: <c>#</c> lines above.</summary>
    Hash,

    /// <summary>SQL: <c>--</c> lines above.</summary>
    DoubleDash,

    /// <summary>Java, TypeScript, Kotlin, Swift, PHP, C: a <c>/** ... */</c> block above.</summary>
    Block,

    /// <summary>Python: a string literal on the line after the declaration.</summary>
    DocstringBelow,
}

/// <summary>
/// One language's declaration grammar, as far as a line at a time can see it.
/// </summary>
/// <param name="Id">Short name, matching the language specialist's where one exists.</param>
/// <param name="Name">What to call it to a person.</param>
/// <param name="Extensions">File extensions, lowercased with the dot.</param>
/// <param name="Types">
/// Matches a line declaring a type, with a <c>name</c> group. Null for a
/// language with no types worth the name.
/// </param>
/// <param name="Members">Matches a line declaring a function, method or the like, with a <c>name</c> group.</param>
/// <param name="Docs">Where the documenting comment sits and what it looks like.</param>
public sealed record SymbolLanguage(
    string Id,
    string Name,
    IReadOnlyList<string> Extensions,
    Regex? Types,
    Regex Members,
    DocStyle Docs);

/// <summary>
/// The languages the scan reads, and how to read each.
/// </summary>
/// <remarks>
/// <para>
/// Each entry is a pair of line patterns and a comment style, and nothing
/// more: no parser, no tree, no dependency on a grammar somebody else keeps.
/// That is the same trade the C# scan made and for the same reason. A pattern
/// finds what a person skimming would find, and where it is wrong it leaves a
/// declaration out rather than inventing one — a declaration split across
/// lines, a name inside a string that looks like code.
/// </para>
/// <para>
/// What counts as the surface differs by language and is taken from what the
/// line itself says. Go exports by capital letter, so only those are read;
/// Rust marks <c>pub</c>; Python names a private thing with an underscore;
/// Java, Kotlin, Swift and PHP say <c>private</c> where they mean it, and the
/// modifier lists below leave that word out, so a line carrying it never
/// reaches the keyword the pattern is waiting for. Where a
/// language puts visibility somewhere other than the declaring line — Ruby's
/// <c>private</c> is a statement, TypeScript's <c>export</c> may be a list at
/// the bottom — everything declared is read, because the alternative is
/// guessing.
/// </para>
/// </remarks>
public static class SymbolLanguages
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    /// <summary>Every language the scan reads, in the order a listing shows them.</summary>
    public static readonly IReadOnlyList<SymbolLanguage> All =
    [
        new(
            "csharp",
            "C#",
            [".cs"],
            new Regex(
                @"^\s*(?:public|internal)\s+(?:(?:static|sealed|abstract|partial|readonly|ref)\s+)*"
                + @"(?<kind>class|record|struct|interface|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
                Options),
            new Regex(
                @"^\s*public\s+(?:(?:static|async|virtual|override|sealed|partial|new|readonly)\s+)*"
                + @"(?<type>[A-Za-z_][A-Za-z0-9_<>,\.\?\[\]\s]*?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*[\(\{=]",
                Options),
            DocStyle.XmlSlashes),

        new(
            "typescript",
            "TypeScript and JavaScript",
            [".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs"],
            new Regex(
                @"^\s*(?:export\s+)?(?:default\s+)?(?:declare\s+)?(?:abstract\s+)?"
                + @"(?<kind>class|interface|enum|type|namespace)\s+(?<name>[A-Za-z_$][\w$]*)",
                Options),
            new Regex(
                // A function statement, an arrow function bound to a name, or a
                // method inside a class body. The method form is the one that
                // needs guarding: "if (x) {" has its shape, so the control
                // keywords are excluded by name, and it has to be indented so a
                // top-level call is not taken for a declaration.
                @"^(?:\s*(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s*\*?\s*(?<name>[A-Za-z_$][\w$]*)"
                + @"|\s*(?:export\s+)?(?:const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*(?::[^=]+)?=\s*(?:async\s*)?(?:\([^)]*\)|[A-Za-z_$][\w$]*)\s*(?::\s*[^=]+)?=>"
                + @"|\s+(?:(?:public|private|protected|static|async|readonly|override|abstract|get|set)\s+)*"
                + @"(?!if\b|for\b|while\b|switch\b|catch\b|function\b|return\b|constructor\b|super\b|new\b)"
                // A call whose last argument is a callback has a method's
                // shape too: describe("thing", function () { is one, and a
                // test suite would index every case. A parameter list holds
                // no string literal and no function, so either says call.
                + @"(?<name>[A-Za-z_$][\w$]*)\s*(?:<[^>]*>)?\((?![^)]*(?:[""'`]|\bfunction\b|=>))[^)]*\)\s*(?::\s*[^{;=]+)?\s*\{)",
                Options),
            DocStyle.Block),

        new(
            "python",
            "Python",
            [".py", ".pyi"],
            new Regex(@"^\s*class\s+(?<name>(?!_)\w+)", Options),
            new Regex(@"^\s*(?:async\s+)?def\s+(?<name>(?!_)\w+)", Options),
            DocStyle.DocstringBelow),

        new(
            "go",
            "Go",
            [".go"],
            new Regex(@"^type\s+(?<name>[A-Z]\w*)\b", Options),
            new Regex(@"^func\s+(?:\([^)]*\)\s*)?(?<name>[A-Z]\w*)\s*[\(\[]", Options),
            DocStyle.DoubleSlash),

        new(
            "rust",
            "Rust",
            [".rs"],
            new Regex(
                @"^\s*pub(?:\([^)]*\))?\s+(?<kind>struct|enum|trait|type|union|mod)\s+(?<name>\w+)",
                Options),
            new Regex(
                @"^\s*pub(?:\([^)]*\))?\s+(?:(?:const|async|unsafe|extern\s+""[^""]*"")\s+)*fn\s+(?<name>\w+)",
                Options),
            DocStyle.Slashes),

        new(
            "java",
            "Java",
            [".java"],
            new Regex(
                @"^\s*(?:(?:public|protected|static|final|abstract|sealed|non-sealed|strictfp)\s+)*"
                + @"(?<kind>class|interface|enum|record|@interface)\s+(?<name>\w+)",
                Options),
            new Regex(
                @"^\s*(?:(?:public|protected|static|final|abstract|synchronized|native|default|strictfp)\s+)*"
                + @"(?:<[^>]+>\s+)?(?!return\b|new\b|throw\b|else\b|case\b)(?<type>[\w<>\[\],.?]+)\s+(?<name>\w+)\s*\([^)]*\)\s*(?:throws\s+[\w.,\s]+)?\s*[\{;]",
                Options),
            DocStyle.Block),

        new(
            "kotlin",
            "Kotlin",
            [".kt", ".kts"],
            new Regex(
                @"^\s*(?:(?:public|internal|protected|abstract|open|sealed|data|enum|annotation|inner|value|final|inline)\s+)*"
                + @"(?<kind>class|interface|object)\s+(?<name>\w+)",
                Options),
            new Regex(
                @"^\s*(?:(?:public|internal|protected|open|override|abstract|suspend|inline|operator|infix|tailrec|external)\s+)*"
                + @"fun\s+(?:<[^>]+>\s+)?(?:[\w.<>?]+\.)?(?<name>\w+)\s*\(",
                Options),
            DocStyle.Block),

        new(
            "swift",
            "Swift",
            [".swift"],
            new Regex(
                @"^\s*(?:(?:public|open|internal|final|indirect|@\w+)\s+)*"
                + @"(?<kind>class|struct|enum|protocol|extension|actor)\s+(?<name>\w+)",
                Options),
            new Regex(
                @"^\s*(?:(?:public|open|internal|static|class|final|override|mutating|@\w+)\s+)*"
                + @"func\s+(?<name>\w+)",
                Options),
            DocStyle.Slashes),

        new(
            "ruby",
            "Ruby",
            [".rb", ".rake"],
            new Regex(@"^\s*(?<kind>class|module)\s+(?<name>[A-Z][\w:]*)", Options),
            new Regex(@"^\s*def\s+(?:self\.)?(?<name>[a-z_]\w*[?!=]?)", Options),
            DocStyle.Hash),

        new(
            "php",
            "PHP",
            [".php"],
            new Regex(
                @"^\s*(?:(?:abstract|final|readonly)\s+)*(?<kind>class|interface|trait|enum)\s+(?<name>\w+)",
                Options),
            new Regex(
                @"^\s*(?:(?:public|protected|static|abstract|final)\s+)*function\s+&?(?<name>\w+)",
                Options),
            DocStyle.Block),

        new(
            "c",
            "C and C++",
            [".c", ".h", ".cpp", ".hpp", ".cc", ".hh", ".cxx", ".hxx"],
            new Regex(
                @"^\s*(?:typedef\s+)?(?<kind>struct|class|enum|union|namespace)\s+(?<name>\w+)\s*(?:\{|:|$)",
                Options),
            new Regex(
                // A definition at column zero. Indented calls, prototypes inside
                // a class and control flow all fail one of: no indentation, a
                // type before the name, not a keyword.
                @"^(?!if\b|for\b|while\b|switch\b|return\b|else\b|typedef\b|#)"
                + @"(?<type>(?:[A-Za-z_][\w:<>,]*(?:\s*[*&]+\s*|\s+))+)(?:[\w:]+::)?(?<name>[A-Za-z_]\w*)\s*\(",
                Options),
            DocStyle.Block),

        new(
            "powershell",
            "PowerShell",
            [".ps1", ".psm1"],
            new Regex(@"^\s*(?<kind>class|enum)\s+(?<name>\w+)", Options),
            new Regex(@"^\s*(?:function|filter|workflow)\s+(?<name>[\w\-:]+)", Options),
            DocStyle.Hash),

        new(
            "bash",
            "Shell",
            [".sh", ".bash", ".zsh"],
            null,
            new Regex(@"^\s*(?:function\s+(?<name>[\w\-]+)\s*(?:\(\))?|(?<name>[\w\-]+)\s*\(\)\s*\{?)", Options),
            DocStyle.Hash),

        new(
            "terraform",
            "Terraform",
            [".tf"],
            new Regex(
                @"^\s*(?<kind>resource|data|module)\s+""(?<first>[\w\-]+)""(?:\s+""(?<second>[\w\-]+)"")?",
                Options),
            new Regex(@"^\s*(?<kind>variable|output)\s+""(?<name>[\w\-]+)""", Options),
            DocStyle.Hash),

        new(
            "sql",
            "SQL",
            [".sql"],
            new Regex(
                @"^\s*create\s+(?:or\s+replace\s+)?(?:(?:temp|temporary|materialized|unlogged)\s+)?"
                + @"(?<kind>table|view|type|schema)\s+(?:if\s+not\s+exists\s+)?(?<name>[\w.""\[\]]+)",
                Options | RegexOptions.IgnoreCase),
            new Regex(
                @"^\s*create\s+(?:or\s+replace\s+)?(?:(?:unique|clustered|nonclustered)\s+)?"
                + @"(?<kind>function|procedure|proc|trigger|index)\s+(?:if\s+not\s+exists\s+)?(?<name>[\w.""\[\]]+)",
                Options | RegexOptions.IgnoreCase),
            DocStyle.DoubleDash),
    ];

    /// <summary>The language a file is written in, by its extension, or null.</summary>
    public static SymbolLanguage? For(string file)
    {
        var extension = Path.GetExtension(file);

        if (extension.Length == 0)
        {
            return null;
        }

        foreach (var language in All)
        {
            if (language.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return language;
            }
        }

        return null;
    }

    /// <summary>The names, for telling somebody what is and is not read.</summary>
    public static string Names => string.Join(", ", All.Select(language => language.Name));

    /// <summary>
    /// The specialist ids that mean a repository has files the scan reads.
    /// </summary>
    /// <remarks>
    /// Not every language here has a specialist, so this under-reports rather
    /// than over-reports; a Swift repository is read by the scan and not named
    /// by this. That is the right direction to be wrong in for a line paid for
    /// on every launch.
    /// </remarks>
    public static IReadOnlyList<string> SpecialistIds =>
        [.. All.Select(language => "language." + language.Id)];

    /// <summary>The name a matched declaration gives its symbol.</summary>
    /// <remarks>
    /// Almost always the <c>name</c> group. Terraform is the exception: a
    /// resource is addressed as <c>type.name</c> everywhere else in the
    /// language, so that is the name somebody will look for.
    /// </remarks>
    internal static string NameOf(Match match)
    {
        if (match.Groups["name"].Success)
        {
            return match.Groups["name"].Value;
        }

        var first = match.Groups["first"].Value;
        var second = match.Groups["second"];

        return second.Success ? first + "." + second.Value : first;
    }

    /// <summary>
    /// The first line of what documents a declaration, in this language's style.
    /// </summary>
    /// <remarks>
    /// The C# reader lives with the C# scan, because its XML has a summary and
    /// remarks to tell apart. Everything else is a comment in one of a few
    /// shapes, and the first sentence of it is the summary: that is Go's rule
    /// written down, and every other language's convention in practice.
    /// </remarks>
    internal static string Summary(IReadOnlyList<string> lines, int declaration, DocStyle style) =>
        style switch
        {
            DocStyle.XmlSlashes => SymbolScan.SummaryAbove(lines, declaration),
            DocStyle.Slashes => FirstSentence(LinesAbove(lines, declaration, "///")),
            DocStyle.DoubleSlash => FirstSentence(LinesAbove(lines, declaration, "//")),
            DocStyle.Hash => FirstSentence(HashAbove(lines, declaration)),
            DocStyle.DoubleDash => FirstSentence(LinesAbove(lines, declaration, "--")),
            DocStyle.Block => FirstSentence(BlockAbove(lines, declaration)),
            DocStyle.DocstringBelow => FirstSentence(DocstringBelow(lines, declaration)),
            _ => string.Empty,
        };

    /// <summary>
    /// A comment written as lines with one prefix, read upward from a declaration.
    /// </summary>
    /// <remarks>
    /// Attributes and decorators sit between a comment and what it documents
    /// in most of these languages, so lines beginning <c>@</c>, <c>[</c> or
    /// <c>#[</c> are stepped over. Anything else that is not the comment ends
    /// the search: a blank line above a Go declaration means the comment
    /// above that is somebody else's.
    /// </remarks>
    private static List<string> LinesAbove(IReadOnlyList<string> lines, int declaration, string prefix)
    {
        var collected = new List<string>();

        for (var i = declaration - 1; i >= 0 && declaration - i < 40; i--)
        {
            var trimmed = lines[i].Trim();

            if (collected.Count == 0 && IsDecoration(trimmed))
            {
                continue;
            }

            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                break;
            }

            // A '////' or '//!' is a rule or a module comment, not this.
            var text = trimmed[prefix.Length..];

            if (text.StartsWith(prefix[^1]) || text.StartsWith('!'))
            {
                break;
            }

            collected.Insert(0, text.Trim());
        }

        return collected;
    }

    /// <summary>
    /// <c>#</c> comments, with PowerShell's help block read for its synopsis.
    /// </summary>
    private static List<string> HashAbove(IReadOnlyList<string> lines, int declaration)
    {
        var i = declaration - 1;

        while (i >= 0 && (lines[i].Trim().Length == 0 || IsDecoration(lines[i].Trim())))
        {
            i--;
        }

        if (i >= 0 && lines[i].Trim() == "#>")
        {
            // Read upward inside the block. The lines under a directive are
            // gathered until the directive itself is met, and only .SYNOPSIS
            // keeps them.
            var gathered = new List<string>();

            for (var j = i - 1; j >= 0 && i - j < 60; j--)
            {
                var trimmed = lines[j].Trim();

                if (trimmed.StartsWith("<#", StringComparison.Ordinal))
                {
                    break;
                }

                if (trimmed.StartsWith('.'))
                {
                    if (trimmed.Equals(".SYNOPSIS", StringComparison.OrdinalIgnoreCase))
                    {
                        gathered.Reverse();

                        return gathered;
                    }

                    gathered.Clear();

                    continue;
                }

                if (trimmed.Length > 0)
                {
                    gathered.Add(trimmed);
                }
            }

            return [];
        }

        return LinesAbove(lines, declaration, "#");
    }

    /// <summary>A <c>/** ... */</c> block directly above, without its tags.</summary>
    private static List<string> BlockAbove(IReadOnlyList<string> lines, int declaration)
    {
        var i = declaration - 1;

        while (i >= 0 && (lines[i].Trim().Length == 0 || IsDecoration(lines[i].Trim())))
        {
            i--;
        }

        if (i < 0)
        {
            return [];
        }

        var last = lines[i].Trim();

        if (!last.EndsWith("*/", StringComparison.Ordinal))
        {
            return [];
        }

        var collected = new List<string>();

        for (var j = i; j >= 0 && i - j < 60; j--)
        {
            var trimmed = lines[j].Trim();
            var opened = trimmed.StartsWith("/**", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal);

            var text = trimmed;

            if (opened)
            {
                text = text.TrimStart('/').TrimStart('*');
            }

            if (text.EndsWith("*/", StringComparison.Ordinal))
            {
                text = text[..^2];
            }

            text = text.TrimStart().TrimStart('*').Trim();

            // A tag starts the part that is not the summary.
            if (text.StartsWith('@'))
            {
                collected.Clear();
            }
            else if (text.Length > 0)
            {
                collected.Insert(0, text);
            }

            if (opened)
            {
                return collected;
            }
        }

        return [];
    }

    /// <summary>The string literal on the line after a Python declaration.</summary>
    private static List<string> DocstringBelow(IReadOnlyList<string> lines, int declaration)
    {
        // The signature may wrap; the body starts after the line that closes
        // it with a colon. A signature closed with something after the colon
        // — "def name(self): return self._name" — has its body on the same
        // line and no docstring, and reading on would hand it the next
        // block's.
        var i = declaration;

        var depth = 0;
        var closed = false;

        while (i < lines.Count && i - declaration < 10 && !closed)
        {
            var line = lines[i];
            var comment = line.IndexOf('#');
            var code = (comment >= 0 ? line[..comment] : line).TrimEnd();

            // The colon that ends the signature is the first one outside any
            // bracket; annotations and defaults keep theirs inside. A
            // one-line def closes at its own colon too, and what follows on
            // the next line is the next thing in the file, not a docstring,
            // so it needs no rule of its own: an explicit one stood here and
            // a mutation removing it failed nothing.
            for (var at = 0; at < code.Length; at++)
            {
                switch (code[at])
                {
                    case '(' or '[' or '{':
                        depth++;
                        break;
                    case ')' or ']' or '}':
                        depth--;
                        break;
                    case ':' when depth == 0:
                        closed = true;
                        break;
                }

                if (closed)
                {
                    break;
                }
            }

            i++;
        }

        if (!closed)
        {
            return [];
        }

        while (i < lines.Count && lines[i].Trim().Length == 0)
        {
            i++;
        }

        if (i >= lines.Count)
        {
            return [];
        }

        var first = lines[i].Trim().TrimStart('r', 'R', 'u', 'U', 'f', 'F', 'b', 'B');

        var quote = first.StartsWith("\"\"\"", StringComparison.Ordinal) ? "\"\"\""
            : first.StartsWith("'''", StringComparison.Ordinal) ? "'''"
            : first.StartsWith('"') ? "\""
            : first.StartsWith('\'') ? "'"
            : null;

        if (quote is null)
        {
            return [];
        }

        var text = first[quote.Length..];
        var close = text.IndexOf(quote, StringComparison.Ordinal);

        if (close >= 0)
        {
            text = text[..close];
        }

        if (text.Trim().Length > 0)
        {
            return [text.Trim()];
        }

        // Quotes alone on their line; the text starts on the next.
        return i + 1 < lines.Count ? [lines[i + 1].Trim().Replace(quote, string.Empty)] : [];
    }

    /// <summary>
    /// The first sentence of a comment, or its first line when it has no full stop.
    /// </summary>
    private static string FirstSentence(List<string> comment)
    {
        if (comment.Count == 0)
        {
            return string.Empty;
        }

        var joined = string.Join(' ', comment.TakeWhile(line => line.Length > 0)).Trim();

        var end = joined.IndexOf(". ", StringComparison.Ordinal);

        return end > 0 ? joined[..(end + 1)] : joined;
    }

    private static bool IsDecoration(string trimmed) =>
        trimmed.StartsWith('@') || trimmed.StartsWith('[') || trimmed.StartsWith("#[", StringComparison.Ordinal);
}
