using System.Numerics;
using ASToolkit.Core.Engine.IO;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class Map : IScriptFunctionHost
{
    private const int AnyDirection = 999999;
    private const int PrintfBufferSize = 0x1000;
    private const uint ChecksumPolynomial = 0xEDB88320;
    private const int AllArmies = 0xff;
    private const int EffectCount = 16;
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
    private readonly uint[] _effectStarts = new uint[EffectCount];
    private readonly int[] _effectDurations = new int[EffectCount];
    private readonly int[] _effectFirst = new int[EffectCount];
    private readonly int[] _effectSecond = new int[EffectCount];
    private Vector2 _scrollFrom;
    private Gamma _gammaFrom;
    private readonly Dictionary<int, Sprite> _handles = [];
    private readonly Dictionary<MergedMapSprite, SpriteDefinition> _countedPlacements = [];
    private int _lastHandle;
    private int _unitIndex;
    private OldSpriteType _unitMask;
    private int _spriteLayer;
    private int _spriteIndex;

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

        if (IsMenuClass(sprite.Definition))
        {
            RemoveMenuSprite(sprite);
        }

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

    private int GetTime() => Time;

    [EngineRoutine(0x00406160)]
    private static int ComputeChecksum(ReadOnlySpan<byte> text)
    {
        int end = text.IndexOf((byte)0);
        uint checksum = 0;
        foreach (byte value in end < 0 ? text : text[..end])
        {
            checksum ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                checksum = (checksum & 1) != 0 ? checksum >> 1 ^ ChecksumPolynomial : checksum >> 1;
            }
        }

        return unchecked((int)checksum);
    }

    [EngineRoutine(0x004066e0)]
    private static ReadOnlyMemory<byte> Printf(Script script)
    {
        FormatArgument argument = script.IsTextOnTop ? script.PopText() : script.PopNumber();
        var format = script.PopText();
        Span<byte> buffer = stackalloc byte[PrintfBufferSize];
        int length = PrintFormat.Format(buffer, format.Span, [argument]);

        return buffer[..length].ToArray();
    }

    private static ReadOnlyMemory<byte> ChangeCase(ReadOnlySpan<byte> text, byte first, byte last, int shift)
    {
        int end = text.IndexOf((byte)0);
        byte[] result = (end < 0 ? text : text[..end]).ToArray();
        for (int i = 0; i < result.Length; i++)
        {
            if (result[i] >= first && result[i] <= last)
            {
                result[i] = (byte)(result[i] + shift);
            }
        }

        return result;
    }

    private void LogInvalidDefinition(ReadOnlySpan<byte> function, int id) =>
        Session.Logger.Write("!!!ERROR!!!SCRIPT: Invalid nvid %s %i"u8, (ReadOnlyMemory<byte>)function.ToArray(), id);

    [EngineRoutine(0x0043e140)]
    private bool UpdateMenus(PlayerInput input)
    {
        _menuCurrent = null;
        _menuClicks = MenuClicks.None;
        foreach (var sprite in _menuSprites)
        {
            var definition = sprite.Definition;
            var state = sprite.Animation;

            if (definition.MoveMask == 0 || state is SpriteAnimation.Birth or >= SpriteAnimation.Death or SpriteAnimation.Close or SpriteAnimation.Open)
                continue;

            double top = (double)sprite.Position.Y - sprite.Position.Z;
            var cursor = input.Cursor;
            bool isMissed = cursor.X < (double)sprite.Position.X - definition.Half.X || cursor.X > (double)sprite.Position.X + definition.Half.X ||
                cursor.Y <= top - definition.Object.Size.Z - definition.Half.Y || cursor.Y >= top + definition.Half.Y ||
                _menuCurrent is not null && sprite.Position.Z <= _menuCurrent.Position.Z;
            if (isMissed)
            {
                sprite.ChangeAnimation((SpriteAnimation)((int)state & 1));
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

    internal Sprite? MenuLeftClick => _menuClicks.HasFlag(MenuClicks.Left) ? _menuCurrent : null;

    private Sprite? MenuRightClick => _menuClicks.HasFlag(MenuClicks.Right) ? _menuCurrent : null;

    [EngineRoutine(0x0043e880)]
    private int GetMenuIdUnderCursor() => _menuCurrent is null ? 0 : (int)_menuCurrent.Definition.Id;

    [EngineRoutine(0x0043e8a0)]
    private int GetMenuDirectionUnderCursor() =>
        _menuCurrent is null ? 0 : _menuCurrent.Direction.GetDirectionIndex(_menuCurrent.Definition.Layout.Directions);

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
        Session.Cursor!.ReplaceDefinition(Definitions.Empty);
        Session.IsCursorHardware = true;
        LoadCursor();
    }

    [EngineRoutine(0x00446f70)]
    private void UseSoftwareCursor()
    {
        if (!Session.IsCursorHardware)
            return;

        TurnCursorOff();
        var cursor = Session.Cursor!;
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
        var cursor = Session.Cursor!;
        if (Session.IsCursorHardware && type > LastCursorAnimation)
        {
            cursor.SetAnimationValue((SpriteAnimation)type);
            return;
        }

        cursor.ChangeAnimation((SpriteAnimation)type);
    }

    private static bool IsMenuClass(SpriteDefinition definition) => definition.Object.SpriteClass is SpriteClass.Frame or SpriteClass.Text;

    // TODO: Port releasing the sprites of one menu file (0x43e5c0)
    [EngineRoutine(0x0043dfe0)]
    private void ReleaseMenuSprites()
    {
        for (int index = _menuSprites.Count - 1; index >= 0; index--)
        {
            if (index >= _menuSprites.Count)
                continue;

            var sprite = _menuSprites[index];
            int last = _menuSprites.Count - 1;
            _menuSprites[index] = _menuSprites[last];
            _menuSprites.RemoveAt(last);
            if (_menuCurrent == sprite)
            {
                _menuCurrent = null;
            }

            DestroySprite(sprite);
        }
    }

    [EngineRoutine(0x0043deb0)]
    private void RemoveMenuSprite(Sprite sprite)
    {
        if (_menuCurrent == sprite)
        {
            _menuCurrent = null;
        }

        int index = _menuSprites.LastIndexOf(sprite);
        if (index < 0)
            return;

        int last = _menuSprites.Count - 1;
        _menuSprites[index] = _menuSprites[last];
        _menuSprites.RemoveAt(last);
    }

    private Sprite? MenuFind(Script script)
    {
        int direction = script.PopNumber();
        int id = script.PopNumber();
        var definition = Definitions.Find((KnownObject)id);

        if (definition is null)
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

    private static bool IsMenuMatch(Sprite sprite, SpriteDefinition definition, int direction) =>
        sprite.Definition == definition && (direction == AnyDirection || sprite.Direction.GetDirectionIndex(definition.Layout.Directions) == direction);

    private void MenuAction(Script script)
    {
        script.PopNumber();
        script.PopNumber();
        int var1 = script.PopNumber();
        int act = script.PopNumber();
        int direction = script.PopNumber();
        int id = script.PopNumber();
        var definition = Definitions.Find((KnownObject)id);

        if (definition is null)
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
                Action(sprite, (ActionCode)act, var1);
            }
        }
    }

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
                int id = script.PopNumber();
                var definition = Definitions.Find((KnownObject)id);
                if (definition is null)
                {
                    LogInvalidDefinition("for CreateSprite()"u8, id);
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
                int id = script.PopNumber();
                var definition = Definitions.Find((KnownObject)id);
                if (definition is null)
                {
                    LogInvalidDefinition("for ChangeZUnit"u8, id);
                }
                else
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
                int id = script.PopNumber();
                var definition = Definitions.Find((KnownObject)id);
                if (definition is null)
                {
                    LogInvalidDefinition("for CanPlace"u8, id);
                }

                PushSprite(script, FindPlacementBlocker(definition ?? Definitions.Empty, new Vector3(x, y, z)));
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
            {
                int type = script.PopNumber();
                var definition = Definitions.Find((KnownObject)script.PopNumber());
                if (definition is null)
                {
                    script.PushNumber(0);
                    break;
                }

                switch ((VidDataType)type)
                {
                    case VidDataType.MaxHp:
                        script.PushNumber(definition.MaxHp);
                        break;
                    case VidDataType.BattleRange:
                        script.PushNumber((int)definition.BattleRange);
                        break;
                    case VidDataType.Ammo:
                        script.PushNumber(GetAmmoOwner(definition).AmmoCapacity);
                        break;
                    case VidDataType.Count:
                        script.PushNumber(Sum(definition.Counts));
                        break;
                    case VidDataType.KilledUnit:
                        script.PushNumber(Sum(definition.Killed));
                        break;
                    case >= VidDataType.KilledUnit0 and <= VidDataType.KilledUnit3:
                        script.PushNumber(definition.Killed[type - (int)VidDataType.KilledUnit0]);
                        break;
                    case >= VidDataType.Count0 and <= VidDataType.Count3:
                        script.PushNumber(definition.Counts[type - (int)VidDataType.Count0]);
                        break;
                    case >= VidDataType.MaxHp0 and <= VidDataType.MaxHp3:
                        script.PushNumber(definition.ArmyMaxHp[type - (int)VidDataType.MaxHp0]);
                        break;
                    case VidDataType.SpriteClass:
                        script.PushNumber((int)definition.Object.SpriteClass);
                        break;
                    case VidDataType.Speed:
                        script.PushNumber(definition.ScriptSpeed);
                        break;
                    case VidDataType.Lifetime:
                        script.PushNumber(definition.Lifetime);
                        break;
                    case VidDataType.DetectRange:
                        script.PushNumber((int)definition.DetectRange);
                        break;
                    case VidDataType.WeaponAim:
                        script.PushNumber((int)definition.Aim);
                        break;
                    case VidDataType.MoveMask:
                        script.PushNumber((int)definition.MoveMask);
                        break;
                    case VidDataType.BuildTime:
                        script.PushNumber(definition.BuildTime);
                        break;
                    case VidDataType.NotCreateAsChild:
                        script.PushNumber(definition.NotCreatedAsChild);
                        break;
                    case VidDataType.Link:
                        script.PushNumber((int)(definition.LinkDefinition?.Id ?? 0));
                        break;
                    case >= VidDataType.Child and < VidDataType.ChildCounts:
                        script.PushNumber((int)(Definitions.Find((KnownObject)Math.Abs(definition.Children[type - (int)VidDataType.Child]))?.Id ?? 0));
                        break;
                    case >= VidDataType.ChildCounts and < VidDataType.Damage:
                        script.PushNumber(definition.ChildCounts[type - (int)VidDataType.ChildCounts]);
                        break;
                    case VidDataType.Damage:
                        script.PushNumber(definition.Damage);
                        break;
                    case VidDataType.RecolorUnit:
                        script.PushNumber(Sum(definition.Recolored));
                        break;
                    case >= VidDataType.RecolorUnit0 and <= VidDataType.RecolorUnit3:
                        script.PushNumber(definition.Recolored[type - (int)VidDataType.RecolorUnit0]);
                        break;
                    case VidDataType.Name:
                        script.PushText(GameTextEncoding.Cyrillic.GetBytes(Definitions.GetName(definition)));
                        break;
                    case VidDataType.SpriteType:
                        script.PushNumber((int)definition.Object.SpriteType.ToOldSpriteType());
                        break;
                    case VidDataType.Directions:
                        script.PushNumber(definition.Object.Directions);
                        break;
                    case VidDataType.Hide:
                        script.PushNumber(definition.IsHidden ? 1 : 0);
                        break;
                    case VidDataType.FrameSpeed:
                        script.PushNumber((short)definition.SpriteSpeed);
                        break;
                }

                break;
            }
            case ScriptFunction.SetVidData:
            {
                int value = script.PopNumber();
                int type = script.PopNumber();
                var definition = Definitions.Find((KnownObject)script.PopNumber());
                if (definition is null)
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
                            if (entry.Sprite is not null && entry.Sprite.Definition == definition)
                            {
                                entry.Sprite.MaxSpeed = definition.Speed;
                            }
                        }

                        break;
                    case VidDataType.Lifetime:
                        if (definition.Id != 0)
                        {
                            definition.Lifetime = value;
                            definition.HasKeyframes = true;
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
                        definition.LinkDefinition = Definitions.Find((KnownObject)value);
                        break;
                    case >= VidDataType.Child and < VidDataType.ChildCounts:
                    {
                        var child = Definitions.Find((KnownObject)Math.Abs(value));
                        if (value == 0 || child is not null)
                        {
                            definition.Children[type - (int)VidDataType.Child] = value;
                        }

                        if (child is not null && child.Object.Properties.HasFlag(ObjectProperties.BirthAsSmoke))
                        {
                            definition.HasKeyframes = true;
                        }

                        break;
                    }
                    case >= VidDataType.ChildCounts and < VidDataType.Damage:
                        definition.ChildCounts[type - (int)VidDataType.ChildCounts] = value;
                        break;
                    case VidDataType.Damage:
                        definition.Damage = value;
                        break;
                    case VidDataType.Hide:
                        for (var hidden = definition; hidden is not null; hidden = hidden.LinkDefinition)
                        {
                            hidden.IsHidden = value != 0;
                        }

                        break;
                    case VidDataType.FrameSpeed:
                        definition.SetFrameSpeed((short)value);
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

            case ScriptFunction.MenuFind:
                PushSprite(script, MenuFind(script));
                break;
            // TODO: Port loading menu files
            case ScriptFunction.MenuLoad:
                script.Pop();
                break;
            case ScriptFunction.MenuRelease:
                if (script.PopText().IsEmpty)
                {
                    ReleaseMenuSprites();
                }

                break;
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
                int x = script.PopNumber();
                int direction = script.PopNumber();
                int id = script.PopNumber();
                var definition = Definitions.Find((KnownObject)id);
                if (definition is null)
                {
                    LogInvalidDefinition("for MenuCreate"u8, id);
                    script.PushNumber(0);
                    break;
                }

                var heading = new Angle(unchecked((byte)((direction << 8) / definition.Layout.Directions)));
                PushSprite(script, CreateSprite(definition, new Vector3(x, y, z), heading));
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
            case ScriptFunction.SetGraphDetail:
                script.Pop();
                break;
            case ScriptFunction.MessageText:
            {
                int y = script.PopNumber();
                var target = new Vector2(script.PopNumber(), y);
                _players[ControlledPlayer].Messages.Add(script.PopText(), target);
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
            case ScriptFunction.SetWind:
                PopValues(script, 2);
                break;
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
                SetGamma(Gamma.FromScript((uint)script.PopNumber()));
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
                if (!IsPlayingDemo)
                {
                    script.Pop();
                    Session.RecordsNextMap = true;
                }

                break;
            case ScriptFunction.GetString:
            {
                var keyword = script.PopText();
                script.PushText(ProfileFile.GetString(_fileSystem, StringsFile, script.PopText().Span, keyword.Span));
                break;
            }
            case ScriptFunction.Save:
            case ScriptFunction.Pause:
            case ScriptFunction.Exec:
            case ScriptFunction.FClose:
                script.Pop();
                break;
            case ScriptFunction.GetPreviousMapName:
            case ScriptFunction.GetAsProtectUserName:
            case ScriptFunction.GetMapName:
                script.PushText(ReadOnlyMemory<byte>.Empty);
                break;
            case ScriptFunction.Crc32:
                script.PushNumber(ComputeChecksum(script.PopText().Span));
                break;
            case ScriptFunction.Printf:
                script.PushText(Printf(script));
                break;
            case ScriptFunction.ToLower:
                script.PushText(ChangeCase(script.PopText().Span, (byte)'A', (byte)'Z', 'a' - 'A'));
                break;
            case ScriptFunction.ToUpper:
                script.PushText(ChangeCase(script.PopText().Span, (byte)'a', (byte)'z', 'A' - 'a'));
                break;
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

        sprite.ActionStack.Insert(0, order);
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

    [EngineRoutine(0x0042da30)]
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

            case ActionCode.GetTextDesc when sprite.Text is not null:
                script.PushText(sprite.Text.Description);
                return;
            case ActionCode.GetText when sprite.Text is not null:
                script.PushText(sprite.Text.Shown);
                return;

            // TODO: Port the trains
            case ActionCode.FirstEngine or ActionCode.LastEngine or ActionCode.NextEngine:
                PushSprite(script, null);
                return;
        }

        script.PushNumber(Action(sprite, code, first, second, third));
    }

    // TODO: Port the frame capture of the alpha appearance
    [EngineRoutine(0x00402690)]
    private void ExecEffect(int effect, int first, int second, int duration)
    {
        if (effect is < 0 or >= EffectCount)
            return;

        var screenEffect = (ScreenEffect)effect;

        if (screenEffect is ScreenEffect.Fade or ScreenEffect.FadeIn or ScreenEffect.FadeOut)
        {
            _effectStarts[(int)ScreenEffect.FadeOut] = 0;
            _effectStarts[(int)ScreenEffect.FadeIn] = 0;
            _effectStarts[(int)ScreenEffect.Fade] = 0;
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
        else if (screenEffect == ScreenEffect.Scroll)
        {
            _scrollFrom = new Vector2(Camera.Y);
        }
        else if (screenEffect == ScreenEffect.ShiftGamma)
        {
            _gammaFrom = Gamma;
        }
    }

    [EngineRoutine(0x004311a0)]
    private void UpdateEffects(bool draw)
    {
        uint now = _clock.WallTime;
        ExpireEffect(ScreenEffect.AlphaAppear, now);

        const int scroll = (int)ScreenEffect.Scroll;
        if (_effectStarts[scroll] != 0)
        {
            uint elapsed = now - _effectStarts[scroll];
            int length = _effectDurations[scroll];
            if ((uint)length < elapsed)
            {
                SetPosition(new Vector2(_effectFirst[scroll], _effectSecond[scroll]), CameraMove.None);
                _effectStarts[scroll] = 0;
            }
            else
            {
                float x = (float)(((double)_effectFirst[scroll] - _scrollFrom.X) * (int)elapsed / length + _scrollFrom.X);
                float y = (float)(((double)_effectSecond[scroll] - _scrollFrom.Y) * (int)elapsed / length + _scrollFrom.Y);
                SetPosition(new Vector2(x, y), CameraMove.None);
            }
        }

        ExpireEffect(ScreenEffect.Nuke, now);
        ExpireEffect(ScreenEffect.Fade, now);
        ExpireEffect(ScreenEffect.FadeIn, now);
        ExpireEffect(ScreenEffect.FadeOut, now);

        const int shift = (int)ScreenEffect.ShiftGamma;
        if (_effectStarts[shift] == 0)
            return;

        uint shifted = now - _effectStarts[shift];
        var target = Gamma.FromScript((uint)_effectFirst[shift]);
        if (shifted >= (uint)_effectDurations[shift])
        {
            SetGamma(target);
            _effectStarts[shift] = 0;

            return;
        }

        if (!draw)
            return;

        double share = (double)shifted / _effectDurations[shift];
        uint negative = 0, positive = 0;
        for (int channel = GammaChannelBits * (GammaChannels - 1); channel >= 0; channel -= GammaChannelBits)
        {
            int from = GetGammaChannel(_gammaFrom, channel);
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

        SetGamma(new Gamma(negative, positive));
    }

    private void ExpireEffect(ScreenEffect effect, uint now)
    {
        int index = (int)effect;

        if (_effectStarts[index] != 0 && now - _effectStarts[index] >= (uint)_effectDurations[index])
        {
            _effectStarts[index] = 0;
        }
    }

    private static int GetGammaChannel(Gamma gamma, int shift) => gamma.GetNegative(shift) != 0 ? -gamma.GetNegative(shift) : gamma.GetPositive(shift);

    [EngineRoutine(0x00431c50)]
    private void SetGamma(Gamma gamma) => Gamma = gamma;

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

    [EngineRoutine(0x00431c00)]
    private int GetEffectState(int effect)
    {
        if (effect is <= (int)ScreenEffect.None or >= EffectCount || _effectStarts[effect] == 0)
            return -1;

        return (int)(unchecked(_clock.WallTime * 100 - _effectStarts[effect] * 100) / (uint)_effectDurations[effect]);
    }
}
