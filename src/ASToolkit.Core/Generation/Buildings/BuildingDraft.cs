using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Buildings;

// Holds the state a building grammar and its services build together, such as the grid of rooms, the doors the design marks, the program's
// start, keys and end, each room's outline rects and the counts of what the build did
internal sealed class BuildingDraft
{
    private static readonly string[] TallyNames = Enum.GetValues<BuildingTally>()
        .Select(tally => string.Concat(tally.ToString().Select((letter, index) => index > 0 && char.IsUpper(letter) ? $"_{char.ToLowerInvariant(letter)}" : $"{char.ToLowerInvariant(letter)}")))
        .ToArray();

    private int _group;

    public BuildingDraft(BuildingRequest request, GrammarRandom random)
    {
        Request = request;
        Random = random;
    }

    public BuildingRequest Request { get; }

    public GrammarRandom Random { get; }

    public BuildingGrid Grid { get; } = new();

    public Dictionary<string, int> Counts { get; } = [];

    public List<(int From, int To)> Locks { get; } = [];

    public List<(int Room, int Passage)> Junctions { get; } = [];

    public List<RoomPair> ClosedWalls { get; } = [];

    public List<RoomPair> JammedWalls { get; } = [];

    public List<RoomPair> OpenWalls { get; } = [];

    public List<RoomPair> Shortcuts { get; } = [];

    public int? Start { get; set; }

    public List<int> Keys { get; } = [];

    public int? GateRoom { get; set; }

    public int? Trigger { get; set; }

    public int? End { get; set; }

    public List<int> Run { get; set; } = [];

    public HashSet<int> SecondWing { get; } = [];

    public HashSet<int> LockedSiteRooms { get; } = [];

    public float RoomSide { get; set; }

    public Dictionary<int, List<UnitRect>> Shapes { get; } = [];

    public HashSet<int> Shaped { get; } = [];

    public HashSet<int> WingPieces { get; } = [];

    public HashSet<int> KeepsOutline { get; } = [];

    public HashSet<int> OfficeWing { get; } = [];

    public HashSet<int> WingPods { get; } = [];

    public void Count(BuildingTally tally, int amount = 1) => Add(TallyNames[(int)tally], amount);

    public void Count(BuildingTally tally, LabWing wing, int amount = 1) => Add($"{TallyNames[(int)tally]}_{wing}", amount);

    public int NewGroup() => ++_group;

    public int AddArm(int owner, UnitRect rect, bool alongU)
    {
        int id = Grid.Add(rect, GridKind.Arm, Grid[owner].Group, Grid[owner].Phase, alongU: alongU, owner: owner);
        Shapes[id] = [rect];
        Shapes[owner].Add(rect);
        return id;
    }

    public UnitRect RectOf(int id) => Grid[id].Rect;

    public IReadOnlyList<UnitRect> GetShapes(int id) => Shapes.TryGetValue(id, out var shapes) ? shapes : [RectOf(id)];

    public int Size(float low, float high) => Math.Max(3, GrammarRandom.Round(RoomSide * Random.Uniform(low, high)));

    public RoomType DrawType(IReadOnlyList<(RoomType Type, float Weight)> types, RoomType main) => Random.Next() < 0.6f ? main : Random.Weighted(types);

    private void Add(string name, int amount) => Counts[name] = Counts.GetValueOrDefault(name) + amount;
}
