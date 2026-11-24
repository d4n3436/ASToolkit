using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Text;
using ASToolkit.Engine.IO;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Map : IScriptFunctionHost
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
    public Registry Registry { get; }

    /// <summary>
    /// Gets the session the map belongs to.
    /// </summary>
    public GameSession Session { get; }

    private static ReadOnlySpan<byte> Base64Alphabet => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"u8;

    private IGameFileSystem ScriptFiles => field ??= FileSystem as MemoryWriteOverlay ?? new MemoryWriteOverlay(FileSystem);

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
                PushSprite(script, Flagman(script.PopNumber()));
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
                script.PushInt(DirectionTo(script));
                break;
            case ScriptFunction.ChangeZUnit:
                ChangeZUnit(script);
                break;
            case ScriptFunction.GetGroundZ:
                script.PushInt((int)GetGroundZ(PopPoint(script)));
                break;
            case ScriptFunction.CanPlace:
                PushSprite(script, CanPlace(script));
                break;
            case ScriptFunction.MapSizeX:
                PushInt((int)Size.Width);
                break;
            case ScriptFunction.MapSizeY:
                PushInt((int)Size.Height);
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
                script.PushInt(SizeTo(script));
                break;
            case ScriptFunction.GetUnitVid:
                script.PushInt((int)(PopSprite(script)?.Vid.Id ?? 0));
                break;
            case ScriptFunction.GetX:
                script.PushInt((int)(PopSprite(script)?.Position.X ?? 0));
                break;
            case ScriptFunction.GetY:
                script.PushInt((int)(PopSprite(script)?.Position.Y ?? 0));
                break;
            case ScriptFunction.GetZ:
                script.PushInt((int)(PopSprite(script)?.Position.Z ?? 0));
                break;
            case ScriptFunction.GetDirection:
                script.PushInt(PopSprite(script)?.Direction.Value ?? 0);
                break;
            case ScriptFunction.GetAnimation:
                script.PushInt((int)(PopSprite(script)?.Animation ?? 0));
                break;
            case ScriptFunction.Load:
                RequestLoad(script.PopText().Span);
                break;
            case ScriptFunction.GetInputX:
                script.PushInt((int)Input.Cursor.X);
                break;
            case ScriptFunction.GetInputY:
                script.PushInt((int)Input.Cursor.Y);
                break;
            case ScriptFunction.GetKey:
                script.PushInt(Input.Key);
                break;
            case ScriptFunction.GetInputState:
                script.PushInt((int)GetInputState());
                break;
            case ScriptFunction.SetScrollType:
                _scrollType = (ScrollType)script.PopNumber();
                break;
            case ScriptFunction.GetScrollType:
                script.PushInt((int)_scrollType);
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
                script.PushInt(Session.Graphics.GetEffectState(this, script.PopNumber()));
                break;
            case ScriptFunction.Charat:
                script.PushInt(Charat(script));
                break;
            case ScriptFunction.Random:
                script.PushInt(NextRandom(script.PopNumber()));
                break;
            case ScriptFunction.GetTime:
                script.PushInt(GetTime());
                break;
            case ScriptFunction.GetVidData:
                GetVidData(script);
                break;
            case ScriptFunction.SetVidData:
                SetVidData(script);
                break;
            case ScriptFunction.Log:
                Session.Error.Write(script.PopText().Span);
                break;
            case ScriptFunction.ReloadVid:
                ReloadVid(Session.Error, Sound);
                break;
            case ScriptFunction.OldStrlen:
            case ScriptFunction.Strlen:
                PushInt(PopText().Length);
                break;
            case ScriptFunction.Itoa:
                PushText(ScriptStack.FormatNumber(PopNumber()));
                break;
            case ScriptFunction.Sin:
                PushInt((int)(new Angle((byte)PopNumber()).Sine * TrigonometryScale));
                break;
            case ScriptFunction.Cos:
                PushInt((int)(new Angle((byte)PopNumber()).Cosine * TrigonometryScale));
                break;
            case ScriptFunction.GetScreenInputX:
                PushInt((int)Input.ScreenCursor.X);
                break;
            case ScriptFunction.GetScreenInputY:
                PushInt((int)Input.ScreenCursor.Y);
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
                Menu.Load(script.PopText().Span);
                break;
            case ScriptFunction.MenuRelease:
                MenuRelease(script);
                break;
            case ScriptFunction.MenuNVidUnderCursor:
                script.PushInt(Menu.GetIdUnderCursor());
                break;
            case ScriptFunction.MenuNDirUnderCursor:
                script.PushInt(Menu.GetDirectionUnderCursor());
                break;
            case ScriptFunction.MenuAction:
                MenuAction(script);
                break;
            case ScriptFunction.MenuCreate:
                MenuCreate(script);
                break;
            case ScriptFunction.MenuLClick:
                PushSprite(script, Menu.LeftClick);
                break;
            case ScriptFunction.MenuRClick:
                PushSprite(script, Menu.RightClick);
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
                script.PushInt((int)Session.ScreenSize.Width);
                break;
            case ScriptFunction.ScreenY:
                script.PushInt((int)Session.ScreenSize.Height);
                break;
            case ScriptFunction.ToScreenX:
                script.PushInt((int)(script.PopNumber() - Camera.X));
                break;
            case ScriptFunction.ToScreenY:
                script.PushInt(ToScreenY(script));
                break;

            case ScriptFunction.SetSoundVolume:
                Sound.SetVolume(script.PopNumber());
                break;
            case ScriptFunction.SetMusicVolume:
                Sound.SetMusicVolume(script.PopNumber(), _clock.WallTime);
                break;
            case ScriptFunction.PlaySfx:
                Sound.Play(script.PopNumber(), 0, Sound.FullLoudness);
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
                script.PushInt(Sound.IsPlayMusic(_clock.WallTime) ? 1 : 0);
                break;

            case ScriptFunction.PlayMovie:
                Session.Graphics.PlayMovie(FileSystem, script.PopText().Span, Session.Error);
                break;
            case ScriptFunction.IsPlayMovie:
                script.PushInt(Session.Graphics.Movie.IsOpen ? 1 : 0);
                break;
            case ScriptFunction.StopMovie:
                Session.Graphics.Movie.Release();
                break;

            case ScriptFunction.SetGamma:
                Session.Graphics.SetGamma(Gamma.FromScript((uint)script.PopNumber()), this);
                break;
            case ScriptFunction.CountGamma:
                script.PushInt(CountGamma(script));
                break;
            case ScriptFunction.GetGamma:
                script.PushInt((int)Gamma.ToScript());
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
                    CursorSprite.ChangeAnimation(0);
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
                Session.Error.Write($"Exec '{MyError.Decode(script.PopText().Span)}'");
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
                PushInt(ComputeChecksum(PopText().Span));
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
                PushInt(FOpen());
                break;
            case ScriptFunction.FCreate:
                PushInt(FCreate());
                break;
            case ScriptFunction.FEof:
                PushInt(FEof());
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
                PushInt(GetPlayer(PopNumber()).Money);
                break;

            // TODO: Port the trains and the rail map
            case ScriptFunction.TrainProperty:
                PopNumber();
                PopSprite();
                PushInt(0);
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
                Session.Error.Write($"!!!ERROR!!!LOGIC: Unknown extern Function {function}");
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
    internal void PushInt(int value) => Script.PushInt(value);

    [EngineRoutine(0x0043a720)]
    internal void PushSprite(int value) => Script.PushSprite(value);

    [EngineRoutine(0x0040fc40)]
    internal Sprite? GetSpriteScr(SpriteSearch search, Vector2 point)
    {
        if (!TryGetSearchedTypes(ref search, out var types, out var armies, out var vid))
            return null;

        Sprite? found = null;

        if (search.HasFlag(SpriteSearch.Hash))
        {
            float ground = GetGroundZ(point);
            Collector.FirstHashInBox(point - new Vector2(ScreenSpriteReach), new Vector2(point.X + ScreenSpriteReach, ground + point.Y + ScreenSpriteReach));
            for (var sprite = Collector.NextHashInBox(); sprite is not null; sprite = Collector.NextHashInBox())
            {
                if (sprite.Owner is null && IsSearched(sprite, search, types, armies) && IsScreenHit(sprite, sprite.Vid.Half, point, found))
                {
                    found = sprite;
                }
            }

            return found;
        }

        if (IsListedSearch(types))
        {
            var listed = Collector.Listed;
            for (int i = listed.Length - 1; i >= 0; i--)
            {
                var sprite = listed[i];

                if (IsSearched(sprite, search, types, armies) && IsScreenHit(sprite, sprite.Vid.Half, point, found))
                {
                    found = sprite;
                }
            }

            return found;
        }

        if (vid is not null && vid.Class == SpriteClass.Frame)
        {
            for (int i = Menu.Count - 1; i >= 0; i--)
            {
                var sprite = Menu[i];

                if (sprite.Owner is null && IsSearched(sprite, search, types, armies) && IsScreenHit(sprite, sprite.Vid.Half, point, found))
                {
                    found = sprite;
                }
            }

            return found;
        }

        bool regions = types.HasFlag(OldSpriteType.Region);
        int first = vid is null ? 0 : (int)vid.Layer;
        int last = vid is null ? UpdateOrder.UpdatedLayerCount : (int)vid.Layer + 1;
        for (int layer = first; layer < last; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];

                if (sprite is null || sprite.Owner is not null || !IsSearched(sprite, search, types, armies))
                    continue;

                var half = sprite.Vid.Half;

                if (regions && sprite.Vid.Class == SpriteClass.Region)
                {
                    half = sprite is Region region ? region.Size * 0.5f : Vector2.Zero;
                }

                if (IsScreenHit(sprite, half, point, found))
                {
                    found = sprite;
                }
            }
        }

        return found;
    }

    [EngineRoutine(0x00405a90)]
    internal Vid Vid(int id) => FindVid((KnownObject)id) ?? EmptyVid;

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

    private static bool IsMenuClass(Vid vid) => vid.Class is SpriteClass.Frame or SpriteClass.Text;

    private static bool IsMenuMatch(Sprite sprite, Vid vid, int direction) =>
        sprite.Vid == vid && (direction == AnyDirection || vid.GetDirectionIndex(sprite.Direction) == direction);

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
        bool isText = Script.IsLastStackString;
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

    private void LogInvalidVid(ReadOnlySpan<byte> function, int id) =>
        Session.Error.Write($"!!!ERROR!!!SCRIPT: Invalid nvid {MyError.Decode(function)} {id}");

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
                CursorSprite.Disable();
                break;
            case HardwareCursor:
                CursorSprite.HardwareOn(EmptyVid, FileSystem);
                break;
            case SoftwareCursor:
                CursorSprite.HardwareOff(FindVid(KnownObject.Cursor) ?? EmptyVid, FileSystem);
                break;
            default:
                if (!CursorSprite.IsCursorLoaded)
                {
                    CursorSprite.Enable(FileSystem);
                }

                CursorSprite.ChangeAnimation((SpriteAnimation)type);
                break;
        }
    }

    private Sprite? MenuFind(Script script)
    {
        int direction = script.PopNumber();
        if (!TryLookupVid(script.PopNumber(), "for MenuFind"u8, out var vid))
            return null;

        if (Sum(vid.Counts) == 0)
            return null;

        foreach (var sprite in Menu.Items)
        {
            if (IsMenuMatch(sprite, vid, direction))
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
        if (!TryLookupVid(script.PopNumber(), "for MenuAction"u8, out var vid))
            return;

        for (int i = 0; i < Menu.Count; i++)
        {
            var sprite = Menu[i];

            if (!IsMenuMatch(sprite, vid, direction))
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

    private bool TryLookupVid(int id, ReadOnlySpan<byte> function, out Vid vid)
    {
        vid = Vid(id);
        if (vid != EmptyVid)
            return true;

        LogInvalidVid(function, id);
        return false;
    }

    [EngineRoutine(0x0043a510)]
    private Vid PopVid(Script script)
    {
        var vid = Vid(script.PopNumber());

        if (vid == EmptyVid)
        {
            LogInvalidVid(default, 0);
        }

        return vid;
    }

    [EngineRoutine(0x0043a7e0)]
    internal Player GetPlayer(int army) => _players[army & ASToolkit.Engine.Vid.ArmyMask];

    [EngineRoutine(0x0043a7b0)]
    private void LogMapError(LogError code, ReadOnlySpan<byte> text, int value) => Session.Error.Error(MapLogTag, code, value, text);

    private void PushSprite(Script script, Sprite? sprite) => script.PushSprite(GetSpriteHandle(sprite));

    private void PushSprite(Sprite? sprite) => PushSprite(GetSpriteHandle(sprite));

    [EngineRoutine(0x0040fbb0)]
    private Sprite? GetSprite(SpriteSearch search, Vector2 point, Sprite? previous)
    {
        var sprite = FindNearestSprite(search, point, GetSpriteReach, previous);

        if (sprite is null)
            return null;

        var half = sprite.Vid.Half;

        return sprite.Position.X - half.X <= point.X && point.X <= half.X + sprite.Position.X && sprite.Position.Y - half.Y <= point.Y && point.Y <= half.Y + sprite.Position.Y
            ? sprite : null;
    }

    [EngineRoutine(0x00410670)]
    private Sprite? FindNearestSprite(SpriteSearch search, Vector2 point, float radius, Sprite? previous)
    {
        if (!TryGetSearchedTypes(ref search, out var types, out var armies, out var vid))
            return null;

        float previousDistance = previous is null ? -1 : point.MeasureOctagonalDistance(previous.Position.AsVector2());
        float reach = radius;
        Sprite? found = null;

        if (search.HasFlag(SpriteSearch.Hash))
        {
            Collector.FirstHashInBox(point - new Vector2(radius), point + new Vector2(radius));
            for (var sprite = Collector.NextHashInBox(); sprite is not null; sprite = Collector.NextHashInBox())
            {
                TakeNearer(sprite);
            }

            return found;
        }

        if (IsListedSearch(types))
        {
            var listed = Collector.Listed;
            for (int i = listed.Length - 1; i >= 0; i--)
            {
                TakeNearer(listed[i]);
            }

            return found;
        }

        if (vid is not null && vid.Class is SpriteClass.Frame or SpriteClass.Text)
        {
            for (int i = Menu.Count - 1; i >= 0; i--)
            {
                TakeNearer(Menu[i]);
            }

            return found;
        }

        int first = vid is null ? 0 : (int)vid.Layer;
        int last = vid is null ? UpdateOrder.UpdatedLayerCount : (int)vid.Layer + 1;
        for (int layer = first; layer < last; layer++)
        {
            var list = _updates.GetLayer(layer);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var sprite = list[i];

                if (sprite is not null)
                {
                    TakeNearer(sprite);
                }
            }
        }

        return found;

        void TakeNearer(Sprite sprite)
        {
            if (sprite.Owner is not null || !IsSearched(sprite, search, types, armies))
                return;

            float distance = point.MeasureOctagonalDistance(sprite.Position.AsVector2());

            if (distance < reach && previousDistance < distance)
            {
                reach = distance;
                found = sprite;
            }
        }
    }

    private static bool IsListedSearch(OldSpriteType types) =>
        (types.HasFlag(OldSpriteType.Unit) || types.HasFlag(OldSpriteType.Avia)) && (types & SearchedTypes & ~GridListTypes) == OldSpriteType.None;

    private static bool IsSearched(Sprite sprite, SpriteSearch search, OldSpriteType types, SpriteSearch armies)
    {
        var vid = sprite.Vid;

        if ((vid.Object.SpriteType.ToOldSpriteType() & types) == 0 || (armies & (SpriteSearch)((int)SpriteSearch.Army0 << ((int)sprite.Army & 3))) == 0)
            return false;

        if (search.HasFlag(SpriteSearch.Free) && sprite.Command != Command.None)
            return false;

        if (search.HasFlag(SpriteSearch.Class) && (int)vid.Class != (int)(search & SpriteSearch.ValueMask))
            return false;

        return !search.HasFlag(SpriteSearch.Id) || (int)vid.Id == (int)(search & SpriteSearch.ValueMask);
    }

    private static bool IsScreenHit(Sprite sprite, Vector2 half, Vector2 point, Sprite? best)
    {
        var size = sprite.Vid.Object.Size;
        float top = sprite.Position.Y - sprite.Position.Z;

        return sprite.Position.X - half.X <= point.X && point.X <= sprite.Position.X + half.X &&
            top - size.Z - half.Y < point.Y && point.Y < top + half.Y &&
            (best is null || size.X < best.Vid.Object.Size.X || size.Y < best.Vid.Object.Size.Y);
    }

    private bool TryGetSearchedTypes(ref SpriteSearch search, out OldSpriteType types, out SpriteSearch armies, out Vid? vid)
    {
        armies = search & SpriteSearch.AnyArmy;
        armies = armies == 0 ? SpriteSearch.AnyArmy : armies;
        vid = null;

        if (!search.HasFlag(SpriteSearch.Id))
        {
            types = (OldSpriteType)((int)search >> TypeShift) & SearchedTypes;
            types = types == 0 ? SearchedTypes : types;

            return true;
        }

        types = 0;
        vid = FindVid((KnownObject)(search & SpriteSearch.ValueMask));

        if (vid is null || Sum(vid.Counts) == 0)
            return false;

        if (vid.Object.Properties.HasFlag(ObjectProperties.Hash))
        {
            search |= SpriteSearch.Hash;
        }

        types = vid.Object.SpriteType.ToOldSpriteType();

        return true;
    }

    private VidSprites EnumerateSprites(Vid vid) => new(_updates.GetLayer(vid.Layer), vid);

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

    [EngineRoutine(0x00412e80)]
    private Sprite? GetNextUnit()
    {
        _unitIndex = Math.Min(_unitIndex, Collector.Listed.Length) - 1;

        return _unitIndex >= 0 ? Collector.Listed[_unitIndex] : null;
    }

    private Sprite? FindUnitOfMask(Sprite? sprite)
    {
        while (sprite is not null && (sprite.Vid.Object.SpriteType.ToOldSpriteType() & _unitMask) == 0)
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
            script.PushInt(0);
            return;
        }

        if ((int)code <= LastAnimationAction)
        {
            sprite.ChangeAnimation((SpriteAnimation)code);
            script.PushInt(0);
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

        script.PushInt(Action(sprite, code, first, second, third));
    }

    private void ExecCreateSprite(Script script)
    {
        var parent = PopSprite(script);
        var direction = new Angle((byte)script.PopNumber());
        var position = PopPosition(script);
        int id = script.PopNumber();
        if (!TryLookupVid(id, "for CreateSprite()"u8, out var vid))
        {
            script.PushInt(0);
            return;
        }

        PushSprite(script, CreateSprite(vid, position, direction, parent));
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
        var vid = FindVid((KnownObject)id);
        if (vid is null)
        {
            LogInvalidVid("for ChangeZUnit"u8, id);
            return;
        }

        foreach (var sprite in EnumerateSprites(vid))
        {
            sprite.ChangeCoor(sprite.Position with { Z = z });
        }
    }

    private Sprite? CanPlace(Script script)
    {
        var position = PopPosition(script);
        int id = script.PopNumber();
        var vid = FindVid((KnownObject)id);
        if (vid is null)
        {
            LogInvalidVid("for CanPlace"u8, id);
        }

        return Collector.CanPlace(vid ?? EmptyVid, position);
    }

    private void Genocide(Script script)
    {
        var vid = PopVid(script);
        if (vid == EmptyVid)
            return;

        foreach (var sprite in EnumerateSprites(vid))
        {
            DeleteSprite(sprite);
        }
    }

    private void ReplaceUnit(Script script)
    {
        var replacement = PopVid(script);
        var vid = PopVid(script);
        if (vid == EmptyVid || replacement == EmptyVid)
            return;

        foreach (var sprite in EnumerateSprites(vid))
        {
            CreateSprite(replacement, sprite.Position, sprite.Direction);
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
        Session.Graphics.Effect(this, script.PopNumber(), first, second, duration);
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
        var vid = PopVid(script);
        if (vid == EmptyVid)
        {
            PushInt(0);
            return;
        }

        switch ((VidDataType)type)
        {
            case VidDataType.MaxHp:
                PushInt(vid.MaxHp);
                break;
            case VidDataType.BattleRange:
                PushInt((int)vid.BattleRange);
                break;
            case VidDataType.Ammo:
                PushInt(GetAmmoOwner(vid).AmmoCapacity);
                break;
            case VidDataType.Count:
                PushInt(vid.GetTotalCount());
                break;
            case VidDataType.KilledUnit:
                PushInt(vid.GetTotalKilled());
                break;
            case >= VidDataType.KilledUnit0 and <= VidDataType.KilledUnit3:
                PushInt(vid.GetKilled(type - (int)VidDataType.KilledUnit0));
                break;
            case >= VidDataType.Count0 and <= VidDataType.Count3:
                PushInt(vid.GetCount(type - (int)VidDataType.Count0));
                break;
            case >= VidDataType.MaxHp0 and <= VidDataType.MaxHp3:
                PushInt(vid.GetArmyMaxHp(type - (int)VidDataType.MaxHp0));
                break;
            case VidDataType.SpriteClass:
                PushInt((int)vid.Class);
                break;
            case VidDataType.Speed:
                PushInt(vid.ScriptSpeed);
                break;
            case VidDataType.Lifetime:
                PushInt(vid.Lifetime);
                break;
            case VidDataType.DetectRange:
                PushInt((int)vid.DetectRange);
                break;
            case VidDataType.WeaponAim:
                PushInt((int)vid.Aim);
                break;
            case VidDataType.MoveMask:
                PushInt((int)vid.MoveMask);
                break;
            case VidDataType.BuildTime:
                PushInt(vid.BuildTime);
                break;
            case VidDataType.NotCreateAsChild:
                PushInt(vid.NotCreatedAsChild);
                break;
            case VidDataType.Link:
                PushInt((int)(vid.LinkVid?.Id ?? 0));
                break;
            case >= VidDataType.Child and < VidDataType.Child + ASToolkit.Engine.Vid.StateCount:
                PushInt((int)(vid.ChildVids[type - (int)VidDataType.Child]?.Id ?? 0));
                break;
            case >= VidDataType.ChildCounts and < VidDataType.ChildCounts + ASToolkit.Engine.Vid.StateCount:
                PushInt(vid.ChildCounts[type - (int)VidDataType.ChildCounts]);
                break;
            case VidDataType.Damage:
                PushInt(vid.Damage);
                break;
            case VidDataType.RecolorUnit:
                PushInt(vid.GetTotalRecolored());
                break;
            case >= VidDataType.RecolorUnit0 and <= VidDataType.RecolorUnit3:
                PushInt(vid.GetRecolored(type - (int)VidDataType.RecolorUnit0));
                break;
            case VidDataType.Name:
                PushText(GameTextEncoding.Cyrillic.GetBytes(GetVidName(vid)));
                break;
            case VidDataType.SpriteType:
                PushInt((int)vid.Object.SpriteType.ToOldSpriteType());
                break;
            case VidDataType.Directions:
                PushInt(vid.Object.Directions);
                break;
            case VidDataType.Hide:
                PushInt(vid.IsHidden ? 1 : 0);
                break;
            case VidDataType.FrameSpeed:
                PushInt(vid.SpriteSpeed);
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
        var vid = PopVid(script);
        if (vid == EmptyVid)
            return;

        switch ((VidDataType)type)
        {
            case VidDataType.MaxHp:
                vid.MaxHp = value;
                break;
            case >= VidDataType.Gamma and < VidDataType.Properties:
                SetVidGamma(vid, type - (int)VidDataType.Gamma, value);
                break;
            case VidDataType.Ammo:
                GetAmmoOwner(vid).AmmoCapacity = value;
                break;
            case VidDataType.KilledUnit:
                vid.Killed.Fill(value);
                break;
            case >= VidDataType.KilledUnit0 and <= VidDataType.KilledUnit3:
                vid.Killed[type - (int)VidDataType.KilledUnit0] = value;
                break;
            case >= VidDataType.MaxHp0 and <= VidDataType.MaxHp3:
                vid.SetMaxHp(this, type - (int)VidDataType.MaxHp0, value);
                break;
            case >= VidDataType.HpCoefficient0 and <= VidDataType.HpCoefficient3:
                vid.SetHpCoeff(this, type - (int)VidDataType.HpCoefficient0, value);
                break;
            case VidDataType.Speed:
                SetVidSpeed(vid, value);
                break;
            case VidDataType.Lifetime:
                if (vid.Id != 0)
                {
                    vid.Lifetime = value;
                    vid.HasTimers = true;
                }

                break;
            case VidDataType.DetectRange:
                vid.DetectRange = value;
                break;
            case VidDataType.WeaponAim:
                vid.Aim = value;
                break;
            case VidDataType.ExchangeVid:
                ExchangeVid(vid, value);
                break;
            case VidDataType.MoveMask:
                vid.MoveMask = (CollisionLayers)value;
                break;
            case VidDataType.BuildTime:
                vid.BuildTime = value;
                break;
            case VidDataType.NotCreateAsChild:
                vid.NotCreatedAsChild = value;
                break;
            case VidDataType.Link:
                vid.LinkVid = Vid(value);
                break;
            case >= VidDataType.Child and < VidDataType.Child + ASToolkit.Engine.Vid.StateCount:
                SetVidChild(vid, type - (int)VidDataType.Child, value);
                break;
            case >= VidDataType.ChildCounts and < VidDataType.ChildCounts + ASToolkit.Engine.Vid.StateCount:
                vid.ChildCounts[type - (int)VidDataType.ChildCounts] = value;
                break;
            case VidDataType.Damage:
                vid.Damage = value;
                break;
            case VidDataType.Hide:
                vid.SetPropHide(value != 0);
                break;
            case VidDataType.FrameSpeed:
                vid.SetFrameSpeed(value);
                break;
            default:
                LogMapError(LogError.Unknown, "SetVid type"u8, type);
                break;
        }
    }

    private void SetVidGamma(Vid vid, int army, int value)
    {
        var gamma = Gamma.FromScript((uint)value);
        vid.Gammas[army] = gamma;
        vid.SetGamma(gamma, army, Session.Graphics.Gamma, Session.Error);
    }

    private void SetVidSpeed(Vid vid, int value)
    {
        vid.Speed = value == ASToolkit.Engine.Vid.Unlimited ? ASToolkit.Engine.Vid.Unlimited : value * ASToolkit.Engine.Vid.PerMillisecond;
        foreach (var entry in _updates.GetLayer(vid.Layer))
        {
            if (entry is not null && entry.Vid == vid && entry.HasExtra)
            {
                entry.MaxSpeed = vid.Speed;
            }
        }
    }

    private void ExchangeVid(Vid vid, int value)
    {
        var other = FindVid((KnownObject)value);
        if (other is not null)
        {
            ExchangeVid(vid, other);
        }
        else
        {
            LogMapError(LogError.Invalid, "SetVid get_image"u8, value);
        }
    }

    private void SetVidChild(Vid vid, int state, int value)
    {
        if (value == 0)
        {
            vid.Children[state] = 0;
            vid.ChildVids[state] = null;
            return;
        }

        int sign = value >> 31;
        var child = FindVid((KnownObject)unchecked((value ^ sign) - sign));
        if (child is null)
        {
            LogMapError(LogError.Invalid, "SetVid child"u8, value);
            return;
        }

        vid.Children[state] = value;
        vid.ChildVids[state] = child;

        if (child.IsBirthAsSmoke())
        {
            vid.HasTimers = true;
        }
    }

    private void MenuRelease(Script script)
    {
        var name = script.PopText();
        if (name.IsEmpty)
        {
            Menu.DeleteAll();
        }
        else
        {
            Menu.DeleteFromFile(name.Span);
        }
    }

    private void MenuCreate(Script script)
    {
        int z = script.PopNumber();
        int y = script.PopNumber() + z;
        var position = new Vector3(script.PopNumber(), y, z);
        int direction = script.PopNumber();
        if (!TryLookupVid(script.PopNumber(), "for MenuCreate"u8, out var vid))
        {
            script.PushInt(0);
            return;
        }

        var heading = new Angle(unchecked((byte)((direction << 8) / vid.Directions)));
        PushSprite(script, CreateSprite(vid, position, heading));
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
        Action(CursorSprite, ActionCode.ChangeCoor, target.X, target.Y);
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

        var items = sprite.GetTextItems();
        var commands = sprite.GetTextActions();
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

        sprite.SetTextItems(text);
        if (text.Span.IndexOf(ItemsEnd) >= 0)
        {
            text = GetAfter(text, ItemsEnd);
        }

        sprite.SetTextActions(text);
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
        script.PushText(Profile.GetString(FileSystem, StringsFile, script.PopText().Span, keyword.Span));
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
        Registry.DeleteKey(ReadText(PopText()), name);
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

        sprite?.AddCommand(act,first,second,third);
    }

    private void ExecSetFlagman(Script script)
    {
        var sprite = PopSprite(script);
        SetFlagman(script.PopNumber(), sprite);
    }

    private void AddUnitLimit(Script script)
    {
        int army = PopNumber();
        var vid = PopVid(script);
        int value = PopNumber();
        if (vid == EmptyVid)
            return;

        if (army == AllArmies)
        {
            vid.MaxUnits = value;
        }
        // TODO: Port AS1's out-of-range write for armies outside 0..3; AS1 writes def+0x398+army*4 into the fields around ArmyMaxUnits
        else if ((uint)army < (uint)vid.ArmyMaxUnits.Length)
        {
            vid.ArmyMaxUnits[army] = value;
        }
    }

    private void SetMoney()
    {
        int money = PopNumber();
        GetPlayer(PopNumber()).Money = money;
    }
}
