using System.Drawing;
using ASToolkit.Core.Formats;
using static ASToolkit.Core.Formats.KnownObject;
using A = ASToolkit.Core.Generation.Dressing.ArrangementKind;
using F = ASToolkit.Core.Levels.PieceFamily;

namespace ASToolkit.Core.Generation.Dressing;

internal sealed partial record FurnitureGrammar
{
    private const byte RowFrame = 128;
    private const byte ColumnFrame = 144;
    private const float StripeZ = 30;
    private const float PadReach = 1.2f;
    private const float PadCorner = 0.85f;
    private const float PadStep = 1.55f;

    private static Dictionary<KnownObject, Arrangement> SceneTable => new()
    {
        [Generator] = new(A.Far, F.Generator, 1, 1, [0, 0], 1, [1, 1], []),
        [Missile] = new(A.Far, F.Missile, 1, 1, [0, 0], 1, [1, 1], []),
        [DynamiteSite] = new(A.Lined, F.Site, 1, 1, [0, 0], 1, [1, 1], [], Lining: new(TeleportPad, 1, 2.2f))
    };

    private static FacingRow[] SceneFacingRows =>
    [
        new(Generator, PlusU, new() { [128] = 1 }),
        new(Generator, MinusV, new() { [0] = 1 }),
        new(DynamiteSite, PlusU, new() { [128] = 0.0625f }),
        new(DynamiteSite, MinusV, new() { [0] = 0.0625f }),
        new(DynamiteSite, MinusU, new() { [64] = 0.125f, [80] = 0.0625f }),
        new(DynamiteSite, PlusV, new() { [192] = 0.125f, [208] = 0.0625f }),
        new(DynamiteSite, PlusU | MinusV, new() { [160] = 0.125f, [144] = 0.0625f, [16] = 0.0625f }),
        new(DynamiteSite, PlusU | PlusV, new() { [224] = 0.094f }),
        new(DynamiteSite, MinusU | MinusV, new() { [96] = 0.094f }),
        new(DynamiteSite, MinusU | PlusV, new() { [32] = 0.0625f })
    ];

    // Offsets are lattice cells
    private static Module[] SceneModuleRows =>
    [
        new(Breaker, 0,
        [
            new(Transformer, 0, -0.45f, -1.1f, 0, 0, 0.5f), new(Transformer, 0, -0.45f, -1.6f, 0, 0, 0.5f),
            new(Transformer, 1, -0.45f, 1.1f, 0, 0, 0.5f), new(Transformer, 1, -0.45f, 1.6f, 0, 0, 0.5f),
            new(Transformer, 2, -0.45f, -2.2f, 0, 0, 0.25f), new(Transformer, 3, -0.45f, 2.2f, 0, 0, 0.25f),
            new(ObjectMarker, 0, -0.68f, 0, 1, 128, 1),
            new(SoldierCorpse, 1, -0.85f, 0.3f, 0, 128, 0.25f), new(SoldierCorpse, 1, -1.1f, -0.4f, 0, 16, 0.25f),
            new(SoldierCorpse, 1, -0.7f, 0.9f, 0, 64, 0.25f), new(SoldierCorpse, 1, -1.2f, 1.2f, 0, 192, 0.25f)
        ]),
        new(Generator, 0, GeneratorPieces(false)),
        new(Generator, 128, GeneratorPieces(true)),
        new(Missile, 0, RackPieces(0, false)),
        new(Missile, 64, RackPieces(64, true)),
        new(Missile, 128, RackPieces(128, true)),
        new(MountedCannon, 0, CannonPieces()),
        new(MountedCannon, 128, CannonPieces()),
        SitePads(0, new PointF(0, -PadReach)),
        SitePads(128, new PointF(PadReach, 0)),
        SitePads(64, new PointF(-PadReach, 0)),
        SitePads(192, new PointF(0, PadReach)),
        SitePads(160, new PointF(PadCorner, -PadCorner)),
        SitePads(224, new PointF(PadCorner, PadCorner)),
        SitePads(96, new PointF(-PadCorner, -PadCorner)),
        SitePads(32, new PointF(-PadCorner, PadCorner)),
        SitePads(144, new PointF(PadReach, -PadStep), new PointF(PadReach, 0), new PointF(PadReach, PadStep)),
        SitePads(16, new PointF(PadStep, -PadReach), new PointF(0, -PadReach), new PointF(-PadStep, -PadReach)),
        SitePads(208, new PointF(-PadCorner, PadReach), new PointF(PadCorner, PadReach)),
        SitePads(80, new PointF(-PadReach, -PadCorner), new PointF(-PadReach, PadCorner))
    ];

