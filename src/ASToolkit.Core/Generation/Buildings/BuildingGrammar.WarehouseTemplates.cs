using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Buildings;

internal abstract partial class BuildingGrammar
{
    private static readonly Dictionary<int, WarehouseArea[][]> WarehouseTemplates = new()
    {
        [6] =
        [
            [
                new WarehouseArea("hall", WarehouseShape.Hall, (6, 8), (18, 24)) { Feature = 2, HasIsland = true, Role = "spine" },
                new WarehouseArea("start", WarehouseShape.Small, (3, 4), (3, 4)) { Host = "hall", HostSide = "MinX", First = RoomType.Office, Role = "start" },
                new WarehouseArea("cluster", WarehouseShape.Cluster, (9, 11), (7, 9)) { IsWide = true, Host = "annex", Rooms = 2 },
                new WarehouseArea("annex", WarehouseShape.Depot, (6, 8), (5, 7)) { IsWide = true, Host = "hall", HostSide = "A", Role = "loot" },
                new WarehouseArea("suite", WarehouseShape.Pocket, (6, 8), (7, 9))
                {
                    Host = "hall", HostSide = "B", Phase = 1, Rooms = 3, Interlocks = false, First = RoomType.Office, Second = RoomType.ComputerRoom, Role = "arena"
                },
                new WarehouseArea("reward", WarehouseShape.Small, (3, 3), (3, 3)) { Host = "hall", Phase = 1, First = RoomType.Storage, Role = "reward" },
                new WarehouseArea("store", WarehouseShape.Small, (3, 4), (3, 4)) { Host = "cluster", Phase = 1, First = RoomType.Storage, Role = "reward" }
            ]
        ],
        [7] =
        [
            [
                new WarehouseArea("hall", WarehouseShape.Hall, (4, 5), (18, 24)) { Feature = 1, Role = "spine" },
                new WarehouseArea("start", WarehouseShape.Pair, (4, 6), (4, 6)) { Host = "hall", First = RoomType.Bare, Second = RoomType.Bare, Role = "route", SecondRole = "start" },
                new WarehouseArea("pocket", WarehouseShape.Pocket, (6, 7), (6, 8)) { IsWide = true, Host = "hall", Rooms = 3, First = RoomType.Passage, Second = RoomType.Storage, Role = "generator" },
                new WarehouseArea("block", WarehouseShape.Block, (8, 10), (9, 11)) { Host = "hall", WayBack = "pocket", Phase = 1, Rooms = 6 },
                new WarehouseArea("reward", WarehouseShape.Pair, (4, 6), (4, 6))
                {
                    Host = "hall", First = RoomType.Office, Second = RoomType.Storage, SecondPhase = 1, Role = "reward", SecondRole = "reward"
                }
            ]
        ],
        [8] =
        [
            Legs(true, true),
            Legs(true, false),
            Legs(false, true),
            Legs(false, false)
        ],
        [9] =
        [
            [
                new WarehouseArea("start", WarehouseShape.Strip, (3, 4), (8, 12)) { Ends = "E", Role = "start" },
                new WarehouseArea("d1", WarehouseShape.Depot, (11, 14), (10, 13)) { IsWide = true, Host = "start", In = "MinX", Faces = "WE", DoorLengths = [3, 4], Arms = 1 },
                new WarehouseArea("c1", WarehouseShape.Strip, (3, 4), (8, 13)) { IsWide = true, Ends = "WE", IsCorridor = true, Host = "d1", HostSide = "MaxX" },
                new WarehouseArea("d2", WarehouseShape.Depot, (11, 14), (10, 13)) { IsWide = true, Host = "c1", In = "MinX", DoorLengths = [2, 3, 4], Arms = 1 },
                new WarehouseArea("gen", WarehouseShape.Depot, (9, 11), (9, 11)) { IsWide = true, Host = "d2", First = RoomType.ComputerRoom, Role = "generator", Touch = ["c2"] },
                new WarehouseArea("c2", WarehouseShape.Strip, (3, 4), (7, 11)) { Ends = "WE", IsCorridor = true, Phase = 1, Host = "d2", HostSide = "MaxX" },
                new WarehouseArea("arena", WarehouseShape.Depot, (13, 16), (13, 16)) { IsWide = true, Host = "c2", In = "MinX", Faces = "W", DoorLengths = [3, 4], Phase = 1, Arms = 1, Pillars = 2, Role = "arena" }
            ]
        ]
    };

    // Returns level 8's chain, its last leg turning north or south off the bays into the arena
    private static WarehouseArea[] Legs(bool hasSecondBay, bool turnsNorth)
    {
        var areas = new List<WarehouseArea>
        {
            new("start", WarehouseShape.Strip, (4, 5), (12, 18)) { Ends = "E", Role = "start" },
            new("bay1", WarehouseShape.Depot, (6, 8), (6, 8)) { IsWide = true, Host = "start", In = "MinX", DoorLengths = [3, 4, 5] }
        };

        if (hasSecondBay)
        {
            areas.Add(new WarehouseArea("leg1", WarehouseShape.Strip, (3, 4), (8, 12)) { IsWide = true, Ends = "WE", IsCorridor = true, Host = "bay1", HostSide = "MaxX" });
            areas.Add(new WarehouseArea("bay2", WarehouseShape.Depot, (6, 8), (6, 8)) { IsWide = true, Host = "leg1", In = "MinX", DoorLengths = [3, 4] });
        }

        areas.AddRange(
        [
            new WarehouseArea("leg2", WarehouseShape.Leg, (3, 4), (8, 12)) { IsWide = true, Ends = turnsNorth ? "N" : "S", Host = hasSecondBay ? "bay2" : "bay1", HostSide = turnsNorth ? "MinY" : "MaxY" },
            new WarehouseArea("arena", WarehouseShape.Depot, (16, 20), (11, 14))
            {
                Host = "leg2", In = turnsNorth ? "MaxY" : "MinY", Faces = turnsNorth ? "SE" : "NE", DoorLengths = [3, 4], Phase = 1, Pillars = 3, Role = "arena"
            },
            new WarehouseArea("dead", WarehouseShape.Strip, (3, 4), (6, 9)) { IsWide = true, Ends = "W", Host = "arena", Phase = 1, Role = "end" }
        ]);

        return areas.ToArray();
    }
}
