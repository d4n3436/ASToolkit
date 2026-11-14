using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

// An entry whose target was deleted has neither a point nor a target
internal readonly record struct UnitOrder(ActionCode Code, Vector2? Point, Unit? Target, int Var1 = 0, int Var2 = 0)
{
    public const int LastState = 0x10;

    public static UnitOrder FromAction(StackAction action) => new(action.Code, null, null, action.Var1, action.Var2);
}
