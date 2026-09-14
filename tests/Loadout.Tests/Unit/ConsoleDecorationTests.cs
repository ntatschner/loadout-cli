using System.Text;
using Loadout.Tui.Terminal;
using FluentAssertions;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The launcher's substitutions reach the static facades the views read.
/// <para>
/// Terminal.Gui 2.5 made <see cref="Glyphs"/> and <see cref="Button"/>
/// defaults read-only, backed by immutable settings records. The screen tests
/// prove the brackets arrive on a rendered button; nothing asserted on the
/// shadow, which only shows in the documentation image. Each test resets the
/// facade to the toolkit's own value first, so it cannot pass on the strength
/// of another test having made the same substitution earlier in the process.
/// </para>
/// </summary>
public sealed class ConsoleDecorationTests
{
    [Fact]
    public void Legible_brackets_reach_the_glyph_facade()
    {
        GlyphSettings.Current = GlyphSettings.Current with
        {
            LeftBracket = new Rune('⟦'),
            RightBracket = new Rune('⟧'),
        };

        ConsoleGlyphs.MakeLegible();

        Glyphs.LeftBracket.Should().Be(new Rune('['));
        Glyphs.RightBracket.Should().Be(new Rune(']'));
    }

    [Fact]
    public void The_theme_removes_button_shadows()
    {
        ButtonSettings.Current = ButtonSettings.Current with { DefaultShadow = ShadowStyles.Opaque };

        LauncherTheme.Apply();

        Button.DefaultShadow.Should().Be(ShadowStyles.None);
    }
}
