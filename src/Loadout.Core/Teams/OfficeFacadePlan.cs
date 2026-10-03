namespace Loadout.Core.Teams;

/// <summary>
/// One storey's outside, bay by bay, read from its floor's plan: for each side
/// of the tower a letter a bay, <c>s</c> solid or <c>g</c> glass, and whether
/// each bay is lit, <c>1</c> or <c>0</c>.
/// </summary>
/// <param name="S">The south side, the front: west to east.</param>
/// <param name="E">The east side: south to north.</param>
/// <param name="N">The north side: east to west.</param>
/// <param name="W">The west side: north to south.</param>
/// <param name="LitS">Which of the south side's bays are lit.</param>
/// <param name="LitE">Which of the east side's bays are lit.</param>
/// <param name="LitN">Which of the north side's bays are lit.</param>
/// <param name="LitW">Which of the west side's bays are lit.</param>
/// <remarks>
/// Each side runs the way the tower's side runs seen from outside, left to
/// right, so the page draws bay i of a side from letter i.
/// </remarks>
public sealed record OfficeFacadeStrips(string S, string E, string N, string W, string LitS, string LitE, string LitN, string LitW);

/// <summary>Reading a storey's outside from its floor.</summary>
public static class OfficeFacadePlan
{
    // Rooms that are passed through rather than sat in: their windows stay dark.
    private static readonly string[] Unlit = ["corridor", "core", "vacant"];

    /// <summary>
    /// The storey's outside: solid where the floor has a solid wall at the
    /// perimeter or against the glass from inside - the north wall, the end of
    /// a room's wall - and glass elsewhere; lit where the room behind the bay
    /// is in use.
    /// </summary>
    /// <param name="scene">The floor as laid out.</param>
    /// <param name="inUse">Whether a room is in use, by the run whose it is, or null for one the floor shares.</param>
    public static OfficeFacadeStrips For(OfficeScene scene, Func<string?, bool> inUse)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(inUse);

        var (w, h) = (scene.Width, scene.Height);
        var first = scene.Atlases is { Count: > 0 } atlases ? atlases[0].Tiles : int.MaxValue;

        bool Solid(int x, int y)
        {
            var tile = scene.Walls is { } walls && y >= 0 && y < walls.Count && x >= 0 && x < walls[y].Count ? walls[y][x] : -1;

            return tile >= 0 && tile < first;
        }

        bool Lit(int x, int y)
        {
            var area = (scene.Areas ?? [])
                .Where(one => x >= one.X && x < one.X + one.W && y >= one.Y && y < one.Y + one.H)
                .OrderBy(one => one.W * one.H)
                .FirstOrDefault();

            return area is not null && !Unlit.Contains(area.Kind) && inUse(area.Run);
        }

        (string Kinds, string Lights) Side(IEnumerable<(int OuterX, int OuterY, int InnerX, int InnerY)> bays)
        {
            var kinds = new System.Text.StringBuilder();
            var lights = new System.Text.StringBuilder();

            foreach (var (ox, oy, ix, iy) in bays)
            {
                var solid = Solid(ox, oy) || Solid(ix, iy);

                kinds.Append(solid ? 's' : 'g');
                lights.Append(!solid && Lit(ix, iy) ? '1' : '0');
            }

            return (kinds.ToString(), lights.ToString());
        }

        var south = Side(Enumerable.Range(0, w).Select(x => (x, h - 1, Math.Clamp(x, 1, w - 2), h - 2)));
        var east = Side(Enumerable.Range(0, h).Select(i => (w - 1, h - 1 - i, w - 2, Math.Clamp(h - 1 - i, 1, h - 2))));
        var north = Side(Enumerable.Range(0, w).Select(i => (w - 1 - i, 0, Math.Clamp(w - 1 - i, 1, w - 2), 1)));
        var west = Side(Enumerable.Range(0, h).Select(y => (0, y, 1, Math.Clamp(y, 1, h - 2))));

        return new OfficeFacadeStrips(south.Kinds, east.Kinds, north.Kinds, west.Kinds, south.Lights, east.Lights, north.Lights, west.Lights);
    }
}
