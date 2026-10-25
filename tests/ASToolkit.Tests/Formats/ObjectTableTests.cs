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
    public void Load_ReadsTheSpriteFieldsOfEveryEntry()
    {
        string game = TestGameFolder.RequireFolder();
        var table = ObjectTable.Load(Path.Combine(game, "objects.res"));
        foreach (var id in table.Ids)
        {
            Assert.True(table.TryFind(id, out var obj));
            Assert.InRange(obj.Directions, 1, 256);
            Assert.InRange(obj.FramesPerDirection, 0, 4096);
            Assert.InRange(obj.GammaRed, -255, 255);
            Assert.InRange(obj.GammaGreen, -255, 255);
            Assert.InRange(obj.GammaBlue, -255, 255);
            Assert.InRange(obj.GammaAlpha, -255, 255);
        }
    }

    [Fact]
    public void Load_ReadsTheTorsoLinkedToThePlayerLegs()
    {
        string game = TestGameFolder.RequireFolder();
        var table = ObjectTable.Load(Path.Combine(game, "objects.res"));
        Assert.True(table.TryFind(table.IsAlienShooter2 ? (KnownObject)250 : KnownObject.PlayerStart, out var legs));
        Assert.True(table.TryFind(legs.Link, out var torso));
        Assert.Equal(SpriteType.Sprite, torso.SpriteType);
        Assert.Equal(table.IsAlienShooter2 ? 0 : 33, legs.LinkOffset.Z);
    }

    [Fact]
    public void Load_ReadsTheZombieShooterFields()
    {
        byte[] zombieShooter = new byte[696];
        WriteFloat(zombieShooter, 36, 700);
        WriteFloat(zombieShooter, 44, 800);
        byte[] zombieShooter2 = new byte[708];
        zombieShooter2[692] = 100;

        string path1 = Path.GetTempFileName();
        string path2 = Path.GetTempFileName();
        File.WriteAllBytes(path1, BuildObjectsRes((1, "speeds", zombieShooter, "vid\\1.vid")));
        File.WriteAllBytes(path2, BuildObjectsRes((2, "opaque", zombieShooter2, "vid\\2.vid")));
        try
        {
            var table1 = ObjectTable.Load(path1);
            Assert.True(table1.TryFind((KnownObject)1, out var one));
            Assert.Equal(700, one.MaxSpeed, 3);
            Assert.Equal(800, one.MaxZSpeed, 3);
            Assert.Equal(255, one.Opacity);

            var table2 = ObjectTable.Load(path2);
            Assert.True(table2.TryFind((KnownObject)2, out var two));
            Assert.Equal(100, two.Opacity);
            Assert.Equal(0, two.MaxSpeed, 3);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    private static void WriteFloat(byte[] body, int offset, float value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, body, offset, 4);
    }

    private static byte[] BuildObjectsRes(params (int Id, string Name, byte[] Body, string Path)[] records)
    {
        byte[] recordsData = BuildRecords(records);
        byte[] chunk = BuildChunk(recordsData, records.Length);
        byte[] file = new byte[12 + chunk.Length];
        CopyAscii("RES ", file, 0);
        WriteUInt32(file, 4, 4 + chunk.Length);
        CopyAscii("DATA", file, 8);
        chunk.CopyTo(file, 12);
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

    private static byte[] BuildChunk(byte[] recordsData, int count)
    {
        byte[] chunk = new byte[20 + recordsData.Length];
        CopyAscii("OBJ ", chunk, 0);
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
