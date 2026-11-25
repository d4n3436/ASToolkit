using System.Text.Unicode;

namespace ASToolkit.Engine;

public partial class Map
{
    private const int MouseTipKeyLength = 32;

    private static ReadOnlySpan<byte> MouseTipsSection => "MouseTips"u8;

    [EngineRoutine(0x0040f460)]
    internal ReadOnlyMemory<byte> GetMouseTipsString()
    {
        var text = _flags.HasFlag(MapFlags.Paused) ? default : _players[ControlledPlayer].GetMouseTipsString();
        if (!text.IsEmpty || Menu.Current is null)
            return text;

        int id = Menu.GetIdUnderCursor();
        Span<byte> key = stackalloc byte[MouseTipKeyLength];
        Utf8.TryWrite(key, $"MenuVid{id}AllDir", out int written);
        text = Session.Strings?.GetString(MouseTipsSection, key[..written]) ?? default;

        if (!text.IsEmpty)
            return text;

        Utf8.TryWrite(key, $"MenuVid{id}Dir{Menu.GetDirectionUnderCursor()}", out written);

        return Session.Strings?.GetString(MouseTipsSection, key[..written]) ?? default;
    }
}
