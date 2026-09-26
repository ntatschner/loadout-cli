using FluentAssertions;
using Loadout.Agents.Ideas;
using Loadout.Core.Ideas;
using Loadout.Models.Agents;
using Loadout.Models.Ideas;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Splitting a dump against a scripted agent.</summary>
public sealed class DumpSplitterTests : IDisposable
{
    private const string Notes = "- fix the flaky login test\n- a status page for the home lab";

    private const string Faithful =
        """{"contract":"dump/1","items":[{"title":"Flaky test","excerpt":"fix the flaky login test","kind":"task","project":"","reason":"Clear."},{"title":"Status page","excerpt":"a status page for the home lab","kind":"idea","project":"homelab","reason":"Needs thought."}]}""";

    private const string Reworded =
        """{"contract":"dump/1","items":[{"title":"Flaky test","excerpt":"repair the unreliable login test","kind":"task","project":"","reason":""}]}""";

    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private DumpSplitter Splitter(ScriptedDetachedLauncher launcher) =>
        new(_space.Dumps, new FakeProjects("homelab", _space.Scratch("repo")), launcher, _space.Paths);

    private async Task<DumpPlace> KeptAsync() =>
        new(null, (await _space.Dumps.KeepAsync(null, Notes, "pasted")).Value!.Id);

    [Fact]
    public async Task A_split_is_proposed_and_nothing_is_recorded()
    {
        var place = await KeptAsync();
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Faithful));

        var split = await Splitter(launcher).SplitAsync(new SplitRequest(place));

        split.Succeeded.Should().BeTrue(split.Error);
        split.Value!.Dump.Items.Select(i => i.Kind).Should().Equal(DumpItemKind.Task, DumpItemKind.Idea);
        split.Value.Dump.Items[1].Project.Should().Be("homelab");
        (await _space.Tasks.ListAsync(null)).Value!.Should().BeEmpty();
        launcher.Pipe!.Written.ToString().Should().Contain("a status page for the home lab", "the notes are in the prompt");
    }

    [Fact]
    public async Task The_agent_is_given_no_tools_at_all()
    {
        var place = await KeptAsync();
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Faithful));

        await Splitter(launcher).SplitAsync(new SplitRequest(place));

        launcher.Options!.AllowedTools.Should().BeEmpty("a split is a reading of text, not an investigation");
        launcher.Options.Permission.Should().Be(HeadlessPermission.DenyUnlessAllowed);
        launcher.Options.OutputSchemaJson.Should().Be(DumpSchema.Version1);
    }

    [Fact]
    public async Task A_split_that_rewords_the_notes_is_sent_back_once_and_then_refused()
    {
        var place = await KeptAsync();
        var launcher = new ScriptedDetachedLauncher(
            ScriptedDetachedLauncher.Result(Reworded),
            ScriptedDetachedLauncher.Result(Reworded));

        var split = await Splitter(launcher).SplitAsync(new SplitRequest(place));

        split.Failed.Should().BeTrue();
        launcher.Pipe!.Written.ToString().Should().Contain("word for word");

        var dump = (await _space.Dumps.ReadAsync(place)).Value!;
        dump.Items.Should().BeEmpty();
        dump.LastError.Should().Contain("word for word");
        dump.Text.Should().Be(Notes, "the notes are kept whatever happened to the split");
    }
}
