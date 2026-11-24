using System.Numerics;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

/// <summary>
/// Represents the game world as played by the game, running the loaded level frame by frame.
/// </summary>
public sealed class MapSteam : Map
{
    private const int DemoStopResult = 999999;
    private const int FlagmanKey = 'I';
    private const int GridKey = 'O';
    private const int HashedNumbersKey = 'H';
    private const int HashedSpritesKey = 'G';
    private const int MainFunction = -1;
    private const int PresentationDuration = 1000;
    private const uint PresentationPeriod = 2000;
    private const float PresentationTop = 32;
    private const int RailsKey = 'R';
    private const int ReloadKey = '~';
    private const int SingleStepKey = 'P';
    private const int StepKey = 'p';

    private readonly MouseTips _mouseTips;

    private static ReadOnlySpan<byte> PresentationText => "Presentation version. Not for sale!"u8;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapSteam"/> class with no level loaded.
    /// </summary>
    /// <param name="gameFolder">The game folder that holds the sprite files of the object IDs.</param>
    /// <param name="objects">The object table.</param>
    /// <param name="fileSystem">The file system that holds the game's files.</param>
    /// <param name="registry">The registry of the level scripts, or null for an empty one.</param>
    /// <param name="session">The session the map belongs to, or null to create one.</param>
    public MapSteam(string gameFolder, ObjectTable objects, IGameFileSystem fileSystem, Registry? registry = null, GameSession? session = null)
        : base(gameFolder, objects, fileSystem, registry, session)
    {
        _mouseTips = new MouseTips(this);
    }

    [EngineRoutine(0x00405160)]
    internal override void DeletePointerToSprite(Sprite removed)
    {
        _mouseTips.DeletePointerToSprite(removed);
        base.DeletePointerToSprite(removed);
    }

    /// <summary>
    /// Runs one frame of the level, advancing the clock and playing or recording the demo frame first.
    /// </summary>
    /// <param name="wallTime">The wall time of the frame in milliseconds.</param>
    /// <param name="now">The real time in milliseconds that paces a played demo.</param>
    /// <param name="waitUntil">The real time to wait for before the frame, or <paramref name="now"/> when the frame is due.</param>
    /// <returns><see langword="true"/> when the game is quitting; otherwise, <see langword="false"/>.</returns>
    /// <remarks>The frame doesn't run while the window is inactive or a menu is open, unless the debug keys allow it.</remarks>
    [EngineRoutine(0x00405190)]
    public override bool Tact(uint wallTime, uint now, out uint waitUntil)
    {
        waitUntil = now;

        if (AdvanceClock(wallTime))
            return true;

        if (Constant.IsDebugMode)
        {
            switch (Input.Key)
            {
                case HashedSpritesKey:
                    _debug ^= StepFlags.DrawHashedSprites;
                    break;
                case HashedNumbersKey:
                    _debug ^= StepFlags.DrawHashedNumbers;
                    break;
                case FlagmanKey:
                    _flags ^= MapFlags.DrawFlagman;
                    break;
                case GridKey:
                    _flags ^= MapFlags.DrawGridZ | MapFlags.DrawSpriteBoxes;
                    break;
                case SingleStepKey:
                    _debug ^= StepFlags.SingleStep;
                    break;
                case RailsKey:
                    _flags ^= MapFlags.DrawRails;
                    break;
                case ReloadKey:
                    ReloadVid(Session.Error, Sound);
                    break;
            }
        }

        bool isActive = _flags.HasFlag(MapFlags.WindowActive);

        if (!isActive && !_debug.HasFlag(StepFlags.RunInBackground) || Session.Graphics.IsMenuLoopActive || Constant.IsDebugMode && _debug.HasFlag(StepFlags.SingleStep) && Input.Key != StepKey)
        {
            _clock.Rewind();
            return false;
        }

        bool canDraw = !(!isActive && Session.IsFullScreen || Session.Graphics.Movie.IsOpen);
        if (canDraw && Session.Graphics.PreTact() != 0)
            return false;

        _stepCount++;
        int demoResult = StepDemo(now, out waitUntil) ? 1 : 0;

        if (demoResult == DemoStopResult)
        {
            Session.Graphics.PostTact(true);
            return false;
        }

        bool draw = demoResult != 0 && canDraw;
        Menu.Control(Input);
        ScriptRun(MainFunction, null);

        if (draw && Session.Graphics.Movie.IsOpen)
        {
            draw = false;
            canDraw = false;
        }

        SetGammaOverrides(false);
        Session.Graphics.UpdateScreen(this, draw);
        SetGammaOverrides(true);

        bool isPaused = _flags.HasFlag(MapFlags.Paused);

        if (isPaused)
        {
            for (int i = Menu.Count - 1; i >= 0; i--)
            {
                Menu[i].Tact();
            }
        }
        else
        {
            for (int layer = 0; layer < UpdatedLayerCount; layer++)
            {
                var list = _layers[layer];
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    list[i]?.Tact();
                }
            }
        }

        _mouseTips.Tact(Input);
        Scroll();

