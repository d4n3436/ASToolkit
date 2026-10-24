using System.Buffers.Binary;
using System.Drawing;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents the payload of a 908 spawner, an action stack the engine runs from the end.
/// </summary>
public sealed class SpawnerProgram
{
    private static readonly byte[] DefaultTrailer = [0, 0, 0, 0, 3, 0, 0, 0];

    /// <summary>
    /// Gets the actions in payload order.
    /// </summary>
    public required IReadOnlyList<SpawnerAction> Actions { get; init; }

    /// <summary>
    /// Gets the bytes after the actions.
    /// </summary>
    public required byte[] Trailer { get; init; }

    /// <summary>
    /// Gets the monsters the spawner builds, in order.
    /// </summary>
    public IEnumerable<KnownObject> Monsters => Actions.Where(a => a.Op == SpawnerAction.Build).Select(a => (KnownObject)a.A);

    /// <summary>
    /// Gets the trigger point and radius, or null when the spawner starts at once.
    /// </summary>
    public (int X, int Y, int Radius)? Trigger
    {
        get
        {
            foreach (var action in Actions)
            {
                if (action.Op == SpawnerAction.Trigger)
                {
                    return (action.A, action.B, action.C);
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the waypoints of the spawned monsters in payload order.
    /// </summary>
    public IEnumerable<Point> Waypoints => Actions.Where(a => a.Op == SpawnerAction.Move).Select(a => new Point(a.A, a.B));

    /// <summary>
    /// Parses a spawner payload, or returns null when it is too short.
    /// </summary>
    public static SpawnerProgram? Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return null;
        }

        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (count < 0 || 4 + 16 * count > payload.Length)
        {
            return null;
        }

        var actions = new SpawnerAction[count];
        for (int index = 0; index < count; index++)
        {
            var a = payload.Slice(4 + 16 * index, 16);
            actions[index] = new SpawnerAction(BinaryPrimitives.ReadInt32LittleEndian(a), BinaryPrimitives.ReadInt32LittleEndian(a[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(a[8..]), BinaryPrimitives.ReadInt32LittleEndian(a[12..]));
        }

        return new SpawnerProgram { Actions = actions, Trailer = payload[(4 + 16 * count)..].ToArray() };
    }

    /// <summary>
    /// Creates a program that waits for its trigger, builds the monsters in order and sends them along the waypoints,
    /// the first waypoint being the first destination.
    /// </summary>
    public static SpawnerProgram Create(IEnumerable<KnownObject> monsters, (float X, float Y, float Radius) trigger, IEnumerable<PointF> waypoints)
    {
        var actions = new List<SpawnerAction>();
        foreach ((float x, float y) in waypoints.Reverse())
        {
            actions.Add(new SpawnerAction(SpawnerAction.Move, (int)x, (int)y, 0));
        }

        actions.Add(new SpawnerAction(SpawnerAction.Stop, 0, 0, 0));
        foreach (var monster in monsters)
        {
            actions.Add(new SpawnerAction(SpawnerAction.Build, (int)monster, 0, 0));
        }

        actions.Add(new SpawnerAction(SpawnerAction.Trigger, (int)trigger.X, (int)trigger.Y, (int)trigger.Radius));
        return new SpawnerProgram { Actions = actions, Trailer = DefaultTrailer };
    }

    /// <summary>
    /// Returns the payload bytes.
    /// </summary>
    public byte[] ToPayload()
    {
        byte[] bytes = new byte[4 + 16 * Actions.Count + Trailer.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)Actions.Count);
        for (int actionIndex = 0; actionIndex < Actions.Count; actionIndex++)
        {
            var a = bytes.AsSpan(4 + 16 * actionIndex, 16);
            BinaryPrimitives.WriteInt32LittleEndian(a, Actions[actionIndex].Op);
            BinaryPrimitives.WriteInt32LittleEndian(a[4..], Actions[actionIndex].A);
            BinaryPrimitives.WriteInt32LittleEndian(a[8..], Actions[actionIndex].B);
            BinaryPrimitives.WriteInt32LittleEndian(a[12..], Actions[actionIndex].C);
        }

        Trailer.CopyTo(bytes, 4 + 16 * Actions.Count);
        return bytes;
    }
}
