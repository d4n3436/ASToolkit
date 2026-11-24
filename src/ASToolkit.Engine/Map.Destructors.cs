namespace ASToolkit.Engine;

public sealed partial class Map
{
    /// <summary>
    /// Deletes a sprite.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    public void DeleteSprite(Sprite sprite) => sprite.Destruct();
}
