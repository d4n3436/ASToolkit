using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Rendering;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class KeyframeTests
{
    private const int Period = 1400;
    private const int RedStep = 36;

    [Fact]
    public void Step_KeyframedUnit_AdvancesThroughItsKeysAndRestarts()
    {
        var kinds = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(kinds);
        var source = kinds.Find(KnownObject.RedMonster1)!;
        var kind = new ObjectDefinition(source.Id, source.Object, CreateWeapon(source.Weapon), source.SpriteSpeed);
        var unit = world.AddMonster(kind, world.Player.Position + new Vector3(600, 0, 0));
        int start = world.Time;

        while (world.Time - start < Period / 2)
        {
            world.Step(default);
        }

        float expected = (world.Time - world.FrameTime - unit.KeyframeStart) * (WeaponRecord.KeyframeCount - 1) / (float)Period;
        Assert.InRange(unit.KeyframePosition, expected - 0.01f, expected + 0.01f);

        while (world.Time - start <= Period + Period / 2)
        {
            world.Step(default);
        }

        Assert.True(unit.KeyframeStart > start);
    }

    [Fact]
    public void GetGamma_BetweenKeys_InterpolatesTheGamma()
    {
        var gamma = CreateWeapon(default).GetGamma(3.5f).Pack();

        Assert.Equal(RedStep * 3 + RedStep / 2, gamma.GetPositive(16));
        Assert.Equal(0, gamma.GetNegative(16));
    }

    private static WeaponRecord CreateWeapon(in WeaponRecord source)
    {
        byte[] bytes = new byte[Marshal.SizeOf<WeaponRecord>()];
        MemoryMarshal.Write(bytes, in source);
        MemoryMarshal.Write(bytes.AsSpan(92), Period);
        for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
        {
            MemoryMarshal.Write(bytes.AsSpan(96 + key * 4), key / (float)(WeaponRecord.KeyframeCount - 1));
            MemoryMarshal.Write(bytes.AsSpan(128 + key * 4), key * RedStep);
        }

        return MemoryMarshal.Read<WeaponRecord>(bytes);
    }
}
