using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

/// <summary>
/// Represents an object created by a placed sprite's action stack.
/// </summary>
/// <param name="Id">The created object.</param>
/// <param name="Position">The position where it was created.</param>
/// <param name="Time">The simulation time in milliseconds.</param>
/// <param name="Source">The placement that ran the action stack.</param>
public sealed record SpawnEvent(KnownObject Id, Vector3 Position, int Time, MergedMapSprite? Source);
