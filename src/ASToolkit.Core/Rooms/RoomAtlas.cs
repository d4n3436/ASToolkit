using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Survey;

namespace ASToolkit.Core.Rooms;

/// <summary>
/// Represents the rooms of an original level with their roles and types, the types read by eye where the contents mislead.
/// </summary>
public sealed class RoomAtlas
{
    private const int MinPieces = 3;
    private const float ChairReach = 70;
    private const float ComputerReach = 40;
    private const int DeskWeight = 4;
    private const float MinGroundShare = 0.5f;

    private static readonly HashSet<KnownObject> Ground = [KnownObject.Earth, KnownObject.Grass, KnownObject.VolumetricGrass, KnownObject.FlatGrass];

    private static readonly HashSet<KnownObject> Stock = [KnownObject.Crate, KnownObject.Barrel, KnownObject.RadioactiveBarrel];

    private static readonly (RoomType Type, int Weight, int Least, KnownObject[] Ids)[] Groups =
    [
        (RoomType.Lab, 1, 2, [KnownObject.TestTubes, KnownObject.LabEquipment, KnownObject.ExperimentFlasks, KnownObject.RadioactiveFlask]),
        (RoomType.ComputerRoom, 2, 2, [KnownObject.BigComputer, KnownObject.BioContainer]),
        (RoomType.MachineRoom, 2, 2, [KnownObject.Transformer, KnownObject.ElectricPanel]),
        (RoomType.Depot, 2, 2, [KnownObject.Container, KnownObject.Container2, KnownObject.Container2Alt, KnownObject.Truck, KnownObject.Missile]),
        (RoomType.Storage, 1, 3, [KnownObject.Shelves, KnownObject.Cabinet, KnownObject.Boxes, KnownObject.Cylinder, KnownObject.PetrolGenerator]),
        (RoomType.Lounge, 1, 2, [KnownObject.Plant, KnownObject.WaterCylinder]),
        (RoomType.Quarters, 1, 2, [KnownObject.Books, KnownObject.GymEquipment, KnownObject.Toilet, KnownObject.RecordPlayer, KnownObject.Television, KnownObject.TrashCan])
    ];

    private static readonly HashSet<KnownObject> NotPieces =
        [KnownObject.Generator, KnownObject.GeneratorDead, KnownObject.Breaker, KnownObject.KeypadOn, KnownObject.KeypadOff, KnownObject.BrokenKeypad, KnownObject.WoodenDoor];

    private static readonly (string Map, int X, int Y, RoomType Type, RoomType? Side)[] Corrections =
    [
        ("Level_02.map", 2600, 1540, RoomType.Depot, null),
        ("Level_03.map", 1120, 2716, RoomType.Hall, null),
        ("Level_05.map", 3600, 1876, RoomType.Hall, null),
        ("Level_06.map", 2587, 1664, RoomType.Storage, null),
        ("Level_08.map", 2272, 1920, RoomType.Hall, null),
        ("Level_08.map", 2295, 1360, RoomType.MachineRoom, null),
        ("Level_08.map", 2272, 4256, RoomType.ComputerRoom, null),
        ("Level_10.map", 3960, 3504, RoomType.Hall, null),
        ("Level_10.map", 3397, 2272, RoomType.Arena, null),
        ("Level_10.map", 4005, 5072, RoomType.Arena, null),
        ("Level_10.map", 2250, 3120, RoomType.Arena, null),
        ("Level_10.map", 3915, 1904, RoomType.Arena, null),
        ("Level_10.map", 1687, 3488, RoomType.Arena, null),
        ("Level_10.map", 6165, 3536, RoomType.Arena, null),
        ("Level_10.map", 6457, 2208, RoomType.Arena, null),
        ("Level_10.map", 5760, 1680, RoomType.Arena, null),
        ("Level_10.map", 2160, 5296, RoomType.Arena, null),
        ("Level_10.map", 1395, 4784, RoomType.Arena, null),
        ("ADDON/Level_02.map", 2600, 4536, RoomType.Hall, RoomType.Office),
        ("ADDON/Level_02.map", 2000, 4424, RoomType.Hall, RoomType.Office),
        ("ADDON/Level_02.map", 4260, 3738, RoomType.Hall, RoomType.Office),
        ("ADDON/Level_02.map", 3680, 4732, RoomType.Lab, null),
        ("ADDON/Level_02.map", 1800, 3556, RoomType.Lab, RoomType.Depot),
        ("ADDON/Level_02.map", 4640, 4032, RoomType.Storage, null),
        ("ADDON/Level_03.map", 3120, 672, RoomType.Hall, RoomType.Office),
        ("ADDON/Level_03.map", 2900, 882, RoomType.Office, null),
        ("ADDON2/Level_03.map", 2520, 1820, RoomType.Hall, null),
        ("ADDON2/Level_03.map", 1480, 560, RoomType.Lab, null),
        ("ADDON2/Level_03.map", 3180, 602, RoomType.Quarters, null),
        ("ADDON2/Level_03.map", 800, 2240, RoomType.Quarters, null),
        ("ADDON2/Level_04.map", 3487, 352, RoomType.Office, null),
        ("ADDON2/Level_05.map", 4400, 1345, RoomType.Depot, null),
        ("ADDON2/Level_05.map", 5040, 1793, RoomType.Depot, null),
        ("ADDON2/Level_05.map", 5480, 2101, RoomType.Depot, null),
        ("ADDON2/Level_05.map", 5920, 2409, RoomType.Depot, null)
    ];

