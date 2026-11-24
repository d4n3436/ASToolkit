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
        SpriteDefinition? first = null;
        bool isAnyLoaded = false;

        foreach (var definition in Definitions.LoadOrder)
        {
            if (definition is HardwareSpriteDefinition hardware && !hardware.IsCopy)
            {
                for (int surface = 0; surface < hardware.SurfaceCount; surface++)
                {
                    Session.Graphics.DrawLoadBar(this, isAnyLoaded && first is not null ? first : Definitions.Empty);
                }
            }

            isAnyLoaded = true;

            if (definition == Definitions.LoadingScreen)
            {
                first = definition;
            }

            Session.Graphics.DrawLoadBar(this, first);
        }
    }
}
