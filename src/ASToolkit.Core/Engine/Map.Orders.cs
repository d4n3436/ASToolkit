using System.Buffers.Text;
using System.Numerics;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class Map
{
    private const int CommandBias = 0x3c;

    private readonly Dictionary<Sprite, PendingBuild> _builds = new(ReferenceEqualityComparer.Instance);

    private static ReadOnlySpan<byte> ItemMark => [1];

    private static ReadOnlySpan<byte> ItemsEnd => [2];

    private static ReadOnlySpan<byte> CommandEnd => ";"u8;

    /// <summary>
    /// Returns the definition a building is building.
    /// </summary>
    /// <param name="building">The building.</param>
    /// <returns>The definition, or null when the building is idle.</returns>
    public SpriteDefinition? GetPendingBuild(Sprite building) => _builds.TryGetValue(building, out var pending) ? pending.Definition : null;

    /// <summary>
    /// Creates a sprite of the default definition at a point, the goal of an order on that point.
    /// </summary>
    /// <param name="position">The point.</param>
    /// <returns>The goal sprite.</returns>
    public Sprite ConstructGoal(Vector3 position) => ConstructSprite(Definitions.Empty, position, default, Army.Player, 0);

    [EngineRoutine(0x00406e50)]
    private static ReadOnlyMemory<byte> GetAfter(ReadOnlyMemory<byte> text, ReadOnlySpan<byte> delimiter)
    {
        var source = CRuntime.GetString(text);
        int index = source.Span.IndexOf(delimiter);

        return index < 0 ? ReadOnlyMemory<byte>.Empty : source[(index + delimiter.Length)..];
    }

    [EngineRoutine(0x00446480)]
    private static ReadOnlyMemory<byte> GetTextItems(Sprite sprite)
    {
        if (sprite.Items.Count == 0)
            return ReadOnlyMemory<byte>.Empty;

        var text = new List<byte>();
        Span<byte> number = stackalloc byte[11];
        foreach (int item in sprite.Items)
        {
            text.AddRange(ItemMark);
            Utf8Formatter.TryFormat(item, number, out int length);
            text.AddRange(number[..length]);
        }

        text.AddRange(ItemsEnd);

        return text.ToArray();
    }

    [EngineRoutine(0x00446570)]
    private static void SetTextItems(Sprite sprite, ReadOnlyMemory<byte> text)
    {
        if (text.IsEmpty)
            return;

        sprite.EnsureExtra();
        text = GetAfter(text, ItemMark);
        int item = 0;
        while (!text.IsEmpty)
        {
            if (CRuntime.TryScanInteger(text.Span, out int scanned))
            {
                item = scanned;
            }

            sprite.Items.Add(item);
            text = GetAfter(text, ItemMark);
        }
    }

    [EngineRoutine(0x004458a0)]
    private static ReadOnlyMemory<byte> GetCommands(Sprite sprite)
    {
        var text = new List<byte>();
        Span<byte> number = stackalloc byte[11];
        foreach (var order in sprite.ActionStack.Orders)
        {
            text.Add(unchecked((byte)(order.Value + CommandBias)));
            Utf8Formatter.TryFormat(order.Var1, number, out int length);
            text.AddRange(number[..length]);
            text.Add((byte)',');
            Utf8Formatter.TryFormat(order.Var2, number, out length);
            text.AddRange(number[..length]);
            text.Add((byte)',');
            Utf8Formatter.TryFormat(order.Var3, number, out length);
            text.AddRange(number[..length]);
            text.AddRange(CommandEnd);
        }

        return text.ToArray();
    }

    [EngineRoutine(0x00445990)]
    private void SetCommands(Sprite sprite, ReadOnlyMemory<byte> text)
    {
        int var1 = 0, var2 = 0, var3 = 0;
        while (!text.IsEmpty)
        {
            var rest = text.Span;
            byte command = rest[0];
            int i = 1;
            if (CRuntime.TryScanInteger(rest[i..], out int scanned, out int length))
            {
                var1 = scanned;
                i += length;
                if (i < rest.Length && rest[i] == ',' && CRuntime.TryScanInteger(rest[(i + 1)..], out scanned, out length))
                {
                    var2 = scanned;
                    i += 1 + length;
                    if (i < rest.Length && rest[i] == ',' && CRuntime.TryScanInteger(rest[(i + 1)..], out scanned))
                    {
                        var3 = scanned;
                    }
                }
            }

            sprite.ActionStack.Push(CreateAct(command - CommandBias, var1, var2, var3));
            text = GetAfter(text, CommandEnd);
        }
    }

    private Act CreateAct(int act, int var1, int var2, int var3) =>
        new(act, var1, var2, var3, Act.HasSpriteTarget((ActionCode)(act & byte.MaxValue)) ? FindSprite(var1) : null);

    private Vector3 GetBuildPosition(Sprite builder, int x, int y)
    {
        var position = builder.Position;
        float atX = x == 0 && y == 0 ? position.X : x;
        float atY = x == 0 && y == 0 ? position.Y : y;

        if (x < 0)
        {
            atX = position.X - x - _rng.Next() % (1 - x) * 2;
        }

        if (y < 0)
        {
            atY = position.Y - y - _rng.Next() % (1 - y) * 2;
        }

        return new Vector3(atX, atY, position.Z);
    }

    private readonly record struct PendingBuild(SpriteDefinition Definition, int X, int Y);
}
