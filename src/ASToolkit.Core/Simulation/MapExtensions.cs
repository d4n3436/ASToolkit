using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Provides views of a map's update lists for the simulation harness, derived from the engine state.
/// </summary>
public static class MapExtensions
{
    extension(Map map)
    {
        /// <summary>
        /// Gets the sprites of the update lists, layer by layer, in update order.
        /// </summary>
        public SpriteView UpdatedSprites => new(map, SpriteViewKind.Updated);

        /// <summary>
        /// Gets the units of the collision list that are not scripted, buildings, the player's body or its torso.
        /// </summary>
        /// <remarks>Deleting sprites while enumerating can skip a unit, because the collision list fills a removed slot with its last entry.</remarks>
        public SpriteView Monsters => new(map, SpriteViewKind.Monsters);

        /// <summary>
        /// Gets the updated sprites that did not come from the map, plus the scripted ones.
        /// </summary>
        public SpriteView ActiveSprites => new(map, SpriteViewKind.Active);

        /// <summary>
        /// Gets the scripted sprites in the update lists.
        /// </summary>
        public SpriteView ScriptedSprites => new(map, SpriteViewKind.Scripted);

        /// <summary>
        /// Returns whether a living monster has an object id.
        /// </summary>
        /// <param name="id">The object id.</param>
        /// <returns><see langword="true"/> if a living monster has the object id; otherwise, <see langword="false"/>.</returns>
        public bool HasLivingMonster(KnownObject id)
        {
            foreach (var sprite in map.Monsters)
            {
                if (sprite.Definition.Id == id && sprite.IsAlive)
                    return true;
            }

            return false;
        }
    }
}
