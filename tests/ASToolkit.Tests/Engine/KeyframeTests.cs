using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class KeyframeTests
{
    private const int Period = 1400;
    private const int RedStep = 36;

    [Fact]
    public void Step_KeyframedSprite_AdvancesThroughItsKeysAndRestarts()
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(definitions);
        var source = definitions.Find(KnownObject.RedMonster1)!;
        var definition = new SpriteDefinition(source.Id, source.Object, CreateWeapon(source.Weapon), source.SpriteSpeed) { HasTimers = true };
        var sprite = map.AddMonster(definition, map.Player.Position + new Vector3(600, 0, 0));
        int start = map.Time;

        while (map.Time - start < Period / 2)
        {
            map.Play(default);
        }

        float expected = (map.Time - map.FrameTime - sprite.KeyframeStart) * (WeaponRecord.KeyframeCount - 1) / (float)Period;
        Assert.InRange(sprite.KeyframePosition, expected - 0.01f, expected + 0.01f);

        while (map.Time - start <= Period + Period / 2)
        {
            map.Play(default);
        }

        Assert.True(sprite.KeyframeStart > start);
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
