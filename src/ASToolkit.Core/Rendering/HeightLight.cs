using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static ASToolkit.Core.Rendering.MapRenderer;

namespace ASToolkit.Core.Rendering;

internal sealed class HeightLight
{
    public const int ClearDepth = 0x3ff;
    private const int MaxReach = 512;
    private const int AreaDivisor = 500;
    private const int TexelStep = 4;
    private const int SecondSample = 3;
    private const int NoHeight = 0x7fff;
    private const int HeightShift = 3;
    private const int HeightBias = 128;
    private const int MaxStrength = 255;
    private const int RedBlueShift = 3;
    private const int GreenShift = 2;
    private const int LiftNumerator = 2;
    private const int LiftDenominator = 3;
    private const int LiftBase = 8;
    private const int ReachNumerator = 3;
    private const int ReachAlignMask = ~3;
    private const int SamplesPerTexel = 2;
    private const int AlongDivisor = 4;
    private const int AcrossWeight = 9;
    private const int AcrossDivisor = 4;
    private const int FullStrength = 256;
    private const int MinTexels = 3;
    private const float FirstTexelCenter = 1.5f;
    private const float PixelCenter = 0.5f;
    private const int EdgeTexels = 2;
    private const float RedBlueMax = 31;
    private const float GreenMax = 63;
    private const int QuadDepthLift = 50;

    private readonly float _x;
    private readonly float _y;
    private readonly int _z;
    private readonly int _lift;
    private readonly Size _reach;
    private readonly int _area;
    private byte[] _texels = [];
    private readonly Vector3 _color;
    private readonly int _gain;
    private readonly float _quadDepth;
    private bool[] _blocked = [];

    public HeightLight(OmniLight light, Vector3 color, int insertAt)
    {
        int sizeX = (int)(2 * light.RadiusX);
        int sizeY = (int)(2 * light.RadiusY);
        _z = (int)light.Position.Z;
        _lift = _z * LiftNumerator / LiftDenominator + LiftBase;
        _x = light.Position.X;
        _y = light.Position.Y - light.Position.Z - (_lift - light.Position.Z);
        _reach = new Size(Math.Min(MaxReach, sizeX / 2 * ReachNumerator & ReachAlignMask), Math.Min(MaxReach, sizeY / 2 * ReachNumerator & ReachAlignMask));
        _area = sizeX * sizeY / AreaDivisor;
        _color = color;
        _gain = light.Doubled ? 2 : 1;
        _quadDepth = light.Position.Z + 2 * light.RadiusX + QuadDepthLift;
        InsertAt = insertAt;
        Quad = Rectangle.FromLTRB((int)(_x - _reach.Width), (int)(_y - _reach.Height), (int)(_x + _reach.Width), (int)(_y + _reach.Height));
        Samples = new Rectangle((int)_x - _reach.Width, (int)_y - _reach.Height, 2 * _reach.Width, 2 * _reach.Height);
    }

    public int InsertAt { get; }

    public Rectangle Quad { get; }

    // Pixels whose heights the texture reads, both samples of every texel
    public Rectangle Samples { get; }

    private int Columns => _reach.Width / SamplesPerTexel;

    private int Rows => _reach.Height / SamplesPerTexel;

    public static Rectangle GetQuad(OmniLight light) => new HeightLight(light, Vector3.Zero, 0).Quad;

    public void Fill(ReadOnlySpan<int> depth, RectangleF view)
    {
        _texels = new byte[Columns * Rows];
        int left = (int)_x;
        int top = (int)_y;
        for (int row = 0; row < Rows; row++)
        {
            int dy = row * TexelStep - _reach.Height;
            for (int column = 0; column < Columns; column++)
            {
                int dx = column * TexelStep - _reach.Width;
                int first = GetHeight(depth, view, _x + dx, _y + dy, left + dx - Samples.Left, top + dy - Samples.Top);
                int second = GetHeight(depth, view, _x + dx + SecondSample, _y + dy + SecondSample, left + dx + SecondSample - Samples.Left,
                    top + dy + SecondSample - Samples.Top);
                int height = Math.Min(first, second);
                if (height == NoHeight)
                {
                    continue;
                }

                int along = height - _z;
                int across = height - _lift + dy;
                int distance = along * along / AlongDivisor + AcrossWeight * across * across / AcrossDivisor + dx * dx;
                if (_area != 0)
                {
                    distance /= _area;
                }

                _texels[row * Columns + column] = (byte)Math.Clamp(FullStrength - distance, 0, MaxStrength);
            }
        }
    }

