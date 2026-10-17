using System.Text;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Provides the text encoding of the game's names and level scripts.
/// </summary>
public static class GameTextEncoding
{
    private static readonly Lazy<Encoding> CyrillicEncoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251);
    });

    /// <summary>
    /// Gets the Windows-1251 encoding.
    /// </summary>
    public static Encoding Cyrillic => CyrillicEncoding.Value;
}
