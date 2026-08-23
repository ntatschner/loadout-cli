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
        result = AssignedSecret().Replace(result, "$1" + Placeholder);
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
    /// An environment-style assignment whose name suggests a credential. Covers
    /// the shape a redacted-environment dump would otherwise expose.
    /// </summary>
    [GeneratedRegex(
        @"\b([A-Za-z_][A-Za-z0-9_]*(?:KEY|TOKEN|SECRET|PASSWORD|PASSWD|CREDENTIAL|APIKEY)[A-Za-z0-9_]*\s*[=:]\s*)\S+",
        RegexOptions.IgnoreCase, 1000)]
    private static partial Regex AssignedSecret();

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
