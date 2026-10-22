using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation.Content;

/// <summary>
/// Represents where the original levels put door keypads relative to each kind and facing of door.
/// </summary>
public sealed class KeypadTable
{
    private const double Reach = 30;
    private static readonly KnownObject[] Doors = [KnownObject.SmallDoor, KnownObject.DoubleDoor, KnownObject.DoubleDoorFrame, KnownObject.SmallDoorFrame];

    private readonly Dictionary<(KnownObject Door, byte DoorDir, bool Broken), List<PropPlacement>> _samples;
    private readonly Dictionary<KnownObject, double> _shares;

    private KeypadTable(Dictionary<(KnownObject, byte, bool), List<PropPlacement>> samples, Dictionary<KnownObject, double> shares)
    {
        _samples = samples;
        _shares = shares;
    }

    /// <summary>
    /// Records every keypad next to a door in the given levels.
    /// </summary>
    public static KeypadTable Load(string gameFolder, IEnumerable<string> levels)
    {
        var samples = new Dictionary<(KnownObject, byte, bool), List<PropPlacement>>();
        var doors = new Dictionary<KnownObject, int>();
        var keyed = new Dictionary<KnownObject, int>();
        foreach (string level in levels)
        {
            var sp = MapResourceBuilder.Load(Path.Combine(gameFolder, "Maps", level)).Sprites;
            var keypads = sp.Where(s => s.Id is KnownObject.KeypadOn or KnownObject.KeypadOff or KnownObject.BrokenKeypad).ToArray();
            foreach (var door in sp.Where(s => Doors.Contains(s.Id)))
            {
                doors[door.Id] = doors.GetValueOrDefault(door.Id) + 1;
                var keypad = keypads.FirstOrDefault(k => Math.Sqrt(Math.Pow(k.X - door.X, 2) + Math.Pow(k.Y - door.Y, 2)) < Reach);
                if (keypad is null)
                {
                    continue;
                }

                keyed[door.Id] = keyed.GetValueOrDefault(door.Id) + 1;
                var key = (door.Id, door.Facing, keypad.Id == KnownObject.BrokenKeypad);
                if (!samples.TryGetValue(key, out var list))
                {
                    list = [];
                    samples[key] = list;
                }

                list.Add(new PropPlacement(keypad.Id, keypad.X - door.X, keypad.Y - door.Y, keypad.Z, keypad.Facing));
            }
        }

        return new KeypadTable(samples, doors.ToDictionary(p => p.Key, p => keyed.GetValueOrDefault(p.Key) / (double)p.Value));
    }

    /// <summary>
    /// Returns the share of the originals' doors of a kind that have a keypad.
    /// </summary>
    public double GetShare(KnownObject door) => _shares.GetValueOrDefault(door);

    /// <summary>
    /// Returns a keypad placement relative to a door, taken from a door of the same kind and facing in the originals,
    /// or null when the originals have none.
    /// </summary>
    public PropPlacement? Pick(Random rng, KnownObject door, byte doorDir, bool broken)
    {
        var list = _samples.GetValueOrDefault((door, doorDir, broken));
        return list?[rng.Next(list.Count)];
    }
}
