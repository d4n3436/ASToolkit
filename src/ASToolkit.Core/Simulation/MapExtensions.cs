using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Simulation;

/// <summary>
/// Provides views of a map's update lists, derived from the engine state.
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
        /// Gets the units of the collision list other than the scripted ones, buildings, the player's body and its torso.
        /// </summary>
        /// <remarks>Deleting sprites while enumerating can skip a unit, because the collision list fills a removed slot with its last entry.</remarks>
        public SpriteView Monsters => new(map, SpriteViewKind.Monsters);

        /// <summary>
        /// Gets the scripted sprites and the updated sprites that didn't come from the map.
        /// </summary>
        public SpriteView ActiveSprites => new(map, SpriteViewKind.Active);

        /// <summary>
        /// Gets the scripted sprites in the update lists.
        /// </summary>
        public SpriteView ScriptedSprites => new(map, SpriteViewKind.Scripted);

        /// <summary>
        /// Determines whether a living monster has the given object ID.
        /// </summary>
        /// <param name="id">The object ID.</param>
        /// <returns><see langword="true"/> if a living monster has the object ID; otherwise, <see langword="false"/>.</returns>
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
