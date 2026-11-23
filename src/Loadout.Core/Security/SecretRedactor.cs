using System.Text.RegularExpressions;

namespace Loadout.Core.Security;

/// <summary>
/// Removes credential-looking material from text before it reaches a log, the
/// console, a handoff or an audit record (spec sections 52 and 80).
/// <para>
/// Every error string in the launcher passes through here. That is deliberate
/// belt-and-braces: the launcher already avoids putting secrets into messages,
/// but a git or agent subprocess can echo a token back in its own stderr, and
/// that text is not under the launcher's control.
/// </para>
/// <para>
/// Redaction is a safety net and not a guarantee. It cannot recognise an
/// arbitrary opaque string, so the primary defence remains never passing
/// secrets where they could be printed.
/// </para>
/// </summary>
public static partial class SecretRedactor
{
    private const string Placeholder = "[redacted]";

    /// <summary>Returns the text with recognisable credentials replaced.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = UrlCredentials().Replace(text, "$1" + Placeholder + "@");
        result = AuthorizationHeader().Replace(result, "$1 " + Placeholder);
        result = FlagSecret().Replace(result, "$1" + Placeholder);
        result = AssignedSecret().Replace(result, "$1" + Placeholder);
        result = BareSecret().Replace(result, "$1" + Placeholder);
        result = JsonSecretField().Replace(result, "$1" + Placeholder + "\"");
        result = KnownTokenShape().Replace(result, Placeholder);

        return result;
    }

    /// <summary>
    /// Credentials embedded in a URL, which is how a token most often leaks:
    /// git echoes the remote back in its error messages.
    /// </summary>
    [GeneratedRegex(@"([a-zA-Z][a-zA-Z0-9+.-]*://)[^/\s:@]+:[^/\s@]+@", RegexOptions.None, 1000)]
    private static partial Regex UrlCredentials();

    [GeneratedRegex(@"\b(Authorization:\s*(?:Bearer|Basic|token))\s+\S+",
        RegexOptions.IgnoreCase, 1000)]
    private static partial Regex AuthorizationHeader();

    /// <summary>
    /// The value of an assignment or a flag: a quoted value taken whole, or
    /// else everything up to the next whitespace.
    /// </summary>
    /// <remarks>
    /// Half a secret is still a leak, so a quote is followed to its close. A
    /// quote that never closes, which is what a cut-off output leaves, runs to
    /// the end of its line rather than the end of the text. Inside either kind
    /// of quote, a quote escaped by a backslash (bash), a backtick or doubling
    /// (PowerShell) is part of the value. Reading one wrongly can only stretch
    /// a redaction, never shorten it, and the two quotes mirror each other so
    /// Studio's port and the shared cases stay simple.
    /// </remarks>
    private const string Value =
        @"(?:""(?:\\""|`""|""""|[^""\r\n])*""?|'(?:\\'|`'|''|[^'\r\n])*'?|\S+)";

    /// <summary>
    /// A double-dash flag whose name suggests a credential, with its value
    /// after '=' or a space: <c>--token abc</c>, <c>--api-key=abc</c>.
    /// </summary>
    /// <remarks>
    /// The long keywords count anywhere in the name and in any case. KEY counts
    /// only as a word of its own after '-' or '_', so <c>--api-key</c> and
    /// <c>--ssh_key</c> are caught and <c>--key</c>, <c>--keyring</c> and
    /// <c>--monkey</c> are not. With a space, a value starting with '-' is the
    /// next flag rather than a value, and the value has to be on the same line.
    /// Single-dash flags are left alone.
    /// </remarks>
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_-])(--(?:[A-Za-z0-9_-]*(?:TOKEN|SECRET|PASSWORD|PASSWD|CREDENTIAL|APIKEY)|[A-Za-z0-9_-]*[-_]KEY(?![A-Za-z0-9]))[A-Za-z0-9_-]*(?:=|[ \t]+(?!-)))"
            + Value,
        RegexOptions.IgnoreCase, 1000)]
    private static partial Regex FlagSecret();

    /// <summary>
    /// An environment-style assignment whose name suggests a credential. Covers
    /// the shape a redacted-environment dump would otherwise expose.
    /// </summary>
    /// <remarks>
    /// The name needs a character before the keyword (<c>DB_PASSWORD</c>);
    /// <see cref="BareSecret"/> covers a keyword on its own. KEY after any
    /// letter counts here, so <c>--monkey=abc</c> is redacted although the flag
    /// rule leaves <c>--monkey abc</c> alone. That is deliberate: narrowing
    /// this rule would loosen redaction on every error the launcher prints.
    /// </remarks>
    [GeneratedRegex(
        @"\b([A-Za-z_][A-Za-z0-9_]*(?:KEY|TOKEN|SECRET|PASSWORD|PASSWD|CREDENTIAL|APIKEY)[A-Za-z0-9_]*\s*[=:]\s*)"
            + Value,
        RegexOptions.IgnoreCase, 1000)]
    private static partial Regex AssignedSecret();

    /// <summary>
    /// A keyword on its own as a variable name, in capitals and with '=' only:
    /// <c>TOKEN=abc</c>, <c>PASSWORD_FILE=/run/secret</c>.
    /// </summary>
    /// <remarks>
    /// Environment dumps are written in capitals, and that is where a bare
    /// <c>TOKEN=</c> turns up. Lower case, or a colon, is far more often error
    /// wording (<c>token: missing</c>) than a secret, and the known-token-shape
    /// rule still catches a real token wherever it appears. Bare KEY is left
    /// out for the same reason.
    /// </remarks>
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_])((?:TOKEN|SECRET|PASSWORD|PASSWD|CREDENTIAL|APIKEY)[A-Z0-9_]*=)" + Value,
        RegexOptions.None, 1000)]
    private static partial Regex BareSecret();

    [GeneratedRegex(
        @"(""(?:[a-zA-Z0-9_]*(?:key|token|secret|password|credential)[a-zA-Z0-9_]*)""\s*:\s*"")[^""]*""",
        RegexOptions.IgnoreCase, 1000)]
    private static partial Regex JsonSecretField();

    /// <summary>
    /// Vendor token prefixes that are unambiguous on sight. Deliberately narrow:
    /// a loose pattern here would redact ordinary output and make diagnostics
    /// useless, which is its own kind of failure.
    /// </summary>
    [GeneratedRegex(@"\b(?:sk-ant-|sk-|ghp_|gho_|ghs_|github_pat_|xox[baprs]-)[A-Za-z0-9_\-]{8,}",
        RegexOptions.None, 1000)]
    private static partial Regex KnownTokenShape();
}
