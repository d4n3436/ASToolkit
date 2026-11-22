using ASToolkit.Formats;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents a handler a level script defines for the death of an object.
/// </summary>
/// <param name="Id">The object whose death runs the handler.</param>
/// <param name="State">The death state that runs the handler.</param>
/// <param name="WinsLevel">A value indicating whether the handler wins the level.</param>
/// <param name="Destroys">The objects the handler removes from the map.</param>
/// <param name="Kills">The objects the handler puts into their death state.</param>
public sealed record ScriptHandler(KnownObject Id, SpriteAnimation State, bool WinsLevel, IReadOnlyList<KnownObject> Destroys, IReadOnlyList<KnownObject> Kills);
