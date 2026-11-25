using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Sprite
{
    private const int MaxStreamPeriod = 30000;

    /// <summary>
    /// Updates the sprite for one frame, deleting it when its death animation ends.
    /// </summary>
    [EngineRoutine(0x00440110)]
    internal virtual void Tact()
    {
        if (Vid.HasKeyframes)
        {
            ref readonly var weapon = ref Vid.Weapon;
            int elapsed = Map.CurrentTime - KeyframeStart;
            int keyPeriod = weapon.AnimationPeriod;
            bool timed = true;

            if (keyPeriod == Vid.Unlimited)
            {
                if (Animation > SpriteAnimation.Birth && LastFrame != FirstFrame)
                {
                    keyPeriod = Vid.GetVisualFrameDuration(Animation) * (LastFrame - FirstFrame);
                }
                else if (Lifetime == Vid.Unlimited)
                {
                    timed = false;
                }
                else
                {
                    keyPeriod = Lifetime + elapsed;
                }
            }

            if (timed && keyPeriod != 0)
            {
                int key = (int)KeyframePosition + 1;
                while (key < WeaponRecord.KeyframeCount - 1 && keyPeriod * GetKeyTime(weapon, key) <= elapsed)
                {
                    key++;
                }

                key--;
                float start = keyPeriod * GetKeyTime(weapon, key);
                KeyframePosition = (elapsed - start) / ((GetKeyTime(weapon, key + 1) - GetKeyTime(weapon, key)) * keyPeriod) + key;
            }
        }

        if (Owner is null)
        {
            int lastTickTime = LastTickTime;
            LastTickTime = Map.PreviousCurrentTime;
            MoveTact();
            LastTickTime = lastTickTime;
        }

        Map.RaiseHeights(Vid, this);

        int emitSlot = (int)Animation;
        var emitChild = Vid.ChildVids[emitSlot];

        if (emitChild is not null && Vid.Class == SpriteClass.Region && Vid.ChildX[emitSlot].IsExactlyZero() && Vid.ChildY[emitSlot].IsExactlyZero())
        {
            var region = this as Region;
            bool isMapCovered = region is not null && region.Flags.HasFlag(RegionFlags.CoversMap);
            var regionSize = region?.Size ?? Vector2.Zero;
            float regionWidth = isMapCovered ? Map.Size.Width : regionSize.X;
            float regionHeight = isMapCovered ? Map.Size.Height : regionSize.Y;
            float area = regionHeight * regionWidth / emitChild.Size.X / emitChild.Size.Y;
            int count = (int)area;

            if (count != 0 && Map.FrameTime != 0 && Map.Random.Next() % (int)(1000u / (uint)Map.FrameTime / (uint)count + 1) == 0)
            {
                CreateChild();
            }
        }
        else if (emitChild is not null && emitChild.Properties.HasFlag(ObjectProperties.BirthAsSmoke) && (uint)(Map.CurrentTime - (int)((uint)Map.CurrentTime % (uint)StreamPeriod)) > (uint)(Map.CurrentTime - Map.FrameTime))
        {
            float across = Direction.Sine * Speed;
            float down = Direction.Cosine * Speed + (VerticalSpeed - emitChild.ZSpeed);

            if (emitChild.Properties.HasFlag(ObjectProperties.Wind))
            {
                across -= Map.Session.Graphics.WindDirection.Sine * Map.Session.Graphics.WindSpeed;
                down -= Map.Session.Graphics.WindDirection.Cosine * Map.Session.Graphics.WindSpeed;
            }

            float acrossPeriod = across.IsExactlyZero() ? MaxStreamPeriod : emitChild.Size.X / Math.Abs(across);
            float downPeriod = down.IsExactlyZero() ? MaxStreamPeriod : emitChild.Size.Y / Math.Abs(down);
            uint streamPeriod = (uint)CRuntime.Ftol(Math.Min(acrossPeriod, downPeriod));

            if ((SpriteAnimation)emitSlot == SpriteAnimation.Fight && Vid.HasDoubleAttack)
            {
                streamPeriod >>= 1;
            }

            StreamPeriod = (int)(streamPeriod > MaxStreamPeriod ? MaxStreamPeriod : streamPeriod == 0 ? 1 : streamPeriod);
            CreateChild();
        }

        var state = Animation;

        if (CreatedAt == Map.CurrentTime || (uint)(Map.CurrentTime - (int)((uint)Map.CurrentTime % (uint)Vid.GetVisualFrameDuration(state))) <= (uint)(Map.CurrentTime - Map.FrameTime))
            return;

        if (Command is not Command.None && this is { Command: < Command.Build, HasGoal: false })
        {
            Map.Session.Error.Error($"SPRITE {(int)Vid.Id}", LogError.Text, (int)Command, "command need goal, but goal==NULL"u8);
            SetCommand(Command.None);
        }

        if (Timer != 0)
        {
            Timer = (uint)(Map.CurrentTime - LastTickTime) < (uint)Timer ? Timer - (Map.CurrentTime - LastTickTime) : 0;

            if (this is { Timer: 0, Command: Command.Pause })
            {
                SetCommand(Command.None);
            }
        }

        bool everyFrame = Vid.Properties.HasFlag(ObjectProperties.Track);
        int handler = Vid.StateHandlers[(int)state];

        while (handler >= 0 && (Frame == FirstFrame || everyFrame))
        {
            if (Map.ScriptRun(handler, this) != 0)
                return;

            if (Animation == state)
                break;

            state = Animation;
            handler = Vid.StateHandlers[(int)state];
        }

        int sound = Vid.Sounds[(int)Animation];

        if (sound != 0 && (!HasPlayedStateSound || Map.Sound.IsLooped(sound)))
        {
            HasPlayedStateSound = true;
            PlaySfx(sound);
        }

        var tactChild = Vid.ChildVids[(int)Animation];

        if (tactChild is not null && !tactChild.Properties.HasFlag(ObjectProperties.BirthAsSmoke))
        {
            int emissionFrame = Vid.Properties.HasFlag(ObjectProperties.CreateChildEnd) ? LastFrame : FirstFrame;

            if (Animation == SpriteAnimation.Fight && Vid.HasDoubleAttack
                ? Frame == (LastFrame + 1 + FirstFrame) / 2 || Frame == emissionFrame
                : Vid.Properties.HasFlag(ObjectProperties.Track) || Frame == emissionFrame)
            {
                CreateChild();
            }
        }

        Frame++;

        if (Animation >= SpriteAnimation.Death && (Frame > LastFrame || Vid.GetFrameCount(Animation) == 0))
        {
            Frame = LastFrame;
            Action(ActionCode.Death, 0, 0, 0, null);
            Map.DeleteSprite(this);
            return;
        }

        if (Command is Command.Passive or Command.Pause)
        {
            state = Animation;

            if (state < SpriteAnimation.Death && state != SpriteAnimation.StandOpen && Frame > LastFrame)
            {
                if (Speed.IsExactlyZero())
                {
                    if (state is SpriteAnimation.Go or > SpriteAnimation.Open)
                    {
                        ChangeAnimation(SpriteAnimation.Stand);
                    }
                }
                else if (state != SpriteAnimation.Go)
                {
                    ChangeAnimation(SpriteAnimation.Go);
                }
            }
        }
        else if (Frame > LastFrame || everyFrame)
        {
            if (ActionStack.Count == 0 || Command != Command.None)
            {
                Action(ActionCode.NextCommand, 0, 0, 0, null);
            }
            else
            {
                var order = ActionStack[ActionStack.Count - 1];
                state = Animation;

                if (order.Value > Act.LastState &&
                    (state is > SpriteAnimation.Open and < SpriteAnimation.Death and not SpriteAnimation.StandOpen || state == SpriteAnimation.Go && Speed.IsExactlyZero()))
                {
                    ChangeAnimation(SpriteAnimation.Stand);
                }

                if (order.Value != Act.StopMarker)
                {
                    ActionStack.Pop();

                    if (order.Value <= Act.LastState)
                    {
                        ChangeAnimation((SpriteAnimation)order.Value);
                    }
                    else
                    {
                        Action((ActionCode)order.Value, order.Var1, order.Var2, order.Var3, order.Target);
                    }
                }
            }
        }

        if (HasExtraData && Lifetime != Vid.Unlimited && Animation < SpriteAnimation.Death)
        {
            if ((uint)(Map.CurrentTime - LastTickTime) < (uint)Lifetime)
            {
                Lifetime -= Map.CurrentTime - LastTickTime;
            }
            else
            {
                ChangeAnimation(SpriteAnimation.Death);
            }
        }

        if (HasExtraData && Vid.Weapon.AnimationPeriod != Vid.Unlimited && (uint)Vid.Weapon.AnimationPeriod < (uint)(Map.CurrentTime - KeyframeStart))
        {
            KeyframeStart = Map.CurrentTime;
        }

        LastTickTime = Map.CurrentTime;

        if (Frame > LastFrame)
        {
            Frame = FirstFrame;
        }
    }

    private static float GetKeyTime(in AlienShooterWeaponRecord weapon, int key) =>
        key < 0 ? BitConverter.Int32BitsToSingle(weapon.AnimationPeriod)
        : key < WeaponRecord.KeyframeCount ? weapon.KeyTimes[key] : BitConverter.Int32BitsToSingle(weapon.RedKeys[0]);
}
