using System.Numerics;
using System.Text;
using ASToolkit.Bot;
using ASToolkit.Engine;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;
using Xunit;

namespace ASToolkit.Tests.Engine;

public class ExecFuncTests
{
    private const string ScriptPath = "test.lgc";

    [Fact]
    public void ExecFunc_UnitQueries_ReadThePlayer()
    {
        var (map, script) = Run("""
            int x, distance, vid, nobody;
            main()
            {
              x = GetX(Flagman(0));
              distance = SizeTo(Flagman(0), GetX(Flagman(0)) + 30, GetY(Flagman(0)) + 40);
              vid = GetUnitVid(Flagman(0));
              nobody = Flagman(1);
            }
            """);
        var player = map.Player.Position;

        Assert.Equal((int)player.X, Read(script, "x"));
        Assert.Equal((int)player.AsVector2().MeasureOctagonalDistance(new Vector2((int)player.X + 30, (int)player.Y + 40)), Read(script, "distance"));
        Assert.Equal((int)map.Player.Vid.Id, Read(script, "vid"));
        Assert.Equal(0, Read(script, "nobody"));
    }

    [Fact]
    public void ExecFunc_PauseThenResume_RewindsTheClockToThePause()
    {
        var (map, script) = Run("""
            int unused;
            main()
            {
              Pause(1);
            }
            """);
        int pausedAt = map.Time;
        map.AdvancePacedClock();
        map.AdvancePacedClock();

        Assert.True(map.IsPaused);
        Assert.True(map.Time > pausedAt);

        var fileSystem = new MemoryWriteOverlay(new GameFolder(TestGameFolder.RequireFolder()));
        fileSystem.Set(ScriptPath, "#include \"maps\\export.lgc\"\nmain()\n{\n  Pause(0);\n}\n"u8.ToArray());
        Assert.True(script.Load(fileSystem, ScriptPath));
        script.CallFunction(map, -1, 0, 0, 0);

        Assert.False(map.IsPaused);
        Assert.Equal(pausedAt, map.Time);
        Assert.Equal(pausedAt, map.Player.LastTickTime);
    }

    [Fact]
    public void ExecFunc_TextBuiltIns_FormatAndConvertAsTheCRuntime()
    {
        var (_, script) = Run("""
            int padded, spaced, lower, upper, unchanged, checksum;
            main()
            {
              padded = strlen(Printf("%05i", 42));
              spaced = charat(Printf("%3i", 7), 1);
              lower = charat(ToLower("AbC"), 2);
              upper = charat(ToUpper("abc"), 0);
              unchanged = charat(ToUpper("1z"), 0);
              checksum = Crc32("abc");
            }
            """);

        Assert.Equal(5, Read(script, "padded"));
        Assert.Equal(' ', Read(script, "spaced"));
        Assert.Equal('c', Read(script, "lower"));
        Assert.Equal('A', Read(script, "upper"));
        Assert.Equal('1', Read(script, "unchanged"));
        Assert.Equal(-899311408, Read(script, "checksum"));
    }

    [Fact]
    public void ExecFunc_TextMathAndTime_FollowTheGame()
    {
        var (map, script) = Run("""
            int length, sine, cosine, time, letter, found, roll;
            main()
            {
              length = strlen("abc" + itoa(12));
              sine = Sin(64);
              cosine = Cos(128);
              time = GetTime();
              letter = charat("AB", 1);
              found = MenuFind(5);
              roll = Random(0);
            }
            """);

        Assert.Equal(5, Read(script, "length"));
        Assert.Equal(1024, Read(script, "sine"));
        Assert.Equal(-1024, Read(script, "cosine"));
        Assert.Equal(map.Time, Read(script, "time"));
        Assert.Equal('B', Read(script, "letter"));
        Assert.Equal(0, Read(script, "found"));
        Assert.Equal(0, Read(script, "roll"));
    }

    [Fact]
    public void ExecFunc_CreateSpriteAndDestroy_AddAndRemoveTheSprite()
    {
        var (map, script) = Run("""
            int monster, marker, vid, nothing, gone;
            main()
            {
              monster = CreateSprite(56, GetX(Flagman(0)) + 300, GetY(Flagman(0)), 0);
              marker = CreateSprite(180, GetX(Flagman(0)), GetY(Flagman(0)) + 300, 0);
              vid = GetUnitVid(monster);
              nothing = CreateSprite(100000, 0, 0, 0);
              Destroy(marker);
              gone = GetUnitVid(marker);
            }
            """);

        var monster = map.FindSprite(Read(script, "monster"));
        Assert.NotNull(monster);
        Assert.Contains(monster, map.Monsters);
        Assert.Equal(56, Read(script, "vid"));
        Assert.Equal(0, Read(script, "nothing"));
        Assert.NotEqual(0, Read(script, "marker"));
        Assert.Null(map.FindSprite(Read(script, "marker")));
        Assert.Equal(0, Read(script, "gone"));
    }