    private RoomAtlas()
    {
    }

    /// <summary>
    /// Gets the rooms in place order.
    /// </summary>
    public required IReadOnlyList<AtlasRoom> Rooms { get; init; }

    /// <summary>
    /// Reads the rooms of a surveyed original level.
    /// </summary>
    /// <param name="survey">The survey of the level, named by its map.</param>
    /// <param name="edges">The edges of the level's routing grid.</param>
    /// <param name="sprites">The sprites of the level.</param>
    /// <param name="isOriginal">Whether the level is an original, whose rooms read by eye take their types from the corrections.</param>
    public static RoomAtlas Read(LevelSurvey survey, IReadOnlyDictionary<EdgeKey, EdgeKind> edges, IReadOnlyList<MergedMapSprite> sprites, bool isOriginal = true)
    {
        var roles = RoomRoleMap.Measure(survey);
        var finder = new PartFinder(survey.Lattice, edges);
        var pieces = sprites
            .Where(sprite => IsPiece(sprite.Id))
            .Select(sprite => (Sprite: sprite, Place: survey.FindPlace(sprite.Position.X, sprite.Position.Y)))
            .Where(piece => piece.Place != 0)
            .ToLookup(piece => piece.Place, piece => piece.Sprite);
        
        string map = survey.Name.Replace('\\', '/');
        var owners = survey.Places
            .SelectMany(place => place.Cells.Select(cell => (Cell: cell, place.Id)))
            .ToDictionary(entry => entry.Cell, entry => entry.Id);
        
        var floors = sprites.Where(sprite => (int)sprite.Id is >= (int)KnownObject.BasementFloor and <= (int)KnownObject.FlatGrass)
            .Select(sprite => (sprite.Id, Place: owners.GetValueOrDefault(survey.Lattice.GetCell(sprite.Position.X, sprite.Position.Y))))
            .Where(floor => floor.Place != 0)
            .ToLookup(floor => floor.Place, floor => floor.Id);
        
        var corrections = new Dictionary<int, (RoomType Type, RoomType? Side)>();
        foreach (var correction in Corrections.Where(correction => isOriginal && correction.Map == map))
        {
            int place = owners.GetValueOrDefault(survey.Lattice.GetCell(correction.X, correction.Y));
            if (place == 0 || roles.GetRole(survey.Places[place - 1]) is RoomRole.Closet or RoomRole.Secret)
            {
                throw new InvalidDataException($"The room type read by eye at {correction.X}, {correction.Y} of {map} lands on no room.");
            }

            corrections[place] = (correction.Type, correction.Side);
        }

        var rooms = new List<AtlasRoom>();
        foreach (var place in survey.Places)
        {
            var held = pieces[place.Id].ToArray();
            var here = held.Select(sprite => (sprite.Id, sprite.Position)).ToArray();
            var (parts, scatter) = finder.Find((map, place.Id), place.Cells, held);
            int desks = CountDesks(here);
            int doors = roles.CountDoors(place.Id);
            var proposed = Propose(place, here, desks, doors);
            bool corrected = corrections.TryGetValue(place.Id, out var correction);
            rooms.Add(new AtlasRoom
            {
                MapName = map,
                Place = place,
                Role = GetRole(roles, place, floors[place.Id]),
                Type = corrected ? correction.Type : proposed,
                Side = correction.Side,
                ProposedType = proposed,
                Doors = doors,
                Fill = place.Cells.Count / (float)((place.Cells.Max(cell => cell.U) - place.Cells.Min(cell => cell.U) + 1)
                                                  * (place.Cells.Max(cell => cell.V) - place.Cells.Min(cell => cell.V) + 1)),
                Pieces = here.GroupBy(piece => piece.Id)
                    .OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count()),
                Desks = desks,
                Parts = parts,
                Scatter = scatter
            });
        }

