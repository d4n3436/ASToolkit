using System.Numerics;
using System.Text;
using ASToolkit.Core.Engine;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;
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
        Assert.Equal((int)map.Player.Definition.Id, Read(script, "vid"));
        Assert.Equal(0, Read(script, "nobody"));
    }

    [Fact]
    public void ExecFunc_TextBuiltIns_FormatAndConvertAsTheCRuntime()
    {
        var (_, script) = Run("""
            int padded, hex, lower, upper, unchanged, checksum;
            main()
            {
              padded = strlen(Printf("%05i", 42));
              hex = charat(Printf("%X", 255), 1);
              lower = charat(ToLower("AbC"), 2);
              upper = charat(ToUpper("abc"), 0);
              unchanged = charat(ToUpper("1z"), 0);
              checksum = Crc32("abc");
            }
            """);

        Assert.Equal(5, Read(script, "padded"));
        Assert.Equal('F', Read(script, "hex"));
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
        Assert.Equal(0, Read(script, "marker"));
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
        var definition = map.Definitions.Find(KnownObject.RedMonster1)!;
        var monster = map.FindSprite(Read(script, "first"))!;

        Assert.Equal(1, Read(script, "count"));
        Assert.Equal(1, Read(script, "armyCount"));
        Assert.Equal(77, Read(script, "damage"));
        Assert.Equal(definition.MaxHp * 2, Read(script, "max"));
        Assert.Equal(definition.MaxHp * 2, monster.Hp);
        Assert.Equal(50, Read(script, "exchanged"));
        Assert.Equal(56, Read(script, "reloaded"));
        Assert.Equal(definition.Object.DeathDamage, Read(script, "restored"));
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
        Assert.Equal((int)map.Movement.MapSize.Width, Read(script, "width"));
        Assert.Equal((int)map.Movement.GetHeight(new Vector2((int)player.X, (int)player.Y)), Read(script, "ground"));
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
        Assert.Equal(Enumerable.Range(0, 17).Sum(layer => map.GetLayer(layer).Count(entry => entry.Sprite is not null)), Read(script, "counted"));
        Assert.Equal(sprite.Definition.MaxHp / 2, Read(script, "hp"));
        Assert.Equal(2, Read(script, "army"));
        Assert.Equal(242, Read(script, "item"));
        Assert.Equal(1, Read(script, "have"));
        Assert.Equal(1, Read(script, "deleted"));
        Assert.Equal([242], sprite.Items);
        Assert.Equal(sprite.Link is null ? 0 : 1, Read(script, "link") == 0 ? 0 : 1);
        Assert.Equal(ActionCode.Stop, sprite.ActionStack.Top.Code);
        Assert.Equal(Read(script, "unit"), Read(script, "swapped"));
        Assert.Equal(map.Player, map.GetFlagman(0));
    }

    [Fact]
    public void ExecFunc_ScreenPickMoneyAndTrains_FollowTheGame()
    {
        var definition = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects).Find(KnownObject.RedMonster1)!;
        int above = (int)(definition.Half.Y + definition.Object.Size.Z / 2);
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

    private static (Map Map, Script Script) Run(string text)
    {
        var definitions = new SpriteCatalog(TestGameFolder.RequireFolder(), TestGameFolder.Objects);
        var map = EngineTestMap.CreateMap(definitions);
        var script = Compile(map, text);
        script.CallFunction(map, -1, 0, 0, 0);

        return (map, script);
    }

    private static Script Compile(Map map, string text)
    {
        var script = map.Script;
        var fileSystem = new ScriptOverlay(new GameFolder(TestGameFolder.RequireFolder()), ScriptPath, "#include \"maps\\export.lgc\"\n" + text);
        Assert.True(script.Load(fileSystem, ScriptPath));

        return script;
    }

    private static int Read(Script script, string name) => script.GetSlot(script.Objects[script.FindObject(Encoding.ASCII.GetBytes(name))].Address).GetValue();
}
