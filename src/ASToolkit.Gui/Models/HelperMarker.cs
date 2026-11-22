using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Gui.Models;

/// <summary>
/// Represents the marker the map viewer draws over a helper sprite, a sprite placed on the map that the game never shows, such as a monster spawner, a script trigger or a hidden blocker.
/// </summary>
/// <param name="Id">The object ID of the sprite.</param>
/// <param name="Position">The position of the sprite.</param>
/// <param name="Army">The army of the sprite.</param>
/// <param name="Kind">The role of the sprite.</param>
public sealed record HelperMarker(KnownObject Id, Vector3 Position, Army Army, HelperKind Kind);