        return new RoomAtlas { Rooms = rooms };
    }

    private static RoomRole GetRole(RoomRoleMap roles, LevelPlace place, IEnumerable<KnownObject> floors)
    {
        var role = roles.GetRole(place);
        if (role is RoomRole.Closet or RoomRole.Secret || place.Kind == PlaceKind.Yard)
        {
            return role;
        }

        var tiles = floors.ToArray();
        return tiles.Length > 0 && tiles.Count(Ground.Contains) >= MinGroundShare * tiles.Length ? RoomRole.Outside : role;
    }

    private static bool IsPiece(KnownObject id) =>
        !NotPieces.Contains(id) && (id is KnownObject.BigComputer or KnownObject.ElectricPanel || (int)id is >= 400 and < 446 || (int)id is >= 663 and <= 678);

    private static int CountDesks((KnownObject Id, Vector3 Position)[] pieces)
    {
        var chairs = pieces
            .Where(piece => piece.Id is KnownObject.Chair or KnownObject.NewChair)
            .Select(piece => piece.Position)
            .ToArray();
        
        var tops = pieces
            .Where(piece => piece.Id.Family == PieceFamily.Computer)
            .Select(piece => piece.Position)
            .ToArray();
        
        return pieces.Count(piece => piece.Id == KnownObject.Table
                                     && (chairs.Any(chair => IsNear(chair, piece.Position, ChairReach)) || tops.Any(top => IsNear(top, piece.Position, ComputerReach))));
    }

    private static bool IsNear(Vector3 one, Vector3 other, float reach) => Vector2.Distance(new Vector2(one.X, one.Y), new Vector2(other.X, other.Y)) <= reach;

    private static RoomType Propose(LevelPlace place, (KnownObject Id, Vector3 Position)[] pieces, int desks, int doors)
    {
        if (place.Kind is PlaceKind.SecretCloset or PlaceKind.SpawnCloset)
        {
            return RoomType.Closet;
        }

        if (place.Kind is PlaceKind.Hall or PlaceKind.Corridor)
        {
            return RoomType.Hall;
        }

        int stock = pieces.Count(piece => Stock.Contains(piece.Id));
        var containers = Groups.First(group => group.Type == RoomType.Depot).Ids;
        var stockType = pieces.Any(piece => containers.Contains(piece.Id)) ? RoomType.Depot : RoomType.Storage;
        int cabinets = desks > 0 ? pieces.Count(piece => piece.Id == KnownObject.Cabinet) : 0;
        var best = (Type: RoomType.Office, Score: desks > 0 ? DeskWeight * desks + cabinets : 0);
        foreach ((var type, int weight, int least, var ids) in Groups)
        {
            int count = pieces.Count(piece => ids.Contains(piece.Id) && (piece.Id != KnownObject.Cabinet || desks == 0));
            int stacked = type == stockType ? stock : 0;
            if (count + stacked >= least && count * weight + stacked > best.Score)
            {
                best = (type, count * weight + stacked);
            }
        }

        return best.Score > 0 && pieces.Length >= MinPieces ? best.Type : doors >= 2 ? RoomType.Passage : RoomType.Bare;
    }
}
