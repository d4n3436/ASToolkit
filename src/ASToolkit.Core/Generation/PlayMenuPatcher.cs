using System.Buffers.Binary;
using System.Text;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Generation;

/// <summary>
/// Provides the adding of the generated add-on's button to the play menus. A button is a sprite record of object 705
/// whose direction picks the frame, and the engine reports that direction to the script as the button's number. Its
/// label is a record of object 3 that names a string of the [menu] section.
/// </summary>
internal static class PlayMenuPatcher
{
    /// <summary>
    /// The button number the script tests for.
    /// </summary>
    public const int ButtonNumber = 5;

    private const int Directions = 10;
    private const int LabelOffset = 36;
    private const float Pitch = 26;
    private const float BottomLimit = 470;

    /// <summary>
    /// Returns the menu with a new button under the last add-on button, moving the buttons below it down to make room.
    /// </summary>
    public static byte[] Patch(byte[] original, string labelKey)
    {
        var file = ResContainerBuilder.Parse(original);
        var records = file.GetChunk("SPR ").Records;
        var buttons = records.Select((r, i) => (Index: i, Record: r)).Where(b => IsButton(b.Record)).ToList();
        if (buttons.Any(b => GetButtonNumber(b.Record) == ButtonNumber))
        {
            throw new InvalidDataException($"Expected menu with button {ButtonNumber} to be available.");
        }

        var candidates = buttons.Where(b => GetButtonNumber(b.Record) is 3 or 4).OrderByDescending(b => GetY(b.Record)).ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidDataException("The menu has no add-on button to copy.");
        }

        var template = candidates[0];

        int labelIndex = FindLabel(records, template.Record);
        float newY = GetY(template.Record) + Pitch;
        uint handle = records.Where(r => r.Length >= 8).Max(r => BinaryPrimitives.ReadUInt32LittleEndian(r));

        byte[] button = (byte[])template.Record.Clone();
        SetUInt32(button, 0, handle += 0x100);
        SetUInt32(button, 20, (uint)GetDirection(ButtonNumber));
        SetY(button, newY);
        byte[] label = ReplaceText((byte[])records[labelIndex].Clone(), labelKey);
        SetUInt32(label, 0, handle + 0x100);
        SetY(label, GetY(label) + Pitch);

        float previous = newY;
        foreach (var below in buttons.Where(b => GetY(b.Record) > GetY(template.Record)).OrderBy(b => GetY(b.Record)).ToList())
        {
            float shift = Math.Max(0, previous + Pitch - GetY(below.Record));
            int belowLabel = FindLabel(records, below.Record);
            SetY(below.Record, GetY(below.Record) + shift);
            SetY(records[belowLabel], GetY(records[belowLabel]) + shift);
            previous = GetY(below.Record);
        }

        if (previous > BottomLimit)
        {
            throw new InvalidDataException($"The new button pushes the menu to y {previous}, past {BottomLimit}.");
        }

        records.Insert(Math.Max(template.Index, labelIndex) + 1, button);
        records.Insert(Math.Max(template.Index, labelIndex) + 2, label);
        return file.Build();
    }

    private static bool IsButton(byte[] record) => record.Length >= LabelOffset && (KnownObject)BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)) == KnownObject.MenuButton;

    private static int GetButtonNumber(byte[] record) => (int)((BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(20)) + 128 / Directions) & 255) * Directions >> 8;

    // First direction angle (0 to 255) that shows the button's frame
    private static int GetDirection(int number) => (number * 256 + Directions - 1) / Directions;

    private static float GetX(byte[] record) => BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(8));

    private static float GetY(byte[] record) => BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(12));

    private static void SetY(byte[] record, float y) => BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(12), y);

    private static void SetUInt32(byte[] record, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(offset), value);

    // Labels sit a few pixels off their button's center
    private static int FindLabel(List<byte[]> records, byte[] button)
    {
        int best = -1;
        float distance = float.MaxValue;
        for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            byte[] r = records[recordIndex];
            if (r.Length > LabelOffset + 4 && (KnownObject)BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(4)) == KnownObject.MenuLabel
                                           && Math.Abs(GetX(r) - GetX(button)) <= 5 && Math.Abs(GetY(r) - GetY(button)) is var d and <= 3 && d < distance)
            {
                (best, distance) = (recordIndex, d);
            }
        }

        return best >= 0 ? best : throw new InvalidDataException("A menu button has no label.");
    }

    // Keep the header and replace the zero-terminated key after its u32
    private static byte[] ReplaceText(byte[] label, string key)
    {
        byte[] result = new byte[LabelOffset + 4 + key.Length + 1];
        Array.Copy(label, result, LabelOffset + 4);
        Encoding.ASCII.GetBytes(key, result.AsSpan(LabelOffset + 4));
        return result;
    }
}
