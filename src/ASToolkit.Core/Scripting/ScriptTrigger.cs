using System.Drawing;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Scripting;

/// <summary>
/// Represents a block of a level script that runs once when the player first comes within reach of a point.
/// </summary>
/// <param name="Area">The area the player enters.</param>
/// <param name="Respawn">Where the block moves the respawn point.</param>
/// <param name="RespawnPoint">The respawn point the block names when it names one.</param>
/// <param name="LocksDoors">A value indicating whether the block locks the doors.</param>
/// <param name="LightOff">The ambient color the block turns the lights off to, or null.</param>
/// <param name="Quake">The length of the earthquake the block starts in milliseconds, or 0.</param>
/// <param name="SwapsKeypads">A value indicating whether the block turns the lit keypads off.</param>
/// <param name="Wins">A value indicating whether the block wins the level.</param>
/// <param name="Kills">The objects the block puts into their death state.</param>
public sealed record ScriptTrigger(TriggerArea Area, ScriptRespawn Respawn, Point RespawnPoint, bool LocksDoors, uint? LightOff, int Quake,
    bool SwapsKeypads, bool Wins, IReadOnlyList<KnownObject> Kills);
