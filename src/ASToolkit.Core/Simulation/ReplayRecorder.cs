using System.Numerics;
using System.Runtime.InteropServices;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Simulation;

internal sealed class ReplayRecorder
{
    private const float MinMove = 1e-6f;
    private const float Tolerance = 0.25f;
    private const int MaxLinkDepth = 4;

    private readonly List<OpenTrack> _tracks = [];
    private readonly List<List<SpriteSample>> _trackSamples = [];
    private readonly Dictionary<Unit, int> _open = [];
    private readonly List<PendingSample> _captures = [];
    private readonly HashSet<Unit> _seen = [];
    private readonly List<ScreenInput> _inputs = [];
    private Dictionary<Unit, Vector3> _positions = [];
    private Dictionary<Unit, Vector3> _previousPositions = [];
    private readonly List<SpriteEvent> _createdSprites = [];
    private readonly List<SpriteEvent> _removedSprites = [];
    private readonly HashSet<MergedMapSprite> _gone = new(ReferenceEqualityComparer.Instance);
    private readonly List<PlacementFrame> _placementFrames = [];
    private readonly List<EffectState> _scenery = [];
    private readonly List<CameraShake> _shakes = [];
    private readonly List<ScreenFlash> _flashes = [];
    private readonly List<PlacedUnit> _placed = [];
    private int _spriteCount;
    private int _removedCount = -1;
    private int _settledCount;
    private int _flashedAt = -1;
    private int _shookAt = -1;

    public void Start(GameWorld world)
    {
        _spriteCount = world.Sprites.Count;
        foreach (var sprite in world.Sprites)
        {
            var unit = world.FindPlacementUnit(sprite);

            if (unit is not null && !sprite.Id.IsScripted)
            {
                _placed.Add(new PlacedUnit(sprite, unit) { Position = unit.Position });
            }
        }

        CaptureSprites(world);
    }

    public void Capture(GameWorld world)
    {
        CaptureSprites(world);
        CaptureScreen(world);
        var settled = world.Effects.Settled;
        for (; _settledCount < settled.Count; _settledCount++)
        {
            _scenery.Add(settled[_settledCount]);
        }

        int time = world.Time;
        _inputs.Add(new ScreenInput(time, world.Player.Position, world.Cursor));

        _captures.Clear();
        _seen.Clear();
        (_previousPositions, _positions) = (_positions, _previousPositions);
        _positions.Clear();
        AddUnit(world.Player);
        AddUnit(world.Weapons.Torso);
        foreach (var monster in world.Monsters)
        {
            AddUnit(monster);
        }

        foreach (var effect in world.Effects.Active)
        {
            Add(effect, SpawnedObjectSystem.GetState(effect));
        }

        foreach (var pickup in world.Pickups)
        {
            var body = pickup.Body;

            if (pickup.IsDropped && body is not null)
            {
                Add(body, pickup.GetState(time));
            }
        }

        foreach (var unit in world.Scripted)
        {
            Add(unit, SpawnedObjectSystem.GetState(unit));
        }

        // Take owners before the objects linked to them
        for (int pass = 0; _captures.Count > 0; pass++)
        {
            int kept = 0;
            for (int index = 0; index < _captures.Count; index++)
            {
                var capture = _captures[index];

                if (pass < MaxLinkDepth && capture.Owner is not null && _seen.Contains(capture.Owner) && !_open.ContainsKey(capture.Owner))
                {
                    _captures[kept++] = capture;
                    continue;
                }

                Capture(capture, time);
            }

            _captures.RemoveRange(kept, _captures.Count - kept);
        }

        CloseTracks(time);
        CapturePlacements(world);
    }

    public PlaythroughReplay CreateReplay(IReadOnlyList<SpriteEvent> pickups)
    {
        var tracks = new SpriteTrack[_tracks.Count];
        var samples = new List<SpriteSample>(_trackSamples.Sum(list => list.Count));
        for (int index = 0; index < tracks.Length; index++)
        {
            var list = _trackSamples[index];
            tracks[index] = new SpriteTrack(_tracks[index].Start, _tracks[index].End, samples.Count, list.Count, _tracks[index].Owner, _tracks[index].Lags);
            samples.AddRange(list);
        }

        return new PlaythroughReplay(tracks, samples.ToArray(), _inputs.ToArray(), _removedSprites, _createdSprites, pickups, _placementFrames.ToArray(), _scenery,
            _shakes.ToArray(), _flashes.ToArray());
    }

