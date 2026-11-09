using System.Drawing;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Rooms;

namespace ASToolkit.Core.Generation.Dressing;

internal sealed partial record FurnitureGrammar(
    Dictionary<RoomType, RoomType> Aliases,
    Dictionary<RoomType, RoomProgram> Types,
    Dictionary<PieceFamily, FurnitureFamily> Families,
    Dictionary<KnownObject, Dictionary<byte, Companion[]>> SharedModules,
    Dictionary<KnownObject, Dictionary<int, Dictionary<byte, float>>> Facings,
    Dictionary<KnownObject, AttachedPiece[]> Attached,
    Dictionary<KnownObject, Arrangement> Scenes,
    HeapRule Heap,
    Dictionary<FamilyPair, PairSpacing> Spacing,
    Dictionary<KnownObject, Dictionary<byte, float>> FrameSpacing,
    Dictionary<KnownObject, Dictionary<byte, float>> EndClearance,
    HashSet<KnownObject> AddOnOnly)
{
    // The far faces are the two the camera looks at
    public const int Open = 0;
    public const int PlusU = 1;
    public const int MinusV = 2;
    public const int MinusU = 4;
    public const int PlusV = 8;
    public const int FarFaces = PlusU | MinusV;

    // Band starts in half cells
    public static readonly int[] DensityBands = [0, 200, 400, 800];
    public static readonly int[] WallBands = [0, 250, 500, 1000, 2000];

    public HashSet<KnownObject> GetPieces() => Families.Values
        .SelectMany(family => family.Pieces)
        .Concat(Types.Values.SelectMany(program => program.Companions.Values).Concat(SharedModules.Values).SelectMany(modules => modules.Values).SelectMany(companions => companions).Select(companion => companion.Id))
        .ToHashSet();

    public static Dictionary<KnownObject, Dictionary<byte, Companion[]>> GroupModules(IEnumerable<Module> modules) => modules
        .GroupBy(module => module.Anchor)
        .ToDictionary(anchor => anchor.Key, anchor => anchor.ToDictionary(module => module.Dir, module => module.Pieces));

    private static Dictionary<FamilyPair, PointF[]> GroupOffsets(IEnumerable<HeapOffsets> rows) => rows.ToDictionary(
        row => new FamilyPair(row.From, row.To),
        row => row.From <= row.To ? row.Offsets : row.Offsets.Select(offset => new PointF(-offset.X, -offset.Y)).ToArray());

    private static Dictionary<KnownObject, Dictionary<int, Dictionary<byte, float>>> GroupFacings(IEnumerable<FacingRow> rows) => rows
        .GroupBy(row => row.Id)
        .ToDictionary(id => id.Key, id => id.ToDictionary(row => row.Face, row => row.Weights));
}

internal sealed record RoomProgram(float[][] PiecesPerHundredCells, bool CoverFarWalls, float SignatureShare, Arrangement[] Arrangements,
    Dictionary<KnownObject, Dictionary<byte, Companion[]>> Companions);

// A heap has no family
internal sealed record Arrangement(ArrangementKind Kind, PieceFamily? Family, float Share, float BigShare, float[] PerHundredCells, float MaxPerHundredCells,
    int[] Modules, Dictionary<PieceFamily, float> Members, int OpenFromCells = 0, float OpenShare = 0, float NearShare = 0, bool Signature = false, Lining? Lining = null);

internal sealed record Lining(KnownObject Id, float FarDistance, float NearDistance);

internal enum ArrangementKind
{
    Far,
    Near,
    Grid,
    Heap,
    Open,
    Lined
}

internal sealed record FurnitureFamily(KnownObject[] Pieces, Dictionary<KnownObject, float> MainIds, Dictionary<KnownObject, float> CampaignIds,
    float MixShare, float MinorityShare, float WallDistance, float Step, float GridStep, float GridAcross, float Extent, float[] WallShare, float FarWallMin,
    float NearWallMin, int MinCells, int CampaignMinCells);

internal sealed record Module(KnownObject Anchor, byte Dir, Companion[] Pieces);

// Ids sharing a slot are alternatives, the slot ordering a family's companions by height
internal sealed record Companion(KnownObject Id, int Slot, float U, float V, float Z, byte Facing, float Share);

internal sealed record FacingRow(KnownObject Id, int Face, Dictionary<byte, float> Weights);

internal sealed record AttachedPiece(KnownObject Id, float U, float V, float Share);

internal sealed record HeapRule(Dictionary<PieceFamily, float> Core, Dictionary<PieceFamily, Dictionary<PieceFamily, float>> Mix, int[] Size,
    Dictionary<FamilyPair, PointF[]> Touch, Dictionary<FamilyPair, float> Pairs);

internal readonly record struct FamilyPair
{
    public FamilyPair(PieceFamily first, PieceFamily second)
    {
        First = first <= second ? first : second;
        Second = first <= second ? second : first;
    }

    public PieceFamily First { get; }

    public PieceFamily Second { get; }

    public static FamilyPair? FromPieces(KnownObject first, KnownObject second)
    {
        PieceFamily? a = first.Family, b = second.Family;
        return a is null || b is null ? null : new FamilyPair(a.Value, b.Value);
    }
}

internal readonly record struct PairSpacing(float Same, float Turned);

// Offsets point from a piece of the From family to one of the To family
internal sealed record HeapOffsets(PieceFamily From, PieceFamily To, PointF[] Offsets);