    [Fact]
    public void ExecFunc_DestroyedMonster_LeavesTheMap()
    {
        var (map, script) = Run("""
            int monster;
            main()
            {
              monster = CreateSprite(56, GetX(Flagman(0)) + 300, GetY(Flagman(0)), 0);
              Destroy(monster);
            }
            """);

        Assert.Empty(map.Monsters);
        Assert.Null(map.FindSprite(Read(script, "monster")));
    }

    [Fact]
    public void ExecFunc_VidData_CountsWritesExchangesAndReloads()
    {
        var (map, script) = Run("""
            int first, second, count, armyCount, damage, max, exchanged, reloaded, restored;
            main()
            {
              first = CreateSprite(56, GetX(Flagman(0)) + 300, GetY(Flagman(0)), 0);
              second = CreateSprite(56, GetX(Flagman(0)) + 400, GetY(Flagman(0)), 0);
              Destroy(second);
              count = GetVidData(56, VID_COUNT);
              armyCount = GetVidData(56, VID_COUNT1);
              SetVidData(56, VID_DAMAGE, 77);
              damage = GetVidData(56, VID_DAMAGE);
              SetVidData(56, VID_MAXHP1, GetVidData(56, VID_MAXHP1) * 2);
              max = GetVidData(56, VID_MAXHP1);
              SetVidData(56, VID_EXCHANGEVID, 50);
              exchanged = GetUnitVid(first);
              ReloadVid();
              reloaded = GetUnitVid(first);
              restored = GetVidData(56, VID_DAMAGE);
            }
            """);
        var vid = map.FindVid(KnownObject.RedMonster1)!;
        var monster = map.FindSprite(Read(script, "first"))!;

        Assert.Equal(1, Read(script, "count"));
        Assert.Equal(1, Read(script, "armyCount"));
        Assert.Equal(77, Read(script, "damage"));
        Assert.Equal(vid.MaxHp * 2, Read(script, "max"));
        Assert.Equal(vid.MaxHp * 2, monster.Hp);
        Assert.Equal(50, Read(script, "exchanged"));
        Assert.Equal(56, Read(script, "reloaded"));
        TestGameFolder.Objects.TryFind((KnownObject)56, out var record);
        Assert.NotEqual(77, record.DeathDamage);
        Assert.Equal(record.DeathDamage, Read(script, "restored"));
    }

    [Fact]
    public void ExecFunc_UnitQueries_WalkAndChangeTheMap()
    {
        var (map, script) = Run("""
            int units, direction, width, ground, replaced, removed, free;
            main()
            {
              int u;
              CreateSprite(56, GetX(Flagman(0)) + 300, GetY(Flagman(0)), 0);
              CreateSprite(56, GetX(Flagman(0)) + 400, GetY(Flagman(0)), 0);
              u = FirstUnit(U_UNIT);
              while (u) { units++; u = NextUnit(); }
              direction = DirectionTo(Flagman(0), GetX(Flagman(0)) + 100, GetY(Flagman(0)));
              width = MapSizeX();
              ground = GetGroundZ(GetX(Flagman(0)), GetY(Flagman(0)));
              free = CanPlace(56, GetX(Flagman(0)) + 2000, GetY(Flagman(0)) + 2000, 0);
              ReplaceUnit(56, 50);
              replaced = GetVidData(50, VID_COUNT);
              Genocide(50);
              removed = GetVidData(50, VID_COUNT);
            }
            """);
        var player = map.Player.Position.AsVector2();

        Assert.InRange(Read(script, "units"), 3, map.Collector.Listed.Length + 2);
        Assert.Equal(Angle.East.Value, Read(script, "direction"));
        Assert.Equal((int)map.Size.Width, Read(script, "width"));
        Assert.Equal((int)map.GetGroundZ(new Vector2((int)player.X, (int)player.Y)), Read(script, "ground"));
        Assert.Equal(0, Read(script, "free"));
        Assert.Equal(2, Read(script, "replaced"));
        Assert.Equal(0, Read(script, "removed"));
        Assert.Empty(map.Monsters);
    }

