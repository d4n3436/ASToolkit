using ASToolkit.Engine.IO;
using ASToolkit.Core;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    private static ReadOnlySpan<byte> ObjectSectionText => "load 'VID'"u8;

    [EngineRoutine(0x00411510)]
    private void LoadObjectRecords(bool isMap)
    {
        if (!isMap)
            return;

        // TODO: Port the extra definitions of a map's OBJ chunk (0x411510, 0x411440)
        Session.Logger.Error(MapLogTag, LogError.Section, 0, ObjectSectionText);
    }

    [EngineRoutine(0x0044ca00)]
    private void ConstructBuilding(Sprite sprite)
    {
        ConstructUnit(sprite);
        _builds.Remove(sprite);
    }

    // TODO: Port the CivRobot class vtable (0x47a994): its Tact and actions
    [EngineRoutine(0x0044a590)]
    private void ConstructCivRobot(Sprite sprite)
    {
        ConstructCreature(sprite);
    }
}
