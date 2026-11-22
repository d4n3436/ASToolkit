using System.Drawing;
using System.Numerics;
using ASToolkit.Core.Engine.Scripts;
using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

public sealed partial class Graphics
{
    private const uint FlashRiseParts = 9;
    private const uint FlashFullStrength = 256;
    private const ulong DivideByNine = 0x38e38e39;
    private const ulong DivideByFive = 0xcccccccd;
    private const int DivideByNineShift = 33;
    private const int DivideByFiveShift = 34;
    private const uint FadeRiseNumerator = 4;
    private const uint FadeHoldNumerator = 5;
    private const uint FadeInRiseNumerator = 4;
    private const uint FadeFullStrength = byte.MaxValue;
    private const int GreenByte = 0xff00;
    private const int BlueByte = 0xff;
    private const int RedByte = 0xff0000;
    private const int FlashDuration = 0x900;
    private const int ScrollDuration = 0x200;
    private const int FadeInDuration = 0x500;
    private const int EffectDuration = 0x400;
    private const int GammaChannels = 4;
    private const int CaptureTileSize = 256;
    private static readonly QuadBlend CaptureBlend = new(BlendFactor.InverseSourceAlpha, BlendFactor.SourceAlpha);

    private readonly ushort[] _captureTexels = new ushort[CaptureTileSize * CaptureTileSize];

    /// <summary>
    /// Gets the copy of the screen that an alpha appearance fades from, or <see langword="null"/> until one has started.
    /// </summary>
    public Screen? Capture { get; private set; }

