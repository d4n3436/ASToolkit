using System.Numerics;
using ASToolkit.Bot;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class LinkTests
{
    [Fact]
    public void AddSprite_TankAliens_BuildAnArmedTurretThroughTheirLinks()
    {
        var objects = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(objects);

        var yellow = map.CreateSprite(objects.Find(KnownObject.YellowTankWeaponMonster), map.Player.Position + new Vector3(400, 0, 0), default)!;
        var red = map.CreateSprite(objects.Find(KnownObject.RedTankWeaponMonster), map.Player.Position + new Vector3(0, 400, 0), default)!;

        Assert.Equal(KnownObject.Monster4Weapon2, yellow.Link!.Vid.Id);
        Assert.Equal(KnownObject.MonsterMachineGunDummy, yellow.Link.Link!.Vid.Id);
        Assert.Contains(yellow.Link.Link, map.UpdatedSprites);
        Assert.Equal(KnownObject.Monster4Weapon3, red.Link!.Vid.Id);
        var turret = Assert.Single(map.UpdatedSprites, sprite => sprite.Vid.Id == KnownObject.MonsterMachineGunDummy && sprite.Owner == red.Link);
        Assert.Equal(red.Army, turret.Army);
        Assert.NotEqual(0, turret.Vid.Object.WeaponIndex);
    }

    [Fact]
    public void CreateMap_PlacedTeleportPad_BuildsItsEndlessGenerator()
    {
        var objects = TestGameFolder.CreateCatalog();
        var pad = new MergedMapSprite(1, KnownObject.TeleportPad, new Vector3(1000, 1000, 0), 0, 0, []);

        var map = EngineTestMap.CreateMap(objects, sprites: [pad]);

        Assert.Contains(map.ScriptedSprites, sprite => sprite.Vid.Id.IsEndlessGenerator && sprite.Owner is not null);
    }

    [Fact]
    public void SpriteCatalog_PictureWithoutVidHeader_RunsAtTheDefaultFrameSpeed()
    {
        var objects = TestGameFolder.CreateCatalog();

        Assert.Equal(71, objects.Find(KnownObject.PutDynamiteHere)!.GetVisualFrameDuration(SpriteAnimation.Stand));
    }
}
