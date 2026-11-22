using ASToolkit.Core.Bot;
using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class LinkTests
{
    [Fact]
    public void AddMonster_TankAliens_BuildAnArmedTurretThroughTheirLinks()
    {
        var objects = TestGameFolder.CreateCatalog();
        var map = EngineTestMap.CreateMap(objects);

        var yellow = map.AddMonster(objects.Find(KnownObject.YellowTankWeaponMonster)!, map.Player.Position + new Vector3(400, 0, 0));
        var red = map.AddMonster(objects.Find(KnownObject.RedTankWeaponMonster)!, map.Player.Position + new Vector3(0, 400, 0));

        Assert.Equal(KnownObject.Monster4Weapon2, yellow.Link!.Definition.Id);
        Assert.Equal(KnownObject.MonsterMachineGunDummy, yellow.Link.Link!.Definition.Id);
        Assert.Contains(yellow.Link.Link, map.UpdatedSprites);
        Assert.Equal(KnownObject.Monster4Weapon3, red.Link!.Definition.Id);
        var turret = Assert.Single(map.UpdatedSprites, sprite => sprite.Definition.Id == KnownObject.MonsterMachineGunDummy && sprite.Owner == red.Link);
        Assert.Equal(red.Army, turret.Army);
        Assert.NotEqual(0, turret.Definition.Object.WeaponIndex);
    }

    [Fact]
    public void CreateMap_PlacedTeleportPad_BuildsItsEndlessGenerator()
    {
        var objects = TestGameFolder.CreateCatalog();
        var pad = new MergedMapSprite(1, KnownObject.TeleportPad, new Vector3(1000, 1000, 0), 0, 0, []);

        var map = EngineTestMap.CreateMap(objects, sprites: [pad]);

        Assert.Contains(map.ScriptedSprites, sprite => sprite.Definition.Id.IsEndlessGenerator && sprite.Owner is not null);
    }

    [Fact]
    public void SpriteCatalog_PictureWithoutVidHeader_RunsAtTheDefaultFrameSpeed()
    {
        var objects = TestGameFolder.CreateCatalog();

        Assert.Equal(71, objects.Find(KnownObject.PutDynamiteHere)!.Layout.GetFrameDuration(SpriteAnimation.Stand));
    }
}
