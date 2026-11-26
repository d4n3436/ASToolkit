using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a terrain or object sprite.
/// </summary>
public class Terrain : Sprite
{
    private protected int _unused;

    /// <summary>
    /// Initializes a new instance of the <see cref="Terrain"/> class on a map.
    /// </summary>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, or null.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    [EngineRoutine(0x0044bd60)]
    public Terrain(Vid vid, Vector3 position, Angle direction, Sprite? parent, Map map)
        : base(vid, position, direction, parent, map)
    {
        _unused = -1;
        ShouldRefillAmmo = false;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the terrain refills its ammunition.
    /// </summary>
    public bool ShouldRefillAmmo { get; set; }

    [EngineRoutine(0x0044bea0)]
    internal override int Action(ActionCode act, int var1, int var2, int var3)
    {
        if (act != ActionCode.Repair)
            return base.Action(act, var1, var2, var3);

        Repair(true);

        return 0;
    }

    [EngineRoutine(0x0044bea0)]
    internal override int Action(ActionCode act, ref Resource stream, int version)
    {
        if (act != ActionCode.RestoreOld)
            return base.Action(act, ref stream, version);

        base.Action(act, ref stream, version);
        Span<byte> skipped = stackalloc byte[sizeof(short)];
        stream.Read(skipped[..(version > OldArmyVersion ? sizeof(short) : sizeof(byte))]);

        return 0;
    }

    [EngineRoutine(0x0044bf10)]
    private void Repair(bool relink)
    {
        int army = (int)Army & Vid.ArmyMask;
        ChangeHp(Vid.ArmyMaxHp[army]);
        var linkVid = Vid.LinkVid;

        if (linkVid is not null && Link?.Vid != linkVid && relink)
        {
            var counted = Map.FindVid(KnownObject.AirshipCart);
            var limit = Map.FindVid(KnownObject.Slug);
            bool atLimit = Vid.Id == KnownObject.AirshipCart && (counted?.Counts[army] ?? 0) <= (limit?.Counts[army] ?? 0);

            if (!atLimit)
            {
                CreateLink();
                Map.CreateSprite(Map.Vid((int)KnownObject.TowerRepairSmokeGenerator), Position, default);

                return;
            }
        }

        if (Link is not null && Link.Vid == linkVid)
        {
            Map.Action(Link, ActionCode.Repair);
        }
    }
}
