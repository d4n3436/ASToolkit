using ASToolkit.Core.Engine;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Represents the recorded replay of a playthrough, such as the tracks of its units and effects, the camera inputs and the map changes over time.
/// </summary>
/// <param name="Tracks">The objects drawn over the map, in the order they appeared, the player's legs first.</param>
/// <param name="Samples">The samples of the tracks, grouped by track in time order.</param>
/// <param name="Inputs">The player positions and mouse cursors of every tick, which the game's camera follows.</param>
/// <param name="RemovedSprites">The sprites removed by action stacks and scripts.</param>
/// <param name="CreatedSprites">The sprites the scripts created.</param>
/// <param name="PickupEvents">The time when the player collected each map pickup.</param>
/// <param name="PlacementFrames">The positions of pushed map objects each time one moves.</param>
/// <param name="Scenery">The blood stains, corpses and debris left on the map, with their creation times.</param>
/// <param name="Shakes">The camera offsets of the frames an earthquake shook.</param>
/// <param name="Flashes">The flashes of the whole screen.</param>
public sealed record PlaythroughReplay(
    SpriteTrack[] Tracks,
    SpriteSample[] Samples,
    ScreenInput[] Inputs,
    IReadOnlyList<SpriteEvent> RemovedSprites,
    IReadOnlyList<SpriteEvent> CreatedSprites,
    IReadOnlyList<SpriteEvent> PickupEvents,
    PlacementFrame[] PlacementFrames,
    IReadOnlyList<EffectState> Scenery,
    CameraShake[] Shakes,
    ScreenFlash[] Flashes)
{
    /// <summary>
    /// Gets how a track's object looks at a time, carrying its position on from its last two samples.
    /// </summary>
    /// <param name="track">The index of the track.</param>
    /// <param name="time">The time, in milliseconds.</param>
    /// <returns>How the object looks at the time, with its map position.</returns>
    public SpriteSample GetSample(int track, int time)
    {
        var entry = Tracks[track];
        int at = entry.FirstSample + Math.Max(0, new ReadOnlySpan<SpriteSample>(Samples, entry.FirstSample, entry.SampleCount).CountAtOrBefore(time) - 1);
        var sample = SpriteSample.Predict(Samples[Math.Max(entry.FirstSample, at - 1)], Samples[at], time);

        if (entry.Owner < 0)
            return sample;

        var owner = Tracks[entry.Owner];
        int previous = Inputs.FindLastAtOrBefore(time);
        int ownerTime = !entry.LagsOwner ? time : previous > 0 ? Inputs[previous - 1].Time : time;

        return sample with { Position = sample.Position + GetSample(entry.Owner, Math.Clamp(ownerTime, owner.Start, owner.End - 1)).Position };
    }
}

