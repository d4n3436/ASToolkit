using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Engine;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class KeyframeTests
{
    private const int Period = 1400;
    private const int RedStep = 36;

    [Fact]
    public void Step_KeyframedSprite_AdvancesThroughItsKeysAndRestarts()
    {
        var map = EngineTestMap.CreateMap();
        var source = map.FindVid(KnownObject.RedMonster1)!;
        TestGameFolder.Objects.TryFind(source.Id, out var record);
        var weapons = new AlienShooterWeaponRecord[record.WeaponIndex + 1];
        weapons[record.WeaponIndex] = CreateWeapon(source.Weapon);
        var vid = new Vid(source.Id, record, weapons, source.SpriteSpeed) { HasTimers = true };
        map.SetChildAndLink(vid);
        var sprite = map.CreateSprite(vid, map.Player.Position + new Vector3(600, 0, 0), default)!;
        int start = map.Time;

        while (map.Time - start < Period / 2)
        {
            map.Play(default);
        }

        float expected = (map.Time - sprite.KeyframeStart) * (WeaponRecord.KeyframeCount - 1) / (float)Period;
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
        var gamma = CreateRecord().GetGamma(3.5f).Pack();

        Assert.Equal(RedStep * 3 + RedStep / 2, gamma.GetPositive(16));
        Assert.Equal(0, gamma.GetNegative(16));
    }

    private static AlienShooterWeaponRecord CreateWeapon(in AlienShooterWeaponRecord source)
    {
        byte[] bytes = new byte[Marshal.SizeOf<AlienShooterWeaponRecord>()];
        MemoryMarshal.Write(bytes, source);
        MemoryMarshal.Write(bytes.AsSpan(64), Period);
        for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
        {
            MemoryMarshal.Write(bytes.AsSpan(68 + key * 4), key / (float)(WeaponRecord.KeyframeCount - 1));
            MemoryMarshal.Write(bytes.AsSpan(100 + key * 4), key * RedStep);
        }

        return MemoryMarshal.Read<AlienShooterWeaponRecord>(bytes);
    }

    private static WeaponRecord CreateRecord()
    {
        byte[] bytes = new byte[Marshal.SizeOf<WeaponRecord>()];
        MemoryMarshal.Write(bytes.AsSpan(92), Period);
        for (int key = 0; key < WeaponRecord.KeyframeCount; key++)
        {
            MemoryMarshal.Write(bytes.AsSpan(96 + key * 4), key / (float)(WeaponRecord.KeyframeCount - 1));
            MemoryMarshal.Write(bytes.AsSpan(128 + key * 4), key * RedStep);
        }

        return MemoryMarshal.Read<WeaponRecord>(bytes);
    }
}
