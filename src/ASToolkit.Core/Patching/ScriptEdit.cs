namespace ASToolkit.Core.Patching;

/// <summary>
/// Represents a text replacement in a game file, written with <c>\n</c> line breaks that match the file's own.
/// </summary>
/// <param name="File">The path of the file under the game folder.</param>
/// <param name="Old">The text to replace.</param>
/// <param name="New">The replacement.</param>
/// <param name="Count">The number of times <paramref name="Old"/> occurs in the file.</param>
internal sealed record ScriptEdit(string File, string Old, string New, int Count = 1);
