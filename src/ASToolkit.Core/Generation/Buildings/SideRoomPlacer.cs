using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Buildings;

// Places a building's closets and secret room behind the walls of host rooms
internal sealed class SideRoomPlacer
{
    private const float SecretOffHall = 0.25f;
    private const float SecretRoomShare = 0.25f;
    private const float SpawnOffHall = 0.4f;

    private readonly BuildingDraft _draft;
    private readonly IReadOnlyList<(RoomType Type, float Weight)> _backTypes;

    public SideRoomPlacer(BuildingDraft draft, IReadOnlyList<(RoomType Type, float Weight)> backTypes)
    {
        _draft = draft;
        _backTypes = backTypes;
    }

    public void PlaceSecretRoom()
    {
        if (_draft.Random.Next() >= SecretRoomShare)
        {
            return;
        }

        var path = _draft.Keys
            .Concat(_draft.Run)
            .Concat(new[] { _draft.Start, _draft.End, _draft.Trigger, _draft.GateRoom }.Where(room => room is not null).Select(room => room!.Value))
            .ToHashSet();

        var groupSizes = _draft.Grid.Rooms
            .GroupBy(room => room.Group)
            .ToDictionary(group => group.Key, group => group.Count());

        var hosts = _draft.Grid.Rooms
            .Where(room => room is { Kind: GridKind.Room, IsJoint: false } && room.Type != RoomType.Passage && !path.Contains(room.Id) && groupSizes[room.Group] <= 5)
            .ToArray();

        int along = Math.Max(4, _draft.Size(0.6f, 0.85f));
        int depth = Math.Max(4, _draft.Size(0.5f, 0.7f));
        if (!TryPlaceBeside(_draft.Random.Shuffled(hosts), GridKind.SecretRoom, _ => (along, depth), () => _draft.DrawType(_backTypes, RoomType.Storage)))
        {
            _draft.Count(BuildingTally.SecretRoomUnplaced);
        }
    }

    public void PlaceClosets() => PlaceClosets(_draft.Request.SpawnClosets);

    public void PlaceClosets(int spawnClosets)
    {
        foreach ((var kind, int count, float offHall) in (ReadOnlySpan<(GridKind, int, float)>)
                 [(GridKind.SecretCloset, _draft.Request.SecretClosets, SecretOffHall), (GridKind.SpawnCloset, spawnClosets, SpawnOffHall)])
        {
            for (int index = 0; index < count; index++)
            {
                int width = _draft.Random.Range(2, 4);
                int height = _draft.Random.Range(2, 4);
                bool wantsHall = _draft.Random.Next() < offHall;
                var hosts = _draft.Grid.Rooms
                    .Where(room => room.Kind is GridKind.Room or GridKind.Hall && !room.IsJoint)
                    .ToArray();

                var ordered = _draft.Random.Shuffled(hosts).OrderBy(room => room.Kind == GridKind.Hall != wantsHall);
                if (!TryPlaceBeside(ordered, kind, side => side.RunsAlongU ? (width, height) : (height, width), () => null))
                {
                    _draft.Count(BuildingTally.ClosetUnplaced);
                }
            }
        }
    }

    // Stands a side room behind a wall of the first host with room for it, a unit in from the wall's ends
    private bool TryPlaceBeside(IEnumerable<GridRoom> hosts, GridKind kind, Func<Side, (int Along, int Depth)> sizeOn, Func<RoomType?> drawType)
    {
        foreach (var host in hosts)
        {
            foreach (var side in _draft.Random.Shuffled(Enum.GetValues<Side>()))
            {
                (int low, int high) = host.Rect.GetSpan(side);
                (int along, int depth) = sizeOn(side);
                if (high - low < along + 2)
                {
                    continue;
                }

                var rect = host.Rect.Beside(side, along, depth, _draft.Random.Range(low + 1, high - along));
                int group = _draft.NewGroup();
                if (_draft.Grid.IsFree(rect, group, 1, [host.Group]))
                {
                    _draft.Grid.Add(rect, kind, group, host.Phase, drawType(), host: host.Id);
                    return true;
                }
            }
        }

        return false;
    }
}
