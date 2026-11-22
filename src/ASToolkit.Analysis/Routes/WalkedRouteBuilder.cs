using ASToolkit.Engine;
using ASToolkit.Formats;
using ASToolkit.Analysis.Scripting;

namespace ASToolkit.Analysis.Routes;

/// <summary>
/// Provides the walk of an Alien Shooter 2 level through its quest steps over the ground the engine lets the player walk on.
/// </summary>
public static class WalkedRouteBuilder
{
    /// <summary>
    /// Walks the quest steps of a level from the player start.
    /// </summary>
    /// <remarks>
    /// A sprite the scripts open counts as open once the quest its opening waits for has begun, or once the player has entered the trigger area that opens it.
    /// When the player cannot reach a step, the player goes to a place that gives control of a unit and the unit walks the steps until the player can reach one again.
    /// A sprite to destroy counts as destroyed once a walk passes within a shot of it.
    /// </remarks>
    /// <param name="gameFolder">The game folder, which holds the sprite files.</param>
    /// <param name="map">The map.</param>
    /// <param name="objects">The object table of the game.</param>
    /// <param name="logic">The script events and quest steps of the level.</param>
    /// <returns>The walked route, or null if the map doesn't place a player or the level doesn't have quest steps.</returns>
    public static WalkedRoute? Build(string gameFolder, MapResourceBuilder map, ObjectTable objects, LevelLogic logic)
    {
        var start = map.Sprites.FirstOrDefault(sprite => sprite.Id == KnownObject.SequelPlayer);
        if (start is null || logic.Progression.Count == 0 || !objects.TryFind(KnownObject.SequelPlayer, out var playerObject))
        {
            return null;
        }

        var world = new WalkWorld(map, logic, new SpritePartReader(gameFolder, objects));
        return new RouteWalk(world, map, objects, logic, playerObject, start.Position).Run();
    }
}
