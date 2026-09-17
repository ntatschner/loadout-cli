namespace Loadout.Tui;

/// <summary>The interactive launcher shown when loadout is run with no arguments.</summary>
public interface ILauncherTui
{
    Task<int> RunAsync(CancellationToken ct = default);
}
