using System.Drawing;
using ASToolkit.Formats;
using ASToolkit.Core;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    [EngineRoutine(0x00447030)]
    private int CursorAction(Sprite sprite, int act, int var1, int var2, int var3, Sprite? target)
    {
        switch ((ActionCode)act)
        {
            case ActionCode.ChangeAnimation:
                CursorSprite.ShowCursor((SpriteAnimation)var1);
                return 0;
            case ActionCode.ChangeVid:
                if (Definitions.Find((KnownObject)var1) is null || (KnownObject)var1 == sprite.Definition.Id)
                    return 0;

                AddToUpdates(sprite);
                UnitAction(sprite, act, var1, var2, var3, target);
                Collector.DeleteFromHash(sprite);
                foreach (var link in sprite.EnumerateLinks())
                {
                    Collector.DeleteFromHash(link);
                }

                RemoveFromUpdates(sprite);

                return 0;
            case ActionCode.ChangeCoor:
                MoveSystemCursor(new Point(var1, var2));
                return 0;
            default:
                return DispatchClassAction(sprite, act, var1, var2, var3, target);
        }
    }

    private void MoveSystemCursor(Point target)
    {
        MoveMouse(target + new Size(Input.WindowOrigin), Input.WindowOrigin, Session.View, Camera);
    }
}
