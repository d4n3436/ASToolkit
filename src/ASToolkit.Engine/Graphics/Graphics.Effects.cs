using System.Drawing;
using System.Numerics;
using ASToolkit.Engine.Scripts;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

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

    private readonly ushort[] _captureTexels = new ushort[CaptureTileSize * CaptureTileSize];

    /// <summary>
    /// Gets the copy of the screen that an alpha appearance fades from, or <see langword="null"/> until one has started.
    /// </summary>
    public Screen? Capture { get; private set; }

    [EngineRoutine(0x00402690)]
    internal void Effect(Map map, int effect, int first, int second, int duration)
    {
        if (effect is < 0 or >= EffectCount)
            return;

        var screenEffect = (ScreenEffect)effect;

        if (screenEffect is ScreenEffect.Fade or ScreenEffect.FadeIn or ScreenEffect.FadeOut)
        {
            ClearFades();
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

        EffectStartTimes[effect] = map.RealTime;
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

            ClearFades();
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

    [EngineRoutine(0x00431c00)]
    internal int GetEffectState(Map map, int effect)
    {
        if (effect is <= (int)ScreenEffect.None or >= EffectCount || EffectStartTimes[effect] == 0)
            return -1;

        return (int)(unchecked(map.RealTime * 100 - EffectStartTimes[effect] * 100) / (uint)EffectDurations[effect]);
    }

    [EngineRoutine(0x00431b60)]
    private static uint PackColor(int alpha, int red, int green, int blue) =>
        (uint)((((Math.Clamp(alpha, 0, byte.MaxValue) << 8 | Math.Clamp(red, 0, byte.MaxValue)) << 8 | Math.Clamp(green, 0, byte.MaxValue)) << 8) | Math.Clamp(blue, 0, byte.MaxValue));

    [EngineRoutine(0x004311a0)]
    private void UpdateEffects(Map map, bool draw)
    {
        uint now = map.RealTime;
        bool isShown = draw && Screen is not null;

        if (TryAdvanceEffect(ScreenEffect.AlphaAppear, now, isShown, out uint appeared, out uint appearLength) && Capture is not null)
        {
            DrawAlphaAppear(Capture, appeared, appearLength);
        }

        if (EffectStartTimes[(int)ScreenEffect.Scroll] != 0)
        {
            UpdateScroll(map, now);
        }

        if (TryAdvanceEffect(ScreenEffect.Nuke, now, isShown, out uint elapsed, out uint length))
        {
            DrawNukeFlash(elapsed, length);
        }

        if (TryAdvanceEffect(ScreenEffect.Fade, now, isShown, out elapsed, out length))
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

            DrawGrayFade(strength);
        }

        if (TryAdvanceEffect(ScreenEffect.FadeIn, now, isShown, out elapsed, out length))
        {
            uint rise = length * FadeInRiseNumerator;
            DrawGrayFade(elapsed < (uint)(rise * DivideByFive >> DivideByFiveShift) ? unchecked(elapsed * 5 << 8) / rise : FadeFullStrength);
        }

        if (TryAdvanceEffect(ScreenEffect.FadeOut, now, isShown, out elapsed, out length))
        {
            DrawGrayFade(unchecked((length - elapsed) << 8) / length);
        }

        if (EffectStartTimes[(int)ScreenEffect.ShiftGamma] != 0)
        {
            UpdateGammaShift(map, now, draw);
        }
    }

    [EngineRoutine(0x00434180)]
    private void DrawFlash(Screen screen, uint color)
    {
        SetRenderState(RenderState.SpecularEnable, 0);
        SetAlphaBlend(BlendFactor.DestinationColor, BlendFactor.One);
        ScreenPrimitives.FillRectangle(screen, new Rectangle(Point.Empty, screen.Size), color, Blend.Source, Blend.Destination);
    }

    [EngineRoutine(0x00434060)]
    private void DrawFade(Screen screen, uint color)
    {
        SetAlphaBlend(BlendFactor.Zero, BlendFactor.InverseSourceColor);
        ScreenPrimitives.FillRectangle(screen, new Rectangle(Point.Empty, screen.Size), color, Blend.Source, Blend.Destination);
    }

    private void ClearFades()
    {
        EffectStartTimes[(int)ScreenEffect.FadeOut] = 0;
        EffectStartTimes[(int)ScreenEffect.FadeIn] = 0;
        EffectStartTimes[(int)ScreenEffect.Fade] = 0;
    }

    private bool TryAdvanceEffect(ScreenEffect effect, uint now, bool isShown, out uint elapsed, out uint length)
    {
        uint start = EffectStartTimes[(int)effect];
        length = (uint)EffectDurations[(int)effect];
        elapsed = now - start;
        if (start != 0 && elapsed >= length)
        {
            EffectStartTimes[(int)effect] = 0;
            return false;
        }

        return start != 0 && isShown;
    }

    private void DrawAlphaAppear(Screen capture, uint appeared, uint length)
    {
        var gamma = new Gamma(~PackColor((int)((appeared << 8) / length), byte.MaxValue, byte.MaxValue, byte.MaxValue), Gamma.OpaqueAlpha);
        var texture = new Texture(_captureTexels, new Size(CaptureTileSize, CaptureTileSize), VidPixelFormat.R5G6B5);
        var viewport = Screen!.Viewport;
        SetAlphaBlend(BlendFactor.InverseSourceAlpha, BlendFactor.SourceAlpha);
        for (float top = viewport.Top; top < viewport.Bottom; top += CaptureTileSize)
        {
            for (float left = viewport.Left; left < viewport.Right; left += CaptureTileSize)
            {
                float width = left + CaptureTileSize < viewport.Right ? CaptureTileSize : viewport.Right - left;
                float height = top + CaptureTileSize < viewport.Bottom ? CaptureTileSize : viewport.Bottom - top;
                var area = Rectangle.FromLTRB((int)(left - viewport.Left), (int)(top - viewport.Top), (int)(left + width - viewport.Left), (int)(height + top - viewport.Top));
                if (texture.CopyFromSurface(capture, area, Point.Empty, _session.Error) == 0)
                {
                    texture.DrawFlatQuad(Screen, area, new Rectangle(0, 0, (int)width, (int)height), gamma, Blend, true);
                }
            }
        }
    }

    private void UpdateScroll(Map map, uint now)
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

    private void DrawNukeFlash(uint elapsed, uint length)
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

    private void DrawGrayFade(uint strength) => DrawFade(Screen!, (strength << 8 | strength) << 8 | strength);

    private void UpdateGammaShift(Map map, uint now, bool draw)
    {
        uint shifted = now - EffectStartTimes[(int)ScreenEffect.ShiftGamma];
        var target = Gamma.FromScript((uint)EffectFirst[(int)ScreenEffect.ShiftGamma]);
        if (shifted >= (uint)EffectDurations[(int)ScreenEffect.ShiftGamma])
        {
            SetGamma(target, map);
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

        SetGamma(new Gamma(negative, positive), map);
    }

}
