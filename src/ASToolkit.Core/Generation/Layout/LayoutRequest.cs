namespace ASToolkit.Core.Generation.Layout;

/// <summary>
/// Represents what a layout must hold besides its rooms.
/// </summary>
/// <param name="Gate">The gate, or null for none.</param>
/// <param name="SecretClosets">The number of secret closets.</param>
/// <param name="SpawnClosets">The number of spawn closets.</param>
/// <param name="Shortcuts">The number of breakable shortcuts.</param>
/// <param name="Pens">The number of fenced pens monsters are released from.</param>
public sealed record LayoutRequest(GateRequest? Gate, int SecretClosets, int SpawnClosets, int Shortcuts, int Pens = 0);
