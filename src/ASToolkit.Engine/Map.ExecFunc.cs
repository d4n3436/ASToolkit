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

    /// <summary>
    /// Gets the registry of the level script.
    /// </summary>
    public Registry Registry => Session.Registry;

    /// <summary>
    /// Gets the session the map belongs to.
    /// </summary>
    public GameSession Session { get; }

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
                var createParent = PopSprite(script);
                var createDirection = new Angle((byte)script.PopNumber());
                var createPosition = PopPosition(script);
                int createId = script.PopNumber();
                if (!TryLookupVid(createId, "for CreateSprite()"u8, out var createVid))
                {
                    script.PushInt(0);
                    break;
                }

                PushSprite(script, CreateSprite(createVid, createPosition, createDirection, createParent));
                break;
            case ScriptFunction.Destroy:
                var target = PopSprite(script);
                if (target is not null)
                {
                    DeleteSprite(target);
                }

                break;
            case ScriptFunction.Flagman:
                PushSprite(script, Flagman(script.PopNumber()));
                break;
            case ScriptFunction.FirstUnit:
                _unitMask = (OldSpriteType)script.PopNumber();
                var firstUnit = Sprites.ListedSprites.BeginIterate(ref _unitIndex);
                PushSprite(script, firstUnit is null ? null : FindUnitOfMask(firstUnit));
                break;
            case ScriptFunction.NextUnit:
                PushSprite(script, FindUnitOfMask(Sprites.ListedSprites.NextIterate(ref _unitIndex)));
                break;
            case ScriptFunction.FirstInBox:
                var boxMax = PopPoint(script);
                PushSprite(script, Sprites.First(PopPoint(script), boxMax));
                break;
            case ScriptFunction.NextInBox:
                PushSprite(script, Sprites.NextHashInBox());
                break;
            case ScriptFunction.DirectionTo:
                var directionPoint = PopPoint(script);
                var directionSprite = PopSprite(script);
                script.PushInt(directionSprite is null ? 0 : new Angle(directionPoint - directionSprite.Position.AsVector2()).Value);
                break;
            case ScriptFunction.ChangeZUnit:
                int newZ = script.PopNumber();
                int zId = script.PopNumber();
                var zVid = FindVid((KnownObject)zId);
                if (zVid is null)
                {
                    LogInvalidVid("for ChangeZUnit"u8, zId);
                    break;
                }

                int zLayer = (int)zVid.Layer;
                int zIndex = 0;
                for (var sprite = FirstDrawingSprite(zLayer, ref zIndex); sprite is not null; sprite = NextDrawingSprite(zLayer, ref zIndex))
                {
                    if (sprite.Vid != zVid)
                        continue;

                    sprite.ChangeZCoor(newZ);
                }

                break;
            case ScriptFunction.GetGroundZ:
                script.PushInt((int)GetGroundZ(PopPoint(script)));
                break;
            case ScriptFunction.CanPlace:
                var placePosition = PopPosition(script);
                int placeId = script.PopNumber();
                var placeVid = FindVid((KnownObject)placeId);
                if (placeVid is null)
                {
                    LogInvalidVid("for CanPlace"u8, placeId);
                }

                PushSprite(script, Sprites.CanPlace(placeVid ?? EmptyVid, placePosition));
                break;
            case ScriptFunction.MapSizeX:
                PushInt((int)Size.Width);
                break;
            case ScriptFunction.MapSizeY:
                PushInt((int)Size.Height);
                break;
            case ScriptFunction.Genocide:
                var genocideVid = PopVid(script);
                if (genocideVid == EmptyVid)
                    break;

                int genocideLayer = (int)genocideVid.Layer;
                int genocideIndex = 0;
                for (var sprite = FirstDrawingSprite(genocideLayer, ref genocideIndex); sprite is not null; sprite = NextDrawingSprite(genocideLayer, ref genocideIndex))
                {
                    if (sprite.Vid != genocideVid)
                        continue;

                    DeleteSprite(sprite);
                }

                break;
            case ScriptFunction.ReplaceUnit:
                var replacementVid = PopVid(script);
                var replacedVid = PopVid(script);
                if (replacedVid == EmptyVid || replacementVid == EmptyVid)
                    break;

                int replacedLayer = (int)replacedVid.Layer;
                int replacedIndex = 0;
                for (var sprite = FirstDrawingSprite(replacedLayer, ref replacedIndex); sprite is not null; sprite = NextDrawingSprite(replacedLayer, ref replacedIndex))
                {
                    if (sprite.Vid != replacedVid)
                        continue;

                    CreateSprite(replacementVid, sprite.Position, sprite.Direction);
                    DeleteSprite(sprite);
                }

                break;
            case ScriptFunction.Action:
                int actionThird = script.PopNumber();
                int actionSecond = script.PopNumber();
                int actionFirst = script.PopNumber();
                var actionCode = (ActionCode)script.PopNumber();
                var actionSprite = PopSprite(script);
                if (actionSprite is null)
                {
                    script.PushInt(0);
                    break;
                }

                if ((int)actionCode <= LastAnimationAction)
                {
                    actionSprite.ChangeAnimation((SpriteAnimation)actionCode);
                    script.PushInt(0);
                    break;
                }

                switch (actionCode)
                {
                    case ActionCode.GetGoal or ActionCode.GetLink or ActionCode.GetUplink:
                        script.PushSprite(Action(actionSprite, actionCode, actionFirst, actionSecond, actionThird));
                        return;

                    case ActionCode.GetTextDesc when actionSprite is SText text:
                        script.PushText(text.Description);
                        return;

                    // TODO: Port the trains
                    case ActionCode.FirstEngine or ActionCode.LastEngine or ActionCode.NextEngine:
                        PushSprite(script, null);
                        return;
                }

                script.PushInt(Action(actionSprite, actionCode, actionFirst, actionSecond, actionThird));
                break;
            case ScriptFunction.SizeTo:
                var sizePoint = PopPoint(script);
                var sizeSprite = PopSprite(script);
                script.PushInt(sizeSprite is null ? NoUnitDistance : (int)sizeSprite.Position.AsVector2().NearLength(sizePoint));
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
                var inputFlags = Input.Flags;
                var inputState = ScriptInput.None;
                foreach (var (raw, scriptBit) in InputStateBits)
                {
                    if ((inputFlags & raw) != 0)
                    {
                        inputState |= scriptBit;
                    }
                }

                script.PushInt((int)inputState);
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
                int effectDuration = script.PopNumber();
                int effectSecond = script.PopNumber();
                int effectFirst = script.PopNumber();
                Session.Graphics.Effect(this, script.PopNumber(), effectFirst, effectSecond, effectDuration);
                break;
            case ScriptFunction.SetEnvironment:
                Session.Graphics.SetEnvironment((MapEnvironment)script.PopNumber());
                break;
            case ScriptFunction.GetEffectState:
                script.PushInt(Session.Graphics.GetEffectState(this, script.PopNumber()));
                break;
            case ScriptFunction.Charat:
                int charIndex = script.PopNumber();
                var charText = script.PopText().Span;
                script.PushInt(charIndex == charText.Length ? 0 : (sbyte)charText[charIndex]);
                break;
            case ScriptFunction.Random:
                int randomMax = script.PopNumber();
                script.PushInt(_rng.Next() % (randomMax + 1));
                break;
            case ScriptFunction.GetTime:
                script.PushInt(CurrentTime);
                break;
            case ScriptFunction.GetVidData:
                int getType = PopInt();
                var getVid = PopVid(script);
                if (getVid == EmptyVid)
                {
                    PushInt(0);
                    break;
                }

                switch ((VidDataType)getType)
                {
                    case VidDataType.MaxHp:
                        PushInt(getVid.MaxHp);
                        break;
                    case VidDataType.BattleRange:
                        PushInt((int)getVid.Weapon.BattleRange);
                        break;
                    case VidDataType.Ammo:
                        PushInt(GetAmmoOwner(getVid).Weapon.Ammo);
                        break;
                    case VidDataType.Count:
                        PushInt(getVid.GetTotalCount());
                        break;
                    case VidDataType.KilledUnit:
                        PushInt(getVid.GetTotalKilled());
                        break;
                    case >= VidDataType.KilledUnit0 and <= VidDataType.KilledUnit3:
                        PushInt(getVid.GetKilled(getType - (int)VidDataType.KilledUnit0));
                        break;
                    case >= VidDataType.Count0 and <= VidDataType.Count3:
                        PushInt(getVid.GetCount(getType - (int)VidDataType.Count0));
                        break;
                    case >= VidDataType.MaxHp0 and <= VidDataType.MaxHp3:
                        PushInt(getVid.GetArmyMaxHp(getType - (int)VidDataType.MaxHp0));
                        break;
                    case VidDataType.SpriteClass:
                        PushInt((int)getVid.Class);
                        break;
                    case VidDataType.Speed:
                        PushInt(getVid.ScriptSpeed);
                        break;
                    case VidDataType.Lifetime:
                        PushInt(getVid.Lifetime);
                        break;
                    case VidDataType.DetectRange:
                        PushInt((int)getVid.Weapon.DetectRange);
                        break;
                    case VidDataType.WeaponAim:
                        PushInt((int)getVid.Weapon.Aim);
                        break;
                    case VidDataType.MoveMask:
                        PushInt((int)getVid.MoveMask);
                        break;
                    case VidDataType.BuildTime:
                        PushInt(getVid.Weapon.BuildTime);
                        break;
                    case VidDataType.NotCreateAsChild:
                        PushInt(getVid.NotCreatedAsChild);
                        break;
                    case VidDataType.Link:
                        PushInt((int)(getVid.LinkVid?.Id ?? 0));
                        break;
                    case >= VidDataType.Child and < VidDataType.Child + ASToolkit.Engine.Vid.StateCount:
                        PushInt((int)(getVid.ChildVids[getType - (int)VidDataType.Child]?.Id ?? 0));
                        break;
                    case >= VidDataType.ChildCounts and < VidDataType.ChildCounts + ASToolkit.Engine.Vid.StateCount:
                        PushInt(getVid.ChildCounts[getType - (int)VidDataType.ChildCounts]);
                        break;
                    case VidDataType.Damage:
                        PushInt(getVid.Damage);
                        break;
                    case VidDataType.RecolorUnit:
                        PushInt(getVid.GetTotalRecolored());
                        break;
                    case >= VidDataType.RecolorUnit0 and <= VidDataType.RecolorUnit3:
                        PushInt(getVid.GetRecolored(getType - (int)VidDataType.RecolorUnit0));
                        break;
                    case VidDataType.Name:
                        PushText(GameTextEncoding.Cyrillic.GetBytes(GetVidName(getVid)));
                        break;
                    case VidDataType.SpriteType:
                        PushInt((int)getVid.SpriteType);
                        break;
                    case VidDataType.Directions:
                        PushInt(getVid.Directions);
                        break;
                    case VidDataType.Hide:
                        PushInt(getVid.IsHidden ? 1 : 0);
                        break;
                    case VidDataType.FrameSpeed:
                        PushInt(getVid.SpriteSpeed);
                        break;
                    default:
                        LogMapError(LogError.Unknown, "GetVid type"u8, getType);
                        break;
                }

                break;
            case ScriptFunction.SetVidData:
                int setValue = PopInt();
                int setType = PopInt();
                var setVid = PopVid(script);
                if (setVid == EmptyVid)
                    break;

                switch ((VidDataType)setType)
                {
                    case VidDataType.MaxHp:
                        setVid.MaxHp = setValue;
                        break;
                    case >= VidDataType.Gamma and < VidDataType.Properties:
                        var gamma = Gamma.FromScript((uint)setValue);
                        setVid.Gammas[setType - (int)VidDataType.Gamma] = gamma;
                        setVid.SetGamma(gamma, setType - (int)VidDataType.Gamma, Session.Graphics.Gamma, Session);
                        break;
                    case VidDataType.Ammo:
                        GetAmmoOwner(setVid).Weapon.Ammo = setValue;
                        break;
                    case VidDataType.KilledUnit:
                        setVid.Killed.Fill(setValue);
                        break;
                    case >= VidDataType.KilledUnit0 and <= VidDataType.KilledUnit3:
                        setVid.Killed[setType - (int)VidDataType.KilledUnit0] = setValue;
                        break;
                    case >= VidDataType.MaxHp0 and <= VidDataType.MaxHp3:
                        setVid.SetMaxHp(this, setType - (int)VidDataType.MaxHp0, setValue);
                        break;
                    case >= VidDataType.HpCoefficient0 and <= VidDataType.HpCoefficient3:
                        setVid.SetHpCoeff(this, setType - (int)VidDataType.HpCoefficient0, setValue);
                        break;
                    case VidDataType.Speed:
                        setVid.Speed = setValue == ASToolkit.Engine.Vid.Unlimited ? ASToolkit.Engine.Vid.Unlimited : setValue * ASToolkit.Engine.Vid.PerMillisecond;
                        int speedLayer = (int)setVid.Layer;
                        int speedIndex = 0;
                        for (var entry = FirstDrawingSprite(speedLayer, ref speedIndex); entry is not null; entry = NextDrawingSprite(speedLayer, ref speedIndex))
                        {
                            if (entry.Vid == setVid && entry.HasExtraData)
                            {
                                entry.MaxSpeed = setVid.Speed;
                            }
                        }

                        break;
                    case VidDataType.Lifetime:
                        if (setVid.Id == 0)
                            break;

                        setVid.Lifetime = setValue;
                        setVid.HasTimers = true;
                        break;
                    case VidDataType.DetectRange:
                        setVid.Weapon.DetectRange = setValue;
                        break;
                    case VidDataType.WeaponAim:
                        setVid.Weapon.Aim = setValue;
                        break;
                    case VidDataType.ExchangeVid:
                        var exchangeOther = FindVid((KnownObject)setValue);
                        if (exchangeOther is not null)
                        {
                            ExchangeVid(setVid, exchangeOther);
                        }
                        else
                        {
                            LogMapError(LogError.Invalid, "SetVid get_image"u8, setValue);
                        }

                        break;
                    case VidDataType.MoveMask:
                        setVid.MoveMask = (CollisionLayers)setValue;
                        break;
                    case VidDataType.BuildTime:
                        setVid.Weapon.BuildTime = setValue;
                        break;
                    case VidDataType.NotCreateAsChild:
                        setVid.NotCreatedAsChild = setValue;
                        break;
                    case VidDataType.Link:
                        setVid.LinkVid = Vid(setValue);
                        break;
                    case >= VidDataType.Child and < VidDataType.Child + ASToolkit.Engine.Vid.StateCount:
                        int childState = setType - (int)VidDataType.Child;
                        if (setValue == 0)
                        {
                            setVid.Children[childState] = 0;
                            setVid.ChildVids[childState] = null;
                            break;
                        }

                        int childSign = setValue >> 31;
                        var childVid = FindVid((KnownObject)unchecked((setValue ^ childSign) - childSign));
                        if (childVid is null)
                        {
                            LogMapError(LogError.Invalid, "SetVid child"u8, setValue);
                            break;
                        }

                        setVid.Children[childState] = setValue;
                        setVid.ChildVids[childState] = childVid;

                        if (childVid.IsBirthAsSmoke())
                        {
                            setVid.HasTimers = true;
                        }

                        break;
                    case >= VidDataType.ChildCounts and < VidDataType.ChildCounts + ASToolkit.Engine.Vid.StateCount:
                        setVid.ChildCounts[setType - (int)VidDataType.ChildCounts] = setValue;
                        break;
                    case VidDataType.Damage:
                        setVid.Damage = setValue;
                        break;
                    case VidDataType.Hide:
                        setVid.SetPropHide(setValue != 0);
                        break;
                    case VidDataType.FrameSpeed:
                        setVid.SetFrameSpeed(setValue);
                        break;
                    default:
                        LogMapError(LogError.Unknown, "SetVid type"u8, setType);
                        break;
                }

                break;
            case ScriptFunction.Log:
                Session.Error.Log(script.PopText().Span);
                break;
            case ScriptFunction.ReloadVid:
                ReloadVid();
                break;
            case ScriptFunction.OldStrlen:
            case ScriptFunction.Strlen:
                PushInt(PopText().Length);
                break;
            case ScriptFunction.Itoa:
                PushText(ScriptStack.FormatNumber(PopInt()));
                break;
            case ScriptFunction.Sin:
                PushInt((int)(new Angle((byte)PopInt()).Sine * TrigonometryScale));
                break;
            case ScriptFunction.Cos:
                PushInt((int)(new Angle((byte)PopInt()).Cosine * TrigonometryScale));
                break;
            case ScriptFunction.GetScreenInputX:
                PushInt((int)Input.ScreenCursor.X);
                break;
            case ScriptFunction.GetScreenInputY:
                PushInt((int)Input.ScreenCursor.Y);
                break;
            case ScriptFunction.SetAutoReBirth:
                script.Stack.Pop();
                break;
            case ScriptFunction.CanMoveEngineTo:
                PopValues(3);
                break;
            case ScriptFunction.CanAttackEngine:
                PopValues(2);
                break;

            case ScriptFunction.MenuFind:
                int findDirection = script.PopNumber();
                if (!TryLookupVid(script.PopNumber(), "for MenuFind"u8, out var findVid) || Sum(findVid.Counts) == 0)
                {
                    PushSprite(script, null);
                    break;
                }

                Sprite? foundMenuSprite = null;
                foreach (var sprite in Menu.Items)
                {
                    if (!IsMenuMatch(sprite, findVid, findDirection))
                        continue;

                    foundMenuSprite = sprite;
                    break;
                }

                PushSprite(script, foundMenuSprite);
                break;
            case ScriptFunction.MenuLoad:
                Menu.Load(script.PopText().Span);
                break;
            case ScriptFunction.MenuRelease:
                var releaseName = script.PopText();
                if (releaseName.IsEmpty)
                {
                    Menu.DeleteAll();
                }
                else
                {
                    Menu.DeleteFromFile(releaseName.Span);
                }

                break;
            case ScriptFunction.MenuNVidUnderCursor:
                script.PushInt(Menu.GetIdUnderCursor());
                break;
            case ScriptFunction.MenuNDirUnderCursor:
                script.PushInt(Menu.GetDirectionUnderCursor());
                break;
            case ScriptFunction.MenuAction:
                int menuVar3 = script.PopNumber();
                int menuVar2 = script.PopNumber();
                int menuVar1 = script.PopNumber();
                int menuAct = script.PopNumber();
                int menuDirection = script.PopNumber();
                if (!TryLookupVid(script.PopNumber(), "for MenuAction"u8, out var menuVid))
                    break;

                for (int i = 0; i < Menu.Count; i++)
                {
                    var sprite = Menu[i];

                    if (!IsMenuMatch(sprite, menuVid, menuDirection))
                        continue;

                    if (menuAct <= Act.LastState)
                    {
                        sprite.ChangeAnimation((SpriteAnimation)menuAct);
                    }
                    else
                    {
                        Action(sprite, (ActionCode)menuAct, menuVar1, menuVar2, menuVar3);
                    }
                }

                break;
            case ScriptFunction.MenuCreate:
                int menuZ = script.PopNumber();
                int menuY = script.PopNumber() + menuZ;
                var menuPosition = new Vector3(script.PopNumber(), menuY, menuZ);
                int menuDirection2 = script.PopNumber();
                if (!TryLookupVid(script.PopNumber(), "for MenuCreate"u8, out var menuCreateVid))
                {
                    script.PushInt(0);
                    break;
                }

                var menuHeading = new Angle(unchecked((byte)((menuDirection2 << 8) / menuCreateVid.Directions)));
                PushSprite(script, CreateSprite(menuCreateVid, menuPosition, menuHeading));
                break;
            case ScriptFunction.MenuLClick:
                PushSprite(script, Menu.LeftClick);
                break;
            case ScriptFunction.MenuRClick:
                PushSprite(script, Menu.RightClick);
                break;

            case ScriptFunction.SetCursor:
                int cursorType = script.PopNumber();
                if (cursorType == CursorOff)
                {
                    Mouse.Disable();
                    break;
                }

                if (cursorType == HardwareCursor)
                {
                    Mouse.HardwareOn(EmptyVid, FileSystem);
                    break;
                }

                if (cursorType == SoftwareCursor)
                {
                    Mouse.HardwareOff(FindVid(KnownObject.Cursor) ?? EmptyVid, FileSystem);
                    break;
                }

                if (!Mouse.IsCursorLoaded)
                {
                    Mouse.Enable(FileSystem);
                }

                Mouse.ChangeAnimation((SpriteAnimation)cursorType);
                break;
            case ScriptFunction.SetStateBar:
                script.Stack.Pop();
                break;
            case ScriptFunction.SetGraphDetail:
                script.Stack.Pop();
                break;
            case ScriptFunction.MessageText:
                var messageTarget = PopPoint(script);
                _players[ControlledPlayer].Messages.Put(script.PopText(), messageTarget);
                break;
            case ScriptFunction.SetPosition:
                SetShiftCoordinates(PopPoint(script), ShiftMove.None);
                break;
            case ScriptFunction.SetMouseClick:
                int clickKey = script.PopNumber();
                int clickButton = script.PopNumber();
                if (clickButton == FirstMouseClick)
                {
                    _controls.First = new ControlBinding((VirtualKey)clickKey, (VirtualKey)clickKey);
                }
                else if (clickButton == SecondMouseClick)
                {
                    _controls.Second = new ControlBinding((VirtualKey)clickKey, (VirtualKey)clickKey);
                }

                break;
            case ScriptFunction.SetCursorPosition:
                int cursorY = script.PopNumber();
                var cursorTarget = new Point(script.PopNumber(), cursorY);
                Action(Mouse, ActionCode.ChangeCoor, cursorTarget.X, cursorTarget.Y);
                break;
            case ScriptFunction.SetWind:
                var windDirection = new Angle((byte)script.PopNumber());
                Session.Graphics.SetWind(PopInt(), windDirection);
                break;
            case ScriptFunction.ScreenX:
                script.PushInt((int)Session.Graphics.ScreenSize.Width);
                break;
            case ScriptFunction.ScreenY:
                script.PushInt((int)Session.Graphics.ScreenSize.Height);
                break;
            case ScriptFunction.ToScreenX:
                script.PushInt((int)ToScreenX(script.PopNumber()));
                break;
            case ScriptFunction.ToScreenY:
                int screenZ = script.PopNumber();
                int screenY = script.PopNumber();
                script.PushInt((int)ToScreenY(screenY, screenZ));
                break;

            case ScriptFunction.SetSoundVolume:
                Sound.VolumeSound(script.PopNumber());
                break;
            case ScriptFunction.SetMusicVolume:
                Sound.VolumeMusic(script.PopNumber(), Session.RealCurrentTime);
                break;
            case ScriptFunction.PlaySfx:
                Sound.PlaySfx(script.PopNumber(), 0, Sound.FullLoudness);
                break;
            case ScriptFunction.StopSfx:
                Sound.StopSfx(script.PopNumber(), Session.RealCurrentTime);
                break;
            case ScriptFunction.StopMusic:
                Sound.StopMusic(Session.RealCurrentTime);
                break;
            case ScriptFunction.PlaySfxFromCoor:
                var sfxPoint = PopPoint(script);
                Sound.PlayAt(script.PopNumber(), sfxPoint - Shift - Session.Graphics.ScreenSize.ToVector2() * HalfShare);
                break;
            case ScriptFunction.PlayMusicFile:
                int musicLoop = script.PopNumber();
                Sound.FadeAndPlayFile(script.PopText().Span, musicLoop, Session.RealCurrentTime);
                break;
            case ScriptFunction.IsPlayMusic:
                script.PushInt(Sound.IsPlayMusic(Session.RealCurrentTime) ? 1 : 0);
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
                int gammaShare = script.PopNumber();
                int gammaLast = script.PopNumber();
                script.PushInt(BlendGamma(script.PopNumber(), gammaLast, gammaShare));
                break;
            case ScriptFunction.GetGamma:
                script.PushInt((int)Gamma.ToScript());
                break;

            // TODO: Port the saves
            case ScriptFunction.GetCommands:
                var commandsSprite = PopSprite(script);
                if (commandsSprite is null)
                {
                    script.PushText(default);
                    break;
                }

                var commandItems = commandsSprite.GetTextItems();
                var commandActions = commandsSprite.GetTextActions();
                byte[] commandText = new byte[commandItems.Length + commandActions.Length];
                commandItems.CopyTo(commandText);
                commandActions.CopyTo(commandText.AsMemory(commandItems.Length));
                script.PushText(commandText);
                break;
            case ScriptFunction.SetCommands:
                var commandsText = script.PopText();
                var setCommandsSprite = PopSprite(script);
                if (setCommandsSprite is null)
                    break;

                setCommandsSprite.SetTextItems(commandsText);
                if (commandsText.Span.IndexOf(ItemsEnd) >= 0)
                {
                    commandsText = commandsText.After(ItemsEnd);
                }

                setCommandsSprite.SetTextActions(commandsText);
                break;
            case ScriptFunction.FRead:
                var readFile = PopScriptFile();
                var readLine = IsPlayingDemo || readFile is null ? default : readFile.ReadLine();
                // TODO: Port the demo stream strings of FRead and the shared text buffer a null handle pushes; recording stores each line and playback reads it back
                PushText(readLine);
                break;
            case ScriptFunction.FWrite:
                var writeText = PopText();
                var writeFile = PopScriptFile();
                if (writeFile is null)
                    break;

                writeFile.WriteText(writeText.Span);
                writeFile.SeekBack();
                writeFile.WriteLineFeed();
                break;
            case ScriptFunction.SaveDemo:
                if (!IsPlayingDemo)
                {
                    SaveDemo(script.PopText().Span);
                }

                break;
            case ScriptFunction.GetString:
                var stringKeyword = script.PopText();
                script.PushText(Session.Strings?.GetString(script.PopText().Span, stringKeyword.Span) ?? default);
                break;
            case ScriptFunction.Pause:
                if (script.PopNumber() != 0)
                {
                    PauseOn();
                    Mouse.ChangeAnimation(0);
                }
                else
                {
                    PauseOff();
                }

                break;
            case ScriptFunction.Save:
                PopText();
                break;
            case ScriptFunction.Exec:
                // TODO: Port the shell launch of Exec; the port only logs the command
                Session.Error.Log($"Exec '{MyError.Decode(script.PopText().Span)}'");
                break;
            case ScriptFunction.FClose:
                int closeHandle = PopInt();
                if (_scriptFiles.Remove(closeHandle, out var closeFile))
                {
                    closeFile.Dispose();
                }

                break;
            case ScriptFunction.GetPreviousMapName:
                script.PushText(_previousMapName);
                break;
            case ScriptFunction.GetAsProtectUserName:
                script.PushText(default);
                break;
            case ScriptFunction.GetMapName:
                script.PushText(_mapName);
                break;
            case ScriptFunction.Crc32:
                PushInt(ComputeChecksum(PopText().Span));
                break;
            case ScriptFunction.Printf:
                bool isText = Script.IsLastStackString;
                var printedText = isText ? PopText() : default;
                int printedNumber = isText ? 0 : PopInt();
                var format = PopText();
                PushText(MyLib.Printf(format.Span, isText, printedText.Span, printedNumber));
                break;
            case ScriptFunction.ToLower:
                byte[] lowerText = CRuntime.GetString(PopText().Span).ToArray();
                CRuntime.ToLowerInPlace(lowerText);
                PushText(lowerText);
                break;
            case ScriptFunction.ToUpper:
                byte[] upperText = CRuntime.GetString(PopText().Span).ToArray();
                CRuntime.ToUpperInPlace(upperText);
                PushText(upperText);
                break;
            case ScriptFunction.FOpen:
                string openName = ReadText(PopText());
                int openHandle = 0;
                if (!IsPlayingDemo)
                {
                    var openFile = openName.Length != 0 && ScriptFiles.TryRead(openName, out var openData) ? new ScriptFile(openData.Span, null) : null;
                    if (openFile is null)
                    {
                        LogMapError(LogError.Open, Encoding.ASCII.GetBytes(openName), 0);
                    }

                    openHandle = AddScriptFile(openFile);
                }

                PushInt(openHandle);
                break;
            case ScriptFunction.FCreate:
                string createName = ReadText(PopText());
                int createHandle = 0;
                if (!IsPlayingDemo)
                {
                    var createWriter = createName.Length != 0 ? ScriptFiles.OpenWrite(createName, false) : null;
                    createHandle = AddScriptFile(createWriter is null ? null : new ScriptFile(default, createWriter));
                }

                PushInt(createHandle);
                break;
            case ScriptFunction.FEof:
                var eofFile = PopScriptFile();
                PushInt(eofFile is null ? 1 : eofFile.IsEnd ? EndOfFileFlag : 0);
                break;
            case ScriptFunction.GetReg:
                string regDefault = ReadText(PopText());
                string regName = ReadText(PopText());
                PushText(GameTextEncoding.Cyrillic.GetBytes(Registry.Get(ReadText(PopText()), regName, regDefault)));
                break;
            case ScriptFunction.SetReg:
                string regValue = ReadText(PopText());
                string setRegName = ReadText(PopText());
                Registry.Set(ReadText(PopText()), setRegName, regValue);
                break;
            case ScriptFunction.DelReg:
                string delRegName = ReadText(PopText());
                Registry.DeleteKey(ReadText(PopText()), delRegName);
                break;
            case ScriptFunction.GetDefaultRegPath:
                PushText(GameTextEncoding.Cyrillic.GetBytes(Registry.DefaultPath));
                break;
            case ScriptFunction.ToBase64:
                int addCode = PopInt();
                PushText(PopText().Span.ToBase64(addCode));
                break;

            case ScriptFunction.GetSprite:
                var getPrevious = PopSprite(script);
                var getPoint = PopPoint(script);
                PushSprite(script, GetSprite((SpriteSearch)script.PopNumber(), getPoint, getPrevious));
                break;
            case ScriptFunction.GetSpriteScr:
                var scrPoint = PopPoint(script);
                PushSprite(script, GetSpriteScr((SpriteSearch)script.PopNumber(), scrPoint));
                break;
            case ScriptFunction.FindNearestSprite:
                var nearestPrevious = PopSprite(script);
                int nearestRadius = script.PopNumber();
                var nearestPoint = PopPoint(script);
                PushSprite(script, FindNearestSprite((SpriteSearch)script.PopNumber(), nearestPoint, nearestRadius, nearestPrevious));
                break;
            case ScriptFunction.FirstSprite:
                _spriteLayer = 0;
                PushSprite(script, FirstDrawingSprite(_spriteLayer, ref _spriteIndex));
                break;
            case ScriptFunction.NextSprite:
                var nextSprite = NextDrawingSprite(_spriteLayer, ref _spriteIndex);
                while (nextSprite is null && _spriteLayer < LayerCount - 1)
                {
                    _spriteLayer++;
                    nextSprite = FirstDrawingSprite(_spriteLayer, ref _spriteIndex);
                }

                PushSprite(script, nextSprite);
                break;
            case ScriptFunction.AddCommand:
                int commandThird = script.PopNumber();
                int commandSecond = script.PopNumber();
                int commandFirst = script.PopNumber();
                int commandAct = script.PopNumber();
                var commandTarget = PopSprite(script);

                commandTarget?.AddActionAfterStop(commandAct,commandFirst,commandSecond,commandThird);
                break;
            case ScriptFunction.SetFlagman:
                var flagmanSprite = PopSprite(script);
                SetFlagman(script.PopNumber(), flagmanSprite);
                break;
            case ScriptFunction.AddUnitLimit:
                int limitArmy = PopInt();
                var limitVid = PopVid(script);
                int limitValue = PopInt();
                if (limitVid == EmptyVid)
                    break;

                if (limitArmy == AllArmies)
                {
                    limitVid.MaxUnits = limitValue;
                }
                // TODO: Port AS1's out-of-range write for armies outside 0..3; AS1 writes def+0x398+army*4 into the fields around ArmyMaxUnits
                else if (limitArmy >= 0 && limitArmy < limitVid.ArmyMaxUnits.Length)
                {
                    limitVid.ArmyMaxUnits[limitArmy] = limitValue;
                }

                break;
            case ScriptFunction.SetEnemyCanAttackNeutralTrains:
                SetMonstersAttackNeutralTrains(PopInt() != 0);
                break;
            case ScriptFunction.SetMoney:
                int newMoney = PopInt();
                Player(PopInt()).Money = newMoney;
                break;
            case ScriptFunction.GetMoney:
                PushInt(Player(PopInt()).Money);
                break;

            // TODO: Port the trains and the rail map
            case ScriptFunction.TrainProperty:
                PopInt();
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
                PopInt();
                PushSprite(null);
                break;
            case ScriptFunction.NextTrain:
                PushSprite(null);
                break;
            case ScriptFunction.SetPushLine:
                PopValues(5);
                break;
            case ScriptFunction.SetCleverEnemyAttack:
                Player(1).IsCleverEnemyAttack = PopInt() != 0;
                break;
            default:
                Session.Error.Log($"!!!ERROR!!!LOGIC: Unknown extern Function {function}");
                break;
        }
    }

    [EngineRoutine(0x00405a90)]
    internal Vid Vid(int id) => ValidateVid(id) ? _vids[id]! : EmptyVid;

    [EngineRoutine(0x0040fc40)]
    internal Sprite? GetSpriteScr(SpriteSearch search, Vector2 point)
    {
        if (!TryGetSearchedTypes(ref search, out var types, out var armies, out var vid))
            return null;

        Sprite? found = null;

        if (search.HasFlag(SpriteSearch.Hash))
        {
            float ground = GetGroundZ(point);
            Sprites.FirstHashInBox(point - new Vector2(ScreenSpriteReach), new Vector2(point.X + ScreenSpriteReach, ground + point.Y + ScreenSpriteReach));
            for (var sprite = Sprites.NextHashInBox(); sprite is not null; sprite = Sprites.NextHashInBox())
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
            var listed = Sprites.ListedSprites;
            int index = 0;
            for (var sprite = listed.BeginIterate(ref index); sprite is not null; sprite = listed.NextIterate(ref index))
            {
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
        int last = vid is null ? UpdatedLayerCount : (int)vid.Layer + 1;
        for (int layer = first; layer < last; layer++)
        {
            int index = 0;
            for (var sprite = FirstDrawingSprite(layer, ref index); sprite is not null; sprite = NextDrawingSprite(layer, ref index))
            {
                if (sprite.Owner is not null || !IsSearched(sprite, search, types, armies))
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

    [EngineRoutine(0x00410640)]
    internal bool ValidateVid(int id) => id >= 0 && id < _vidCount && _vids[id] is not null;

    [EngineRoutine(0x0043a330)]
    internal int PopInt() => Script.PopNumber();

    [EngineRoutine(0x0043a390)]
    internal ReadOnlyMemory<byte> PopText() => Script.PopText();

    [EngineRoutine(0x0043a470)]
    internal int PopHandle() => Script.PopHandle();

    [EngineRoutine(0x0043a5b0)]
    internal void PushInt(int value) => Script.PushInt(value);

    [EngineRoutine(0x0043a640)]
    internal void PushText(ReadOnlyMemory<byte> text) => Script.PushText(text);

    [EngineRoutine(0x0043a720)]
    internal void PushSprite(int value) => Script.PushSprite(value);

    [EngineRoutine(0x0043a7e0)]
    internal Player Player(int army) => _players[army & ASToolkit.Engine.Vid.ArmyMask];

    private static bool IsScreenHit(Sprite sprite, Vector2 half, Vector2 point, Sprite? best)
    {
        var size = sprite.Vid.Size;
        float top = sprite.Position.Y - sprite.Position.Z;

        return sprite.Position.X - half.X <= point.X && point.X <= sprite.Position.X + half.X &&
            top - size.Z - half.Y < point.Y && point.Y < top + half.Y &&
            (best is null || size.X < best.Vid.Size.X || size.Y < best.Vid.Size.Y);
    }

    private static bool IsListedSearch(OldSpriteType types) =>
        (types.HasFlag(OldSpriteType.Unit) || types.HasFlag(OldSpriteType.Avia)) && (types & SearchedTypes & ~GridListTypes) == OldSpriteType.None;

    private static bool IsSearched(Sprite sprite, SpriteSearch search, OldSpriteType types, SpriteSearch armies)
    {
        var vid = sprite.Vid;

        if ((vid.SpriteType & types) == 0 || (armies & (SpriteSearch)((int)SpriteSearch.Army0 << ((int)sprite.Army & 3))) == 0)
            return false;

        if (search.HasFlag(SpriteSearch.Free) && sprite.Command != Command.None)
            return false;

        if (search.HasFlag(SpriteSearch.Class) && (int)vid.Class != (int)(search & SpriteSearch.ValueMask))
            return false;

        return !search.HasFlag(SpriteSearch.Id) || (int)vid.Id == (int)(search & SpriteSearch.ValueMask);
    }

    private static bool IsMenuMatch(Sprite sprite, Vid vid, int direction) =>
        sprite.Vid == vid && (direction == AnyDirection || vid.RealDirection(sprite.Direction) == direction);

    private static string ReadText(ReadOnlyMemory<byte> text) => GameTextEncoding.Cyrillic.GetString(text.Span);

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

    [EngineRoutine(0x00439db0)]
    private static int ComputeChecksum(ReadOnlySpan<byte> text)
    {
        return unchecked((int)MyLib.UpdateChecksum(0, CRuntime.GetString(text)));
    }

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

    private Sprite? FirstDrawingSprite(int layer, ref int index)
    {
        index = _layers[layer].Count;
        return NextDrawingSprite(layer, ref index);
    }

    [EngineRoutine(0x0040f9a0)]
    private Sprite? NextDrawingSprite(int layer, ref int index)
    {
        var list = _layers[layer];
        for (index--; index >= 0; index--)
        {
            var sprite = list[index];

            if (sprite is not null)
                return sprite;
        }

        return null;
    }

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

        float previousDistance = previous is null ? -1 : point.NearLength(previous.Position.AsVector2());
        float reach = radius;
        Sprite? found = null;

        if (search.HasFlag(SpriteSearch.Hash))
        {
            Sprites.FirstHashInBox(point - new Vector2(radius), point + new Vector2(radius));
            for (var sprite = Sprites.NextHashInBox(); sprite is not null; sprite = Sprites.NextHashInBox())
            {
                TakeNearer(sprite);
            }

            return found;
        }

        if (IsListedSearch(types))
        {
            var listed = Sprites.ListedSprites;
            int index = 0;
            for (var sprite = listed.BeginIterate(ref index); sprite is not null; sprite = listed.NextIterate(ref index))
            {
                TakeNearer(sprite);
            }

            return found;
        }

        if (vid?.Class is SpriteClass.Frame or SpriteClass.Text)
        {
            for (int i = Menu.Count - 1; i >= 0; i--)
            {
                TakeNearer(Menu[i]);
            }

            return found;
        }

        int first = vid is null ? 0 : (int)vid.Layer;
        int last = vid is null ? UpdatedLayerCount : (int)vid.Layer + 1;
        for (int layer = first; layer < last; layer++)
        {
            int index = 0;
            for (var sprite = FirstDrawingSprite(layer, ref index); sprite is not null; sprite = NextDrawingSprite(layer, ref index))
            {
                TakeNearer(sprite);
            }
        }

        return found;

        void TakeNearer(Sprite sprite)
        {
            if (sprite.Owner is not null || !IsSearched(sprite, search, types, armies))
                return;

            float distance = point.NearLength(sprite.Position.AsVector2());

            if (distance < reach && previousDistance < distance)
            {
                reach = distance;
                found = sprite;
            }
        }
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

        if (vid.Properties.HasFlag(ObjectProperties.Hash))
        {
            search |= SpriteSearch.Hash;
        }

        types = vid.SpriteType;

        return true;
    }

    private void PopValues(int count)
    {
        for (int i = 0; i < count; i++)
        {
            PopInt();
        }
    }

    private float ToScreenX(float x) => x - Shift.X;

    private float ToScreenY(float y, float z) => y - z - Shift.Y;

    private ScriptFile? PopScriptFile() => _scriptFiles.GetValueOrDefault(PopInt());

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

    private Sprite? FindUnitOfMask(Sprite? sprite)
    {
        while (sprite is not null && (sprite.Vid.SpriteType & _unitMask) == 0)
        {
            sprite = Sprites.ListedSprites.NextIterate(ref _unitIndex);
        }

        return sprite;
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

    private void LogInvalidVid(ReadOnlySpan<byte> function, int id) =>
        Session.Error.Log($"!!!ERROR!!!SCRIPT: Invalid nvid {MyError.Decode(function)} {id}");

    private void PushSprite(Script script, Sprite? sprite) => script.PushSprite(GetSpriteHandle(sprite));

    private void PushSprite(Sprite? sprite) => PushSprite(GetSpriteHandle(sprite));

    [EngineRoutine(0x0043a7b0)]
    private void LogMapError(LogError code, ReadOnlySpan<byte> text, int value) => Session.Error.Error(MapLogTag, code, value, text);
}
