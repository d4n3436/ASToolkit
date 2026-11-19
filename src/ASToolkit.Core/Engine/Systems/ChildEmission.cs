using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal static class ChildEmission
{
    public const float RandomScale = 3.0518509E-05f;

    public const int MirroredPairCount = 2;

    public static ChildBatch GetChildren(SpriteDefinition owner, SpriteDefinition child, SpriteAnimation animation,
        Vector3 position, Angle direction, Random random, bool secondPulse = false) =>
        new(owner, child, animation, position, direction, random, secondPulse);

    public readonly record struct Spawn(KnownObject Id, Vector3 Position, Angle Direction, Vector2 Lateral);

    public readonly record struct ChildBatch(SpriteDefinition Owner, SpriteDefinition Child, SpriteAnimation Animation,
        Vector3 Position, Angle Direction, Random Random, bool SecondPulse)
    {
        public Enumerator GetEnumerator() => new(this);
    }

    public struct Enumerator
    {
        private const int MirroredChildIndex = 1;
        private const int CenteredChildIndex = 2;
        private readonly ChildBatch _source;
        private readonly int _childId;
        private readonly int _count;
        private readonly Vector3 _offset;
        private readonly Angle _offsetDirection;
        private int _index;

        public Enumerator(ChildBatch source)
        {
            _source = source;
            ref readonly var owner = ref source.Owner.Object;
            _childId = source.Owner.Children[(int)source.Animation];
            _count = Math.Abs(source.Owner.ChildCounts[(int)source.Animation]);
            _offset = new Vector3(owner.ChildX[(int)source.Animation], owner.ChildY[(int)source.Animation], owner.ChildZ[(int)source.Animation]);
            _offsetDirection = source.Child.Object.Properties.HasFlag(ObjectProperties.FixedOffset)
                ? default : source.Owner.GetFrameDirection(source.Direction);
            _index = -1;

            if (source.Owner.AttacksTwice && _count == MirroredPairCount)
            {
                _index = source.SecondPulse ? 0 : -1;
                _count = _index + MirroredPairCount;
            }
        }

        public Spawn Current { get; private set; }

        // TODO: Port the region owner's random spread
        [EngineRoutine(0x00444dd0)]
        public bool MoveNext()
        {
            if (++_index >= _count)
                return false;

            var d = _offsetDirection;
            var side = new Vector2(d.Cosine, d.ProjectedSine);
            var forward = new Vector2(d.Sine, -d.ProjectedCosine);
            var offset = _offset.AsVector2();
            Vector2 lateral;
            Vector2 shift;

            if (_childId < 0)
            {
                var spread = offset - new Vector2(_source.Random.Next(), _source.Random.Next()) * (offset + offset) * RandomScale;
                lateral = -(side * spread.X);
                shift = forward * spread.Y + lateral;
            }
            else if (_index == CenteredChildIndex)
            {
                lateral = Vector2.Zero;
                shift = new Vector2(d.Sine, d.ProjectedCosine) * offset.Y;
            }
            else
            {
                lateral = _index == MirroredChildIndex ? -(side * offset.X) : side * offset.X;
                shift = forward * offset.Y + lateral;
            }

            var at = new Vector3(shift + _source.Position.AsVector2(), _offset.Z + _source.Position.Z);
            var direction = _source.Child.Object.Properties.HasFlag(ObjectProperties.RandomBirth)
                ? new Angle((byte)_source.Random.Next(Angle.FullTurn)) : _source.Direction;
            Current = new Spawn(_source.Child.Id, at, direction, lateral);

            return true;
        }
    }
}
