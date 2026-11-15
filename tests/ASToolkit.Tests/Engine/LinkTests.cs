using System.Numerics;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;
using ASToolkit.Core.Simulation;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class LinkTests
{
    [Fact]
    public void AddMonster_TankAliens_BuildAnArmedTurretThroughTheirLinks()
    {
        var objects = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(objects);

        var yellow = world.AddMonster(objects.Find(KnownObject.YellowTankWeaponMonster)!, world.Player.Position + new Vector3(400, 0, 0));
        var red = world.AddMonster(objects.Find(KnownObject.RedTankWeaponMonster)!, world.Player.Position + new Vector3(0, 400, 0));

        Assert.Equal(KnownObject.Monster4Weapon2, yellow.Link!.Kind.Id);
        Assert.Equal(KnownObject.MonsterMachineGunDummy, yellow.Link.Link!.Kind.Id);
        Assert.Contains(yellow.Link.Link, world.Monsters);
        Assert.Equal(KnownObject.Monster4Weapon3, red.Link!.Kind.Id);
        var turret = Assert.Single(world.Monsters, unit => unit.Kind.Id == KnownObject.MonsterMachineGunDummy && unit.Owner == red.Link);
        Assert.Equal(red.Army, turret.Army);
        Assert.NotEqual(0, turret.Kind.Object.WeaponIndex);
    }

    [Fact]
    public void CreateWorld_PlacedTeleportPad_BuildsItsEndlessGenerator()
    {
        var objects = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var pad = new MergedMapSprite(1, KnownObject.TeleportPad, new Vector3(1000, 1000, 0), 0, 0, []);

        var world = EngineTestWorld.CreateWorld(objects, sprites: [pad]);

        Assert.Contains(world.Scripted, unit => unit.Kind.Id.IsEndlessGenerator && unit.Owner is not null);
    }

    [Fact]
    public void ObjectCatalog_PictureWithoutVidHeader_RunsAtTheDefaultFrameSpeed()
    {
        var objects = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);

        Assert.Equal(71, objects.Find(KnownObject.PutDynamiteHere)!.Layout.GetFrameDuration(SpriteAnimation.Stand));
    }

    [Fact]
    public void GetSample_RecordedTicks_StayWithinHalfAPixelOfTheEngine()
    {
        var objects = new ObjectCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var world = EngineTestWorld.CreateWorld(objects);
        var recorder = new ReplayRecorder();
        recorder.Start(world);
        var enemy = world.AddMonster(objects.Find(KnownObject.Monster1)!, world.Player.Position + new Vector3(300, 120, 0), direction: new Angle(192));
        var truth = new List<(int Time, Vector3 Player, Vector3? Enemy, Vector3? Link)>();
        for (int tick = 0; tick < 300; tick++)
        {
            world.Step(new PlayerCommand(null, PlayerCommand.GetCursor(enemy.Position), enemy.IsAlive, PlayerInventory.Pistols));
            recorder.Capture(world);
            bool listed = world.Monsters.Contains(enemy);
            truth.Add((world.Time, world.Player.Position, listed ? enemy.Position : null, listed && enemy.Link?.Owner == enemy ? enemy.Link.Position : null));
        }

        var replay = recorder.CreateReplay([]);
        int enemyTrack = Enumerable.Range(0, replay.Tracks.Length)
            .First(track => replay.Samples[replay.Tracks[track].FirstSample].Id == KnownObject.Monster1);
        int linkTrack = Enumerable.Range(0, replay.Tracks.Length).First(track => replay.Tracks[track].Owner == enemyTrack);
        foreach ((int time, var player, var enemyAt, var linkAt) in truth)
        {
            Assert.InRange(Vector3.Distance(player, replay.GetSample(0, time).Position), 0, 0.5f);

            if (enemyAt is not null && time < replay.Tracks[enemyTrack].End)
            {
                Assert.InRange(Vector3.Distance(enemyAt.Value, replay.GetSample(enemyTrack, time).Position), 0, 0.5f);
            }

            if (linkAt is not null && time >= replay.Tracks[linkTrack].Start && time < replay.Tracks[linkTrack].End)
            {
                Assert.InRange(Vector3.Distance(linkAt.Value, replay.GetSample(linkTrack, time).Position), 0, 0.5f);
            }
        }
    }
}
