using AgentWorkspace.Core.Security;
using FluentAssertions;
using Xunit;

namespace AgentWorkspace.Tests.Unit;

/// <summary>
/// Guards the last line of defence before text reaches a log or the console
/// (spec sections 52 and 80). The launcher does not put secrets into messages,
/// but git and agent subprocesses echo their own input back, and that text is
/// not under the launcher's control.
/// </summary>
public sealed class SecretRedactorTests
{
    [Fact]
    public void Credentials_embedded_in_a_remote_url_are_removed()
    {
        // The most realistic leak: git repeats the remote in its errors.
        var text = "fatal: could not read from https://user:ghp_abcdefghijklmnop@github.com/org/repo.git";

        var redacted = SecretRedactor.Redact(text);

        redacted.Should().NotContain("ghp_abcdefghijklmnop");
        redacted.Should().NotContain("user:");
        redacted.Should().Contain("github.com/org/repo.git");
    }

    [Theory]
    [InlineData("ANTHROPIC_API_KEY=sk-ant-secret-value")]
    [InlineData("OPENAI_API_KEY: sk-secretvalue123456")]
    [InlineData("DB_PASSWORD=hunter2hunter2")]
    [InlineData("github_token = ghp_abcdefghijklmnop")]
    public void Assignments_that_name_a_credential_are_redacted(string text)
    {
        var redacted = SecretRedactor.Redact(text);

        redacted.Should().Contain("[redacted]");
        redacted.Should().NotContain("hunter2hunter2");
        redacted.Should().NotContain("sk-ant-secret-value");
    }

    [Fact]
    public void Authorization_headers_are_redacted()
    {
        SecretRedactor.Redact("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature")
            .Should().NotContain("eyJhbGciOiJIUzI1NiJ9");
    }

    [Fact]
    public void Json_credential_fields_are_redacted()
    {
        SecretRedactor.Redact("""{"apiKey": "sk-ant-value", "model": "opus"}""")
            .Should().NotContain("sk-ant-value")
            .And.Contain("opus");
    }

    [Fact]
    public void Ordinary_diagnostic_text_survives_intact()
    {
        // Over-redaction is its own failure: doctor is what people run when
        // something is already wrong, and a report full of [redacted] is
        // useless.
        const string Text = "git version 2.54.0; workspace synced; 12 projects registered";

        SecretRedactor.Redact(Text).Should().Be(Text);
    }

    [Fact]
    public void Null_and_empty_input_produce_empty_output() =>
        SecretRedactor.Redact(null).Should().BeEmpty();
}
