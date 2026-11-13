using System.Collections;
using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal static class ChildEmission
{
    public const float VerticalProjection = 4096 * 0.00017262212f;

    public const int MirroredPairCount = 2;

    public static ChildBatch GetChildren(ObjectDefinition owner, ObjectDefinition child, SpriteAnimation animation,
        Vector3 position, Facing facing, Random random, bool secondPulse = false) =>
        new(owner, child, animation, position, facing, random, secondPulse);

    public readonly record struct Spawn(KnownObject Id, Vector3 Position, Facing Facing, Vector2 Lateral);

    public readonly record struct ChildBatch(ObjectDefinition Owner, ObjectDefinition Child, SpriteAnimation Animation,
        Vector3 Position, Facing Facing, Random Random, bool SecondPulse) : IEnumerable<Spawn>
    {
        public Enumerator GetEnumerator() => new(this);

        IEnumerator<Spawn> IEnumerable<Spawn>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct Enumerator : IEnumerator<Spawn>
    {
        private const int CenteredChildIndex = 2;
        private readonly ChildBatch _source;
        private readonly int _childId;
        private readonly int _count;
        private readonly float _x;
        private readonly float _y;
        private readonly float _height;
        private readonly Vector2 _forward;
        private readonly Vector2 _side;
        private int _index;

        public Enumerator(ChildBatch source)
        {
            _source = source;
            ref readonly var owner = ref source.Owner.Object;
            _childId = owner.Children[(int)source.Animation];
            _count = Math.Abs(owner.ChildCounts[(int)source.Animation]);
            _x = owner.ChildX[(int)source.Animation];
            _y = owner.ChildY[(int)source.Animation];
            _height = owner.ChildHeight[(int)source.Animation];
            var quantized = owner.Directions > 0
                ? new Facing(unchecked((byte)(owner.GetDirection(source.Facing.Value) * Facing.FullTurn / owner.Directions))) : source.Facing;

            if (source.Child.Object.Flags.HasFlag(ObjectFlags.FixedOffset))
            {
                quantized = default;
            }

            _forward = quantized.Direction;
            _side = new Vector2(-_forward.Y, _forward.X);
            _index = -1;

            if (source.Owner.AttacksTwice && _count == MirroredPairCount)
            {
                _index = source.SecondPulse ? 0 : -1;
                _count = _index + MirroredPairCount;
            }
        }

        public Spawn Current { get; private set; }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (++_index >= _count)
                return false;

            float across = _childId < 0 ? _source.Random.NextSingle() * _x * 2 - _x
                : _index == 1 ? -_x : _index == CenteredChildIndex ? 0 : _x;
            float along = _childId < 0 ? _y - _source.Random.NextSingle() * _y * 2 : _y;
            var lateral = _side * across;
            var offset = lateral + _forward * along;

            if (_childId > 0 && _index == CenteredChildIndex)
            {
                lateral = Vector2.Zero;
                offset = _forward with { Y = -_forward.Y } * along;
            }

            var at = _source.Position + new Vector3(offset.X, offset.Y * VerticalProjection, _height);
            var direction = _source.Child.Object.Flags.HasFlag(ObjectFlags.RandomFacing)
                ? new Facing((byte)_source.Random.Next(Facing.FullTurn)) : _source.Facing;
            Current = new Spawn(_source.Child.Id, at, direction, lateral with { Y = lateral.Y * VerticalProjection });

            return true;
        }

        public readonly void Dispose() { }

        void IEnumerator.Reset() => throw new NotSupportedException();
    }
}
