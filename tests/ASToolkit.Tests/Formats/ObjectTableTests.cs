using System.Runtime.InteropServices;
using System.Text;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Formats;

public class ObjectTableTests
{
    [Fact]
    public void Load_MatchesTheFixtureTable()
    {
        string game = TestGameFolder.RequireFolder();
        var table = ObjectTable.Load(Path.Combine(game, "objects.res"));
        var expected = FixtureReader.Read<List<ObjectRow>>("objects.json");
        Assert.Equal(expected.Count, table.Ids.Count);
        foreach (var row in expected)
        {
            var id = (KnownObject)row.Id;
            Assert.True(table.TryFind(id, out var obj));
            Assert.Equal(row.Flags, (uint)obj.Flags);
            Assert.Equal(row.Type, (uint)obj.SpriteClass);
            Assert.Equal(row.Hp, obj.MaxHp);
            Assert.Equal(row.Path, table.GetPath(id));
            Assert.Equal(row.Ext[0], obj.Size.X, 3);
            Assert.Equal(row.Ext[1], obj.Size.Y, 3);
            Assert.Equal(row.Ext[2], obj.Size.Z, 3);
        }
    }

    [Fact]
    public void Load_ReadsTheAlienShooterMotionAndGravity()
    {
        var table = ObjectTable.Load(Path.Combine(TestGameFolder.RequireFolder(), "objects.res"));
        Assert.Equal(174, table.Gravity);
        Assert.Equal(84, table.HeavyGravity);
        Assert.True(table.TryFind(KnownObject.Monster1, out var monster));
        Assert.Equal(0, monster.ZSpeed);
        Assert.Equal(1300, monster.Acceleration);
        Assert.Equal(1300, monster.Deceleration);
        Assert.Equal(1000, monster.TurnPeriod);
        Assert.Equal(10, monster.RestHeight);
    }

    [Fact]
    public void Load_ReadsTheWeaponAndAttackOfAMonster()
    {
        string game = TestGameFolder.RequireFolder();
        var table = ObjectTable.Load(Path.Combine(game, "objects.res"));
        Assert.True(table.TryFind(KnownObject.Monster1, out var monster));
        Assert.Equal(11, monster.WeaponIndex);
        Assert.Equal(80, monster.Speed, 3);
        Assert.True(table.TryGetWeapon(monster, out var weapon));
        Assert.Equal(830, weapon.DetectRange, 3);
        Assert.Equal(50, weapon.BattleRange, 3);
        Assert.Equal(600, weapon.ReloadTime);
        var attack = (KnownObject)monster.Children[(int)SpriteAnimation.Fight];
        Assert.Equal(1, monster.ChildCounts[(int)SpriteAnimation.Fight]);
        Assert.True(table.TryFind(attack, out var hit));
        Assert.Equal(4, hit.Damage);
    }

    [Fact]
    public void Load_ReadsTheSpriteFieldsOfEveryEntry()
    {
        string game = TestGameFolder.RequireFolder();
        var table = ObjectTable.Load(Path.Combine(game, "objects.res"));
        foreach (var id in table.Ids)
        {
            Assert.True(table.TryFind(id, out var obj));
            Assert.InRange(obj.Directions, 1, 256);
            Assert.InRange(obj.FramesPerDirection, 0, 4096);
            Assert.InRange(obj.Gamma.Red, -255, 255);
            Assert.InRange(obj.Gamma.Green, -255, 255);
            Assert.InRange(obj.Gamma.Blue, -255, 255);
            Assert.InRange(obj.Gamma.Alpha, -255, 255);
        }
    }

    [Fact]
    public void Load_ReadsTheTorsoLinkedToThePlayerLegs()
    {
        string game = TestGameFolder.RequireFolder();
        var table = ObjectTable.Load(Path.Combine(game, "objects.res"));
        Assert.True(table.TryFind(table.IsAlienShooter2 ? KnownObject.SequelPlayer : KnownObject.PlayerStart, out var legs));
        Assert.True(table.TryFind(legs.Link, out var torso));
        Assert.Equal(SpriteType.Sprite, torso.SpriteType);
        Assert.Equal(table.IsAlienShooter2 ? 0 : 33, legs.LinkOffset.Z);
    }