    [Fact]
    public void ExecFunc_SpriteQueriesAndUnitActions_FollowTheGame()
    {
        var (map, script) = Run("""
            int unit, found, near, first, counted, hp, army, item, have, deleted, link, flagman, swapped;
            main()
            {
              unit = CreateSprite(56, GetX(Flagman(0)) + 400, GetY(Flagman(0)), 0);
              found = GetSprite(GETSPRITE_VID + 56, GetX(unit), GetY(unit));
              near = FindNearestSprite(GETSPRITE_VID + 56, GetX(Flagman(0)), GetY(Flagman(0)), 1000);
              first = FirstSprite();
              for (first = NextSprite(); first; first = NextSprite())
                counted++;
              Action(unit, ACT_SET_HP, 0, 50);
              hp = Action(unit, ACT_GET_HP);
              Action(unit, ACT_SET_ARMY, 2);
              army = Action(unit, ACT_GET_ARMY);
              Action(unit, ACT_ADD_ITEM, 241);
              Action(unit, ACT_ADD_ITEM, 242);
              item = Action(unit, ACT_GET_ITEM, 1);
              have = Action(unit, ACT_HAVE_ITEM, 241);
              deleted = Action(unit, ACT_DELETE_ITEM, 241);
              link = Action(unit, ACT_GET_LINK);
              AddCommand(unit, ACT_STOP, 1);
              flagman = Flagman(0);
              SetFlagman(0, unit);
              swapped = Flagman(0);
              SetFlagman(0, flagman);
            }
            """);
        var sprite = map.Monsters.Single();

        Assert.Equal(Read(script, "unit"), Read(script, "found"));
        Assert.Equal(Read(script, "unit"), Read(script, "near"));
        Assert.Equal(Enumerable.Range(0, 17).Sum(layer => map.GetLayer(layer).Count(entry => entry is not null)), Read(script, "counted"));
        Assert.Equal(sprite.Vid.MaxHp / 2, Read(script, "hp"));
        Assert.Equal(2, Read(script, "army"));
        Assert.Equal(242, Read(script, "item"));
        Assert.Equal(1, Read(script, "have"));
        Assert.Equal(1, Read(script, "deleted"));
        Assert.Equal([242], sprite.Items);
        Assert.Equal(sprite.Link is null ? 0 : 1, Read(script, "link") == 0 ? 0 : 1);
        Assert.Equal(ActionCode.Stop, sprite.ActionStack[sprite.ActionStack.Count - 1].Code);
        Assert.Equal(Read(script, "unit"), Read(script, "swapped"));
        Assert.Equal(map.Player, map.Flagman(0));
    }

    [Fact]
    public void ExecFunc_ScreenPickMoneyAndTrains_FollowTheGame()
    {
        var vid = EngineTestMap.CreateMap().FindVid(KnownObject.RedMonster1)!;
        int above = (int)(vid.Half.Y + vid.Size.Z / 2);
        var (map, script) = Run($$"""
            int unit, picked, raised, footprint, below, money, other, train, property, engine;
            main()
            {
              unit = CreateSprite(56, GetX(Flagman(0)) + 400, GetY(Flagman(0)), 0);
              picked = GetSpriteScr(GETSPRITE_VID + 56, GetX(unit), GetY(unit));
              raised = GetSpriteScr(GETSPRITE_VID + 56, GetX(unit), GetY(unit) - GetZ(unit) - {{above}});
              footprint = GetSprite(GETSPRITE_VID + 56, GetX(unit), GetY(unit) - GetZ(unit) - {{above}});
              below = GetSpriteScr(GETSPRITE_VID + 56, GetX(unit), GetY(unit) + 200);
              money = GetMoney(0);
              SetMoney(6, 25);
              other = GetMoney(2);
              train = FirstTrain(1);
              property = TrainProperty(unit, 1);
              engine = Action(unit, ACT_FIRST_ENGINE);
            }
            """);

        Assert.NotEqual(0, Read(script, "unit"));
        Assert.Equal(Read(script, "unit"), Read(script, "picked"));
        Assert.Equal(Read(script, "unit"), Read(script, "raised"));
        Assert.Equal(0, Read(script, "footprint"));
        Assert.Equal(0, Read(script, "below"));
        Assert.Equal(1000, Read(script, "money"));
        Assert.Equal(25, Read(script, "other"));
        Assert.Equal(0, Read(script, "train"));
        Assert.Equal(0, Read(script, "property"));
        Assert.Equal(0, Read(script, "engine"));
        Assert.Single(map.Monsters);
    }