    [EngineRoutine(0x00402690)]
    internal void ExecEffect(Map map, int effect, int first, int second, int duration)
    {
        if (effect is < 0 or >= EffectCount)
            return;

        var screenEffect = (ScreenEffect)effect;

        if (screenEffect is ScreenEffect.Fade or ScreenEffect.FadeIn or ScreenEffect.FadeOut)
        {
            EffectStartTimes[(int)ScreenEffect.FadeOut] = 0;
            EffectStartTimes[(int)ScreenEffect.FadeIn] = 0;
            EffectStartTimes[(int)ScreenEffect.Fade] = 0;
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

        EffectStartTimes[effect] = map.WallTime;
        EffectDurations[effect] = duration;
        EffectFirst[effect] = first;
        EffectSecond[effect] = second;
        if (screenEffect == ScreenEffect.None)
        {
            Array.Clear(EffectStartTimes);
        }
        else if (screenEffect == ScreenEffect.AlphaAppear)
        {
            if (Screen is null)
                return;

            if (Capture is null || Capture.Size != Screen.Size || Capture.IsTrueColor != Screen.IsTrueColor || Capture.Format != Screen.Format)
            {
                Capture = new Screen(Screen.Size, Screen.IsTrueColor, Screen.Format);
            }

            EffectStartTimes[(int)ScreenEffect.FadeOut] = 0;
            EffectStartTimes[(int)ScreenEffect.FadeIn] = 0;
            EffectStartTimes[(int)ScreenEffect.Fade] = 0;
            Screen.CopyColorsTo(Capture);
        }
        else if (screenEffect == ScreenEffect.Scroll)
        {
            ScrollFrom = new Vector2(map.Camera.Y);
        }
        else if (screenEffect == ScreenEffect.ShiftGamma)
        {
            GammaFrom = Gamma;
        }
    }

    [EngineRoutine(0x004311a0)]
    private void UpdateEffects(Map map, bool draw)
    {
        uint now = map.WallTime;
        bool isShown = draw && Screen is not null;

        if (UpdateEffectTime(ScreenEffect.AlphaAppear, now, out uint appeared, out uint appearLength) && isShown && Capture is not null)
        {
            var tint = new Gamma(~PackColor((int)((appeared << 8) / appearLength), byte.MaxValue, byte.MaxValue, byte.MaxValue), Gamma.OpaqueAlpha);
            var texture = new Texture(_captureTexels, new Size(CaptureTileSize, CaptureTileSize), VidPixelFormat.R5G6B5);
            var clip = Screen!.Clip;
            for (float top = clip.Top; top < clip.Bottom; top += CaptureTileSize)
            {
                for (float left = clip.Left; left < clip.Right; left += CaptureTileSize)
                {
                    float width = left + CaptureTileSize < clip.Right ? CaptureTileSize : clip.Right - left;
                    float height = top + CaptureTileSize < clip.Bottom ? CaptureTileSize : clip.Bottom - top;
                    var area = Rectangle.FromLTRB((int)(left - clip.Left), (int)(top - clip.Top), (int)(left + width - clip.Left), (int)(height + top - clip.Top));
                    if (texture.CopyFromSurface(Capture, area, Point.Empty) == 0)
                    {
                        texture.DrawFlatQuad(Screen, area, new Rectangle(0, 0, (int)width, (int)height), tint, CaptureBlend, true);
                    }
                }
            }
        }

        if (EffectStartTimes[(int)ScreenEffect.Scroll] != 0)
        {
            uint scrolled = now - EffectStartTimes[(int)ScreenEffect.Scroll];
            int duration = EffectDurations[(int)ScreenEffect.Scroll];
            if ((uint)duration < scrolled)
            {
                map.SetPosition(new Vector2(EffectFirst[(int)ScreenEffect.Scroll], EffectSecond[(int)ScreenEffect.Scroll]), CameraMove.None);
                EffectStartTimes[(int)ScreenEffect.Scroll] = 0;
            }
            else
            {
                var position = new Vector2((EffectFirst[(int)ScreenEffect.Scroll] - ScrollFrom.X) * (int)scrolled / duration + ScrollFrom.X,
                    (EffectSecond[(int)ScreenEffect.Scroll] - ScrollFrom.Y) * (int)scrolled / duration + ScrollFrom.Y);
                map.SetPosition(position, CameraMove.None);
            }
        }

        if (UpdateEffectTime(ScreenEffect.Nuke, now, out uint elapsed, out uint length) && isShown)
        {
            uint rise = length / FlashRiseParts;
            uint strength = elapsed < rise ? elapsed * FlashRiseParts * FlashFullStrength / length : (length - elapsed) * FlashFullStrength / (length - rise);
            int color = EffectFirst[(int)ScreenEffect.Nuke];
            int green = (color & GreenByte) * (int)strength >> 8 & GreenByte;
            int red = color * (int)strength >> 8 & RedByte;
            int blue = (color & BlueByte) * (int)strength >> 8;
            uint flash = (uint)(green + blue + red);
            DrawFlash(Screen!, flash);
            DrawFlash(Screen!, flash);
            DrawFlash(Screen!, flash);
        }

        if (UpdateEffectTime(ScreenEffect.Fade, now, out elapsed, out length) && isShown)
        {
            uint rise = length * FadeRiseNumerator;
            uint hold = (uint)(length * FadeHoldNumerator * DivideByNine >> DivideByNineShift);
            uint strength;
            if (elapsed < (uint)(rise * DivideByNine >> DivideByNineShift))
            {
                strength = unchecked(elapsed * 9 << 8) / rise;
            }
            else if (elapsed <= hold)
            {
                strength = FadeFullStrength;
            }
            else
            {
                strength = unchecked((length - elapsed) * 9 << 8) / rise;
            }

            DrawFade(Screen!, GetFadeColor(strength));
        }

        if (UpdateEffectTime(ScreenEffect.FadeIn, now, out elapsed, out length) && isShown)
        {
            uint rise = length * FadeInRiseNumerator;
            uint strength = elapsed < (uint)(rise * DivideByFive >> DivideByFiveShift) ? unchecked(elapsed * 5 << 8) / rise : FadeFullStrength;
            DrawFade(Screen!, GetFadeColor(strength));
        }

        if (UpdateEffectTime(ScreenEffect.FadeOut, now, out elapsed, out length) && isShown)
        {
            DrawFade(Screen!, GetFadeColor(unchecked((length - elapsed) << 8) / length));
        }

        if (EffectStartTimes[(int)ScreenEffect.ShiftGamma] == 0)
            return;

        uint shifted = now - EffectStartTimes[(int)ScreenEffect.ShiftGamma];
        var target = Gamma.FromScript((uint)EffectFirst[(int)ScreenEffect.ShiftGamma]);
        if (shifted >= (uint)EffectDurations[(int)ScreenEffect.ShiftGamma])
        {
            SetGamma(target, map.Definitions);
            EffectStartTimes[(int)ScreenEffect.ShiftGamma] = 0;
            return;
        }

        if (!draw)
            return;

        float share = (float)shifted / EffectDurations[(int)ScreenEffect.ShiftGamma];
        uint negative = 0, positive = 0;
        for (int channel = Gamma.ChannelBits * (GammaChannels - 1); channel >= 0; channel -= Gamma.ChannelBits)
        {
            int from = GammaFrom.GetChannel(channel);
            int value = Math.Clamp((int)((target.GetChannel(channel) - from) * share + from), -byte.MaxValue, byte.MaxValue);
            if (value < 0)
            {
                negative |= (uint)-value << channel;
            }
            else
            {
                positive |= (uint)value << channel;
            }
        }

        SetGamma(new Gamma(negative, positive), map.Definitions);
    }

    private bool UpdateEffectTime(ScreenEffect effect, uint now, out uint elapsed, out uint length)
    {
        int i = (int)effect;
        uint start = EffectStartTimes[i];
        elapsed = now - start;
        length = (uint)EffectDurations[i];
        if (start == 0)
            return false;

        if (elapsed < length)
            return true;

        EffectStartTimes[i] = 0;
        return false;
    }

    [EngineRoutine(0x00431b60)]
    private static uint PackColor(int alpha, int red, int green, int blue) =>
        (uint)((((Math.Clamp(alpha, 0, byte.MaxValue) << 8 | Math.Clamp(red, 0, byte.MaxValue)) << 8 | Math.Clamp(green, 0, byte.MaxValue)) << 8) | Math.Clamp(blue, 0, byte.MaxValue));

    private static uint GetFadeColor(uint strength) => (strength << 8 | strength) << 8 | strength;

    [EngineRoutine(0x00431c00)]
    internal int GetEffectState(Map map, int effect)
    {
        if (effect is <= (int)ScreenEffect.None or >= EffectCount || EffectStartTimes[effect] == 0)
            return -1;

        return (int)(unchecked(map.WallTime * 100 - EffectStartTimes[effect] * 100) / (uint)EffectDurations[effect]);
    }

    [EngineRoutine(0x00434180)]
    private static void DrawFlash(Screen screen, uint color) =>
        ScreenPrimitives.FillRectangle(screen, new Rectangle(Point.Empty, screen.Size), color, BlendFactor.DestinationColor, BlendFactor.One);

    [EngineRoutine(0x00434060)]
    private static void DrawFade(Screen screen, uint color) =>
        ScreenPrimitives.FillRectangle(screen, new Rectangle(Point.Empty, screen.Size), color, BlendFactor.Zero, BlendFactor.InverseSourceColor);
}
