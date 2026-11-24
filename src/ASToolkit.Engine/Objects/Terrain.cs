using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents a terrain or object sprite.
/// </summary>
public class Terrain : Sprite
{
    private const KnownObject RelinkCountedId = (KnownObject)35;
    private const KnownObject RelinkEffectId = (KnownObject)590;
    private const KnownObject RelinkLimitId = KnownObject.Slug;

    /// <summary>
    /// Initializes a new instance of the <see cref="Terrain"/> class.
    /// </summary>
    /// <param name="number">The number of the sprite.</param>
    /// <param name="vid">The shared data and frames of the sprite's object ID.</param>
    /// <param name="position">The position of the sprite.</param>
    /// <param name="army">The army of the sprite.</param>
    /// <param name="hitPoints">The hit points of the sprite.</param>
    /// <param name="map">The map the sprite belongs to.</param>
    /// <param name="direction">The direction of the sprite.</param>
    public Terrain(int number, Vid vid, Vector3 position, Army army, int hitPoints, Map map, Angle direction = default)
        : base(number, vid, position, army, hitPoints, map, direction)
    {
    }

    /// <summary>
    /// Gets or sets a value indicating whether the terrain refills its ammunition.
    /// </summary>
    public bool ShouldRefillAmmo { get; set; }

    [EngineRoutine(0x0044bea0)]
    internal override int Action(int act, int var1, int var2, int var3, Sprite? target)
    {
        if ((ActionCode)act != ActionCode.Repair)
            return base.Action(act, var1, var2, var3, target);

        Repair(true);

        return 0;
    }

    [EngineRoutine(0x0044bea0)]
    internal override int Action(int act, ref Resource stream, int version)
    {
        if ((ActionCode)act != ActionCode.RestoreOld)
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
        Map.ChangeHp(this, Vid.ArmyMaxHp[army]);
        var linkVid = Vid.LinkVid;

        if (linkVid is not null && Link?.Vid != linkVid && relink)
        {
            var counted = Map.Vids.Find(RelinkCountedId);
            var limit = Map.Vids.Find(RelinkLimitId);
            bool atLimit = Vid.Id == RelinkCountedId && (counted?.Counts[army] ?? 0) <= (limit?.Counts[army] ?? 0);

            if (!atLimit)
            {
                Map.CreateLink(this);
                Map.AddSprite(Map.Vid((int)RelinkEffectId), Position, default);

                return;
            }
        }

        if (Link is not null && Link.Vid == linkVid)
        {
            Map.Action(Link, ActionCode.Repair);
        }
    }
}
