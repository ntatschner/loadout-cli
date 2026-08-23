using System.Text.RegularExpressions;

namespace AgentWorkspace.Core.Security;

/// <summary>
/// Reports whether a piece of text looks like it contains a credential, and
/// which pattern matched.
/// <para>
/// The companion to <see cref="SecretRedactor"/>, and a different job:
/// redaction rewrites text so it is safe to print, whereas scanning answers
/// "should this be written down at all". Instruction files, memory topics and
/// context bundles are committed to the workspace repository, so the answer
/// there has to be no before the write, not a redaction afterwards.
/// </para>
/// <para>
/// A match returns the <em>name</em> of the pattern and never the matched text.
/// A scanner that quoted what it found would copy the credential into the
/// finding, the console, and any log or audit record downstream, which is the
/// exact disclosure it exists to prevent.
/// </para>
/// </summary>
public static partial class SecretScanner
{
    private static readonly (string Name, Func<Regex> Pattern)[] Patterns =
    [
        ("Anthropic API key", AnthropicKey),
        ("OpenAI API key", OpenAiKey),
        ("GitHub token", GitHubToken),
        ("Slack token", SlackToken),
        ("Google API key", GoogleKey),
        ("AWS access key id", AwsAccessKey),
        ("AWS secret access key", AwsSecretKey),
        ("Azure storage key", AzureStorageKey),
        ("private key block", PrivateKeyBlock),
        ("JSON web token", JsonWebToken),
        ("credentials in a URL", UrlCredentials),
        ("connection string password", ConnectionStringPassword),
        ("assigned secret", AssignedSecret),
    ];

    /// <summary>
    /// Names of every pattern that matched, in declaration order. Empty when
    /// nothing matched.
    /// </summary>
    public static IReadOnlyList<string> Match(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var matched = new List<string>();

        foreach (var (name, pattern) in Patterns)
        {
            try
            {
                if (pattern().IsMatch(text))
                {
                    matched.Add(name);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathological input must not stall a write, but nor should it
                // pass unremarked: a scan that could not finish is reported as
                // a possible match so the caller still refuses.
                matched.Add(name + " (scan did not complete)");
            }
        }

        return matched;
    }

    /// <summary>Whether the text matched any pattern.</summary>
    public static bool LooksLikeSecret(string? text) => Match(text).Count > 0;

    [GeneratedRegex(@"\bsk-ant-[A-Za-z0-9_\-]{16,}", RegexOptions.None, 1000)]
    private static partial Regex AnthropicKey();

    [GeneratedRegex(@"\bsk-(?!ant-)[A-Za-z0-9]{20,}", RegexOptions.None, 1000)]
    private static partial Regex OpenAiKey();

    [GeneratedRegex(@"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{20,}|\bgithub_pat_[A-Za-z0-9_]{20,}",
        RegexOptions.None, 1000)]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\bxox[baprs]-[A-Za-z0-9\-]{10,}", RegexOptions.None, 1000)]
    private static partial Regex SlackToken();

    [GeneratedRegex(@"\bAIza[A-Za-z0-9_\-]{35}", RegexOptions.None, 1000)]
    private static partial Regex GoogleKey();

    [GeneratedRegex(@"\b(?:AKIA|ASIA|ABIA|ACCA)[A-Z0-9]{16}\b", RegexOptions.None, 1000)]
    private static partial Regex AwsAccessKey();

    /// <summary>
    /// Requires the surrounding assignment. A bare forty-character base64 run
    /// occurs in ordinary content — hashes, ids, base64 fragments — and
    /// flagging it on sight would make the scanner cry wolf until people
    /// stopped believing it.
    /// </summary>
    [GeneratedRegex(@"(?i)aws_?secret_?access_?key\s*[=:]\s*['""]?[A-Za-z0-9/+=]{40}",
        RegexOptions.None, 1000)]
    private static partial Regex AwsSecretKey();

    [GeneratedRegex(@"(?i)(?:AccountKey|SharedAccessSignature)\s*=\s*[A-Za-z0-9/+=]{20,}",
        RegexOptions.None, 1000)]
    private static partial Regex AzureStorageKey();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.None, 1000)]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}",
        RegexOptions.None, 1000)]
    private static partial Regex JsonWebToken();

    [GeneratedRegex(@"[a-zA-Z][a-zA-Z0-9+.\-]*://[^/\s:@]+:[^/\s@]{3,}@", RegexOptions.None, 1000)]
    private static partial Regex UrlCredentials();

    [GeneratedRegex(@"(?i)\b(?:password|pwd)\s*=\s*[^;\s""']{6,}", RegexOptions.None, 1000)]
    private static partial Regex ConnectionStringPassword();

    /// <summary>
    /// A credential-suggesting name assigned a long opaque value. The length
    /// floor is what keeps documentation prose out of the results:
    /// "API_KEY = your key here" should not trip it, but a real key will.
    /// </summary>
    [GeneratedRegex(
        @"(?i)\b[A-Za-z_][A-Za-z0-9_]*(?:api_?key|secret|token|passwd|credential)[A-Za-z0-9_]*\s*[=:]\s*['""]?[A-Za-z0-9/+=_\-]{20,}",
        RegexOptions.None, 1000)]
    private static partial Regex AssignedSecret();
}
