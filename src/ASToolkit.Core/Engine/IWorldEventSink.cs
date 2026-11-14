using System.Numerics;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal interface IWorldEventSink
{
    void RecordShot(in ShotTrace shot);

    void RecordSpawn(SpawnEvent spawn);

    void RecordSpriteCreation(MergedMapSprite sprite, int time);

    void RecordSpriteRemoval(MergedMapSprite sprite, int time);

    void RecordPickup(MergedMapSprite sprite, int time);

    void RecordScenery(in EffectState scenery);

    void RecordCameraShake(CameraShake shake);

    void RecordScreenFlash(ScreenFlash flash);
}
