using System.Numerics;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class Map : IScriptFunctionHost
{
    private const int AllArmies = 0xff;
    private const int EffectCount = 16;
    private const int FlashDuration = 0x900;
    private const int ScrollDuration = 0x200;
    private const int FadeInDuration = 0x500;
    private const int EffectDuration = 0x400;
    private const int NoUnitDistance = 60000;
    private const float TrigonometryScale = 1024;
    private const int LastAnimationAction = 0x10;
    private const float GetSpriteReach = 300;
    private const float ScreenSpriteReach = 300;
    private const int TypeShift = 20;
    private const OldSpriteType SearchedTypes = OldSpriteType.Terrain | OldSpriteType.Object | OldSpriteType.Unit | OldSpriteType.Avia |
        OldSpriteType.Menu | OldSpriteType.Railway | OldSpriteType.Region | OldSpriteType.Cannon | OldSpriteType.Sprite;
    private const OldSpriteType GridListTypes = OldSpriteType.Unit | OldSpriteType.Avia;
    private readonly uint[] _effectStarts = new uint[EffectCount];
    private readonly int[] _effectDurations = new int[EffectCount];
    private readonly int[] _effectFirst = new int[EffectCount];
    private readonly int[] _effectSecond = new int[EffectCount];
    private readonly Dictionary<int, Sprite> _handles = [];
    private readonly Dictionary<MergedMapSprite, SpriteDefinition> _countedPlacements = [];
    private int _lastHandle;
    private int _unitIndex;
    private OldSpriteType _unitMask;
    private int _spriteLayer;
    private int _spriteIndex;
    private int _scrollType;

    /// <summary>
    /// Returns the handle the level script refers to a sprite by, allocating one when needed.
    /// </summary>
    /// <param name="sprite">The sprite, or null.</param>
    /// <returns>The handle, or 0 for null.</returns>
    public int GetSpriteHandle(Sprite? sprite)
    {
        if (sprite is null)
            return 0;

        if (sprite.Handle == 0)
        {
            sprite.Handle = ++_lastHandle;
            _handles[sprite.Handle] = sprite;
        }

        return sprite.Handle;
    }

    /// <summary>
    /// Returns the sprite a script handle refers to.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <returns>The sprite, or null when the handle is stale.</returns>
    public Sprite? FindSprite(int handle) => _handles.TryGetValue(handle, out var sprite) && sprite.Handle == handle ? sprite : null;

    private static void CountSprite(Sprite sprite)
    {
        if (sprite.IsCounted)
            return;

        sprite.IsCounted = true;
        sprite.Definition.Counts[(int)sprite.Army & 3]++;
    }

    private void UnlinkDestroyed(Sprite sprite)
    {
        var owner = sprite.Owner;

        if (owner is not null && owner.Link == sprite)
        {
            owner.Link = null;
        }

        foreach (var link in sprite.EnumerateLinks())
        {
            if (link.Owner != sprite)
                continue;
            
            DestroySprite(link);

            if (sprite.Link == link)
            {
                sprite.Link = null;
            }
        }
    }

    /// <summary>
    /// Runs a sprite's destroy handler and frees its count and script handle.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    public void ReleaseSprite(Sprite sprite)
    {
        RunDestroyHandler(sprite);

        if (sprite.IsCounted)
        {
            sprite.IsCounted = false;
            int army = (int)sprite.Army & 3;
            if (sprite.Definition.Counts[army] != 0)
            {
                sprite.Definition.Counts[army]--;
            }
        }

        if (sprite.Handle == 0)
            return;

        Script.ClearSpriteHandle(sprite.Handle);
        _handles.Remove(sprite.Handle);
        sprite.Handle = 0;
    }

    private void ReleasePlacement(MergedMapSprite placement)
    {
        if (!_countedPlacements.Remove(placement, out var definition))
            return;

        int army = (int)placement.Army & 3;
        if (definition.Counts[army] != 0)
        {
            definition.Counts[army]--;
        }
    }

    /// <summary>
    /// Gets the level script.
    /// </summary>
    public Script Script { get; } = new();

    /// <summary>
    /// Gets the registry the level script reads and writes.
    /// </summary>
    public ScriptRegistry Registry { get; }

    /// <summary>
    /// Gets the map state that outlives the level.
    /// </summary>
    public GameSession Session { get; }

    private int GetTime() => Time + FrameTime;

    private static string ReadText(ReadOnlyMemory<byte> text) => GameTextEncoding.Cyrillic.GetString(text.Span);

    private Sprite? PopSprite(Script script) => FindSprite(script.PopNumber());

    private void PushSprite(Script script, Sprite? sprite) => script.PushSprite(GetSpriteHandle(sprite));

    /// <summary>
    /// Creates a sprite of a definition at a point.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite the new one takes its army and height from, and links to when it is a linker.</param>
    /// <returns>The sprite, or null when the definition's limit is reached.</returns>
    [EngineRoutine(0x00405bd0)]
    public Sprite? CreateSprite(SpriteDefinition definition, Vector3 position, Angle direction, Sprite? parent = null)
    {
        if (definition.MaxUnits >= 0 && definition.MaxUnits <= Sum(definition.Counts))
            return null;

        if (definition.Object.Properties.HasFlag(ObjectProperties.ZeroZ))
        {
            float ground = Movement.GetSupportHeight(definition.Object, position.AsVector2());
            position.Z = parent is null ? ground : position.Z - parent.Position.Z + ground;
        }

        var placement = new MergedMapSprite(0, definition.Id, position, direction, parent?.Army ?? Definitions.GetArmy(definition), []);
        _sprites.Add(placement);
        Definitions.AddTerrain(Movement, placement);
        var sprite = PlaceSprite(placement, definition);

        if (sprite is null && definition.MaxHp == 0 && definition.Object.SpriteClass != SpriteClass.Cannon)
        {
            ReleasePlacement(placement);
            sprite = new Sprite(-1, definition, position, placement.Army, 0) { Placement = placement, Direction = direction };
            _statics[placement] = sprite;
            CountSprite(sprite);
        }

        if (sprite is not null && definition.Object.SpriteClass == SpriteClass.Linker)
        {
            SpawnedObjectSystem.ConstructLinker(sprite, parent, position, direction);
        }

        if (sprite is not null && sprite.Role != UpdateRole.Monster)
        {
            RunCreationHook(sprite);
        }

        return sprite;
    }


    /// <summary>
    /// Destroys a sprite with the objects linked to it.
    /// </summary>
    /// <param name="sprite">The sprite.</param>
    [EngineRoutine(0x0043f520)]
    public void DestroySprite(Sprite sprite)
    {
        if (sprite.Role is not (UpdateRole.Player or UpdateRole.Torso))
        {
            UnlinkDestroyed(sprite);
        }

        switch (sprite.Role)
        {
            case UpdateRole.Monster:
                _updates.Remove(sprite.Definition.Layer, sprite);
                RemoveMonster(sprite);
                Collector.DeleteFromHash(sprite);
                Delete(sprite);
                ReleaseSprite(sprite);
                break;
            case UpdateRole.Scripted:
                _updates.Remove(sprite.Definition.Layer, sprite);
                RemoveScripted(sprite);
                break;
            case UpdateRole.Effect:
                _updates.Remove(sprite.Definition.Layer, sprite);
                Effects.Leave(sprite);
                break;
            case UpdateRole.Player:
                DestroyPlayer();
                break;
            case UpdateRole.Torso:
                throw new NotSupportedException("Cannot destroy the player's torso alone.");
            default:
                if (sprite.Placement is not null)
                {
                    Destroy(sprite.Placement);
                }
                else
                {
                    Collector.DeleteFromHash(sprite);
                    Effects.Detach(sprite);
                }

                ReleaseSprite(sprite);
                break;
        }
    }

    /// <summary>
    /// Runs a built-in function of the script language.
    /// </summary>
    /// <param name="script">The script whose stack holds the arguments.</param>
    /// <param name="function">The built-in's number.</param>
    [EngineRoutine(0x00435690)]
    public void ExecFunc(Script script, int function)
    {
        switch ((ScriptFunction)function)
        {
            case ScriptFunction.CreateSprite:
            {
                var parent = PopSprite(script);
                var direction = new Angle((byte)script.PopNumber());
                int z = script.PopNumber();
                int y = script.PopNumber();
                int x = script.PopNumber();
                var definition = Definitions.Find((KnownObject)script.PopNumber());
                if (definition is null)
                {
                    script.PushNumber(0);
                    break;
                }

                PushSprite(script, CreateSprite(definition, new Vector3(x, y, z), direction, parent));
                break;
            }
            case ScriptFunction.Destroy:
            {
                var sprite = PopSprite(script);
                if (sprite is not null)
                {
                    DestroySprite(sprite);
                }

                break;
            }
            case ScriptFunction.Flagman:
            {
                PushSprite(script, GetFlagman(script.PopNumber()));
                break;
            }
            case ScriptFunction.FirstUnit:
            {
                _unitMask = (OldSpriteType)script.PopNumber();
                var sprites = Collector.Listed;
                _unitIndex = sprites.Length - 1;
                PushSprite(script, sprites.IsEmpty ? null : FindUnitOfMask(sprites[_unitIndex]));
                break;
            }
            case ScriptFunction.NextUnit:
                PushSprite(script, FindUnitOfMask(GetNextUnit()));
                break;
            case ScriptFunction.FirstInBox:
            {
                int bottom = script.PopNumber();
                int right = script.PopNumber();
                int top = script.PopNumber();
                int left = script.PopNumber();
                Collector.FirstHashInBox(new Vector2(left, top), new Vector2(right, bottom));
                break;
            }
            case ScriptFunction.NextInBox:
                PushSprite(script, Collector.NextHashInBox());
                break;
            case ScriptFunction.DirectionTo:
            {
                int y = script.PopNumber();
                int x = script.PopNumber();
                var sprite = PopSprite(script);
                script.PushNumber(sprite is null ? 0 : Angle.FromOffset(new Vector2(x, y) - sprite.Position.AsVector2()).Value);
                break;
            }
            case ScriptFunction.ChangeZUnit:
            {
                int z = script.PopNumber();
                var definition = Definitions.Find((KnownObject)script.PopNumber());
                if (definition is not null)
                {
                    foreach (var sprite in GetSpritesOfDefinition(definition))
                    {
                        sprite.ChangeCoor(sprite.Position with { Z = z });
                    }
                }

                break;
            }
            case ScriptFunction.GetGroundZ:
            {
                int y = script.PopNumber();
                int x = script.PopNumber();
                script.PushNumber((int)Movement.GetHeight(new Vector2(x, y)));
                break;
            }
            case ScriptFunction.CanPlace:
            {
                int z = script.PopNumber();
                int y = script.PopNumber();
                int x = script.PopNumber();
                var definition = Definitions.Find((KnownObject)script.PopNumber());
                PushSprite(script, definition is null ? null : FindPlacementBlocker(definition, new Vector3(x, y, z)));
                break;
            }
            case ScriptFunction.MapSizeX:
                script.PushNumber((int)Movement.MapSize.Width);
                break;
            case ScriptFunction.MapSizeY:
                script.PushNumber((int)Movement.MapSize.Height);
                break;
            case ScriptFunction.Genocide:
            {
                var definition = Definitions.Find((KnownObject)script.PopNumber());
                if (definition is not null)
                {
                    foreach (var sprite in GetSpritesOfDefinition(definition))
                    {
                        DestroySprite(sprite);
                    }
                }

                break;
            }
            case ScriptFunction.ReplaceUnit:
            {
                var replacement = Definitions.Find((KnownObject)script.PopNumber());
                var definition = Definitions.Find((KnownObject)script.PopNumber());
                if (definition is not null && replacement is not null)
                {
                    foreach (var sprite in GetSpritesOfDefinition(definition))
                    {
                        CreateSprite(replacement, sprite.Position, sprite.Direction);
                        DestroySprite(sprite);
                    }
                }

                break;
            }
            case ScriptFunction.Action:
                ExecAction(script);
                break;
            case ScriptFunction.SizeTo:
            {
                int y = script.PopNumber();
                int x = script.PopNumber();
                var sprite = PopSprite(script);
                script.PushNumber(sprite is null ? NoUnitDistance : (int)sprite.Position.AsVector2().MeasureOctagonalDistance(new Vector2(x, y)));
                break;
            }
            case ScriptFunction.GetUnitVid:
                script.PushNumber((int)(PopSprite(script)?.Definition.Id ?? 0));
                break;
            case ScriptFunction.GetX:
                script.PushNumber((int)(PopSprite(script)?.Position.X ?? 0));
                break;
            case ScriptFunction.GetY:
                script.PushNumber((int)(PopSprite(script)?.Position.Y ?? 0));
                break;
            case ScriptFunction.GetZ:
                script.PushNumber((int)(PopSprite(script)?.Position.Z ?? 0));
                break;
            case ScriptFunction.GetDirection:
                script.PushNumber(PopSprite(script)?.Direction.Value ?? 0);
                break;
            case ScriptFunction.GetAnimation:
                script.PushNumber((int)(PopSprite(script)?.Animation ?? 0));
                break;
            case ScriptFunction.Load:
                MapToLoad = script.PopText().ToArray();
                break;
            case ScriptFunction.GetInputX:
                script.PushNumber((int)Input.Cursor.X);
                break;
            case ScriptFunction.GetInputY:
                script.PushNumber((int)Input.Cursor.Y);
                break;
            case ScriptFunction.GetKey:
                script.PushNumber(Input.Key);
                break;
            case ScriptFunction.GetInputState:
                script.PushNumber((int)GetInputState());
                break;
            case ScriptFunction.SetScrollType:
                _scrollType = script.PopNumber();
                break;
            case ScriptFunction.GetScrollType:
                script.PushNumber(_scrollType);
                break;
            case ScriptFunction.SetPlayerControl:
                SetPlayerControl(script.PopNumber() != 0);
                break;
            case ScriptFunction.Exit:
                script.PopText();
                IsQuitting = true;
                break;
            case ScriptFunction.Effect:
                ExecEffect(script);
                break;
            case ScriptFunction.SetEnvironment:
                SetEnvironment((MapEnvironment)script.PopNumber());
                break;
            case ScriptFunction.GetEffectState:
                script.PushNumber(GetEffectState(script.PopNumber()));
                break;
            case ScriptFunction.Charat:
            {
                int index = script.PopNumber();
                var text = script.PopText().Span;
                // Unused on AS1
                script.PushNumber(index == text.Length ? 0 : (sbyte)text[index]);
                break;
            }
            case ScriptFunction.Random:
            {
                int max = script.PopNumber();
                script.PushNumber(_rng.Next() % (max + 1));
                break;
            }
            case ScriptFunction.GetTime:
                script.PushNumber(GetTime());
                break;
            case ScriptFunction.GetVidData:
                GetVidData(script);
                break;
            case ScriptFunction.SetVidData:
                SetVidData(script);
                break;
            case ScriptFunction.ReloadVid:
                Definitions.ReloadRecords();
                break;
            case ScriptFunction.OldStrlen:
            case ScriptFunction.Strlen:
                script.PushNumber(script.PopText().Length);
                break;
            case ScriptFunction.Itoa:
                script.PushText(StackObject.FormatNumber(script.PopNumber()));
                break;
            case ScriptFunction.Sin:
                script.PushNumber((int)(new Angle((byte)script.PopNumber()).Sine * TrigonometryScale));
                break;
            case ScriptFunction.Cos:
                script.PushNumber((int)(new Angle((byte)script.PopNumber()).Cosine * TrigonometryScale));
                break;
            case ScriptFunction.GetScreenInputX:
                script.PushNumber((int)Input.ScreenCursor.X);
                break;
            case ScriptFunction.GetScreenInputY:
                script.PushNumber((int)Input.ScreenCursor.Y);
                break;
            case ScriptFunction.SetAutoReBirth:
                script.Pop();
                break;
            case ScriptFunction.CanMoveEngineTo:
                PopValues(script, 3);
                break;
            case ScriptFunction.CanAttackEngine:
                PopValues(script, 2);
                break;

            // TODO: Port the menus
            case ScriptFunction.MenuFind:
                PopValues(script, 2);
                PushSprite(script, null);
                break;
            case ScriptFunction.MenuLoad:
            case ScriptFunction.MenuRelease:
                script.Pop();
                break;
            case ScriptFunction.MenuNVidUnderCursor:
            case ScriptFunction.MenuNDirUnderCursor:
                script.PushNumber(0);
                break;
            case ScriptFunction.MenuAction:
                PopValues(script, 6);
                break;
            case ScriptFunction.MenuCreate:
                PopValues(script, 4);
                if (Definitions.Find((KnownObject)script.PopNumber()) is not null)
                {
                    PushSprite(script, null);
                }

                break;
            case ScriptFunction.MenuLClick:
            case ScriptFunction.MenuRClick:
                PushSprite(script, null);
                break;

            // TODO: Port the HUD, cursor and camera
            case ScriptFunction.SetCursor:
            case ScriptFunction.SetStateBar:
            case ScriptFunction.SetGraphDetail:
                script.Pop();
                break;
            case ScriptFunction.MessageText:
                PopValues(script, 3);
                break;
            case ScriptFunction.SetPosition:
            case ScriptFunction.SetMouseClick:
            case ScriptFunction.SetCursorPosition:
            case ScriptFunction.SetWind:
                PopValues(script, 2);
                break;
            case ScriptFunction.ScreenX:
            case ScriptFunction.ScreenY:
                script.PushNumber(0);
                break;
            case ScriptFunction.ToScreenX:
                break;
            case ScriptFunction.ToScreenY:
                script.Pop();
                break;

            // TODO: Port sound, music and movies
            case ScriptFunction.SetSoundVolume:
            case ScriptFunction.SetMusicVolume:
            case ScriptFunction.PlaySfx:
            case ScriptFunction.StopSfx:
            case ScriptFunction.PlayMovie:
                script.Pop();
                break;
            case ScriptFunction.StopMusic:
            case ScriptFunction.StopMovie:
                break;
            case ScriptFunction.PlaySfxFromCoor:
                PopValues(script, 3);
                break;
            case ScriptFunction.PlayMusicFile:
                PopValues(script, 2);
                break;
            case ScriptFunction.IsPlayMovie:
            case ScriptFunction.IsPlayMusic:
                script.PushNumber(0);
                break;

            // TODO: Port the gamma
            case ScriptFunction.SetGamma:
                script.Pop();
                break;
            case ScriptFunction.CountGamma:
                PopValues(script, 3);
                script.PushNumber(0);
                break;
            case ScriptFunction.GetGamma:
                script.PushNumber(0);
                break;

            // TODO: Port saves, pausing, commands, map names, files and the system
            case ScriptFunction.GetCommands:
            case ScriptFunction.FRead:
                script.Pop();
                script.PushText(ReadOnlyMemory<byte>.Empty);
                break;
            case ScriptFunction.SetCommands:
            case ScriptFunction.FWrite:
                PopValues(script, 2);
                break;
            case ScriptFunction.SaveDemo:
                // Unused on AS1
                if (!IsPlayingDemo)
                {
                    script.Pop();
                }

                break;
            case ScriptFunction.GetString:
            case ScriptFunction.Printf:
            case ScriptFunction.Save:
            case ScriptFunction.Pause:
            case ScriptFunction.Exec:
            case ScriptFunction.Log:
            case ScriptFunction.FClose:
                script.Pop();
                break;
            case ScriptFunction.GetPreviousMapName:
            case ScriptFunction.GetAsProtectUserName:
            case ScriptFunction.GetMapName:
                script.PushText(ReadOnlyMemory<byte>.Empty);
                break;
            case ScriptFunction.Crc32:
            case ScriptFunction.FOpen:
            case ScriptFunction.FCreate:
                script.Pop();
                script.PushNumber(0);
                break;
            case ScriptFunction.FEof:
                script.Pop();
                script.PushNumber(1);
                break;
            case ScriptFunction.GetReg:
            {
                var fallback = script.PopText();
                string name = ReadText(script.PopText());
                string? value = Registry.Get(ReadText(script.PopText()), name);
                script.PushText(value is null ? fallback : GameTextEncoding.Cyrillic.GetBytes(value));
                break;
            }
            case ScriptFunction.SetReg:
            {
                string value = ReadText(script.PopText());
                string name = ReadText(script.PopText());
                Registry.Set(ReadText(script.PopText()), name, value);
                break;
            }
            case ScriptFunction.DelReg:
            {
                string name = ReadText(script.PopText());
                Registry.Delete(ReadText(script.PopText()), name);
                break;
            }
            case ScriptFunction.GetDefaultRegPath:
                script.PushText(GameTextEncoding.Cyrillic.GetBytes(Registry.DefaultPath));
                break;
            case ScriptFunction.ToLower:
            case ScriptFunction.ToUpper:
                script.PushText(script.PopText());
                break;
            case ScriptFunction.ToBase64:
                PopValues(script, 2);
                script.PushText(ReadOnlyMemory<byte>.Empty);
                break;

            case ScriptFunction.GetSprite:
            {
                var previous = PopSprite(script);
                int y = script.PopNumber();
                int x = script.PopNumber();
                PushSprite(script, GetSprite((SpriteSearch)script.PopNumber(), x, y, previous));
                break;
            }
            case ScriptFunction.GetSpriteScr:
            {
                // Unused on AS1
                int y = script.PopNumber();
                int x = script.PopNumber();
                PushSprite(script, GetSpriteScr((SpriteSearch)script.PopNumber(), x, y));
                break;
            }
            case ScriptFunction.FindNearestSprite:
            {
                var previous = PopSprite(script);
                int radius = script.PopNumber();
                int y = script.PopNumber();
                int x = script.PopNumber();
                PushSprite(script, FindNearestSprite((SpriteSearch)script.PopNumber(), x, y, radius, previous));
                break;
            }
            case ScriptFunction.FirstSprite:
                _spriteLayer = 0;
                _spriteIndex = _updates.GetLayer(0).Count;
                PushSprite(script, FindListedSprite());
                break;
            case ScriptFunction.NextSprite:
                PushSprite(script, GetNextSprite());
                break;
            case ScriptFunction.AddCommand:
            {
                int third = script.PopNumber();
                int second = script.PopNumber();
                int first = script.PopNumber();
                int act = script.PopNumber();
                var sprite = PopSprite(script);

                if (sprite is not null)
                {
                    AddCommand(sprite, act, first, second, third);
                }

                break;
            }
            case ScriptFunction.SetFlagman:
            {
                var sprite = PopSprite(script);
                SetFlagman(script.PopNumber(), sprite);
                break;
            }
            case ScriptFunction.AddUnitLimit:
            {
                int value = script.PopNumber();
                var definition = Definitions.Find((KnownObject)script.PopNumber());
                int army = script.PopNumber();
                if (definition is null)
                    break;

                if (army == AllArmies)
                {
                    definition.MaxUnits = value;
                }
                else
                {
                    definition.ArmyMaxUnits[army & 3] = value;
                }

                break;
            }
            case ScriptFunction.SetEnemyCanAttackNeutralTrains:
                // Unused on AS1
                Session.CanMonstersAttackNeutralTrains = script.PopNumber() != 0;
                break;
            case ScriptFunction.SetMoney:
            {
                // Unused on AS1
                int money = script.PopNumber();
                _players[script.PopNumber() & ArmyMask].Money = money;
                break;
            }
            case ScriptFunction.GetMoney:
                // Unused on AS1
                script.PushNumber(_players[script.PopNumber() & ArmyMask].Money);
                break;

            // TODO: Port the trains, the rail map and the clever enemy attack
            case ScriptFunction.TrainProperty:
                script.PopNumber();
                PopSprite(script);
                script.PushNumber(0);
                break;
            case ScriptFunction.SetSemaphore:
                PopValues(script, 4);
                break;
            case ScriptFunction.BreakTrain:
            case ScriptFunction.PatrolEngine:
                PopValues(script, 2);
                PopSprite(script);
                break;
            case ScriptFunction.FirstTrain:
                script.PopNumber();
                PushSprite(script, null);
                break;
            case ScriptFunction.NextTrain:
                PushSprite(script, null);
                break;
            case ScriptFunction.SetPushLine:
                PopValues(script, 5);
                break;
            case ScriptFunction.SetCleverEnemyAttack:
                script.PopNumber();
                break;
        }
    }

    [EngineRoutine(0x0040fbb0)]
    private Sprite? GetSprite(SpriteSearch search, float x, float y, Sprite? previous)
    {
        var sprite = FindNearestSprite(search, x, y, GetSpriteReach, previous);

        if (sprite is null)
            return null;

        var half = sprite.Definition.Half;

        return sprite.Position.X - half.X <= x && x <= half.X + sprite.Position.X && sprite.Position.Y - half.Y <= y && y <= half.Y + sprite.Position.Y
            ? sprite : null;
    }

    [EngineRoutine(0x00410670)]
    private Sprite? FindNearestSprite(SpriteSearch search, float x, float y, float radius, Sprite? previous)
    {
        if (!TryGetSearchedTypes(ref search, out var types, out var armies, out var definition))
            return null;

        var point = new Vector2(x, y);
        float previousDistance = previous is null ? -1 : point.MeasureOctagonalDistance(previous.Position.AsVector2());
        var query = new SpriteQuery(new SpriteFilter(search, types, armies), point, previousDistance, radius);
        ScanSprites(ref query, search, types, definition, point - new Vector2(radius), point + new Vector2(radius), true);

        return query.Found;
    }

    [EngineRoutine(0x0040fc40)]
    private Sprite? GetSpriteScr(SpriteSearch search, float x, float y)
    {
        if (!TryGetSearchedTypes(ref search, out var types, out var armies, out var definition))
            return null;

        var point = new Vector2(x, y);
        var query = new ScreenSpriteQuery(new SpriteFilter(search, types, armies), point);
        var reach = new Vector2(ScreenSpriteReach);
        float ground = Movement.GetHeight(point);
        ScanSprites(ref query, search, types, definition, point - reach, new Vector2(x + ScreenSpriteReach, ground + y + ScreenSpriteReach), false);

        return query.Found;
    }

    private bool TryGetSearchedTypes(ref SpriteSearch search, out OldSpriteType types, out SpriteSearch armies, out SpriteDefinition? definition)
    {
        armies = search & SpriteSearch.AnyArmy;
        armies = armies == 0 ? SpriteSearch.AnyArmy : armies;
        definition = null;

        if (!search.HasFlag(SpriteSearch.Id))
        {
            types = (OldSpriteType)((int)search >> TypeShift) & SearchedTypes;
            types = types == 0 ? SearchedTypes : types;

            return true;
        }

        types = 0;
        definition = Definitions.Find((KnownObject)(search & SpriteSearch.ValueMask));

        if (definition is null || Sum(definition.Counts) == 0)
            return false;

        if (definition.Object.Properties.HasFlag(ObjectProperties.Hash))
        {
            search |= SpriteSearch.Hash;
        }

        types = definition.Object.SpriteType.ToOldSpriteType();

        return true;
    }

    private void ScanSprites<TQuery>(ref TQuery query, SpriteSearch search, OldSpriteType types, SpriteDefinition? definition, Vector2 min, Vector2 max, bool textInMenu)
        where TQuery : struct, ISpriteQuery
    {
        if (search.HasFlag(SpriteSearch.Hash))
        {
            Collector.FirstHashInBox(min, max);
            for (var sprite = Collector.NextHashInBox(); sprite is not null; sprite = Collector.NextHashInBox())
            {
                query.Consider(sprite, false);
            }

            return;
        }

        if ((types & GridListTypes) != 0 && (types & SearchedTypes & ~GridListTypes) == 0)
        {
            var listed = Collector.Listed;
            for (int i = listed.Length - 1; i >= 0; i--)
            {
                query.Consider(listed[i], true);
            }

            return;
        }

        // TODO: Port the menu list of menu sprites
        if (definition is not null && (definition.Object.SpriteClass == SpriteClass.Frame || (textInMenu && definition.Object.SpriteClass == SpriteClass.Text)))
            return;

        int first = definition?.Layer ?? 0;
        int last = definition is null ? UpdateOrder.UpdatedLayerCount : definition.Layer + 1;
        for (int layer = first; layer < last; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i].Sprite;

                if (sprite is not null)
                {
                    query.Consider(sprite, false);
                }
            }
        }
    }

    [EngineRoutine(0x0040f9a0)]
    private Sprite? FindListedSprite()
    {
        var list = _updates.GetLayer(_spriteLayer);
        for (_spriteIndex--; _spriteIndex >= 0; _spriteIndex--)
        {
            var sprite = list[_spriteIndex].Sprite;

            if (sprite is not null)
                return sprite;
        }

        return null;
    }

    private Sprite? GetNextSprite()
    {
        var sprite = FindListedSprite();
        while (sprite is null && _spriteLayer < UpdateOrder.LayerCount - 1)
        {
            _spriteLayer++;
            _spriteIndex = _updates.GetLayer(_spriteLayer).Count;
            sprite = FindListedSprite();
        }

        return sprite;
    }

    [EngineRoutine(0x00445b10)]
    private void AddCommand(Sprite sprite, int act, int var1, int var2, int var3)
    {
        var order = new Act(act, var1, var2, var3, Act.NamesSprite((ActionCode)(act & byte.MaxValue)) ? FindSprite(var1) : null);
        var orders = sprite.ActionStack.Orders;
        for (int i = orders.Length - 1; i >= 0; i--)
        {
            if (orders[i] is { Value: Act.StopMarker, Var1: 0, Var2: 0, Var3: 0 })
            {
                sprite.ActionStack.Insert(i + 1, order);

                return;
            }
        }

        sprite.ActionStack.Push(order);
    }

    [EngineRoutine(0x00412e80)]
    private Sprite? GetNextUnit()
    {
        var sprites = Collector.Listed;
        _unitIndex = Math.Min(_unitIndex, sprites.Length) - 1;

        return _unitIndex >= 0 ? sprites[_unitIndex] : null;
    }

    private Sprite? FindUnitOfMask(Sprite? sprite)
    {
        while (sprite is not null && (sprite.Definition.Object.SpriteType.ToOldSpriteType() & _unitMask) == 0)
        {
            sprite = GetNextUnit();
        }

        return sprite;
    }

    private List<Sprite> GetSpritesOfDefinition(SpriteDefinition definition)
    {
        var sprites = new List<Sprite>();
        foreach (var entry in _updates.GetLayer(definition.Layer))
        {
            if (entry.Sprite is not null && entry.Sprite.Definition == definition)
            {
                sprites.Add(entry.Sprite);
            }
        }

        AddSpritesOfDefinition(sprites, _statics.Values, definition);
        AddSpritesOfDefinition(sprites, _bodies.Values, definition);

        return sprites;
    }

    private static void AddSpritesOfDefinition(List<Sprite> found, Dictionary<MergedMapSprite, Sprite>.ValueCollection sprites, SpriteDefinition definition)
    {
        foreach (var sprite in sprites)
        {
            if (sprite.Definition == definition)
            {
                found.Add(sprite);
            }
        }
    }

    private Sprite? FindPlacementBlocker(SpriteDefinition definition, Vector3 point)
    {
        if (Movement.GetSupportHeight(definition.Object, point.AsVector2()) > point.Z)
            return Session.Cursor;

        return definition.MoveMask != 0 ? Projectiles.FindSprite(definition, point) : null;
    }

    private static void PopValues(Script script, int count)
    {
        for (int i = 0; i < count; i++)
        {
            script.Pop();
        }
    }

    private void ExecAction(Script script)
    {
        int third = script.PopNumber();
        int second = script.PopNumber();
        int first = script.PopNumber();
        var code = (ActionCode)script.PopNumber();
        var sprite = PopSprite(script);
        if (sprite is null)
        {
            script.PushNumber(0);
            return;
        }

        if ((int)code <= LastAnimationAction)
        {
            sprite.ChangeAnimation((SpriteAnimation)code);
            script.PushNumber(0);
            return;
        }

        switch (code)
        {
            case ActionCode.GetGoal:
                PushSprite(script, sprite.Goal);
                return;
            case ActionCode.GetLink:
                PushSprite(script, sprite.Link);
                return;
            case ActionCode.GetUplink:
                PushSprite(script, sprite.Owner);
                return;

            // TODO: Port the text sprites
            case ActionCode.GetTextDesc when sprite.Definition.Object.SpriteClass == SpriteClass.Text:
                script.PushText(ReadOnlyMemory<byte>.Empty);
                return;
            case ActionCode.GetTextDesc:
                throw new NotSupportedException($"Cannot read the text of {sprite.Definition.Id}.");

            // TODO: Port the trains
            case ActionCode.FirstEngine or ActionCode.LastEngine or ActionCode.NextEngine:
                PushSprite(script, null);
                return;
        }

        script.PushNumber(Action(sprite, code, first, second, third));
    }

    // TODO: Port the screen effects
    [EngineRoutine(0x00402690)]
    private void ExecEffect(Script script)
    {
        int duration = script.PopNumber();
        int second = script.PopNumber();
        int first = script.PopNumber();
        int effect = script.PopNumber();
        if (effect is < 0 or >= EffectCount)
            return;

        var screenEffect = (ScreenEffect)effect;

        if (duration == 0)
        {
            duration = screenEffect switch
            {
                ScreenEffect.Nuke or ScreenEffect.Fade => FlashDuration,
                ScreenEffect.Scroll => ScrollDuration,
                ScreenEffect.FadeIn => FadeInDuration,
                _ => EffectDuration
            };
        }

        _effectStarts[effect] = _clock.WallTime;
        _effectDurations[effect] = duration;
        _effectFirst[effect] = first;
        _effectSecond[effect] = second;
        if (screenEffect == ScreenEffect.None)
        {
            // Unused on AS1
            Array.Clear(_effectStarts);
            ScreenFlash = null;
        }
        else if (screenEffect == ScreenEffect.Nuke)
        {
            ScreenFlash = new ScreenFlash(Time, duration, first);
        }
    }

    [EngineRoutine(0x00431c00)]
    private int GetEffectState(int effect)
    {
        if (effect is <= (int)ScreenEffect.None or >= EffectCount || _effectStarts[effect] == 0)
            return -1;

        return (int)(unchecked(_clock.WallTime * 100 - _effectStarts[effect] * 100) / (uint)_effectDurations[effect]);
    }
}
