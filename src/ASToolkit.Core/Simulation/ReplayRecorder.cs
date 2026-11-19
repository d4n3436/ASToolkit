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
    private readonly Dictionary<Sprite, int> _open = [];
    private readonly List<PendingSample> _captures = [];
    private readonly HashSet<Sprite> _seen = [];
    private readonly List<ScreenInput> _inputs = [];
    private Dictionary<Sprite, Vector3> _positions = [];
    private Dictionary<Sprite, Vector3> _previousPositions = [];
    private readonly List<SpriteEvent> _createdSprites = [];
    private readonly List<SpriteEvent> _removedSprites = [];
    private readonly HashSet<MergedMapSprite> _gone = new(ReferenceEqualityComparer.Instance);
    private readonly List<PlacementFrame> _placementFrames = [];
    private PickupTracker? _pickups;
    private readonly List<EffectState> _scenery = [];
    private readonly List<CameraShake> _shakes = [];
    private readonly List<ScreenFlash> _flashes = [];
    private readonly List<PlacedSprite> _placed = [];
    private int _spriteCount;
    private int _removedCount = -1;
    private int _settledCount;
    private int _flashedAt = -1;
    private int _shookAt = -1;

    public void Start(Map map)
    {
        _pickups = new PickupTracker(map);
        _spriteCount = map.Sprites.Count;
        foreach (var placement in map.Sprites)
        {
            var sprite = map.FindPlacementSprite(placement);

            if (sprite is not null && !placement.Id.IsScripted)
            {
                _placed.Add(new PlacedSprite(placement, sprite) { Position = sprite.Position });
            }
        }

        CaptureSprites(map);
    }

    public void Capture(Map map)
    {
        CaptureSprites(map);
        CaptureScreen(map);
        var settled = map.Effects.Settled;
        for (; _settledCount < settled.Count; _settledCount++)
        {
            _scenery.Add(settled[_settledCount]);
        }

        int time = map.Time;
        _inputs.Add(new ScreenInput(time, map.Player.Position, map.Cursor));

        _captures.Clear();
        _seen.Clear();
        (_previousPositions, _positions) = (_positions, _previousPositions);
        _positions.Clear();
        AddSprite(map.Player);
        AddSprite(map.Weapons.Torso);
        foreach (var monster in map.Monsters)
        {
            AddSprite(monster);
        }

        foreach (var effect in map.Effects.Active)
        {
            Add(effect, SpawnedObjectSystem.GetState(effect));
        }

        foreach (var pickup in (_pickups ??= new PickupTracker(map)).Pickups)
        {
            var body = PickupTracker.IsDropped(pickup) ? map.FindPlacementSprite(pickup) : null;

            if (body is not null)
            {
                Add(body, SpawnedObjectSystem.GetState(body));
            }
        }

        foreach (var sprite in map.Scripted)
        {
            Add(sprite, SpawnedObjectSystem.GetState(sprite));
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
        CapturePlacements(map);
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

    private void AddSprite(Sprite sprite) =>
        Add(sprite, new EffectState(sprite.Definition.Id, sprite.Position, sprite.Direction, sprite.Animation, sprite.CreatedAt, sprite.Frame, sprite.KeyframePosition));

    private void Add(Sprite sprite, in EffectState state)
    {
        var owner = sprite.Owner is not null && sprite.Owner.Link == sprite ? sprite.Owner : null;
        bool rotates = sprite.Definition.Object.Properties.HasFlag(ObjectProperties.HardwareDirect);
        bool lags = owner is not null && sprite.Definition.Layer <= owner.Definition.Layer;
        _captures.Add(new PendingSample(sprite, state, owner, rotates, lags));
        _seen.Add(sprite);
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

    private void Close(Sprite key, int index, int time)
    {
        CollectionsMarshal.AsSpan(_tracks)[index].End = time;
        _open.Remove(key);
    }

    private void CaptureSprites(Map map)
    {
        for (; _spriteCount < map.Sprites.Count; _spriteCount++)
        {
            _createdSprites.Add(new SpriteEvent(map.Sprites[_spriteCount], map.Time));
        }

        if (map.RemovedCount == _removedCount)
            return;

        _removedCount = map.RemovedCount;
        foreach (var sprite in map.Sprites)
        {
            if (map.IsRemoved(sprite) && _gone.Add(sprite))
            {
                _removedSprites.Add(new SpriteEvent(sprite, map.Time));
            }
        }
    }

    private void CaptureScreen(Map map)
    {
        var shake = map.CameraShake;

        if (shake is not null && shake.Value.Time != _shookAt)
        {
            _shookAt = shake.Value.Time;
            _shakes.Add(shake.Value);
        }

        var flash = map.ScreenFlash;

        if (flash is not null && flash.Value.Time != _flashedAt)
        {
            _flashedAt = flash.Value.Time;
            _flashes.Add(flash.Value);
        }
    }

    private void CapturePlacements(Map map)
    {
        var placed = CollectionsMarshal.AsSpan(_placed);
        for (int index = 0; index < placed.Length; index++)
        {
            ref var entry = ref placed[index];

            if (map.IsRemoved(entry.Placement) || Vector3.DistanceSquared(entry.Sprite.Position, entry.Position) <= MinMove)
                continue;

            entry.Position = entry.Sprite.Position;
            _placementFrames.Add(new PlacementFrame(entry.Placement, map.Time, entry.Position));
        }
    }

    private struct PlacedSprite
    {
        public PlacedSprite(MergedMapSprite placement, Sprite sprite)
        {
            Placement = placement;
            Sprite = sprite;
        }

        public MergedMapSprite Placement { get; }

        public Sprite Sprite { get; }

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

    private readonly record struct PendingSample(Sprite Key, EffectState State, Sprite? Owner, bool Rotates, bool Lags);
}