    [Fact]
    public void ExecFunc_MenuFiles_LoadAtTheScreenAndReleaseByFile()
    {
        var (map, script) = Run("""
            int x, y, released, kept;
            main()
            {
              MenuLoad("maps\\play.men");
              MenuLoad("maps\\gamemenu.men");
              x = GetX(MenuFind(708));
              y = GetY(MenuFind(708));
              MenuRelease("maps\\play.men");
              released = MenuFind(708);
              MenuRelease("maps\\gamemenu.men");
              kept = MenuFind(717);
            }
            """);

        Assert.Equal((int)(320 + 192 - 512 + map.Session.Graphics.ScreenSize.Width / 2), Read(script, "x"));
        Assert.Equal((int)(254 + 144 - 384 + map.Session.Graphics.ScreenSize.Height / 2), Read(script, "y"));
        Assert.Equal(0, Read(script, "released"));
        Assert.NotEqual(0, Read(script, "kept"));
    }

    [Fact]
    public void ExecFunc_MenuFiles_RestoreTextFromStrings()
    {
        var (map, _) = Run("""
            main()
            {
              MenuLoad("maps\\gamemenu.men");
            }
            """);
        var texts = Enumerable.Range(0, 17).SelectMany(layer => map.GetLayer(layer))
            .Select(entry => (entry as Text)?.Shown.ToArray()).Where(text => text is not null)
            .Select(text => Encoding.ASCII.GetString(text!)).ToList();

        Assert.Contains("BACK", texts);
        Assert.Contains("MAIN MENU", texts);
    }

    [Fact]
    public void ExecFunc_VidData_WeaponValuesReachEveryObjectIdSharingTheWeapon()
    {
        (int first, int second) = FindWeaponPair(EngineTestMap.CreateMap());
        var (map, script) = Run($$"""
            int detect, aim, build, ammo, damage;
            main()
            {
              SetVidData({{first}}, VID_DETECTRANGE, 123);
              SetVidData({{first}}, VID_WEAPONAIM, 7);
              SetVidData({{first}}, VID_BUILDTIME, 4321);
              SetVidData({{first}}, VID_AMMO, 55);
              SetVidData({{first}}, VID_DAMAGE, 999);
              detect = GetVidData({{second}}, VID_DETECTRANGE);
              aim = GetVidData({{second}}, VID_WEAPONAIM);
              build = GetVidData({{second}}, VID_BUILDTIME);
              ammo = GetVidData({{second}}, VID_AMMO);
              damage = GetVidData({{second}}, VID_DAMAGE);
            }
            """);

        Assert.Equal(123, Read(script, "detect"));
        Assert.Equal(7, Read(script, "aim"));
        Assert.Equal(4321, Read(script, "build"));
        Assert.Equal(55, Read(script, "ammo"));
        Assert.Equal(map.FindVid((KnownObject)second)!.Damage, Read(script, "damage"));
        Assert.NotEqual(999, Read(script, "damage"));
    }

    [Fact]
    public void ExecFunc_SaveDemo_RecordsTheNextLoadIntoTheNamedFile()
    {
        var (map, _) = Run("""
            main()
            {
              SaveDemo("maps\\saved.dem");
            }
            """);
        map.LoadAt(EngineTestMap.MapName, EngineTestMap.StartRealTime);
        var demo = new DemoResource(map.FinishDemoRecording());

        Assert.True(map.FileSystem.TryRead("maps\\saved.dem", out var file));
        Assert.Equal(demo.Data.ToArray(), file.ToArray());
        Assert.Equal(EngineTestMap.MapName.ToArray(), demo.MapName.ToArray());
    }

    private static (int First, int Second) FindWeaponPair(Map map)
    {
        var vids = map.Vids;
        for (int i = 0; i < vids.Length; i++)
        {
            var first = vids[i];
            if (first is null || first.WeaponIndex == 0 || first.LinkVid is not null)
                continue;

            for (int j = i + 1; j < vids.Length; j++)
            {
                var second = vids[j];
                if (second is not null && second.WeaponIndex == first.WeaponIndex && second.LinkVid is null)
                    return (i, j);
            }
        }

        throw new InvalidOperationException("There is no pair of object IDs sharing a weapon.");
    }

    private static (Map Map, Script Script) Run(string text)
    {
        var map = EngineTestMap.CreateMap();
        var script = Compile(map, text);
        script.CallFunction(map, -1, 0, 0, 0);

        return (map, script);
    }

    private static Script Compile(Map map, string text)
    {
        var fileSystem = new MemoryWriteOverlay(new GameFolder(TestGameFolder.RequireFolder()));
        fileSystem.Set(ScriptPath, Encoding.ASCII.GetBytes("#include \"maps\\export.lgc\"\n" + text));
        Assert.True(map.Script.Load(fileSystem, ScriptPath));

        return map.Script;
    }

    private static int Read(Script script, string name) => script.GetSlot(script.Objects[script.Objects.Location(Encoding.ASCII.GetBytes(name))].Address).GetValue();
}
