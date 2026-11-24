using ASToolkit.Engine.IO;

namespace ASToolkit.Engine;

public partial class Map
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

        // TODO: Port the extra VIDs of a map's OBJ chunk (0x411510, 0x411440)
        Session.Error.Error(MapLogTag, LogError.Section, 0, ObjectSectionText);
    }

    private void RedrawObjectRecords()
    {
        Vid? first = null;
        bool isAnyLoaded = false;

        foreach (var vid in Vids.LoadOrder)
        {
            if (vid is VidHardware { IsCopy: false } hardware)
            {
                for (int surface = 0; surface < hardware.SurfaceCount; surface++)
                {
                    Session.Graphics.DrawLoadBar(this, isAnyLoaded && first is not null ? first : Vids.Empty);
                }
            }

            isAnyLoaded = true;

            if (vid == Vids.LoadingScreen)
            {
                first = vid;
            }

            Session.Graphics.DrawLoadBar(this, first);
        }
    }
}
