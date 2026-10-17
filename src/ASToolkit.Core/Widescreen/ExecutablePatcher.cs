using System.Buffers.Binary;

namespace ASToolkit.Core.Widescreen;

/// <summary>
/// Provides the patching of the game's executable to offer widescreen resolutions in a borderless window.
/// </summary>
internal static class ExecutablePatcher
{
    private const int ImageBase = 0x400000;
    private const int WidthsOffset = 0x7f5b0;
    private const int HeightsOffset = 0x7f630;
    private const int DepthsOffset = 0x7f6b0;
    private const int ListBytes = 0x80;
    private const int MaxResolutions = 16;

    private static ReadOnlySpan<int> Widths => [640, 800, 1024, 1280, 1280, 1280, 1366, 1440, 1600, 1680, 1920, 2560];

    private static ReadOnlySpan<int> Heights => [480, 600, 768, 720, 800, 1024, 768, 900, 900, 1050, 1080, 1440];

    private static readonly (int Address, uint Old, uint New)[] BorderlessWindow =
    [
        (0x409de7, 0xef360000, 0xf0000000),
        (0x409ded, 0x90ca0000, 0x90000000)
    ];

    /// <summary>
    /// Replaces the resolution lists of the unmodified executable and makes its window style borderless.
    /// </summary>
    public static void Patch(Span<byte> exe)
    {
        if (!IsStock(exe))
        {
            throw new InvalidDataException("AlienShooter.exe is not the unmodified GOG 1.2 executable.");
        }

        WriteList(exe.Slice(WidthsOffset, ListBytes), Widths);
        WriteList(exe.Slice(HeightsOffset, ListBytes), Heights);
        WriteList(exe.Slice(DepthsOffset, ListBytes), [32]);
        foreach ((int address, uint _, uint replacement) in BorderlessWindow)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(exe[(address - ImageBase)..], replacement);
        }
    }

    private static bool IsStock(ReadOnlySpan<byte> exe)
    {
        if (exe.Length < DepthsOffset + ListBytes)
        {
            return false;
        }

        ReadOnlySpan<int> widths = [640, 800, 1024];
        for (int widthIndex = 0; widthIndex < widths.Length; widthIndex++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(exe[(WidthsOffset + 4 * widthIndex)..]) != widths[widthIndex])
            {
                return false;
            }
        }

        foreach ((int address, uint original, uint _) in BorderlessWindow)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(exe[(address - ImageBase)..]) != original)
            {
                return false;
            }
        }

        return true;
    }

    // The launcher stores at most 16 display modes per adapter, and each list ends with a zero
    private static void WriteList(Span<byte> list, ReadOnlySpan<int> values)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(values.Length, MaxResolutions);
        list.Clear();
        for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(list[(4 * valueIndex)..], (uint)values[valueIndex]);
        }
    }
}
