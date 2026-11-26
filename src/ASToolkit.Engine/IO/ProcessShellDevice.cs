using System.ComponentModel;
using System.Diagnostics;

namespace ASToolkit.Engine.IO;

/// <summary>
/// Opens files, programs and links through the operating system's shell.
/// </summary>
public sealed class ProcessShellDevice : IShellDevice
{
    /// <inheritdoc/>
    public void Execute(string file, string parameters)
    {
        if (file.Length == 0)
            return;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, parameters) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
        }
    }
}
