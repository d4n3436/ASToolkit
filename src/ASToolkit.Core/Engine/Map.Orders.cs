using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Engine;

public sealed partial class Map
{
    private readonly Dictionary<Sprite, PendingBuild> _builds = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Returns the definition a building is building.
    /// </summary>
    /// <param name="building">The building.</param>
    /// <returns>The definition, or null when the building is idle.</returns>
    public SpriteDefinition? GetPendingBuild(Sprite building) => _builds.TryGetValue(building, out var pending) ? pending.Definition : null;

    /// <summary>
    /// Creates a sprite of the default definition at a point, the goal of an order on that point.
    /// </summary>
    /// <param name="position">The point.</param>
    /// <returns>The goal sprite.</returns>
    public Sprite ConstructGoal(Vector3 position)
    {
        var definition = Definitions.Empty;
        var goal = new Sprite(-1, definition, position, Army.Player, 0) { CreatedAt = Time };
        var state = GetInitialAnimation(definition);
        goal.Start(state, default, Time, _rng);
        Collector.InsertToHash(goal);

        if (state != SpriteAnimation.Birth && !IsLoading)
        {
            CreateChildFor(goal, SpriteAnimation.Birth);
        }

        return goal;
    }

    private Vector3 GetBuildPosition(Sprite builder, int x, int y)
    {
        var position = builder.Position;
        float atX = x == 0 && y == 0 ? position.X : x;
        float atY = x == 0 && y == 0 ? position.Y : y;

        if (x < 0)
        {
            atX = position.X - x - _rng.Next() % (1 - x) * 2;
        }

        if (y < 0)
        {
            atY = position.Y - y - _rng.Next() % (1 - y) * 2;
        }

        return new Vector3(atX, atY, position.Z);
    }

    private Sprite? CreateBuilt(SpriteDefinition definition, Vector3 position, Angle direction)
    {
        if (definition.MaxHp > 0)
            return AddMonster(definition, position, direction);

        if (definition.Id.IsEndlessGenerator)
            return AddScripted(definition, position, direction);

        return Effects.Spawn(definition.Id, position, direction, Time);
    }

    private readonly record struct PendingBuild(SpriteDefinition Definition, int X, int Y);
}