    // Brightens the rows of a strip, the canvas starting at scene column left
    public void Apply(Span<Rgb24> pixels, int left, int width, int y0, int y1)
    {
        if (Columns < MinTexels || Rows < MinTexels)
        {
            return;
        }

        int top = Math.Max(y0, Quad.Top);
        int bottom = Math.Min(y1, Quad.Bottom);
        int first = Math.Max(left, Quad.Left);
        int last = Math.Min(left + width, Quad.Right);
        float uStep = (float)(Columns - EdgeTexels) / Quad.Width;
        float vStep = (float)(Rows - EdgeTexels) / Quad.Height;
        for (int y = top; y < bottom; y++)
        {
            float v = FirstTexelCenter + (y - Quad.Top) * vStep - PixelCenter;
            int row = (int)MathF.Floor(v);
            float rowShare = v - row;
            var line = pixels.Slice((y - y0) * width, width);
            for (int x = first; x < last; x++)
            {
                if (_blocked.Length > 0 && _blocked[(y - Quad.Top) * Quad.Width + x - Quad.Left])
                {
                    continue;
                }

                float u = FirstTexelCenter + (x - Quad.Left) * uStep - PixelCenter;
                int column = (int)MathF.Floor(u);
                float columnShare = u - column;
                int upperLeft = GetTexel(column, row);
                int upperRight = GetTexel(column + 1, row);
                int lowerLeft = GetTexel(column, row + 1);
                int lowerRight = GetTexel(column + 1, row + 1);
                float redBlue = Filter(upperLeft >> RedBlueShift, upperRight >> RedBlueShift, lowerLeft >> RedBlueShift, lowerRight >> RedBlueShift,
                    columnShare, rowShare) / RedBlueMax;
                float green = Filter(upperLeft >> GreenShift, upperRight >> GreenShift, lowerLeft >> GreenShift, lowerRight >> GreenShift,
                    columnShare, rowShare) / GreenMax;
                ref var pixel = ref line[x - left];
                pixel = new Rgb24(Brighten(pixel.R, redBlue, _color.X), Brighten(pixel.G, green, _color.Y), Brighten(pixel.B, redBlue, _color.Z));
            }
        }
    }

    // Keeps the light off the pixels of a hardware quad drawn before it with a greater depth, the depth running from top to bottom
    public void Block(Rectangle quad, float topDepth, float bottomDepth)
    {
        var area = Rectangle.Intersect(quad, Quad);
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        for (int y = area.Top; y < area.Bottom; y++)
        {
            float depth = topDepth + (bottomDepth - topDepth) * (y - quad.Top) / quad.Height;
            if (depth <= _quadDepth)
            {
                continue;
            }

            if (_blocked.Length == 0)
            {
                _blocked = new bool[Quad.Width * Quad.Height];
            }

            _blocked.AsSpan((y - Quad.Top) * Quad.Width + area.Left - Quad.Left, area.Width).Fill(true);
        }
    }

    private int GetHeight(ReadOnlySpan<int> depth, RectangleF view, float x, float y, int column, int row)
    {
        if (x < view.Left || x >= view.Right || y < view.Top || y >= view.Bottom)
        {
            return NoHeight;
        }

        return (depth[row * Samples.Width + column] >> HeightShift) - HeightBias;
    }

    private int GetTexel(int column, int row) => _texels[Math.Clamp(row, 0, Rows - 1) * Columns + Math.Clamp(column, 0, Columns - 1)];

    // Modulates the texel by the light color, saturated, and blends it additively
    private byte Brighten(byte color, float texel, float light) =>
        (byte)MathF.Min(MaxStrength, MathF.Round(color * (1 + MathF.Min(1, _gain * texel * light / MaxStrength))));

    private static float Filter(int upperLeft, int upperRight, int lowerLeft, int lowerRight, float columnShare, float rowShare) =>
        Lerp(Lerp(upperLeft, upperRight, columnShare), Lerp(lowerLeft, lowerRight, columnShare), rowShare);

    private static float Lerp(float from, float to, float share) => from + (to - from) * share;
}
