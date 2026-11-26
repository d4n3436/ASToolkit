namespace ASToolkit.Engine.IO;

/// <summary>
/// Opens files, programs and links with the program the operating system associates with them.
/// </summary>
public interface IShellDevice
{
    /// <summary>
    /// Opens a file, program or link in a visible window.
    /// </summary>
    /// <param name="file">The file, program or link to open.</param>
    /// <param name="parameters">The command line passed to a program, or an empty string for none.</param>
    void Execute(string file, string parameters);
}
