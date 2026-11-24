using System.Drawing;
using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map
{

    internal static ReadOnlySpan<byte> ItemsEnd => [2];

    /// <summary>
    /// Returns the shared data of the object ID a building is building.
    /// </summary>
    /// <param name="building">The building.</param>
    /// <returns>The shared data of the object ID, or null when the building is idle.</returns>
    public Vid? GetPendingBuild(Sprite building) => building is Building { PendingBuild: not null } pending ? pending.PendingBuild.Value.Vid : null;

    /// <summary>
    /// Creates the goal sprite of an order on a point.
    /// </summary>
    /// <param name="position">The point.</param>
    /// <returns>The goal sprite.</returns>
    public Sprite ConstructGoal(Vector3 position)
    {
        return new Sprite(Vids.Empty, position, default, null, this);
    }

    internal Vector3 GetBuildPosition(Sprite builder, Point offset)
    {
        var position = builder.Position;
        var at = offset == Point.Empty ? position.AsVector2() : new Vector2(offset.X, offset.Y);

        if (offset.X < 0)
        {
            at.X = position.X - offset.X - _rng.Next() % (1 - offset.X) * 2;
        }

        if (offset.Y < 0)
        {
            at.Y = position.Y - offset.Y - _rng.Next() % (1 - offset.Y) * 2;
        }

        return new Vector3(at, position.Z);
    }

    [EngineRoutine(0x00406e50)]
    internal static ReadOnlyMemory<byte> GetAfter(ReadOnlyMemory<byte> text, ReadOnlySpan<byte> delimiter)
    {
        var source = CRuntime.GetString(text);
        int index = source.Span.IndexOf(delimiter);

        return index < 0 ? default : source[(index + delimiter.Length)..];
    }

    internal Act CreateAct(int act, int var1, int var2, int var3) =>
        new(act, var1, var2, var3, Act.HasSpriteTarget((ActionCode)(act & byte.MaxValue)) ? FindSprite(var1) : null);

}
