namespace ASToolkit.Core.Engine;

internal interface ISpriteQuery
{
    Unit? Found { get; }

    void Consider(Unit sprite, bool listed);
}
