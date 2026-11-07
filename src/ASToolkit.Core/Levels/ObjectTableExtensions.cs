using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Levels;

/// <summary>
/// Provides the classes of objects that go with an <see cref="ObjectTable"/>.
/// </summary>
public static class ObjectTableExtensions
{
    extension(ObjectTable objects)
    {
        /// <summary>
        /// Returns how an object stands in the player's way, walk-through for an object the table does not hold.
        /// </summary>
        public BlockingClass GetBlocking(KnownObject id)
        {
            if (!objects.TryFind(id, out var obj))
            {
                return BlockingClass.WalkThrough;
            }

            return obj.IsSolid ? BlockingClass.Solid : obj.IsHittable ? BlockingClass.Hittable : BlockingClass.WalkThrough;
        }

        /// <summary>
        /// Returns the index of the sprite one of the sprites stands on, -1 when it stands raised on nothing found, or null when it stands on
        /// the floor.
        /// </summary>
        public int? FindSupport(IReadOnlyList<MergedMapSprite> sprites, int index)
        {
            var sprite = sprites[index];
            if (sprite.Position.Z <= 2)
            {
                return null;
            }

            int bestIndex = -1;
            float bestDistance = float.MaxValue;
            for (int otherIndex = 0; otherIndex < sprites.Count; otherIndex++)
            {
                var other = sprites[otherIndex];
                if (otherIndex == index || other.Position.Z >= sprite.Position.Z - 2)
                {
                    continue;
                }

                var size = objects.TryFind(other.Id, out var obj) ? obj.Size : Vector3.Zero;
                if (size.Z < 5 || Math.Abs(other.Position.X - sprite.Position.X) > size.X + 6 || Math.Abs(other.Position.Y - sprite.Position.Y) > size.Y + 6)
                {
                    continue;
                }

                float distance = Math.Abs(other.Position.X - sprite.Position.X) + Math.Abs(other.Position.Y - sprite.Position.Y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = otherIndex;
                }
            }

            return bestIndex;
        }
    }
}