    [Theory]
    [InlineData(688, 36, 40, 44, 48, 80, 676)]
    [InlineData(704, 40, 48, 52, 56, 96, 692)]
    [InlineData(696, 40, 48, 52, 56, 88, 684)]
    [InlineData(708, 40, 48, 52, 56, 96, 696)]
    public void Load_PreservesMotionAndScaleAcrossObjectLayouts(int size, int verticalSpeed, int acceleration,
        int deceleration, int turn, int restHeight, int scale)
    {
        byte[] body = new byte[size];
        const uint collisionMask = (1u << 1) | (1u << 4) | (1u << 30);
        WriteUInt32(body, 12, (int)collisionMask);
        WriteFloat(body, 32, 180);
        WriteFloat(body, verticalSpeed, 42);
        WriteFloat(body, acceleration, 1300);
        WriteFloat(body, deceleration, 450);
        WriteFloat(body, turn, 490);
        WriteFloat(body, restHeight, 9);
        WriteFloat(body, scale, 1.25f);
        if (size != 688)
        {
            WriteFloat(body, 36, 700);
            WriteFloat(body, 44, 800);
        }
        if (size == 708)
        {
            const int blackWhiteOffset = 692;
            const int blackWhiteValue = 100;
            WriteUInt32(body, blackWhiteOffset, blackWhiteValue);
            Assert.Equal(blackWhiteValue, MemoryMarshal.Read<ZombieShooter2GameObject>(body).BlackWhiteValue);
        }

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, BuildObjectsRes(((int)KnownObject.Font, "motion", body, @"vid\font.vid")));
            var table = ObjectTable.Load(path);
            Assert.True(table.TryFind(KnownObject.Font, out var obj));
            Assert.Equal((CollisionLayers)collisionMask, obj.MoveMask);
            Assert.Equal(180, obj.Speed);
            Assert.Equal(42, obj.ZSpeed);
            Assert.Equal(1300, obj.Acceleration);
            Assert.Equal(450, obj.Deceleration);
            Assert.Equal(490, obj.TurnPeriod);
            Assert.Equal(9, obj.RestHeight);
            Assert.Equal(1.25f, obj.Scale.X);
            Assert.Equal(size == 688 ? 0 : 700, obj.MaxSpeed);
            Assert.Equal(size == 688 ? 0 : 800, obj.MaxZSpeed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GameObject_UsesTheAlienShooter2BodyLayout()
    {
        Assert.Equal(704, Marshal.SizeOf<GameObject>());
        Assert.Equal(48, Marshal.OffsetOf<GameObject>(nameof(GameObject.Acceleration)).ToInt32());
        Assert.Equal(128, Marshal.OffsetOf<GameObject>(nameof(GameObject.Directions)).ToInt32());
        Assert.Equal(692, Marshal.OffsetOf<GameObject>(nameof(GameObject.Scale)).ToInt32());
    }

    private static void WriteFloat(byte[] body, int offset, float value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, body, offset, 4);
    }

    private static byte[] BuildObjectsRes(params (int Id, string Name, byte[] Body, string Path)[] records)
    {
        byte[] objects = BuildChunk("OBJ ", BuildRecords(records), records.Length);
        byte[] constants = BuildChunk("CNST", BuildConstants(), 1);
        byte[] file = new byte[12 + objects.Length + constants.Length];
        CopyAscii("RES ", file, 0);
        WriteUInt32(file, 4, 4 + objects.Length + constants.Length);
        CopyAscii("DATA", file, 8);
        objects.CopyTo(file, 12);
        constants.CopyTo(file, 12 + objects.Length);
        return file;
    }

    private static byte[] BuildRecords(params (int Id, string Name, byte[] Body, string Path)[] records)
    {
        byte[][] names = new byte[records.Length][];
        byte[][] paths = new byte[records.Length][];
        int total = 0;
        for (int i = 0; i < records.Length; i++)
        {
            names[i] = WithNull(Encoding.ASCII.GetBytes(records[i].Name));
            paths[i] = WithNull(Encoding.Latin1.GetBytes(records[i].Path));
            total += 8 + names[i].Length + records[i].Body.Length + paths[i].Length;
        }

        byte[] data = new byte[total];
        int offset = 0;
        for (int i = 0; i < records.Length; i++)
        {
            WriteUInt32(data, offset, 4 + names[i].Length + records[i].Body.Length + paths[i].Length);
            WriteUInt32(data, offset + 4, records[i].Id);
            offset += 8;
            names[i].CopyTo(data, offset);
            offset += names[i].Length;
            records[i].Body.CopyTo(data, offset);
            offset += records[i].Body.Length;
            paths[i].CopyTo(data, offset);
            offset += paths[i].Length;
        }

        return data;
    }

    private static byte[] BuildConstants()
    {
        const int constantsSize = 104;
        byte[] record = new byte[4 + constantsSize];
        WriteUInt32(record, 0, constantsSize);
        return record;
    }

    private static byte[] BuildChunk(string tag, byte[] recordsData, int count)
    {
        byte[] chunk = new byte[20 + recordsData.Length];
        CopyAscii(tag, chunk, 0);
        WriteUInt32(chunk, 4, 12 + recordsData.Length);
        WriteUInt32(chunk, 8, int.MinValue);
        WriteUInt32(chunk, 12, 0);
        WriteUInt32(chunk, 16, count);
        recordsData.CopyTo(chunk, 20);
        return chunk;
    }

    private static void WriteUInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static void CopyAscii(string text, byte[] buffer, int offset)
    {
        for (int i = 0; i < text.Length; i++)
        {
            buffer[offset + i] = (byte)text[i];
        }
    }

    private static byte[] WithNull(byte[] bytes)
    {
        byte[] result = new byte[bytes.Length + 1];
        bytes.CopyTo(result, 0);
        return result;
    }

    private sealed record ObjectRow(int Id, uint Flags, uint Type, IReadOnlyList<float> Ext, int Hp, string Path);
}
