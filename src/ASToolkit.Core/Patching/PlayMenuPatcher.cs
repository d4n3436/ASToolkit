using System.Buffers.Binary;
using System.Text;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Patching;

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
    private const float Pitch = 26;
    private const int ElementSize = 28;
    private const float BottomLimit = 470;

    /// <summary>
    /// Returns the menu with a new button under the last add-on button, moving the buttons below it down to make room.
    /// </summary>
    public static byte[] Patch(byte[] original, string labelKey)
    {
        int version = new MenuResource(original).Header.Version;
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

        int labelIndex = FindLabel(records, template.Record, version);
        float newY = GetY(template.Record) + Pitch;
        uint handle = records.Where(r => r.Length >= ElementSize).Max(r => GetSprite(r).Handle);

        byte[] button = (byte[])template.Record.Clone();
        SetUInt32(button, 0, handle += 0x100);
        SetUInt32(button, 20, (uint)GetDirection(ButtonNumber));
        SetY(button, newY);
        byte[] label = ReplaceText(records[labelIndex], version, labelKey);
        SetUInt32(label, 0, handle + 0x100);
        SetY(label, GetY(label) + Pitch);

        float previous = newY;
        foreach (var below in buttons.Where(b => GetY(b.Record) > GetY(template.Record)).OrderBy(b => GetY(b.Record)).ToList())
        {
            float shift = Math.Max(0, previous + Pitch - GetY(below.Record));
            int belowLabel = FindLabel(records, below.Record, version);
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

        bool IsButton(ReadOnlyMemory<byte> record) => record.Length > ElementSize && GetSprite(record).Id == KnownObject.MenuButton;
        
        int GetButtonNumber(ReadOnlyMemory<byte> record) => ((GetSprite(record).Direction.Value + 128 / Directions) & 255) * Directions >> 8;
        
        float GetY(ReadOnlyMemory<byte> record) => GetSprite(record).Position.Y;
        
        MapSprite GetSprite(ReadOnlyMemory<byte> record) => new MenuElement(record, version).Sprite;
    }

    // First direction angle (0 to 255) that shows the button's frame
    private static int GetDirection(int number) => (number * 256 + Directions - 1) / Directions;

    private static void SetY(byte[] record, float y) => BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(12), y);

    private static void SetUInt32(byte[] record, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(offset), value);

    // Labels sit a few pixels off their button's center
    private static int FindLabel(List<byte[]> records, byte[] button, int version)
    {
        var center = new MenuElement(button, version).Sprite.Position;
        int best = -1;
        float distance = float.MaxValue;
        for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            var element = new MenuElement(records[recordIndex], version);
            if (records[recordIndex].Length <= ElementSize || element.Sprite.Id != KnownObject.MenuLabel || element.Text.IsEmpty
                || Math.Abs(element.Sprite.Position.X - center.X) > 5)
            {
                continue;
            }

            float gap = Math.Abs(element.Sprite.Position.Y - center.Y);
            if (gap <= 3 && gap < distance)
            {
                (best, distance) = (recordIndex, gap);
            }
        }

        return best >= 0 ? best : throw new InvalidDataException("A menu button has no label.");
    }

    private static byte[] ReplaceText(byte[] label, int version, string key)
    {
        int textStart = label.Length - new MenuElement(label, version).Text.Length - 1;
        byte[] result = new byte[textStart + key.Length + 1];
        Array.Copy(label, result, textStart);
        Encoding.ASCII.GetBytes(key, result.AsSpan(textStart));
        return result;
    }
}
