using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class Map : IScriptFunctionHost
{
    private const int AnyDirection = 999999;
    private const int PrintfBufferSize = 0x1000;
    private const int Base64ChunkSize = 0x39;
    private const uint ChecksumPolynomial = 0xEDB88320;
    private const int AllArmies = 0xff;
    private const int EffectCount = Graphics.EffectCount;
    private const int FlashDuration = 0x900;
    private const int ScrollDuration = 0x200;
    private const int FadeInDuration = 0x500;
    private const int EffectDuration = 0x400;
    private const int GammaChannels = 4;
    private const int FirstMouseClick = 0x400;
    private const int CursorOff = -1;
    private const int HardwareCursor = 0x100;
    private const int SoftwareCursor = 0x101;
    private const int LastCursorAnimation = 0x10;
    private const KnownObject SoftwareCursorId = (KnownObject)1;
    private const int SecondMouseClick = 0x800;
    private const int GammaChannelBits = 8;
    private const int SignedGammaRange = 0xfe;
    private const int NoUnitDistance = 60000;
    private const float TrigonometryScale = 1024;
    private const int LastAnimationAction = 0x10;
    private const float GetSpriteReach = 300;
    private const float ScreenSpriteReach = 300;
    private const int TypeShift = 20;

    private const OldSpriteType SearchedTypes = OldSpriteType.Terrain | OldSpriteType.Object | OldSpriteType.Unit | OldSpriteType.Avia |
        OldSpriteType.Menu | OldSpriteType.Railway | OldSpriteType.Region | OldSpriteType.Cannon | OldSpriteType.Sprite;

    private const OldSpriteType GridListTypes = OldSpriteType.Unit | OldSpriteType.Avia;
    private const int EndOfFileFlag = 0x10;

    private readonly Dictionary<int, Sprite> _handles = [];
    private int _lastHandle;
    private int _unitIndex;
    private OldSpriteType _unitMask;
    private int _spriteLayer;
    private int _spriteIndex;

    /// <summary>
    /// Gets the level script.
    /// </summary>
    public Script Script { get; } = new();

    /// <summary>
    /// Gets the registry the level script reads and writes.
    /// </summary>
    public ScriptRegistry Registry { get; }

    /// <summary>
    /// Gets the session the map belongs to.
    /// </summary>
    public GameSession Session { get; }

    internal Sprite? MenuLeftClick => _menuClicks.HasFlag(MenuClicks.Left) ? _menuCurrent : null;

    private static ReadOnlySpan<byte> Base64Alphabet => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"u8;

    private static ReadOnlySpan<byte> LogicLogTag => "LOGIC"u8;

    private Sprite? MenuRightClick => _menuClicks.HasFlag(MenuClicks.Right) ? _menuCurrent : null;

    private IGameFileSystem ScriptFiles => _scriptFileSystem ??= _fileSystem as MemoryWriteOverlay ?? new MemoryWriteOverlay(_fileSystem);

    /// <summary>
    /// Returns the handle the level script refers to a sprite by, and allocates one if the sprite doesn't have one yet.
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

    /// <summary>
    /// Creates a sprite of a definition at a point.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, and links to if the new sprite is a linker.</param>
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
        var sprite = PlaceSprite(placement, definition);

        if (sprite is null && definition.MaxHp == 0 && definition.Object.SpriteClass != SpriteClass.Cannon)
        {
            sprite = ConstructSprite(definition, position, direction, Definitions.GetArmy(definition), 0);
            ChangeArmy(sprite, (int)placement.Army);
            sprite.Placement = placement;
            _statics[placement] = sprite;
        }

        if (sprite is not null && definition.Object.SpriteClass == SpriteClass.Linker)
        {
            SpawnedObjectSystem.ConstructLinker(sprite, parent, position, direction);
        }

        if (sprite is not null)
        {
            RunCreationHook(sprite);
        }

        return sprite;
    }

    /// <summary>
    /// Runs a built-in function of the script language.
    /// </summary>
    /// <param name="script">The script whose stack holds the arguments.</param>
    /// <param name="function">The number of the built-in function.</param>
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
                var position = new Vector3(script.PopNumber(), y, z);
                int id = script.PopNumber();
                var definition = LookupDefinition(id);
                if (definition == Definitions.Empty)
                {
                    LogInvalidDefinition("for CreateSprite()"u8, id);
                    script.PushNumber(0);
                    break;
                }

                PushSprite(script, CreateSprite(definition, position, direction, parent));
                break;
            }
            case ScriptFunction.Destroy:
            {
                var sprite = PopSprite(script);
                if (sprite is not null)
                {
                    DeleteSprite(sprite);
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
                PushSprite(script, Collector.First(new Vector2(left, top), new Vector2(right, bottom)));
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
                script.PushNumber(sprite is null ? 0 : new Angle(new Vector2(x, y) - sprite.Position.AsVector2()).Value);
                break;
            }
            case ScriptFunction.ChangeZUnit:
            {
                int z = script.PopNumber();
                int id = script.PopNumber();
                var definition = Definitions.Find((KnownObject)id);
                if (definition is null)
                {
                    LogInvalidDefinition("for ChangeZUnit"u8, id);
                }
                else
                {
                    foreach (var sprite in EnumerateSprites(definition))
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
                var position = new Vector3(script.PopNumber(), y, z);
                int id = script.PopNumber();
                var definition = Definitions.Find((KnownObject)id);
                if (definition is null)
                {
                    LogInvalidDefinition("for CanPlace"u8, id);
                }

                PushSprite(script, FindPlacementBlocker(definition ?? Definitions.Empty, position));
                break;
            }
            case ScriptFunction.MapSizeX:
                PushNumber((int)Movement.MapSize.Width);
                break;
            case ScriptFunction.MapSizeY:
                PushNumber((int)Movement.MapSize.Height);
                break;
            case ScriptFunction.Genocide:
            {
                var definition = PopDefinition(script);
                if (definition != Definitions.Empty)
                {
                    foreach (var sprite in EnumerateSprites(definition))
                    {
                        DeleteSprite(sprite);
                    }
                }

                break;
            }
            case ScriptFunction.ReplaceUnit:
            {
                var replacement = PopDefinition(script);
                var definition = PopDefinition(script);
                if (definition != Definitions.Empty && replacement != Definitions.Empty)
                {
                    foreach (var sprite in EnumerateSprites(definition))
                    {
                        CreateSprite(replacement, sprite.Position, sprite.Direction);
                        DeleteSprite(sprite);
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
                _scrollType = (ScrollType)script.PopNumber();
                break;
            case ScriptFunction.GetScrollType:
                script.PushNumber((int)_scrollType);
                break;
            case ScriptFunction.SetPlayerControl:
                SetPlayerControl(script.PopNumber() != 0);
                break;
            case ScriptFunction.Exit:
                script.PopText();
                IsQuitting = true;
                break;
            case ScriptFunction.Effect:
                int effectDuration = script.PopNumber();
                int effectSecond = script.PopNumber();
                int effectFirst = script.PopNumber();
                ExecEffect(script.PopNumber(), effectFirst, effectSecond, effectDuration);
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
            {
                int type = PopNumber();
                var definition = PopDefinition(script);
                if (definition == Definitions.Empty)
                {
                    PushNumber(0);
                    break;
                }

                switch ((VidDataType)type)
                {
                    case VidDataType.MaxHp:
                        PushNumber(definition.MaxHp);
                        break;
                    case VidDataType.BattleRange:
                        PushNumber((int)definition.BattleRange);
                        break;
                    case VidDataType.Ammo:
                        PushNumber(GetAmmoOwner(definition).AmmoCapacity);
                        break;
                    case VidDataType.Count:
                        PushNumber(definition.GetTotalCount());
                        break;
                    case VidDataType.KilledUnit:
                        PushNumber(definition.GetTotalKilled());
                        break;
                    case >= VidDataType.KilledUnit0 and <= VidDataType.KilledUnit3:
                        PushNumber(definition.GetKilled(type - (int)VidDataType.KilledUnit0));
                        break;
                    case >= VidDataType.Count0 and <= VidDataType.Count3:
                        PushNumber(definition.GetCount(type - (int)VidDataType.Count0));
                        break;
                    case >= VidDataType.MaxHp0 and <= VidDataType.MaxHp3:
                        PushNumber(definition.GetArmyMaxHp(type - (int)VidDataType.MaxHp0));
                        break;
                    case VidDataType.SpriteClass:
                        PushNumber((int)definition.Object.SpriteClass);
                        break;
                    case VidDataType.Speed:
                        PushNumber(definition.ScriptSpeed);
                        break;
                    case VidDataType.Lifetime:
                        PushNumber(definition.Lifetime);
                        break;
                    case VidDataType.DetectRange:
                        PushNumber((int)definition.DetectRange);
                        break;
                    case VidDataType.WeaponAim:
                        PushNumber((int)definition.Aim);
                        break;
                    case VidDataType.MoveMask:
                        PushNumber((int)definition.MoveMask);
                        break;
                    case VidDataType.BuildTime:
                        PushNumber(definition.BuildTime);
                        break;
                    case VidDataType.NotCreateAsChild:
                        PushNumber(definition.NotCreatedAsChild);
                        break;
                    case VidDataType.Link:
                        PushNumber((int)(definition.LinkDefinition?.Id ?? 0));
                        break;
                    case >= VidDataType.Child and < VidDataType.Child + SpriteDefinition.StateCount:
                        PushNumber((int)(Definitions.Find((KnownObject)Math.Abs(definition.Children[type - (int)VidDataType.Child]))?.Id ?? 0));
                        break;
                    case >= VidDataType.ChildCounts and < VidDataType.ChildCounts + SpriteDefinition.StateCount:
                        PushNumber(definition.ChildCounts[type - (int)VidDataType.ChildCounts]);
                        break;
                    case VidDataType.Damage:
                        PushNumber(definition.Damage);
                        break;
                    case VidDataType.RecolorUnit:
                        PushNumber(definition.GetTotalRecolored());
                        break;
                    case >= VidDataType.RecolorUnit0 and <= VidDataType.RecolorUnit3:
                        PushNumber(definition.GetRecolored(type - (int)VidDataType.RecolorUnit0));
                        break;
                    case VidDataType.Name:
                        PushText(GameTextEncoding.Cyrillic.GetBytes(Definitions.GetName(definition)));
                        break;
                    case VidDataType.SpriteType:
                        PushNumber((int)definition.Object.SpriteType.ToOldSpriteType());
                        break;
                    case VidDataType.Directions:
                        PushNumber(definition.Object.Directions);
                        break;
                    case VidDataType.Hide:
                        PushNumber(definition.IsHidden ? 1 : 0);
                        break;
                    case VidDataType.FrameSpeed:
                        PushNumber(definition.SpriteSpeed);
                        break;
                    default:
                        LogMapError(LogError.Unknown, "GetVid type"u8, type);
                        break;
                }

                break;
            }
            case ScriptFunction.SetVidData:
            {
                int value = PopNumber();
                int type = PopNumber();
                var definition = PopDefinition(script);
                if (definition == Definitions.Empty)
                    break;

                switch ((VidDataType)type)
                {
                    case VidDataType.MaxHp:
                        definition.MaxHp = value;
                        break;
                    case >= VidDataType.Gamma and < VidDataType.Properties:
                        definition.Gammas[type - (int)VidDataType.Gamma] = Gamma.FromScript((uint)value);
                        break;
                    case VidDataType.Ammo:
                        GetAmmoOwner(definition).AmmoCapacity = value;
                        break;
                    case VidDataType.KilledUnit:
                        definition.Killed.Fill(value);
                        break;
                    case >= VidDataType.KilledUnit0 and <= VidDataType.KilledUnit3:
                        definition.Killed[type - (int)VidDataType.KilledUnit0] = value;
                        break;
                    case >= VidDataType.MaxHp0 and <= VidDataType.MaxHp3:
                        SetMaxHp(definition, type - (int)VidDataType.MaxHp0, value);
                        break;
                    case >= VidDataType.HpCoefficient0 and <= VidDataType.HpCoefficient3:
                        SetHpCoeff(definition, type - (int)VidDataType.HpCoefficient0, value);
                        break;
                    case VidDataType.Speed:
                        definition.Speed = value == SpriteDefinition.Unlimited ? SpriteDefinition.Unlimited : value * SpriteDefinition.PerMillisecond;
                        foreach (var entry in _updates.GetLayer(definition.Layer))
                        {
                            if (entry is not null && entry.Definition == definition && entry.HasExtra)
                            {
                                entry.MaxSpeed = definition.Speed;
                            }
                        }

                        break;
                    case VidDataType.Lifetime:
                        if (definition.Id != 0)
                        {
                            definition.Lifetime = value;
                            definition.HasTimers = true;
                        }

                        break;
                    case VidDataType.DetectRange:
                        definition.DetectRange = value;
                        break;
                    case VidDataType.WeaponAim:
                        definition.Aim = value;
                        break;
                    case VidDataType.ExchangeVid:
                    {
                        var other = Definitions.Find((KnownObject)value);
                        if (other is not null)
                        {
                            Definitions.Exchange(definition, other);
                        }
                        else
                        {
                            LogMapError(LogError.Invalid, "SetVid get_image"u8, value);
                        }

                        break;
                    }
                    case VidDataType.MoveMask:
                        definition.MoveMask = (CollisionLayers)value;
                        break;
                    case VidDataType.BuildTime:
                        definition.BuildTime = value;
                        break;
                    case VidDataType.NotCreateAsChild:
                        definition.NotCreatedAsChild = value;
                        break;
                    case VidDataType.Link:
                        definition.LinkDefinition = LookupDefinition(value);
                        break;
                    case >= VidDataType.Child and < VidDataType.Child + SpriteDefinition.StateCount:
                    {
                        var child = Definitions.Find((KnownObject)Math.Abs(value));
                        if (value == 0 || child is not null)
                        {
                            definition.Children[type - (int)VidDataType.Child] = value;
                        }
                        else
                        {
                            LogMapError(LogError.Invalid, "SetVid child"u8, value);
                        }

                        if (child is not null && child.IsBirthAsSmoke())
                        {
                            definition.HasTimers = true;
                        }

                        break;
                    }
                    case >= VidDataType.ChildCounts and < VidDataType.ChildCounts + SpriteDefinition.StateCount:
                        definition.ChildCounts[type - (int)VidDataType.ChildCounts] = value;
                        break;
                    case VidDataType.Damage:
                        definition.Damage = value;
                        break;
                    case VidDataType.Hide:
                        definition.SetPropHide(value != 0);
                        break;
                    case VidDataType.FrameSpeed:
                        definition.SetFrameSpeed(value);
                        break;
                    default:
                        LogMapError(LogError.Unknown, "SetVid type"u8, type);
                        break;
                }

                break;
            }
            case ScriptFunction.Log:
                Session.Logger.Write(script.PopText().Span);
                break;
            case ScriptFunction.ReloadVid:
                Definitions.ReloadRecords(Session.Logger);
                break;
            case ScriptFunction.OldStrlen:
            case ScriptFunction.Strlen:
                PushNumber(PopText().Length);
                break;
            case ScriptFunction.Itoa:
                PushText(StackObject.FormatNumber(PopNumber()));
                break;
            case ScriptFunction.Sin:
                PushNumber((int)(new Angle((byte)PopNumber()).Sine * TrigonometryScale));
                break;
            case ScriptFunction.Cos:
                PushNumber((int)(new Angle((byte)PopNumber()).Cosine * TrigonometryScale));
                break;
            case ScriptFunction.GetScreenInputX:
                PushNumber((int)Input.ScreenCursor.X);
                break;
            case ScriptFunction.GetScreenInputY:
                PushNumber((int)Input.ScreenCursor.Y);
                break;
            case ScriptFunction.SetAutoReBirth:
                script.Pop();
                break;
            case ScriptFunction.CanMoveEngineTo:
                PopValues(3);
                break;
            case ScriptFunction.CanAttackEngine:
                PopValues(2);
                break;

            case ScriptFunction.MenuFind:
                PushSprite(script, MenuFind(script));
                break;
            case ScriptFunction.MenuLoad:
                LoadMenu(script.PopText().Span);
                break;
            case ScriptFunction.MenuRelease:
            {
                var name = script.PopText();
                if (name.IsEmpty)
                {
                    DeleteAllMenuSprites();
                }
                else
                {
                    DeleteMenuFromFile(name.Span);
                }

                break;
            }
            case ScriptFunction.MenuNVidUnderCursor:
                script.PushNumber(GetMenuIdUnderCursor());
                break;
            case ScriptFunction.MenuNDirUnderCursor:
                script.PushNumber(GetMenuDirectionUnderCursor());
                break;
            case ScriptFunction.MenuAction:
                MenuAction(script);
                break;
            case ScriptFunction.MenuCreate:
            {
                int z = script.PopNumber();
                int y = script.PopNumber() + z;
                var position = new Vector3(script.PopNumber(), y, z);
                int direction = script.PopNumber();
                int id = script.PopNumber();
                var definition = LookupDefinition(id);
                if (definition == Definitions.Empty)
                {
                    LogInvalidDefinition("for MenuCreate"u8, id);
                    script.PushNumber(0);
                    break;
                }

                var heading = new Angle(unchecked((byte)((direction << 8) / definition.Layout.Directions)));
                PushSprite(script, CreateSprite(definition, position, heading));
                break;
            }
            case ScriptFunction.MenuLClick:
                PushSprite(script, MenuLeftClick);
                break;
            case ScriptFunction.MenuRClick:
                PushSprite(script, MenuRightClick);
                break;

            case ScriptFunction.SetCursor:
                SetCursor(script.PopNumber());
                break;
            case ScriptFunction.SetStateBar:
                script.Pop();
                break;
            case ScriptFunction.SetGraphDetail:
                script.Pop();
                break;
            case ScriptFunction.MessageText:
            {
                int y = script.PopNumber();
                var target = new Vector2(script.PopNumber(), y);
                _players[ControlledPlayer].Messages.Put(script.PopText(), target);
                break;
            }
            case ScriptFunction.SetPosition:
                int positionY = script.PopNumber();
                SetPosition(new Vector2(script.PopNumber(), positionY), CameraMove.None);
                break;
            case ScriptFunction.SetMouseClick:
                SetMouseClick(script.PopNumber(), script.PopNumber());
                break;
            case ScriptFunction.SetCursorPosition:
            {
                int y = script.PopNumber();
                var target = new Point(script.PopNumber(), y);
                Action(CursorSprite, ActionCode.ChangeCoor, target.X, target.Y, 0);
                break;
            }
            case ScriptFunction.SetWind:
            {
                var direction = new Angle((byte)script.PopNumber());
                Session.Graphics.SetWind(PopNumber(), direction);
                break;
            }
            case ScriptFunction.ScreenX:
                script.PushNumber((int)Session.ScreenSize.Width);
                break;
            case ScriptFunction.ScreenY:
                script.PushNumber((int)Session.ScreenSize.Height);
                break;
            case ScriptFunction.ToScreenX:
                script.PushNumber((int)(script.PopNumber() - Camera.X));
                break;
            case ScriptFunction.ToScreenY:
                int screenZ = script.PopNumber();
                int screenY = script.PopNumber();
                script.PushNumber((int)((float)screenY - screenZ - Camera.Y));
                break;

            case ScriptFunction.SetSoundVolume:
                Sound.SetVolume(script.PopNumber());
                break;
            case ScriptFunction.SetMusicVolume:
                Sound.SetMusicVolume(script.PopNumber(), _clock.WallTime);
                break;
            case ScriptFunction.PlaySfx:
                Sound.Play(script.PopNumber(), 0, SoundSystem.FullLoudness);
                break;
            case ScriptFunction.StopSfx:
                Sound.Stop(script.PopNumber(), _clock.WallTime);
                break;
            case ScriptFunction.StopMusic:
                Sound.StopMusic(_clock.WallTime);
                break;
            case ScriptFunction.PlaySfxFromCoor:
                int sfxY = script.PopNumber();
                var sfxPoint = new Vector2(script.PopNumber(), sfxY);
                Sound.PlayAt(script.PopNumber(), sfxPoint - Camera - Session.ScreenSize.ToVector2() * HalfShare);
                break;
            case ScriptFunction.PlayMusicFile:
                int musicLoop = script.PopNumber();
                Sound.PlayMusicFile(script.PopText().Span, musicLoop, _clock.WallTime);
                break;
            case ScriptFunction.IsPlayMusic:
                script.PushNumber(Sound.IsPlayMusic(_clock.WallTime) ? 1 : 0);
                break;

            // TODO: Port the movies
            case ScriptFunction.PlayMovie:
                script.Pop();
                break;
            case ScriptFunction.StopMovie:
                break;
            case ScriptFunction.IsPlayMovie:
                script.PushNumber(0);
                break;

            case ScriptFunction.SetGamma:
                Session.Graphics.SetGamma(Gamma.FromScript((uint)script.PopNumber()));
                break;
            case ScriptFunction.CountGamma:
            {
                int share = script.PopNumber();
                int last = script.PopNumber();
                script.PushNumber(BlendGamma(script.PopNumber(), last, share));
                break;
            }
            case ScriptFunction.GetGamma:
                script.PushNumber((int)Gamma.ToScript());
                break;

            // TODO: Port the saves
            case ScriptFunction.GetCommands:
            {
                var sprite = PopSprite(script);
                if (sprite is null)
                {
                    script.PushText(ReadOnlyMemory<byte>.Empty);
                    break;
                }

                var items = GetTextItems(sprite);
                var commands = GetCommands(sprite);
                byte[] both = new byte[items.Length + commands.Length];
                items.CopyTo(both);
                commands.CopyTo(both.AsMemory(items.Length));
                script.PushText(both);
                break;
            }
            case ScriptFunction.SetCommands:
            {
                var text = script.PopText();
                var sprite = PopSprite(script);
                if (sprite is null)
                    break;

                SetTextItems(sprite, text);
                if (text.Span.IndexOf(ItemsEnd) >= 0)
                {
                    text = GetAfter(text, ItemsEnd);
                }

                SetCommands(sprite, text);
                break;
            }
            case ScriptFunction.FRead:
            {
                var file = PopScriptFile();
                var line = IsPlayingDemo || file is null ? ReadOnlyMemory<byte>.Empty : file.ReadLine();
                // TODO: Port the demo stream strings of FRead and the shared text buffer a null handle pushes; recording stores each line and playback reads it back
                PushText(line);
                break;
            }
            case ScriptFunction.FWrite:
            {
                var text = PopText();
                var file = PopScriptFile();
                if (file is null)
                    break;

                file.WriteText(text.Span);
                file.SeekBack();
                file.WriteLineFeed();
                break;
            }
            case ScriptFunction.SaveDemo:
                if (!IsPlayingDemo)
                {
                    script.Pop();
                    Session.IsRecordingNextMap = true;
                }

                break;
            case ScriptFunction.GetString:
            {
                var keyword = script.PopText();
                script.PushText(ProfileFile.GetString(_fileSystem, StringsFile, script.PopText().Span, keyword.Span));
                break;
            }
            case ScriptFunction.Pause:
                if (script.PopNumber() != 0)
                {
                    Pause();
                    ShowCursor(0);
                }
                else
                {
                    Resume();
                }

                break;
            case ScriptFunction.Save:
                PopText();
                break;
            case ScriptFunction.Exec:
                // TODO: Port the shell launch of Exec; the port only logs the command
                Session.Logger.Write("Exec '%s'"u8, script.PopText());
                break;
            case ScriptFunction.FClose:
            {
                int handle = PopNumber();
                if (_scriptFiles.Remove(handle, out var file))
                {
                    file.Dispose();
                }

                break;
            }
            case ScriptFunction.GetPreviousMapName:
                script.PushText(Session.PreviousMapName);
                break;
            case ScriptFunction.GetAsProtectUserName:
                script.PushText(ReadOnlyMemory<byte>.Empty);
                break;
            case ScriptFunction.GetMapName:
                script.PushText(Session.MapName);
                break;
            case ScriptFunction.Crc32:
                PushNumber(ComputeChecksum(PopText().Span));
                break;
            case ScriptFunction.Printf:
                PushText(Printf());
                break;
            case ScriptFunction.ToLower:
                PushText(ChangeCase(PopText().Span, (byte)'A', (byte)'Z', 'a' - 'A'));
                break;
            case ScriptFunction.ToUpper:
                PushText(ChangeCase(PopText().Span, (byte)'a', (byte)'z', 'A' - 'a'));
                break;
            case ScriptFunction.FOpen:
            {
                string name = ReadText(PopText());
                if (IsPlayingDemo)
                {
                    PushNumber(0);
                    break;
                }

                var file = name.Length != 0 && ScriptFiles.TryRead(name, out var data) ? new ScriptFile(data.Span, null) : null;
                if (file is null)
                {
                    LogMapError(LogError.Open, GameTextEncoding.Cyrillic.GetBytes(name), 0);
                }

                PushNumber(AddScriptFile(file));
                break;
            }
            case ScriptFunction.FCreate:
            {
                string name = ReadText(PopText());
                if (IsPlayingDemo)
                {
                    PushNumber(0);
                    break;
                }

                var writer = name.Length != 0 ? ScriptFiles.OpenWrite(name, false) : null;
                PushNumber(AddScriptFile(writer is null ? null : new ScriptFile(default, writer)));
                break;
            }
            case ScriptFunction.FEof:
            {
                var file = PopScriptFile();
                PushNumber(file is null ? 1 : file.IsEnd ? EndOfFileFlag : 0);
                break;
            }
            case ScriptFunction.GetReg:
            {
                string defaultValue = ReadText(PopText());
                string name = ReadText(PopText());
                PushText(GameTextEncoding.Cyrillic.GetBytes(Registry.Get(ReadText(PopText()), name, defaultValue)));
                break;
            }
            case ScriptFunction.SetReg:
            {
                string value = ReadText(PopText());
                string name = ReadText(PopText());
                Registry.Set(ReadText(PopText()), name, value);
                break;
            }
            case ScriptFunction.DelReg:
            {
                string name = ReadText(PopText());
                Registry.Delete(ReadText(PopText()), name);
                break;
            }
            case ScriptFunction.GetDefaultRegPath:
                PushText(GameTextEncoding.Cyrillic.GetBytes(Registry.DefaultPath));
                break;
            case ScriptFunction.ToBase64:
            {
                int add = PopNumber();
                PushText(ToBase64(PopText().Span, add));
                break;
            }

            case ScriptFunction.GetSprite:
            {
                var previous = PopSprite(script);
                int y = script.PopNumber();
                int x = script.PopNumber();
                PushSprite(script, GetSprite((SpriteSearch)script.PopNumber(), new Vector2(x, y), previous));
                break;
            }
            case ScriptFunction.GetSpriteScr:
            {
                int y = script.PopNumber();
                int x = script.PopNumber();
                PushSprite(script, GetSpriteScr((SpriteSearch)script.PopNumber(), new Vector2(x, y)));
                break;
            }
            case ScriptFunction.FindNearestSprite:
            {
                var previous = PopSprite(script);
                int radius = script.PopNumber();
                int y = script.PopNumber();
                int x = script.PopNumber();
                PushSprite(script, FindNearestSprite((SpriteSearch)script.PopNumber(), new Vector2(x, y), radius, previous));
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
                int army = PopNumber();
                var definition = PopDefinition(script);
                int value = PopNumber();
                if (definition == Definitions.Empty)
                    break;

                if (army == AllArmies)
                {
                    definition.MaxUnits = value;
                }
                // TODO: Port AS1's out-of-range write for armies outside 0..3; AS1 writes def+0x398+army*4 into the fields around ArmyMaxUnits
                else if ((uint)army < (uint)definition.ArmyMaxUnits.Length)
                {
                    definition.ArmyMaxUnits[army] = value;
                }

                break;
            }
            case ScriptFunction.SetEnemyCanAttackNeutralTrains:
                Session.CanMonstersAttackNeutralTrains = PopNumber() != 0;
                break;
            case ScriptFunction.SetMoney:
            {
                int money = PopNumber();
                GetPlayer(PopNumber()).Money = money;
                break;
            }
            case ScriptFunction.GetMoney:
                PushNumber(GetPlayer(PopNumber()).Money);
                break;

            // TODO: Port the trains, the rail map and the clever enemy attack
            case ScriptFunction.TrainProperty:
                PopNumber();
                PopSprite();
                PushNumber(0);
                break;
            case ScriptFunction.SetSemaphore:
                PopValues(4);
                break;
            case ScriptFunction.BreakTrain:
            case ScriptFunction.PatrolEngine:
                PopValues(2);
                PopSprite();
                break;
            case ScriptFunction.FirstTrain:
                PopNumber();
                PushSprite(null);
                break;
            case ScriptFunction.NextTrain:
                PushSprite(null);
                break;
            case ScriptFunction.SetPushLine:
                PopValues(5);
                break;
            case ScriptFunction.SetCleverEnemyAttack:
                PopNumber();
                break;
            default:
                Session.Logger.Write("!!!ERROR!!!LOGIC: Unknown extern Function %i"u8, function);
                break;
        }
    }

    [EngineRoutine(0x0043a330)]
    internal int PopNumber() => Script.PopNumber();

    [EngineRoutine(0x0043a390)]
    internal ReadOnlyMemory<byte> PopText() => Script.PopText();

    [EngineRoutine(0x0043a470)]
    internal int PopHandle(out bool isNotSprite) => Script.PopHandle(out isNotSprite);

    [EngineRoutine(0x0043a640)]
    internal void PushText(ReadOnlyMemory<byte> text) => Script.PushText(text);

    [EngineRoutine(0x0043a5b0)]
    internal void PushNumber(int value) => Script.PushNumber(value);

    [EngineRoutine(0x0043a720)]
    internal void PushSprite(int value) => Script.PushSprite(value);

    private static void CountSprite(Sprite sprite)
    {
        if (sprite.IsCounted)
            return;

        sprite.IsCounted = true;
        sprite.Definition.Counts[(int)sprite.Army & 3]++;
    }

    [EngineRoutine(0x00439db0)]
    private static int ComputeChecksum(ReadOnlySpan<byte> text)
    {
        return unchecked((int)UpdateChecksum(0, CRuntime.GetString(text)));
    }

    [EngineRoutine(0x00406160)]
    private static uint UpdateChecksum(uint checksum, ReadOnlySpan<byte> text)
    {
        foreach (byte value in text)
        {
            checksum ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                checksum = (checksum & 1) != 0 ? checksum >> 1 ^ ChecksumPolynomial : checksum >> 1;
            }
        }

        return checksum;
    }

    [EngineRoutine(0x004072e0)]
    private static ReadOnlyMemory<byte> ToBase64(ReadOnlySpan<byte> text, int add)
    {
        var source = CRuntime.GetString(text);
        byte[] result = new byte[(source.Length + 2) / 3 * 4];
        Span<byte> chunk = stackalloc byte[Base64ChunkSize + 3];
        int written = 0;
        for (int start = 0; start < source.Length; start += Base64ChunkSize)
        {
            int count = Math.Min(Base64ChunkSize, source.Length - start);
            chunk.Clear();
            source.Slice(start, count).CopyTo(chunk);
            for (int i = 0; i < count; i++)
            {
                chunk[i] = unchecked((byte)(chunk[i] + add));
            }

            for (int i = 0; i < count; i += 3)
            {
                int group = (chunk[i] * 0x100 + chunk[i + 1]) * 0x100 + chunk[i + 2];
                for (int digit = 3; digit >= 0; digit--)
                {
                    result[written + digit] = Base64Alphabet[group & 0x3f];
                    group >>= 6;
                }

                written += 4;
            }
        }

        for (int padding = (3 - source.Length % 3) % 3; padding > 0; padding--)
        {
            result[written - padding] = (byte)'=';
        }

        return result;
    }

    private static ReadOnlyMemory<byte> ChangeCase(ReadOnlySpan<byte> text, byte first, byte last, int shift)
    {
        byte[] result = CRuntime.GetString(text).ToArray();
        for (int i = 0; i < result.Length; i++)
        {
            if (result[i] >= first && result[i] <= last)
            {
                result[i] = (byte)(result[i] + shift);
            }
        }

        return result;
    }

    private static bool IsMenuClass(SpriteDefinition definition) => definition.Object.SpriteClass is SpriteClass.Frame or SpriteClass.Text;

    private static bool IsMenuMatch(Sprite sprite, SpriteDefinition definition, int direction) =>
        sprite.Definition == definition && (direction == AnyDirection || definition.Layout.GetDirectionIndex(sprite.Direction) == direction);

    private static string ReadText(ReadOnlyMemory<byte> text) => GameTextEncoding.Cyrillic.GetString(text.Span);

    private static int GetGammaChannel(Gamma gamma, int shift) => gamma.GetNegative(shift) != 0 ? -gamma.GetNegative(shift) : gamma.GetPositive(shift);

    [EngineRoutine(0x0043a950)]
    private static int BlendGamma(int first, int last, int share) =>
        (BlendGammaChannel(first & byte.MaxValue, last & byte.MaxValue, share) & byte.MaxValue) |
        (BlendGammaChannel(first >> GammaChannelBits & byte.MaxValue, last >> GammaChannelBits & byte.MaxValue, share) & byte.MaxValue) << GammaChannelBits |
        (BlendGammaChannel(first >> 2 * GammaChannelBits & byte.MaxValue, last >> 2 * GammaChannelBits & byte.MaxValue, share) & byte.MaxValue) << 2 * GammaChannelBits;

    [EngineRoutine(0x0043a9c0)]
    private static int BlendGammaChannel(int first, int last, int share)
    {
        if (first > sbyte.MaxValue)
        {
            first -= SignedGammaRange;
        }

        if (last > sbyte.MaxValue)
        {
            last -= SignedGammaRange;
        }

        return (last - first) * share / byte.MaxValue + first;
    }

    [EngineRoutine(0x004066e0)]
    private ReadOnlyMemory<byte> Printf()
    {
        FormatArgument argument = Script.IsTextOnTop ? PopText() : PopNumber();
        var format = PopText();
        Span<byte> buffer = stackalloc byte[PrintfBufferSize];
        int length = CRuntime.PrintFormat(buffer, format.Span, [argument]);

        return buffer[..length].ToArray();
    }

    private void PopValues(int count)
    {
        for (int i = 0; i < count; i++)
        {
            PopNumber();
        }
    }

    private int GetTime() => Time;

    private void LogInvalidDefinition(ReadOnlySpan<byte> function, int id) =>
        Session.Logger.Write("!!!ERROR!!!SCRIPT: Invalid nvid %s %i"u8, (ReadOnlyMemory<byte>)function.ToArray(), id);

    [EngineRoutine(0x0043e140)]
    private bool ProcessMenuInput(PlayerInput input)
    {
        _menuCurrent = null;
        _menuClicks = MenuClicks.None;
        foreach (var sprite in _menuSprites)
        {
            var definition = sprite.Definition;

            if (definition.MoveMask == 0 || sprite.Animation is SpriteAnimation.Birth or >= SpriteAnimation.Death or SpriteAnimation.Close or SpriteAnimation.Open)
                continue;

            float top = sprite.Position.Y - sprite.Position.Z;
            var cursor = input.Cursor;
            float left = sprite.Position.X - definition.Half.X;
            float right = sprite.Position.X + definition.Half.X;
            float above = top - definition.Object.Size.Z;
            float high = above - definition.Half.Y;
            float low = top + definition.Half.Y;
            bool isMissed = cursor.X < left || right < cursor.X || cursor.Y <= high || low <= cursor.Y ||
                _menuCurrent is not null && sprite.Position.Z <= _menuCurrent.Position.Z;
            if (isMissed)
            {
                sprite.ChangeAnimation((SpriteAnimation)((int)sprite.Animation & 1));
            }
            else
            {
                _menuCurrent = sprite;
            }
        }

        if (_menuCurrent is null)
            return false;

        if (input.Flags.HasFlag(InputFlags.LeftClick))
        {
            _menuClicks |= MenuClicks.Left;
            input.ClearLeftClick();
            _menuCurrent.ChangeAnimation((SpriteAnimation)((int)_menuCurrent.Animation & 1 | (int)MenuAnimation.PressDown));

            return true;
        }

        if (input.Flags.HasFlag(InputFlags.RightClick))
        {
            _menuClicks |= MenuClicks.Right;
            input.ClearRightClick();
        }

        int current = (int)_menuCurrent.Animation;
        if ((current & ~1) != (int)MenuAnimation.PressDown)
        {
            _menuCurrent.ChangeAnimation((SpriteAnimation)(current & 1 | (int)MenuAnimation.SelectDown));
        }

        return false;
    }

    [EngineRoutine(0x0043e880)]
    private int GetMenuIdUnderCursor() => _menuCurrent is null ? 0 : (int)_menuCurrent.Definition.Id;

    [EngineRoutine(0x0043e8a0)]
    private int GetMenuDirectionUnderCursor() =>
        _menuCurrent is null ? 0 : _menuCurrent.Definition.Layout.GetDirectionIndex(_menuCurrent.Direction);

    private void SetMouseClick(int key, int button)
    {
        if (button == FirstMouseClick)
        {
            _controls.First = new ControlBinding(key, key);
        }
        else if (button == SecondMouseClick)
        {
            _controls.Second = new ControlBinding(key, key);
        }
    }

    private void SetCursor(int type)
    {
        switch (type)
        {
            case CursorOff:
                TurnCursorOff();
                break;
            case HardwareCursor:
                UseHardwareCursor();
                break;
            case SoftwareCursor:
                UseSoftwareCursor();
                break;
            default:
                if (!Session.IsCursorLoaded)
                {
                    LoadCursor();
                }

                ShowCursor(type);
                break;
        }
    }

    [EngineRoutine(0x00446d90)]
    private void LoadCursor() => Session.IsCursorLoaded = true;

    [EngineRoutine(0x00446ef0)]
    private void TurnCursorOff() => Session.IsCursorLoaded = false;

    [EngineRoutine(0x00446f40)]
    private void UseHardwareCursor()
    {
        if (Session.IsCursorHardware)
            return;

        TurnCursorOff();
        CursorSprite.ReplaceDefinition(Definitions.Empty);
        Session.IsCursorHardware = true;
        LoadCursor();
    }

    [EngineRoutine(0x00446f70)]
    private void UseSoftwareCursor()
    {
        if (!Session.IsCursorHardware)
            return;

        TurnCursorOff();
        var cursor = CursorSprite;
        var state = cursor.Animation;
        cursor.ReplaceDefinition(Definitions.Find(SoftwareCursorId) ?? Definitions.Empty);
        cursor.ChangeAnimation(state == SpriteAnimation.Stand ? SpriteAnimation.StopMove : SpriteAnimation.Stand);
        cursor.ChangeAnimation(state);
        Session.IsCursorHardware = false;
        LoadCursor();
    }

    [EngineRoutine(0x00446fd0)]
    private void ShowCursor(int type)
    {
        if (Session.IsCursorHardware && type > LastCursorAnimation)
        {
            CursorSprite.SetAnimationValue((SpriteAnimation)type);
            return;
        }

        CursorSprite.ChangeAnimation((SpriteAnimation)type);
    }

    [EngineRoutine(0x0043dfe0)]
    private void DeleteAllMenuSprites()
    {
        for (int i = 0; i < _menuSprites.Count; i++)
        {
            for (int j = _menuSprites.Count - 1; i < j; j--)
            {
                if (_menuSprites[i] != _menuSprites[j])
                    continue;

                Release(_menuSprites[j]);
                if (j < _menuSprites.Count)
                {
                    int last = _menuSprites.Count - 1;
                    _menuSprites[j] = _menuSprites[last];
                    _menuSprites.RemoveAt(last);
                }
            }
        }

        for (int i = _menuSprites.Count - 1; i >= 0; i--)
        {
            if (i < _menuSprites.Count)
            {
                DeleteMenuSpriteAt(i);
            }
        }
    }

    [EngineRoutine(0x0043deb0)]
    private void RemoveMenuSprite(Sprite sprite)
    {
        int i = _menuSprites.LastIndexOf(sprite);
        if (i < 0)
            return;

        int last = _menuSprites.Count - 1;
        _menuSprites[i] = _menuSprites[last];
        _menuSprites.RemoveAt(last);
        Release(sprite);
    }

    private Sprite? MenuFind(Script script)
    {
        int direction = script.PopNumber();
        int id = script.PopNumber();
        var definition = LookupDefinition(id);

        if (definition == Definitions.Empty)
        {
            LogInvalidDefinition("for MenuFind"u8, id);
            return null;
        }

        if (Sum(definition.Counts) == 0)
            return null;

        foreach (var sprite in _menuSprites)
        {
            if (IsMenuMatch(sprite, definition, direction))
                return sprite;
        }

        return null;
    }

    private void MenuAction(Script script)
    {
        int var3 = script.PopNumber();
        int var2 = script.PopNumber();
        int var1 = script.PopNumber();
        int act = script.PopNumber();
        int direction = script.PopNumber();
        int id = script.PopNumber();
        var definition = LookupDefinition(id);

        if (definition == Definitions.Empty)
        {
            LogInvalidDefinition("for MenuAction"u8, id);
            return;
        }

        for (int i = 0; i < _menuSprites.Count; i++)
        {
            var sprite = _menuSprites[i];

            if (!IsMenuMatch(sprite, definition, direction))
                continue;

            if (act <= Act.LastState)
            {
                sprite.ChangeAnimation((SpriteAnimation)act);
            }
            else
            {
                Action(sprite, (ActionCode)act, var1, var2, var3);
            }
        }
    }

    private ScriptFile? PopScriptFile() => _scriptFiles.GetValueOrDefault(PopNumber());

    private int AddScriptFile(ScriptFile? file)
    {
        if (file is null)
            return 0;

        int handle = 1;
        foreach (int key in _scriptFiles.Keys)
        {
            if (key >= handle)
            {
                handle = key + 1;
            }
        }

        _scriptFiles[handle] = file;

        return handle;
    }

    private Sprite? PopSprite(Script script)
    {
        int handle = script.PopHandle(out bool isNotSprite);

        return FindPoppedSprite(handle, isNotSprite);
    }

    private Sprite? PopSprite()
    {
        int handle = PopHandle(out bool isNotSprite);

        return FindPoppedSprite(handle, isNotSprite);
    }

    private Sprite? FindPoppedSprite(int handle, bool isNotSprite)
    {
        if (isNotSprite)
        {
            Session.Logger.Error(LogicLogTag, LogError.Text, 0, default);
        }

        return FindSprite(handle);
    }

    [EngineRoutine(0x00405a90)]
    private SpriteDefinition LookupDefinition(int id) => Definitions.Find((KnownObject)id) ?? Definitions.Empty;

    [EngineRoutine(0x0043a510)]
    private SpriteDefinition PopDefinition(Script script)
    {
        var definition = LookupDefinition(script.PopNumber());

        if (definition == Definitions.Empty)
        {
            LogInvalidDefinition(default, 0);
        }

        return definition;
    }

    [EngineRoutine(0x0043a7e0)]
    private Player GetPlayer(int army) => _players[army & SpriteDefinition.ArmyMask];

    [EngineRoutine(0x0043a7b0)]
    private void LogMapError(LogError code, ReadOnlySpan<byte> text, int value) => Session.Logger.Error(MapLogTag, code, value, text);

    private void PushSprite(Script script, Sprite? sprite) => script.PushSprite(GetSpriteHandle(sprite));

    private void PushSprite(Sprite? sprite) => PushSprite(GetSpriteHandle(sprite));

    [EngineRoutine(0x0040fbb0)]
    private Sprite? GetSprite(SpriteSearch search, Vector2 point, Sprite? previous)
    {
        var sprite = FindNearestSprite(search, point, GetSpriteReach, previous);

        if (sprite is null)
            return null;

        var half = sprite.Definition.Half;

        return sprite.Position.X - half.X <= point.X && point.X <= half.X + sprite.Position.X && sprite.Position.Y - half.Y <= point.Y && point.Y <= half.Y + sprite.Position.Y
            ? sprite : null;
    }

    [EngineRoutine(0x00410670)]
    private Sprite? FindNearestSprite(SpriteSearch search, Vector2 point, float radius, Sprite? previous)
    {
        if (!TryGetSearchedTypes(ref search, out var types, out var armies, out var definition))
            return null;

        float previousDistance = previous is null ? -1 : point.MeasureOctagonalDistance(previous.Position.AsVector2());
        var query = new SpriteQuery(new SpriteFilter(search, types, armies), point, previousDistance, radius);
        ScanSprites(ref query, search, types, definition, point - new Vector2(radius), point + new Vector2(radius), true);

        return query.Found;
    }

    [EngineRoutine(0x0040fc40)]
    private Sprite? GetSpriteScr(SpriteSearch search, Vector2 point)
    {
        if (!TryGetSearchedTypes(ref search, out var types, out var armies, out var definition))
            return null;

        var query = new ScreenSpriteQuery(new SpriteFilter(search, types, armies), point);
        var reach = new Vector2(ScreenSpriteReach);
        float ground = Movement.GetHeight(point);
        ScanSprites(ref query, search, types, definition, point - reach, new Vector2(point.X + ScreenSpriteReach, ground + point.Y + ScreenSpriteReach), false);

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

        if ((types.HasFlag(OldSpriteType.Unit) || types.HasFlag(OldSpriteType.Avia)) && (types & SearchedTypes & ~GridListTypes) == OldSpriteType.None)
        {
            var listed = Collector.Listed;
            for (int i = listed.Length - 1; i >= 0; i--)
            {
                query.Consider(listed[i], true);
            }

            return;
        }

        if (definition is not null && (definition.Object.SpriteClass == SpriteClass.Frame || (textInMenu && definition.Object.SpriteClass == SpriteClass.Text)))
        {
            for (int i = _menuSprites.Count - 1; i >= 0; i--)
            {
                query.ConsiderMenu(_menuSprites[i]);
            }

            return;
        }

        int first = definition is null ? 0 : (int)definition.Layer;
        int last = definition is null ? UpdateOrder.UpdatedLayerCount : (int)definition.Layer + 1;
        for (int layer = first; layer < last; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];

                if (sprite is not null)
                {
                    query.Consider(sprite, false);
                }
            }
        }
    }

    private DefinitionSprites EnumerateSprites(SpriteDefinition definition) => new(_updates.GetLayer(definition.Layer), definition);

    [EngineRoutine(0x0040f9a0)]
    private Sprite? FindListedSprite()
    {
        var list = _updates.GetLayer(_spriteLayer);
        for (_spriteIndex--; _spriteIndex >= 0; _spriteIndex--)
        {
            var sprite = list[_spriteIndex];

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
        var order = CreateAct(act, var1, var2, var3);
        var orders = sprite.ActionStack.Orders;
        for (int i = orders.Length - 1; i >= 0; i--)
        {
            if (orders[i] is { Value: Act.StopMarker, Var1: 0, Var2: 0, Var3: 0 })
            {
                sprite.ActionStack.Insert(i + 1, order);

                return;
            }
        }

        sprite.ActionStack.Insert(0, order);
    }

    [EngineRoutine(0x00412e80)]
    private Sprite? GetNextUnit()
    {
        _unitIndex = Math.Min(_unitIndex, Collector.Listed.Length) - 1;

        return _unitIndex >= 0 ? Collector.Listed[_unitIndex] : null;
    }

    private Sprite? FindUnitOfMask(Sprite? sprite)
    {
        while (sprite is not null && (sprite.Definition.Object.SpriteType.ToOldSpriteType() & _unitMask) == 0)
        {
            sprite = GetNextUnit();
        }

        return sprite;
    }

    [EngineRoutine(0x0042da30)]
    private Sprite? FindPlacementBlocker(SpriteDefinition definition, Vector3 point)
    {
        if (Movement.GetSupportHeight(definition.Object, point.AsVector2()) > point.Z)
            return CursorSprite;

        return definition.MoveMask != 0 ? Projectiles.FindSprite(definition, point) : null;
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
            case ActionCode.GetGoal or ActionCode.GetLink or ActionCode.GetUplink:
                script.PushSprite(Action(sprite, code, first, second, third));
                return;

            case ActionCode.GetTextDesc when sprite.Text is not null:
                script.PushText(sprite.Text.Description);
                return;

            // TODO: Port the trains
            case ActionCode.FirstEngine or ActionCode.LastEngine or ActionCode.NextEngine:
                PushSprite(script, null);
                return;
        }

        script.PushNumber(Action(sprite, code, first, second, third));
    }

    // TODO: Port the frame capture of the alpha appearance; AS1 also clears the three fade timers when the capture surface exists
    [EngineRoutine(0x00402690)]
    private void ExecEffect(int effect, int first, int second, int duration)
    {
        if (effect is < 0 or >= EffectCount)
            return;

        var screenEffect = (ScreenEffect)effect;

        if (screenEffect is ScreenEffect.Fade or ScreenEffect.FadeIn or ScreenEffect.FadeOut)
        {
            Session.Graphics.EffectStartTimes[(int)ScreenEffect.FadeOut] = 0;
            Session.Graphics.EffectStartTimes[(int)ScreenEffect.FadeIn] = 0;
            Session.Graphics.EffectStartTimes[(int)ScreenEffect.Fade] = 0;
        }

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

        Session.Graphics.EffectStartTimes[effect] = _clock.WallTime;
        Session.Graphics.EffectDurations[effect] = duration;
        Session.Graphics.EffectFirst[effect] = first;
        Session.Graphics.EffectSecond[effect] = second;
        if (screenEffect == ScreenEffect.None)
        {
            Array.Clear(Session.Graphics.EffectStartTimes);
            ScreenFlash = null;
        }
        else if (screenEffect == ScreenEffect.Nuke)
        {
            ScreenFlash = new ScreenFlash(Time, duration, first);
        }
        else if (screenEffect == ScreenEffect.Scroll)
        {
            Session.Graphics.ScrollFrom = new Vector2(Camera.Y);
        }
        else if (screenEffect == ScreenEffect.ShiftGamma)
        {
            Session.Graphics.GammaFrom = Gamma;
        }
    }

    [EngineRoutine(0x004311a0)]
    private void UpdateEffects(bool draw)
    {
        uint now = _clock.WallTime;
        ExpireEffect(ScreenEffect.AlphaAppear, now);

        const int scroll = (int)ScreenEffect.Scroll;
        if (Session.Graphics.EffectStartTimes[scroll] != 0)
        {
            uint elapsed = now - Session.Graphics.EffectStartTimes[scroll];
            int length = Session.Graphics.EffectDurations[scroll];
            if ((uint)length < elapsed)
            {
                SetPosition(new Vector2(Session.Graphics.EffectFirst[scroll], Session.Graphics.EffectSecond[scroll]), CameraMove.None);
                Session.Graphics.EffectStartTimes[scroll] = 0;
            }
            else
            {
                var from = Session.Graphics.ScrollFrom;
                var position = new Vector2((Session.Graphics.EffectFirst[scroll] - from.X) * (int)elapsed / length + from.X,
                    (Session.Graphics.EffectSecond[scroll] - from.Y) * (int)elapsed / length + from.Y);
                SetPosition(position, CameraMove.None);
            }
        }

        ExpireEffect(ScreenEffect.Nuke, now);
        ExpireEffect(ScreenEffect.Fade, now);
        ExpireEffect(ScreenEffect.FadeIn, now);
        ExpireEffect(ScreenEffect.FadeOut, now);

        const int shift = (int)ScreenEffect.ShiftGamma;
        if (Session.Graphics.EffectStartTimes[shift] == 0)
            return;

        uint shifted = now - Session.Graphics.EffectStartTimes[shift];
        var target = Gamma.FromScript((uint)Session.Graphics.EffectFirst[shift]);
        if (shifted >= (uint)Session.Graphics.EffectDurations[shift])
        {
            Session.Graphics.SetGamma(target);
            Session.Graphics.EffectStartTimes[shift] = 0;

            return;
        }

        if (!draw)
            return;

        float share = (float)shifted / Session.Graphics.EffectDurations[shift];
        uint negative = 0, positive = 0;
        for (int channel = GammaChannelBits * (GammaChannels - 1); channel >= 0; channel -= GammaChannelBits)
        {
            int from = GetGammaChannel(Session.Graphics.GammaFrom, channel);
            int value = Math.Clamp((int)((GetGammaChannel(target, channel) - from) * share + from), -byte.MaxValue, byte.MaxValue);
            if (value < 0)
            {
                negative |= (uint)-value << channel;
            }
            else
            {
                positive |= (uint)value << channel;
            }
        }

        Session.Graphics.SetGamma(new Gamma(negative, positive));
    }

    private void ExpireEffect(ScreenEffect effect, uint now)
    {
        int index = (int)effect;

        if (Session.Graphics.EffectStartTimes[index] != 0 && now - Session.Graphics.EffectStartTimes[index] >= (uint)Session.Graphics.EffectDurations[index])
        {
            Session.Graphics.EffectStartTimes[index] = 0;
        }
    }

    [EngineRoutine(0x00431c00)]
    private int GetEffectState(int effect)
    {
        if (effect is <= (int)ScreenEffect.None or >= EffectCount || Session.Graphics.EffectStartTimes[effect] == 0)
            return -1;

        return (int)(unchecked(_clock.WallTime * 100 - Session.Graphics.EffectStartTimes[effect] * 100) / (uint)Session.Graphics.EffectDurations[effect]);
    }
}
