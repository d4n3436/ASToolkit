using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Map : IScriptFunctionHost
{
    private const int AnyDirection = 999999;
    private const int Base64ChunkSize = 0x39;
    private const uint ChecksumPolynomial = 0xEDB88320;
    private const int AllArmies = 0xff;
    private const int FirstMouseClick = 0x400;
    private const int CursorOff = -1;
    private const int HardwareCursor = 0x100;
    private const int SoftwareCursor = 0x101;
    private const int SecondMouseClick = 0x800;
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
    /// Gets the registry of the level script.
    /// </summary>
    public ScriptRegistry Registry { get; }

    /// <summary>
    /// Gets the session the map belongs to.
    /// </summary>
    public GameSession Session { get; }

    internal Sprite? MenuLeftClick => _menuClicks.HasFlag(MenuClicks.Left) ? MenuCurrent : null;

    private static ReadOnlySpan<byte> Base64Alphabet => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"u8;

    private Sprite? MenuRightClick => _menuClicks.HasFlag(MenuClicks.Right) ? MenuCurrent : null;

    private IGameFileSystem ScriptFiles => field ??= _fileSystem as MemoryWriteOverlay ?? new MemoryWriteOverlay(_fileSystem);

    /// <summary>
    /// Returns the script handle of a sprite, allocating one if the sprite doesn't have one yet.
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
            Handles[sprite.Handle] = sprite;
        }

        return sprite.Handle;
    }

    /// <summary>
    /// Returns the sprite a script handle refers to.
    /// </summary>
    /// <param name="handle">The handle.</param>
    /// <returns>The sprite, or null when the handle is stale.</returns>
    public Sprite? FindSprite(int handle) => Handles.TryGetValue(handle, out var sprite) && sprite.Handle == handle ? sprite : null;

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
                ExecCreateSprite(script);
                break;
            case ScriptFunction.Destroy:
                Destroy(script);
                break;
            case ScriptFunction.Flagman:
                PushSprite(script, GetFlagman(script.PopNumber()));
                break;
            case ScriptFunction.FirstUnit:
                PushSprite(script, FirstUnit(script));
                break;
            case ScriptFunction.NextUnit:
                PushSprite(script, FindUnitOfMask(GetNextUnit()));
                break;
            case ScriptFunction.FirstInBox:
                PushSprite(script, FirstInBox(script));
                break;
            case ScriptFunction.NextInBox:
                PushSprite(script, Collector.NextHashInBox());
                break;
            case ScriptFunction.DirectionTo:
                script.PushNumber(DirectionTo(script));
                break;
            case ScriptFunction.ChangeZUnit:
                ChangeZUnit(script);
                break;
            case ScriptFunction.GetGroundZ:
                script.PushNumber((int)Movement.GetHeight(PopPoint(script)));
                break;
            case ScriptFunction.CanPlace:
                PushSprite(script, CanPlace(script));
                break;
            case ScriptFunction.MapSizeX:
                PushNumber((int)Movement.MapSize.Width);
                break;
            case ScriptFunction.MapSizeY:
                PushNumber((int)Movement.MapSize.Height);
                break;
            case ScriptFunction.Genocide:
                Genocide(script);
                break;
            case ScriptFunction.ReplaceUnit:
                ReplaceUnit(script);
                break;
            case ScriptFunction.Action:
                ExecAction(script);
                break;
            case ScriptFunction.SizeTo:
                script.PushNumber(SizeTo(script));
                break;
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
                RequestLoad(script.PopText().Span);
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
                Input.PostMessage(WindowMessageId.Close, 0, 0);
                break;
            case ScriptFunction.Effect:
                Effect(script);
                break;
            case ScriptFunction.SetEnvironment:
                Session.Graphics.SetEnvironment((MapEnvironment)script.PopNumber());
                break;
            case ScriptFunction.GetEffectState:
                script.PushNumber(Session.Graphics.GetEffectState(this, script.PopNumber()));
                break;
            case ScriptFunction.Charat:
                script.PushNumber(Charat(script));
                break;
            case ScriptFunction.Random:
                script.PushNumber(NextRandom(script.PopNumber()));
                break;
            case ScriptFunction.GetTime:
                script.PushNumber(GetTime());
                break;
            case ScriptFunction.GetVidData:
                GetVidData(script);
                break;
            case ScriptFunction.SetVidData:
                SetVidData(script);
                break;
            case ScriptFunction.Log:
                Session.Logger.Write(script.PopText().Span);
                break;
            case ScriptFunction.ReloadVid:
                Definitions.ReloadRecords(Session.Logger, Sound);
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
                MenuRelease(script);
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
                MenuCreate(script);
                break;
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
                MessageText(script);
                break;
            case ScriptFunction.SetPosition:
                SetPosition(PopPoint(script), CameraMove.None);
                break;
            case ScriptFunction.SetMouseClick:
                SetMouseClick(script.PopNumber(), script.PopNumber());
                break;
            case ScriptFunction.SetCursorPosition:
                SetCursorPosition(script);
                break;
            case ScriptFunction.SetWind:
                SetWind(script);
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
                script.PushNumber(ToScreenY(script));
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
                PlaySfxFromCoor(script);
                break;
            case ScriptFunction.PlayMusicFile:
                PlayMusicFile(script);
                break;
            case ScriptFunction.IsPlayMusic:
                script.PushNumber(Sound.IsPlayMusic(_clock.WallTime) ? 1 : 0);
                break;

            case ScriptFunction.PlayMovie:
                Session.Graphics.PlayMovie(_fileSystem, script.PopText().Span, Session.Logger);
                break;
            case ScriptFunction.IsPlayMovie:
                script.PushNumber(Session.Graphics.IsMovieOpen ? 1 : 0);
                break;
            case ScriptFunction.StopMovie:
                Session.Graphics.StopMovie();
                break;

            case ScriptFunction.SetGamma:
                Session.Graphics.SetGamma(Gamma.FromScript((uint)script.PopNumber()), Definitions);
                break;
            case ScriptFunction.CountGamma:
                script.PushNumber(CountGamma(script));
                break;
            case ScriptFunction.GetGamma:
                script.PushNumber((int)Gamma.ToScript());
                break;

            // TODO: Port the saves
            case ScriptFunction.GetCommands:
                ExecGetCommands(script);
                break;
            case ScriptFunction.SetCommands:
                ExecSetCommands(script);
                break;
            case ScriptFunction.FRead:
                FRead();
                break;
            case ScriptFunction.FWrite:
                FWrite();
                break;
            case ScriptFunction.SaveDemo:
                if (!IsPlayingDemo)
                {
                    script.Pop();
                    Session.IsRecordingNextMap = true;
                }

                break;
            case ScriptFunction.GetString:
                GetString(script);
                break;
            case ScriptFunction.Pause:
                if (script.PopNumber() != 0)
                {
                    Pause();
                    CursorSprite.ShowCursor(0);
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
                Session.Logger.Write($"Exec '{GameLogger.Decode(script.PopText().Span)}'");
                break;
            case ScriptFunction.FClose:
                FClose();
                break;
            case ScriptFunction.GetPreviousMapName:
                script.PushText(Session.PreviousMapName);
                break;
            case ScriptFunction.GetAsProtectUserName:
                script.PushText(default);
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
                ToLower();
                break;
            case ScriptFunction.ToUpper:
                ToUpper();
                break;
            case ScriptFunction.FOpen:
                PushNumber(FOpen());
                break;
            case ScriptFunction.FCreate:
                PushNumber(FCreate());
                break;
            case ScriptFunction.FEof:
                PushNumber(FEof());
                break;
            case ScriptFunction.GetReg:
                GetReg();
                break;
            case ScriptFunction.SetReg:
                SetReg();
                break;
            case ScriptFunction.DelReg:
                DelReg();
                break;
            case ScriptFunction.GetDefaultRegPath:
                PushText(GameTextEncoding.Cyrillic.GetBytes(Registry.DefaultPath));
                break;
            case ScriptFunction.ToBase64:
                ExecToBase64();
                break;

            case ScriptFunction.GetSprite:
                PushSprite(script, ExecGetSprite(script));
                break;
            case ScriptFunction.GetSpriteScr:
                PushSprite(script, ExecGetSpriteScr(script));
                break;
            case ScriptFunction.FindNearestSprite:
                PushSprite(script, ExecFindNearestSprite(script));
                break;
            case ScriptFunction.FirstSprite:
                _spriteLayer = 0;
                _spriteIndex = _updates.GetLayer(0).Count;
                PushSprite(script, FindListedSprite());
                break;
            case ScriptFunction.NextSprite:
                PushSprite(script, GetNextSprite());
                break;
            case ScriptFunction.AddCommand:
                ExecAddCommand(script);
                break;
            case ScriptFunction.SetFlagman:
                ExecSetFlagman(script);
                break;
            case ScriptFunction.AddUnitLimit:
                AddUnitLimit(script);
                break;
            case ScriptFunction.SetEnemyCanAttackNeutralTrains:
                SetMonstersAttackNeutralTrains(PopNumber() != 0);
                break;
            case ScriptFunction.SetMoney:
                SetMoney();
                break;
            case ScriptFunction.GetMoney:
                PushNumber(GetPlayer(PopNumber()).Money);
                break;

            // TODO: Port the trains and the rail map
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
                GetPlayer(1).IsCleverEnemyAttack = PopNumber() != 0;
                break;
            default:
                Session.Logger.Write($"!!!ERROR!!!LOGIC: Unknown extern Function {function}");
                break;
        }
    }

    [EngineRoutine(0x0043a330)]
    internal int PopNumber() => Script.PopNumber();

    [EngineRoutine(0x0043a390)]
    internal ReadOnlyMemory<byte> PopText() => Script.PopText();

    [EngineRoutine(0x0043a470)]
    internal int PopHandle() => Script.PopHandle();

    [EngineRoutine(0x0043a640)]
    internal void PushText(ReadOnlyMemory<byte> text) => Script.PushText(text);

    [EngineRoutine(0x0043a5b0)]
    internal void PushNumber(int value) => Script.PushNumber(value);

    [EngineRoutine(0x0043a720)]
    internal void PushSprite(int value) => Script.PushSprite(value);

    [EngineRoutine(0x0043deb0)]
    internal void RemoveSprite(List<Sprite> sprites, Sprite sprite)
    {
        int i = sprites.LastIndexOf(sprite);
        if (i < 0)
            return;

        int last = sprites.Count - 1;
        sprites[i] = sprites[last];
        sprites.RemoveAt(last);
        Release(sprite);
    }

    [EngineRoutine(0x0040fc40)]
    internal Sprite? GetSpriteScr(SpriteSearch search, Vector2 point)
    {
        if (!TryGetSearchedTypes(ref search, out var types, out var armies, out var definition))
            return null;

        var query = new ScreenSpriteQuery(new SpriteFilter(search, types, armies), point);
        var reach = new Vector2(ScreenSpriteReach);
        float ground = Movement.GetHeight(point);
        ScanSprites(ref query, search, types, definition, point - reach, new Vector2(point.X + ScreenSpriteReach, ground + point.Y + ScreenSpriteReach), false);

        return query.Found;
    }

    [EngineRoutine(0x00405a90)]
    internal SpriteDefinition LookupDefinition(int id) => Definitions.Find((KnownObject)id) ?? Definitions.Empty;

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

    private static bool IsMenuClass(SpriteDefinition definition) => definition.Class is SpriteClass.Frame or SpriteClass.Text;

    private static bool IsMenuMatch(Sprite sprite, SpriteDefinition definition, int direction) =>
        sprite.Definition == definition && (direction == AnyDirection || definition.GetDirectionIndex(sprite.Direction) == direction);

    private static string ReadText(ReadOnlyMemory<byte> text) => GameTextEncoding.Cyrillic.GetString(text.Span);

    [EngineRoutine(0x0043a950)]
    private static int BlendGamma(int first, int last, int share) =>
        (BlendGammaChannel(first & byte.MaxValue, last & byte.MaxValue, share) & byte.MaxValue) |
        (BlendGammaChannel(first >> Gamma.ChannelBits & byte.MaxValue, last >> Gamma.ChannelBits & byte.MaxValue, share) & byte.MaxValue) << Gamma.ChannelBits |
        (BlendGammaChannel(first >> 2 * Gamma.ChannelBits & byte.MaxValue, last >> 2 * Gamma.ChannelBits & byte.MaxValue, share) & byte.MaxValue) << 2 * Gamma.ChannelBits;

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

    private static byte[] FormatPrintf(ReadOnlySpan<byte> format, bool isText, ReadOnlySpan<byte> text, int number)
    {
        var output = new List<byte>(format.Length);
        bool isConsumed = false;
        int i = 0;
        while (i < format.Length)
        {
            byte current = format[i++];
            if (current != (byte)'%')
            {
                output.Add(current);
                continue;
            }

            int start = i - 1;
            bool isZeroPadded = i < format.Length && format[i] == (byte)'0';
            if (isZeroPadded)
            {
                i++;
            }

            int width = 0;
            while (i < format.Length && format[i] is >= (byte)'0' and <= (byte)'9')
            {
                width = width * 10 + format[i++] - '0';
            }

            byte conversion = i < format.Length ? format[i++] : (byte)0;
            bool isWidthUsed = isZeroPadded || width != 0;
            if (isConsumed || conversion == (byte)'s' && (isWidthUsed || !isText) || conversion == (byte)'c' && (isWidthUsed || isText) || conversion == (byte)'i' && isText ||
                conversion is not ((byte)'s' or (byte)'c' or (byte)'i'))
            {
                throw new NotSupportedException($"The Printf conversion '{Encoding.ASCII.GetString(format[start..i])}' is not supported.");
            }

            isConsumed = true;
            if (conversion == (byte)'s')
            {
                foreach (byte b in text)
                {
                    output.Add(b);
                }
            }
            else if (conversion == (byte)'c')
            {
                output.Add((byte)number);
            }
            else
            {
                string digits = number.ToString(CultureInfo.InvariantCulture);
                if (isZeroPadded)
                {
                    digits = number < 0 ? "-" + digits[1..].PadLeft(Math.Max(width - 1, 0), '0') : digits.PadLeft(width, '0');
                }
                else
                {
                    digits = digits.PadLeft(width);
                }

                output.AddRange(Encoding.ASCII.GetBytes(digits));
            }
        }

        return output.ToArray();
    }

    private static Vector2 PopPoint(Script script)
    {
        int y = script.PopNumber();
        return new Vector2(script.PopNumber(), y);
    }

    private static Vector3 PopPosition(Script script)
    {
        int z = script.PopNumber();
        int y = script.PopNumber();
        return new Vector3(script.PopNumber(), y, z);
    }

    [EngineRoutine(0x004066e0)]
    private ReadOnlyMemory<byte> Printf()
    {
        bool isText = Script.IsTextOnTop;
        var text = isText ? PopText() : default;
        int number = isText ? 0 : PopNumber();
        var format = PopText();

        return FormatPrintf(format.Span, isText, text.Span, number);
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
        Session.Logger.Write($"!!!ERROR!!!SCRIPT: Invalid nvid {GameLogger.Decode(function)} {id}");

    [EngineRoutine(0x0043e140)]
    private bool ProcessMenuInput(PlayerInput input)
    {
        MenuCurrent = null;
        _menuClicks = MenuClicks.None;
        foreach (var sprite in MenuList)
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
                MenuCurrent is not null && sprite.Position.Z <= MenuCurrent.Position.Z;
            if (isMissed)
            {
                sprite.ChangeAnimation((SpriteAnimation)((int)sprite.Animation & 1));
            }
            else
            {
                MenuCurrent = sprite;
            }
        }

        if (MenuCurrent is null)
            return false;

        if (input.Flags.HasFlag(InputFlags.LeftClick))
        {
            _menuClicks |= MenuClicks.Left;
            input.ClearLeftClick();
            MenuCurrent.ChangeAnimation((SpriteAnimation)((int)MenuCurrent.Animation & 1 | (int)MenuAnimation.PressDown));

            return true;
        }

        if (input.Flags.HasFlag(InputFlags.RightClick))
        {
            _menuClicks |= MenuClicks.Right;
            input.ClearRightClick();
        }

        int current = (int)MenuCurrent.Animation;
        if ((current & ~1) != (int)MenuAnimation.PressDown)
        {
            MenuCurrent.ChangeAnimation((SpriteAnimation)(current & 1 | (int)MenuAnimation.SelectDown));
        }

        return false;
    }

    [EngineRoutine(0x0043e880)]
    private int GetMenuIdUnderCursor() => MenuCurrent is null ? 0 : (int)MenuCurrent.Definition.Id;

    [EngineRoutine(0x0043e8a0)]
    private int GetMenuDirectionUnderCursor() =>
        MenuCurrent is null ? 0 : MenuCurrent.Definition.GetDirectionIndex(MenuCurrent.Direction);

    private void SetMouseClick(int key, int button)
    {
        if (button == FirstMouseClick)
        {
            _controls.First = new ControlBinding((VirtualKey)key, (VirtualKey)key);
        }
        else if (button == SecondMouseClick)
        {
            _controls.Second = new ControlBinding((VirtualKey)key, (VirtualKey)key);
        }
    }

    private void SetCursor(int type)
    {
        switch (type)
        {
            case CursorOff:
                CursorSprite.TurnCursorOff();
                break;
            case HardwareCursor:
                CursorSprite.UseHardwareCursor(Definitions.Empty, _fileSystem);
                break;
            case SoftwareCursor:
                CursorSprite.UseSoftwareCursor(Definitions.Find(KnownObject.Cursor) ?? Definitions.Empty, _fileSystem);
                break;
            default:
                if (!CursorSprite.IsCursorLoaded)
                {
                    CursorSprite.LoadCursor(_fileSystem);
                }

                CursorSprite.ShowCursor((SpriteAnimation)type);
                break;
        }
    }

    [EngineRoutine(0x0043dfe0)]
    private void DeleteAllMenuSprites()
    {
        for (int i = 0; i < MenuList.Count; i++)
        {
            for (int j = MenuList.Count - 1; i < j; j--)
            {
                if (MenuList[i] != MenuList[j])
                    continue;

                Release(MenuList[j]);
                if (j < MenuList.Count)
                {
                    int last = MenuList.Count - 1;
                    MenuList[j] = MenuList[last];
                    MenuList.RemoveAt(last);
                }
            }
        }

        for (int i = MenuList.Count - 1; i >= 0; i--)
        {
            if (i < MenuList.Count)
            {
                DeleteMenuSpriteAt(i);
            }
        }
    }

    private Sprite? MenuFind(Script script)
    {
        int direction = script.PopNumber();
        if (!TryLookupDefinition(script.PopNumber(), "for MenuFind"u8, out var definition))
            return null;

        if (Sum(definition.Counts) == 0)
            return null;

        foreach (var sprite in MenuList)
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
        if (!TryLookupDefinition(script.PopNumber(), "for MenuAction"u8, out var definition))
            return;

        for (int i = 0; i < MenuList.Count; i++)
        {
            var sprite = MenuList[i];

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
        return FindSprite(script.PopHandle());
    }

    private Sprite? PopSprite()
    {
        return FindSprite(PopHandle());
    }

    private bool TryLookupDefinition(int id, ReadOnlySpan<byte> function, out SpriteDefinition definition)
    {
        definition = LookupDefinition(id);
        if (definition != Definitions.Empty)
            return true;

        LogInvalidDefinition(function, id);
        return false;
    }

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

        if (definition is not null && (definition.Class == SpriteClass.Frame || (textInMenu && definition.Class == SpriteClass.Text)))
        {
            for (int i = MenuList.Count - 1; i >= 0; i--)
            {
                query.ConsiderMenu(MenuList[i]);
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
        while (sprite is null && _spriteLayer < LayerCount - 1)
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

            case ActionCode.GetTextDesc when sprite is Text text:
                script.PushText(text.Description);
                return;

            // TODO: Port the trains
            case ActionCode.FirstEngine or ActionCode.LastEngine or ActionCode.NextEngine:
                PushSprite(script, null);
                return;
        }

        script.PushNumber(Action(sprite, code, first, second, third));
    }

    private void ExecCreateSprite(Script script)
    {
        var parent = PopSprite(script);
        var direction = new Angle((byte)script.PopNumber());
        var position = PopPosition(script);
        int id = script.PopNumber();
        if (!TryLookupDefinition(id, "for CreateSprite()"u8, out var definition))
        {
            script.PushNumber(0);
            return;
        }

        PushSprite(script, AddSprite(definition, position, direction, parent));
    }

    private void Destroy(Script script)
    {
        var sprite = PopSprite(script);
        if (sprite is not null)
        {
            DeleteSprite(sprite);
        }
    }

    private Sprite? FirstUnit(Script script)
    {
        _unitMask = (OldSpriteType)script.PopNumber();
        var sprites = Collector.Listed;
        _unitIndex = sprites.Length - 1;
        return sprites.IsEmpty ? null : FindUnitOfMask(sprites[_unitIndex]);
    }

    private Sprite? FirstInBox(Script script)
    {
        var max = PopPoint(script);
        return Collector.First(PopPoint(script), max);
    }

    private int DirectionTo(Script script)
    {
        var point = PopPoint(script);
        var sprite = PopSprite(script);
        return sprite is null ? 0 : new Angle(point - sprite.Position.AsVector2()).Value;
    }

    private void ChangeZUnit(Script script)
    {
        int z = script.PopNumber();
        int id = script.PopNumber();
        var definition = Definitions.Find((KnownObject)id);
        if (definition is null)
        {
            LogInvalidDefinition("for ChangeZUnit"u8, id);
            return;
        }

        foreach (var sprite in EnumerateSprites(definition))
        {
            sprite.ChangeCoor(sprite.Position with { Z = z });
        }
    }

    private Sprite? CanPlace(Script script)
    {
        var position = PopPosition(script);
        int id = script.PopNumber();
        var definition = Definitions.Find((KnownObject)id);
        if (definition is null)
        {
            LogInvalidDefinition("for CanPlace"u8, id);
        }

        return Collector.CanPlace(definition ?? Definitions.Empty, position);
    }

    private void Genocide(Script script)
    {
        var definition = PopDefinition(script);
        if (definition == Definitions.Empty)
            return;

        foreach (var sprite in EnumerateSprites(definition))
        {
            DeleteSprite(sprite);
        }
    }

    private void ReplaceUnit(Script script)
    {
        var replacement = PopDefinition(script);
        var definition = PopDefinition(script);
        if (definition == Definitions.Empty || replacement == Definitions.Empty)
            return;

        foreach (var sprite in EnumerateSprites(definition))
        {
            AddSprite(replacement, sprite.Position, sprite.Direction);
            DeleteSprite(sprite);
        }
    }

    private int SizeTo(Script script)
    {
        var point = PopPoint(script);
        var sprite = PopSprite(script);
        return sprite is null ? NoUnitDistance : (int)sprite.Position.AsVector2().MeasureOctagonalDistance(point);
    }

    private void Effect(Script script)
    {
        int duration = script.PopNumber();
        int second = script.PopNumber();
        int first = script.PopNumber();
        Session.Graphics.ExecEffect(this, script.PopNumber(), first, second, duration);
    }

    private int Charat(Script script)
    {
        int index = script.PopNumber();
        var text = script.PopText().Span;
        return index == text.Length ? 0 : (sbyte)text[index];
    }

    private int NextRandom(int max) => _rng.Next() % (max + 1);

    private void GetVidData(Script script)
    {
        int type = PopNumber();
        var definition = PopDefinition(script);
        if (definition == Definitions.Empty)
        {
            PushNumber(0);
            return;
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
                PushNumber((int)definition.Class);
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
    }

    private void SetVidData(Script script)
    {
        int value = PopNumber();
        int type = PopNumber();
        var definition = PopDefinition(script);
        if (definition == Definitions.Empty)
            return;

        switch ((VidDataType)type)
        {
            case VidDataType.MaxHp:
                definition.MaxHp = value;
                break;
            case >= VidDataType.Gamma and < VidDataType.Properties:
                SetVidGamma(definition, type - (int)VidDataType.Gamma, value);
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
                SetVidSpeed(definition, value);
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
                ExchangeVid(definition, value);
                break;
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
                SetVidChild(definition, type - (int)VidDataType.Child, value);
                break;
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
    }

    private void SetVidGamma(SpriteDefinition definition, int army, int value)
    {
        var gamma = Gamma.FromScript((uint)value);
        definition.Gammas[army] = gamma;
        definition.SetGamma(gamma, army, Session.Graphics.Gamma, Session.Logger);
    }

    private void SetVidSpeed(SpriteDefinition definition, int value)
    {
        definition.Speed = value == SpriteDefinition.Unlimited ? SpriteDefinition.Unlimited : value * SpriteDefinition.PerMillisecond;
        foreach (var entry in _updates.GetLayer(definition.Layer))
        {
            if (entry is not null && entry.Definition == definition && entry.HasExtra)
            {
                entry.MaxSpeed = definition.Speed;
            }
        }
    }

    private void ExchangeVid(SpriteDefinition definition, int value)
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
    }

    private void SetVidChild(SpriteDefinition definition, int state, int value)
    {
        if (value == 0)
        {
            definition.Children[state] = 0;
            return;
        }

        int sign = value >> 31;
        var child = Definitions.Find((KnownObject)unchecked((value ^ sign) - sign));
        if (child is null)
        {
            LogMapError(LogError.Invalid, "SetVid child"u8, value);
            return;
        }

        definition.Children[state] = value;

        if (child.IsBirthAsSmoke())
        {
            definition.HasTimers = true;
        }
    }

    private void MenuRelease(Script script)
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
    }

    private void MenuCreate(Script script)
    {
        int z = script.PopNumber();
        int y = script.PopNumber() + z;
        var position = new Vector3(script.PopNumber(), y, z);
        int direction = script.PopNumber();
        if (!TryLookupDefinition(script.PopNumber(), "for MenuCreate"u8, out var definition))
        {
            script.PushNumber(0);
            return;
        }

        var heading = new Angle(unchecked((byte)((direction << 8) / definition.Directions)));
        PushSprite(script, AddSprite(definition, position, heading));
    }

    private void MessageText(Script script)
    {
        var target = PopPoint(script);
        _players[ControlledPlayer].Messages.Put(script.PopText(), target);
    }

    private void SetCursorPosition(Script script)
    {
        int y = script.PopNumber();
        var target = new Point(script.PopNumber(), y);
        Action(CursorSprite, ActionCode.ChangeCoor, target.X, target.Y, 0);
    }

    private void SetWind(Script script)
    {
        var direction = new Angle((byte)script.PopNumber());
        Session.Graphics.SetWind(PopNumber(), direction);
    }

    private int ToScreenY(Script script)
    {
        int z = script.PopNumber();
        int y = script.PopNumber();
        return (int)((float)y - z - Camera.Y);
    }

    private void PlaySfxFromCoor(Script script)
    {
        var point = PopPoint(script);
        Sound.PlayAt(script.PopNumber(), point - Camera - Session.ScreenSize.ToVector2() * HalfShare);
    }

    private void PlayMusicFile(Script script)
    {
        int loop = script.PopNumber();
        Sound.PlayMusicFile(script.PopText().Span, loop, _clock.WallTime);
    }

    private int CountGamma(Script script)
    {
        int share = script.PopNumber();
        int last = script.PopNumber();
        return BlendGamma(script.PopNumber(), last, share);
    }

    private void ExecGetCommands(Script script)
    {
        var sprite = PopSprite(script);
        if (sprite is null)
        {
            script.PushText(default);
            return;
        }

        var items = GetTextItems(sprite);
        var commands = GetCommands(sprite);
        byte[] both = new byte[items.Length + commands.Length];
        items.CopyTo(both);
        commands.CopyTo(both.AsMemory(items.Length));
        script.PushText(both);
    }

    private void ExecSetCommands(Script script)
    {
        var text = script.PopText();
        var sprite = PopSprite(script);
        if (sprite is null)
            return;

        SetTextItems(sprite, text);
        if (text.Span.IndexOf(ItemsEnd) >= 0)
        {
            text = GetAfter(text, ItemsEnd);
        }

        SetCommands(sprite, text);
    }

    private void FRead()
    {
        var file = PopScriptFile();
        var line = IsPlayingDemo || file is null ? default : file.ReadLine();
        // TODO: Port the demo stream strings of FRead and the shared text buffer a null handle pushes; recording stores each line and playback reads it back
        PushText(line);
    }

    private void FWrite()
    {
        var text = PopText();
        var file = PopScriptFile();
        if (file is null)
            return;

        file.WriteText(text.Span);
        file.SeekBack();
        file.WriteLineFeed();
    }

    private void GetString(Script script)
    {
        var keyword = script.PopText();
        script.PushText(ProfileFile.GetString(_fileSystem, StringsFile, script.PopText().Span, keyword.Span));
    }

    private void FClose()
    {
        int handle = PopNumber();
        if (_scriptFiles.Remove(handle, out var file))
        {
            file.Dispose();
        }
    }

    private void ToLower()
    {
        byte[] text = CRuntime.GetString(PopText().Span).ToArray();
        CRuntime.ToLowerInPlace(text);
        PushText(text);
    }

    private void ToUpper()
    {
        byte[] text = CRuntime.GetString(PopText().Span).ToArray();
        CRuntime.ToUpperInPlace(text);
        PushText(text);
    }

    private int FOpen()
    {
        string name = ReadText(PopText());
        if (IsPlayingDemo)
            return 0;

        var file = name.Length != 0 && ScriptFiles.TryRead(name, out var data) ? new ScriptFile(data.Span, null) : null;
        if (file is null)
        {
            LogMapError(LogError.Open, Encoding.ASCII.GetBytes(name), 0);
        }

        return AddScriptFile(file);
    }

    private int FCreate()
    {
        string name = ReadText(PopText());
        if (IsPlayingDemo)
            return 0;

        var writer = name.Length != 0 ? ScriptFiles.OpenWrite(name, false) : null;
        return AddScriptFile(writer is null ? null : new ScriptFile(default, writer));
    }

    private int FEof()
    {
        var file = PopScriptFile();
        return file is null ? 1 : file.IsEnd ? EndOfFileFlag : 0;
    }

    private void GetReg()
    {
        string defaultValue = ReadText(PopText());
        string name = ReadText(PopText());
        PushText(GameTextEncoding.Cyrillic.GetBytes(Registry.Get(ReadText(PopText()), name, defaultValue)));
    }

    private void SetReg()
    {
        string value = ReadText(PopText());
        string name = ReadText(PopText());
        Registry.Set(ReadText(PopText()), name, value);
    }

    private void DelReg()
    {
        string name = ReadText(PopText());
        Registry.Delete(ReadText(PopText()), name);
    }

    private void ExecToBase64()
    {
        int add = PopNumber();
        PushText(ToBase64(PopText().Span, add));
    }

    private Sprite? ExecGetSprite(Script script)
    {
        var previous = PopSprite(script);
        var point = PopPoint(script);
        return GetSprite((SpriteSearch)script.PopNumber(), point, previous);
    }

    private Sprite? ExecGetSpriteScr(Script script)
    {
        var point = PopPoint(script);
        return GetSpriteScr((SpriteSearch)script.PopNumber(), point);
    }

    private Sprite? ExecFindNearestSprite(Script script)
    {
        var previous = PopSprite(script);
        int radius = script.PopNumber();
        var point = PopPoint(script);
        return FindNearestSprite((SpriteSearch)script.PopNumber(), point, radius, previous);
    }

    private void ExecAddCommand(Script script)
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
    }

    private void ExecSetFlagman(Script script)
    {
        var sprite = PopSprite(script);
        SetFlagman(script.PopNumber(), sprite);
    }

    private void AddUnitLimit(Script script)
    {
        int army = PopNumber();
        var definition = PopDefinition(script);
        int value = PopNumber();
        if (definition == Definitions.Empty)
            return;

        if (army == AllArmies)
        {
            definition.MaxUnits = value;
        }
        // TODO: Port AS1's out-of-range write for armies outside 0..3; AS1 writes def+0x398+army*4 into the fields around ArmyMaxUnits
        else if ((uint)army < (uint)definition.ArmyMaxUnits.Length)
        {
            definition.ArmyMaxUnits[army] = value;
        }
    }

    private void SetMoney()
    {
        int money = PopNumber();
        GetPlayer(PopNumber()).Money = money;
    }
}
