using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

internal sealed class ReplayRecorder : IWorldEventSink
{
    private const int FrameInterval = 100;
    private const int PlayerUnits = 2;
    private const float MinMove = 1e-6f;

    private readonly List<EffectState> _buffer = [];
    private EffectState[] _lastEffects = [];

    private readonly List<PlaythroughFrame> _frames = [];
    private readonly List<EffectFrame> _effectFrames = [];
    private readonly List<UnitFrame> _unitFrames = [];
    private readonly List<ShotTrace> _shots = [];
    private readonly List<SpawnEvent> _spawns = [];
    private readonly List<SpriteEvent> _createdSprites = [];
    private readonly List<SpriteEvent> _removedSprites = [];
    private readonly List<SpriteEvent> _pickups = [];
    private readonly List<PlacementFrame> _placementFrames = [];
    private readonly List<EffectState> _scenery = [];
    private readonly List<CameraShake> _shakes = [];
    private readonly List<ScreenFlash> _flashes = [];
    private readonly List<UnitTrack> _tracks = [];
    private readonly List<int> _listed = [];
    private List<PlacedUnit>? _placed;
    private int _frameSlot = -1;

    public void Capture(GameWorld world)
    {
        world.CopyVisualEffects(_buffer);
        var effects = CollectionsMarshal.AsSpan(_buffer);

        if (!effects.SequenceEqual(_lastEffects))
        {
            _lastEffects = effects.ToArray();
            _effectFrames.Add(new EffectFrame(world.Time, _lastEffects));
        }

        CaptureUnitFrame(world, world.Player);
        CaptureUnitFrame(world, world.Weapons.Torso);
        foreach (var monster in world.Monsters)
        {
            CaptureUnitFrame(world, monster);
        }

        MarkDeleted(world.Time);
        CapturePlacements(world);

        if (world.Time / FrameInterval != _frameSlot)
        {
            _frameSlot = world.Time / FrameInterval;
            CaptureFrame(world);
        }
    }

    // Record placed bodies and stand-ins whenever they stand somewhere new
    private void CapturePlacements(GameWorld world)
    {
        if (_placed is null)
        {
            _placed = [];
            foreach (var sprite in world.Sprites)
            {
                var unit = world.FindPlacementUnit(sprite);

                if (unit is not null)
                {
                    _placed.Add(new PlacedUnit(sprite, unit) { Position = unit.Position });
                }
            }
        }

        var placed = CollectionsMarshal.AsSpan(_placed);
        for (int index = 0; index < placed.Length; index++)
        {
            ref var entry = ref placed[index];

            if (world.IsRemoved(entry.Sprite) || Vector3.DistanceSquared(entry.Unit.Position, entry.Position) <= MinMove)
                continue;

            entry.Position = entry.Unit.Position;
            _placementFrames.Add(new PlacementFrame(entry.Sprite, world.Time, entry.Position));
        }
    }

    private void MarkDeleted(int time)
    {
        for (int index = _listed.Count - 1; index >= 0; index--)
        {
            int unit = _listed[index];

            ref var track = ref CollectionsMarshal.AsSpan(_tracks)[unit];

            if (track.SeenAt == time)
                continue;

            _unitFrames.Add(new UnitFrame(time, unit, UnitFrame.Deleted));
            track.ShownFrame = UnitFrame.Deleted;
            _listed[index] = _listed[^1];
            _listed.RemoveAt(_listed.Count - 1);
        }
    }

    private void CaptureUnitFrame(GameWorld world, Unit unit)
    {
        while (_tracks.Count <= unit.Number)
        {
            _tracks.Add(new UnitTrack { ShownFrame = UnitFrame.Deleted, SeenAt = -1 });
        }

        ref var track = ref CollectionsMarshal.AsSpan(_tracks)[unit.Number];

        if (track.SeenAt < 0)
        {
            _listed.Add(unit.Number);
        }

        track.SeenAt = world.Time;

        if (track.ShownFrame == unit.Frame)
            return;

        track.ShownFrame = unit.Frame;
        _unitFrames.Add(new UnitFrame(world.Time, unit.Number, unit.Frame));
    }

    public void Finish(GameWorld world)
    {
        if (_frames.Count > 0 && _frames[^1].Time < world.Time)
        {
            CaptureFrame(world);
        }
    }

    private void CaptureFrame(GameWorld world)
    {
        var units = new UnitState[PlayerUnits + world.Monsters.Count];
        units[0] = world.Weapons.GetPlayerState();
        units[1] = world.Weapons.Torso.GetState();
        int index = PlayerUnits;
        foreach (var monster in world.Monsters)
        {
            units[index++] = monster.GetState();
        }

        _frames.Add(new PlaythroughFrame(world.Time, units, _lastEffects, world.Cursor));
    }

    public PlaythroughReplay CreateReplay() =>
        new(_frames, _unitFrames, _effectFrames, _shots, _spawns, _removedSprites, _createdSprites, _pickups, _placementFrames, _scenery, _shakes, _flashes);

    public void RecordShot(in ShotTrace shot) => _shots.Add(shot);

    public void RecordSpawn(SpawnEvent spawn) => _spawns.Add(spawn);

    public void RecordSpriteCreation(MergedMapSprite sprite, int time) => _createdSprites.Add(new SpriteEvent(sprite, time));

    public void RecordSpriteRemoval(MergedMapSprite sprite, int time) => _removedSprites.Add(new SpriteEvent(sprite, time));

    public void RecordPickup(MergedMapSprite sprite, int time) => _pickups.Add(new SpriteEvent(sprite, time));

    public void RecordScenery(in EffectState scenery) => _scenery.Add(scenery);

    public void RecordCameraShake(CameraShake shake) => _shakes.Add(shake);

    public void RecordScreenFlash(ScreenFlash flash) => _flashes.Add(flash);

    private struct PlacedUnit
    {
        public PlacedUnit(MergedMapSprite sprite, Unit unit)
        {
            Sprite = sprite;
            Unit = unit;
        }

        public MergedMapSprite Sprite { get; }

        public Unit Unit { get; }

        public Vector3 Position { get; set; }
    }

    private struct UnitTrack
    {
        public int ShownFrame;
        public int SeenAt;
    }
}