        if (!isPaused && _flags.HasFlag(MapFlags.PlayerControlled))
        {
            if (_flags.HasFlag(MapFlags.DrawPresentation) && _clock.WallTime - _presentationShownAt > PresentationPeriod)
            {
                var vid = FindVid(KnownObject.Font) ?? EmptyVid;
                var banner = CreateSprite(vid, new Vector3(Session.ScreenSize.Width * HalfShare, PresentationTop, 0), default)!;
                _presentationShownAt = _clock.WallTime;
                Action(banner, ActionCode.SetBehave, 1);
                banner.SetTextItems(PresentationText.ToArray());
                Action(banner, ActionCode.Pause, PresentationDuration);
                banner.ActionStack.InsertFirst(new Act((int)SpriteAnimation.Death, 0, 0, 0));
            }

            foreach (var controller in _players)
            {
                controller.Control(Input);
            }
        }

        if (draw)
        {
            DrawSecondaryInfo();
        }

        if (canDraw)
        {
            Session.Graphics.PostTact(true);
        }

        Sound.Tact(_rng, Time, _clock.WallTime);

        return false;
    }

    [EngineRoutine(0x00405ac0)]
    internal override void Release()
    {
        _flags &= ~MapFlags.MonstersAttackNeutralTrains;
        _mouseTips.Clear();
        base.Release();
    }

    [EngineRoutine(0x00405af0)]
    internal override void DrawSecondaryInfo()
    {
        base.DrawSecondaryInfo();

        if (_debug.HasFlag(StepFlags.DrawHashedNumbers))
        {
            for (int index = Collector.Listed.Length - 1; index >= 0; index = Math.Min(Collector.Listed.Length, index) - 1)
            {
                var sprite = Collector.Listed[index];

                if (sprite.Owner is null)
                {
                    Session.Graphics.DrawFormattedText(GetScreenPoint(sprite), $"{sprite.Hp}");
                }
            }
        }

        if (_debug.HasFlag(StepFlags.DrawHashedSprites))
        {
            for (int index = Collector.Listed.Length - 1; index >= 0; index = Math.Min(Collector.Listed.Length, index) - 1)
            {
                Collector.Listed[index].DrawGoalLine();
            }
        }
    }

    /// <summary>
    /// Creates a sprite of an object ID, or of the object ID that a region at the position replaces it with, and runs the creation handler of
    /// the sprite's object ID.
    /// </summary>
    /// <param name="vid">The shared data of the object ID.</param>
    /// <param name="position">The position.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="parent">The sprite that the new sprite takes its army and height from, and links to if the new sprite is a linker.</param>
    /// <returns>The sprite, or <see langword="null"/> if <paramref name="vid"/> is null, its limit is reached or its class isn't valid.</returns>
    [EngineRoutine(0x00405bd0)]
    public override Sprite? CreateSprite(Vid? vid, Vector3 position, Angle direction, Sprite? parent = null)
    {
        if (vid is null)
            return null;

        if (vid.IsRegionReplaced)
        {
            vid = Region.ReplaceVid(this, vid, position);
        }

        if (vid.MaxUnits >= 0 && vid.MaxUnits <= Sum(vid.Counts))
            return null;

        Sprite sprite;

        switch (vid.Class)
        {
            case SpriteClass.Terrain or SpriteClass.Object:
                sprite = new Terrain(vid, position, direction, parent, this);
                break;
            case SpriteClass.Building:
                sprite = new Building(vid, position, direction, parent, this);
                break;
            case SpriteClass.CivRobot:
                sprite = new CivRobot(vid, position, direction, parent, this);
                break;
            case SpriteClass.Engine:
                sprite = new Engine(vid, position, direction, parent, this);
                break;
            case SpriteClass.Rail:
                sprite = new Rail(vid, position, direction, parent, this);
                break;
            case SpriteClass.Depo:
                sprite = new Depo(vid, position, direction, parent, this);
                break;
            case SpriteClass.Creature:
                sprite = new Creature(vid, position, direction, parent, this);
                break;
            case SpriteClass.Balloon:
                sprite = new Balloon(vid, position, direction, parent, this);
                break;
            default:
                return base.CreateSprite(vid, position, direction, parent);
        }

        if (!_flags.HasFlag(MapFlags.Loading))
        {
            RunCreationHandler(sprite);
        }

        return sprite;
    }

    private void SetGammaOverrides(bool clear)
    {
        var flagman = Flagman(ControlledPlayer);

        if (flagman is { Army: Army.Player } && Constant.SelectedGamma != 0)
        {
            OverrideGamma(flagman, clear ? default : Gamma.FromScript(Constant.SelectedGamma));
        }

        var hovered = _players[ControlledPlayer].Hovered;

        if (hovered is null || Constant.HoveredGamma == 0 && Constant.HoveredEnemyGamma == 0 || !clear && !_flags.HasFlag(MapFlags.MouseTips))
            return;

        OverrideGamma(hovered, clear ? default : Gamma.FromScript(hovered.Army == Army.Monsters ? Constant.HoveredEnemyGamma : Constant.HoveredGamma));
    }
}
