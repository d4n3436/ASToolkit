using ASToolkit.Core.Engine;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the recorded replay of a playthrough, such as the unit snapshots, sprite frames, effects, shots and map changes over time.
/// </summary>
/// <param name="Frames">The snapshots of the units and effects at a fixed interval.</param>
/// <param name="UnitFrames">The sprite frames of the units each time one changes, in time order.</param>
/// <param name="EffectFrames">The visual effects recorded at simulation ticks.</param>
/// <param name="Shots">The shots fired during the playthrough.</param>
/// <param name="Spawns">The objects created by action stacks.</param>
/// <param name="RemovedSprites">The sprites removed by action stacks and scripts.</param>
/// <param name="CreatedSprites">The sprites the scripts created.</param>
/// <param name="PickupEvents">The time when the player collected each map pickup.</param>
/// <param name="DoorFrames">The door heights at their animation ticks.</param>
/// <param name="Scenery">The blood stains, corpses and debris left on the map, with their creation times.</param>
public sealed record PlaythroughReplay(
    IReadOnlyList<PlaythroughFrame> Frames,
    IReadOnlyList<UnitFrame> UnitFrames,
    IReadOnlyList<EffectFrame> EffectFrames,
    IReadOnlyList<ShotTrace> Shots,
    IReadOnlyList<SpawnEvent> Spawns,
    IReadOnlyList<SpriteEvent> RemovedSprites,
    IReadOnlyList<SpriteEvent> CreatedSprites,
    IReadOnlyList<SpriteEvent> PickupEvents,
    IReadOnlyList<DoorFrame> DoorFrames,
    IReadOnlyList<EffectState> Scenery);
