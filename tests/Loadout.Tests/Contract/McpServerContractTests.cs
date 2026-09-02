using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The launcher answering an agent over MCP.
/// </summary>
/// <remarks>
/// <para>
/// Driven as a client drives it — a real handshake over stdin and stdout —
/// because that is the only thing that proves the protocol. Calling the tool
/// methods directly would prove they compute the right answer and nothing about
/// whether an agent could ever reach them.
/// </para>
/// <para>
/// The transport is the part most easily broken by accident: anything written
/// to standard output that is not a protocol message is a malformed frame, and
/// the session ends without saying why. A stray Console.WriteLine anywhere in
/// the startup path would fail these and nothing else.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class McpServerContractTests
{
    private sealed class Session : IDisposable
    {
        private readonly Process _process;
        private int _id;

        internal Session(string executable)
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo(executable)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };

            _process.StartInfo.ArgumentList.Add("mcp");
            _process.StartInfo.ArgumentList.Add("serve");

            _process.Start();
        }

        internal JsonElement Request(string method, object? parameters = null)
        {
            var id = ++_id;

            Send(new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } });

            var line = _process.StandardOutput.ReadLine();

            line.Should().NotBeNullOrWhiteSpace($"'{method}' has to answer");

            return JsonDocument.Parse(line!).RootElement.Clone();
        }

        internal void Notify(string method) =>
            Send(new { jsonrpc = "2.0", method });

        private void Send(object message)
        {
            _process.StandardInput.WriteLine(JsonSerializer.Serialize(message));
            _process.StandardInput.Flush();
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }

            _process.Dispose();
        }
    }

    private static Session Start()
    {
        var executable = LoadoutProcess.Executable;

        executable.Should().NotBeNull("the command line has to be built");

        var session = new Session(executable!);

        var initialize = session.Request("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "contract-test", version = "1" },
        });

        // Answered as a server, not as a command line that printed something.
        initialize.GetProperty("result").GetProperty("serverInfo")
            .GetProperty("name").GetString().Should().Be("loadout");

        session.Notify("notifications/initialized");

        return session;
    }

    [BuiltCliFact]
    public void It_speaks_the_protocol_and_names_its_tools()
    {
        using var session = Start();

        var tools = session.Request("tools/list")
            .GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();

        tools.Should().Contain("loadout_specialist");
        tools.Should().Contain("loadout_effective_instructions");
        tools.Should().Contain("loadout_remember");
        tools.Should().Contain("loadout_mode");
    }

    [BuiltCliFact]
    public void A_mode_can_be_changed_and_answers_with_the_posture()
    {
        using var session = Start();

        var answer = session.Request("tools/call", new
        {
            name = "loadout_mode",
            arguments = new { mode = "investigate" },
        });

        var text = answer.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString() ?? string.Empty;

        // The posture itself, not a note saying it was changed. An agent that
        // is told "you are now investigating" and nothing else has been given
        // a label rather than a direction.
        text.Should().Contain("Reproduce first");
    }

    [BuiltCliFact]
    public void A_mode_that_does_not_exist_is_refused_rather_than_quietly_defaulted()
    {
        using var session = Start();

        var answer = session.Request("tools/call", new
        {
            name = "loadout_mode",
            arguments = new { mode = "yolo" },
        });

        var text = answer.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString() ?? string.Empty;

        // The resolver falls back to the default for a name it does not know,
        // which is right when a person typed it and wrong here: an agent told
        // nothing would carry on believing it had switched.
        text.Should().Contain("no 'yolo' mode");
        text.Should().Contain("investigate");
    }

    [BuiltCliFact]
    public void It_hands_over_nothing_that_changes_the_machine()
    {
        using var session = Start();

        var tools = session.Request("tools/list")
            .GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString() ?? string.Empty)
            .ToList();

        // A tool an agent can call unprompted is a decision taken with nobody
        // watching. Reading is safe and one screened fact is safe; pushing to a
        // remote, changing the machine and starting another agent are not, and
        // the way they get offered is by somebody adding a tool without asking
        // whether it belongs.
        tools.Should().NotContain(name =>
            name.Contains("save", StringComparison.OrdinalIgnoreCase)
            || name.Contains("push", StringComparison.OrdinalIgnoreCase)
            || name.Contains("launch", StringComparison.OrdinalIgnoreCase)
            || name.Contains("update", StringComparison.OrdinalIgnoreCase)
            || name.Contains("setup", StringComparison.OrdinalIgnoreCase));
    }

    [BuiltCliFact]
    public void A_tool_answers_with_what_the_command_would_say()
    {
        using var session = Start();

        var answer = session.Request("tools/call", new
        {
            name = "loadout_specialist",
            arguments = new { id = "language.rust" },
        });

        var text = answer.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString() ?? string.Empty;

        // The whole file, as 'instructions show' would give it: the same call,
        // so an agent and a person are told the same thing about the same
        // specialist.
        text.Should().Contain("id: language.rust");
        text.Should().Contain("## Working rules");
    }

    [BuiltCliFact]
    public void Asking_for_something_that_is_not_there_is_answered_rather_than_thrown()
    {
        using var session = Start();

        var answer = session.Request("tools/call", new
        {
            name = "loadout_specialist",
            arguments = new { id = "language.cobol" },
        });

        var text = answer.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString() ?? string.Empty;

        // An error frame would end the agent's turn; a sentence lets it carry
        // on and ask for something that exists.
        text.Should().Contain("no specialist");
    }
}
