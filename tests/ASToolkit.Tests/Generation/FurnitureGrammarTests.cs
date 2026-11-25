using ASToolkit.Formats;
using ASToolkit.Generation.Dressing;
using ASToolkit.Analysis.Levels;
using ASToolkit.Analysis.Rooms;
using Xunit;

namespace ASToolkit.Tests.Generation;

public class FurnitureGrammarTests
{
    private const float TierSlack = 3;

    private static readonly FurnitureGrammar Grammar = FurnitureGrammar.Default;

    [Theory]
    [InlineData(RoomType.Office, PieceFamily.Table)]
    [InlineData(RoomType.ComputerRoom, PieceFamily.Bank)]
    [InlineData(RoomType.Storage, PieceFamily.Shelf)]
    public void Default_RoomTypes_HoldTheirDefiningFamily(RoomType type, PieceFamily family)
    {
        Assert.Contains(Grammar.Types[type].Arrangements, arrangement => arrangement.Family == family);
    }

    [Fact]
    public void Default_Arrangements_NameFamiliesWithPieces()
    {
        var families = Grammar.Types.Values
            .SelectMany(session => session.Arrangements)
            .Where(arrangement => arrangement.Kind != ArrangementKind.Heap)
            .SelectMany(arrangement => arrangement.Members.Keys.Append(arrangement.Family!.Value))
            .Concat(Grammar.Heap.Core.Keys)
            .Concat(Grammar.Heap.Mix.Keys);

        Assert.All(families, family => Assert.True(Grammar.Families.ContainsKey(family), family.ToString()));
    }

    [Fact]
    public void Default_Families_LeaveWindowsOut()
    {
        Assert.DoesNotContain(Grammar.Families.Values, family => family.Pieces.Contains(KnownObject.Windows));
    }

    [Fact]
    public void Default_Bands_MatchTheirTables()
    {
        Assert.All(Grammar.Types.Values, session => Assert.Equal(FurnitureGrammar.DensityBands.Length, session.PiecesPerHundredCells.Length));
        Assert.All(Grammar.Families.Values, family => Assert.Equal(FurnitureGrammar.WallBands.Length, family.WallShare.Length));
    }

    [Fact]
    public void Default_StackedCompanions_StandOnTheTierUnderThem()
    {
        var stacks = Grammar.Types.Values
            .Select(session => session.Companions)
            .Append(Grammar.SharedModules)
            .SelectMany(table => table.SelectMany(anchor => anchor.Value.Select(dir => new Module(anchor.Key, dir.Key, dir.Value))))
            .Select(module => (module.Anchor, Tiers: module.Pieces.Where(piece => piece.Id == module.Anchor && piece.Z > 0).Select(piece => piece.Z).ToArray()))
            .Where(stack => stack.Tiers.Length > 0)
            .ToArray();

        var tierHeights = stacks.GroupBy(stack => stack.Anchor).ToDictionary(group => group.Key, group => group.Min(stack => stack.Tiers.Min()));

        string[] floating = stacks
            .SelectMany(stack => stack.Tiers
                .Where(z => z > tierHeights[stack.Anchor] + TierSlack && !stack.Tiers.Any(under => MathF.Abs(z - tierHeights[stack.Anchor] - under) <= TierSlack))
                .Select(z => $"{stack.Anchor} at z {z}"))
            .ToArray();

        Assert.True(floating.Length == 0, string.Join(Environment.NewLine, floating));
    }
}
