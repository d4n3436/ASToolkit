using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Buildings;

internal abstract partial class BuildingGrammar
{

    protected void BuildOfficeBlock()
    {
        var block = new OfficeBlock(Random);
        var layout = block.Draw();
        Count(BuildingTally.OfficeBlockRedraws, block.Redraws);
        if (layout is null)
        {
            return;
        }

        var ids = layout.Rooms.Select(room => AddDepot(room.Cells.ToRects(), room.Type, room.Phase, NewGroup())).ToList();
        var open = new HashSet<RoomPair>();
        foreach ((int a, int b) in layout.Doors)
        {
            AddDesignedDoor(ids[a], ids[b], open);
        }

        CloseWing(ids, open, new HashSet<int>());
        Run = layout.Run.Select(index => ids[index]).ToList();
        Start = Run[0];
        Trigger = Run[1];
        Keys.Add(Run[^1]);
        End = ids[layout.End];
        SecondWing.UnionWith(ids.Where(id => Grid[id].Phase > 0));
        foreach ((var rect, int host) in layout.Closets)
        {
            Grid.Add(rect, GridKind.SpawnCloset, NewGroup(), Grid[ids[host]].Phase, host: ids[host]);
        }
    }

    // Places the secret closets, the office block having laid its spawn closets itself
    protected void PlaceOfficeClosets() => new SideRoomPlacer(Draft, BackTypes).PlaceClosets(0);
}
