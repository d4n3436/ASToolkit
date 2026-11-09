using System.Buffers.Binary;
using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Represents the payload of a 908 spawner, an action stack the engine runs from the end.
/// </summary>
public sealed class SpawnerProgram
{
    private const int ActionSize = 16;

    private static readonly byte[] DefaultTrailer = [0, 0, 0, 0, 3, 0, 0, 0];

    /// <summary>
    /// Gets the actions in payload order.
    /// </summary>
    public required IReadOnlyList<StackAction> Actions { get; init; }

    /// <summary>
    /// Gets the bytes after the actions.
    /// </summary>
    public required byte[] Trailer { get; init; }

    /// <summary>
    /// Gets the monsters the spawner builds, in order.
    /// </summary>
    public IEnumerable<KnownObject> Monsters => Actions.Where(action => action.Act == (int)ActionCode.BuildUnit).Select(action => (KnownObject)action.Var1);

    /// <summary>
    /// Gets the trigger point and radius, or null when the spawner starts at once.
    /// </summary>
    public TriggerArea? Trigger
    {
        get
        {
            foreach (var action in Actions)
            {
                if (action.Act == (int)ActionCode.FlagmanTrigger)
                {
                    return new TriggerArea(new Point(action.Var1, action.Var2), action.Var3);
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the waypoints of the spawned monsters in payload order.
    /// </summary>
    public IEnumerable<Point> Waypoints => Actions.Where(action => action.Act == (int)ActionCode.Move).Select(action => new Point(action.Var1, action.Var2));

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
        int end = sizeof(int) + ActionSize * count;
        if (count < 0 || end > payload.Length)
        {
            return null;
        }

        var actions = MemoryMarshal.Cast<byte, StackAction>(payload[sizeof(int)..end]).ToArray();
        return new SpawnerProgram { Actions = actions, Trailer = payload[end..].ToArray() };
    }

    /// <summary>
    /// Creates a program that waits for its trigger, builds the monsters in order and sends them along the waypoints,
    /// the first waypoint being the first destination.
    /// </summary>
    public static SpawnerProgram Create(IEnumerable<KnownObject> monsters, TriggerArea trigger, IEnumerable<PointF> waypoints)
    {
        var actions = new List<StackAction>();
        foreach ((float x, float y) in waypoints.Reverse())
        {
            actions.Add(new StackAction((int)ActionCode.Move, (int)x, (int)y, 0));
        }

        actions.Add(new StackAction((int)ActionCode.StopStack, 0, 0, 0));
        foreach (var monster in monsters)
        {
            actions.Add(new StackAction((int)ActionCode.BuildUnit, (int)monster, 0, 0));
        }

        actions.Add(new StackAction((int)ActionCode.FlagmanTrigger, trigger.Center.X, trigger.Center.Y, trigger.Radius));
        return new SpawnerProgram { Actions = actions, Trailer = DefaultTrailer };
    }

    /// <summary>
    /// Returns the payload bytes.
    /// </summary>
    public byte[] ToPayload()
    {
        int end = sizeof(int) + ActionSize * Actions.Count;
        byte[] bytes = new byte[end + Trailer.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)Actions.Count);
        for (int actionIndex = 0; actionIndex < Actions.Count; actionIndex++)
        {
            var action = Actions[actionIndex];
            MemoryMarshal.Write(bytes.AsSpan(sizeof(int) + ActionSize * actionIndex), in action);
        }

        Trailer.CopyTo(bytes, end);
        return bytes;
    }
}