    private void AddUnit(Unit unit) =>
        Add(unit, new EffectState(unit.Kind.Id, unit.Position, unit.Direction, unit.Animation, unit.CreatedAt, unit.Frame, unit.KeyframePosition));

    private void Add(Unit unit, in EffectState state)
    {
        var owner = unit.Owner is not null && unit.Owner.Link == unit ? unit.Owner : null;
        bool rotates = unit.Kind.Object.Properties.HasFlag(ObjectProperties.HardwareDirect);
        bool lags = owner is not null && unit.Kind.Layer <= owner.Kind.Layer;
        _captures.Add(new PendingSample(unit, state, owner, rotates, lags));
        _seen.Add(unit);
    }

    // Keeps a sample only where the object's drawn look changes or it leaves the straight line of its last two samples
    private void Capture(in PendingSample capture, int time)
    {
        var key = capture.Key;
        var state = capture.State;

        int owner = capture.Owner is not null && _seen.Contains(capture.Owner) && _open.TryGetValue(capture.Owner, out int found) ? found : -1;

        if (!_open.TryGetValue(key, out int index))
        {
            index = -1;
        }
        else if (_tracks[index].Id != state.Id || _tracks[index].StartedAt != state.StartedAt || _tracks[index].Owner != owner || _tracks[index].Lags != capture.Lags)
        {
            Close(key, index, time);
            index = -1;
        }

        _positions[key] = state.Position;
        var position = owner < 0 ? state.Position : state.Position - GetOwnerPosition(capture);
        var sample = new SpriteSample(time, state.Id, position, state.Frame, state.Direction, state.Animation, state.Keyframe);

        if (index < 0)
        {
            index = _tracks.Count;
            _tracks.Add(new OpenTrack { Start = time, End = int.MaxValue, Id = state.Id, StartedAt = state.StartedAt, Owner = owner, Lags = capture.Lags });
            _trackSamples.Add([sample]);
            _open.Add(key, index);
        }
        else
        {
            var samples = _trackSamples[index];
            var last = samples[^1];
            var before = samples.Count > 1 ? samples[^2] : last;
            var predicted = SpriteSample.Predict(before, last, time);

            if (sample.Frame != last.Frame || (capture.Rotates && sample.Direction != last.Direction) || sample.Animation != last.Animation ||
                Vector3.DistanceSquared(predicted.Position, sample.Position) > Tolerance * Tolerance || Math.Abs(predicted.Keyframe - sample.Keyframe) > Tolerance)
            {
                samples.Add(sample);
            }
        }
    }

    private Vector3 GetOwnerPosition(in PendingSample capture)
    {
        var owner = capture.Owner!;

        return capture.Lags && _previousPositions.TryGetValue(owner, out var previous) ? previous : _positions.GetValueOrDefault(owner, owner.Position);
    }

    private void CloseTracks(int time)
    {
        foreach ((var key, int index) in _open)
        {
            if (!_seen.Contains(key))
            {
                Close(key, index, time);
            }
        }
    }

    private void Close(Unit key, int index, int time)
    {
        CollectionsMarshal.AsSpan(_tracks)[index].End = time;
        _open.Remove(key);
    }

    private void CaptureSprites(GameWorld world)
    {
        for (; _spriteCount < world.Sprites.Count; _spriteCount++)
        {
            _createdSprites.Add(new SpriteEvent(world.Sprites[_spriteCount], world.Time));
        }

        if (world.RemovedCount == _removedCount)
            return;

        _removedCount = world.RemovedCount;
        foreach (var sprite in world.Sprites)
        {
            if (world.IsRemoved(sprite) && _gone.Add(sprite))
            {
                _removedSprites.Add(new SpriteEvent(sprite, world.Time));
            }
        }
    }

    private void CaptureScreen(GameWorld world)
    {
        var shake = world.CameraShake;

        if (shake is not null && shake.Value.Time != _shookAt)
        {
            _shookAt = shake.Value.Time;
            _shakes.Add(shake.Value);
        }

        var flash = world.ScreenFlash;

        if (flash is not null && flash.Value.Time != _flashedAt)
        {
            _flashedAt = flash.Value.Time;
            _flashes.Add(flash.Value);
        }
    }

    private void CapturePlacements(GameWorld world)
    {
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

    private struct OpenTrack
    {
        public int Start;
        public int End;
        public KnownObject Id;
        public int StartedAt;
        public int Owner;
        public bool Lags;
    }

    private readonly record struct PendingSample(Unit Key, EffectState State, Unit? Owner, bool Rotates, bool Lags);
}
