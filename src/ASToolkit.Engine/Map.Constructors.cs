using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

public sealed partial class Map
{
    private static ReadOnlySpan<byte> ObjectSectionText => "load 'VID'"u8;

    [EngineRoutine(0x00411510)]
    private void LoadObjectRecords(bool isMap)
    {
        if (!isMap)
        {
            RedrawObjectRecords();
            return;
        }

        // TODO: Port the extra definitions of a map's OBJ chunk (0x411510, 0x411440)
        Session.Logger.Error(MapLogTag, LogError.Section, 0, ObjectSectionText);
    }

    private void RedrawObjectRecords()
    {
        int redraw = 0;
        SpriteDefinition? first = null;
        bool isAnyLoaded = false;

        foreach (var definition in Definitions.LoadOrder)
        {
            if (definition is HardwareSpriteDefinition hardware)
            {
                for (int surface = 0; surface < hardware.SurfaceCount; surface++)
                {
                    Session.Graphics.DrawLoadingScreen(this, isAnyLoaded && first is not null ? first : Definitions.Empty, Session.StartupLoadingRedraws.Contains(redraw++));
                }
            }

            isAnyLoaded = true;

            if (definition == Definitions.LoadingScreen)
            {
                first = definition;
            }

            Session.Graphics.DrawLoadingScreen(this, first, Session.StartupLoadingRedraws.Contains(redraw++));
        }
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
