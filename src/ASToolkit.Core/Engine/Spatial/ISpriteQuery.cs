namespace ASToolkit.Core.Engine;

internal interface ISpriteQuery
{
    Sprite? Found { get; }

    void Consider(Sprite sprite, bool listed);

    void ConsiderMenu(Sprite sprite);
}