    private static Companion[] GeneratorPieces(bool turned) => new Companion[]
        {
            new(Transformer, 0, 1.2f, 0, 0, 0, 0.375f), new(Transformer, 0, 1.7f, 0, 0, 0, 0.375f),
            new(Transformer, 1, -1.2f, 0, 0, 0, 0.25f), new(Transformer, 1, -1.7f, 0, 0, 0, 0.25f),
            new(ShootHere, 0, 0, 0, 110, 0, 0.5f),
            new(SoldierCorpse, 1, 1.2f, 2.4f, 0, 64, 0.125f), new(SoldierCorpse, 1, -1.2f, 2.4f, 0, 192, 0.125f),
            new(SoldierCorpse, 1, 0, 2.7f, 0, 128, 0.125f), new(SoldierCorpse, 1, 2, 2, 0, 16, 0.125f)
        }
        .Select(piece => Turn(piece, turned, piece.Id == Transformer ? (byte)(turned ? 128 : 0) : piece.Facing))
        .ToArray();

    private static Companion[] RackPieces(byte dir, bool turned) => new Companion[]
        {
            new(Missile, 0, -0.44f, 0, 0, dir, 1), new(Missile, 1, 0.44f, 0, 0, dir, 1),
            new(WallInscriptions, 0, -0.85f, 1.31f, StripeZ, RowFrame, 1), new(WallInscriptions, 1, -0.15f, 1.31f, StripeZ, RowFrame, 1),
            new(WallInscriptions, 2, 0.55f, 1.31f, StripeZ, RowFrame, 1), new(WallInscriptions, 3, -1.35f, 0.42f, StripeZ, ColumnFrame, 1),
            new(WallInscriptions, 4, -1.35f, 1.12f, StripeZ, ColumnFrame, 1), new(WallInscriptions, 5, 0.67f, 0.42f, StripeZ, ColumnFrame, 1),
            new(WallInscriptions, 6, 0.67f, 1.12f, StripeZ, ColumnFrame, 1)
        }
        .Select(piece => Turn(piece, turned, piece.Id != WallInscriptions || !turned ? piece.Facing : piece.Facing == RowFrame ? ColumnFrame : RowFrame))
        .ToArray();

    private static Companion[] CannonPieces()
    {
        const float minU = -2.55f, maxU = 2.2f, minV = -1.4f, maxV = 2.7f, spacing = 0.72f;
        var rows = new[] { minV, maxV }.SelectMany(v => Enumerable.Range(0, 7).Select(step => (U: minU + 0.25f + step * spacing, V: v, Frame: RowFrame)));
        var columns = new[] { minU, maxU }.SelectMany(u => Enumerable.Range(0, 6).Select(step => (U: u, V: minV + 0.35f + step * spacing, Frame: ColumnFrame)));
        float[] crateShares = [1, 1, 0.6f, 0.6f, 0.5f, 0.4f, 0.4f];
        (float U, float V)[] crates = [(-2, -2), (-1.3f, -2.1f), (-0.6f, -2), (-1.7f, -2.6f), (-1, -2.7f), (0.1f, -2.2f), (-2.4f, -2.5f)];
        return rows.Concat(columns)
            .Select((stripe, slot) => new Companion(WallInscriptions, slot, stripe.U, stripe.V, StripeZ, stripe.Frame, 1))
            .Concat(crates.Select((crate, slot) => new Companion(Crate, slot, crate.U, crate.V, 0, 0, crateShares[slot])))
            .Append(new Companion(ObjectMarker, 26, 0.33f, 1.58f, 1, 0, 1))
            .ToArray();
    }

    private static Module SitePads(byte dir, params PointF[] pads) =>
        new(DynamiteSite, dir, pads.Select((pad, slot) => new Companion(TeleportPad, slot, pad.X, pad.Y, 0, 0, 1)).ToArray());

    private static Companion Turn(Companion piece, bool turned, byte facing) =>
        turned ? piece with { U = -piece.V, V = piece.U, Facing = facing } : piece with { Facing = facing };
}
