using System.Drawing;
using System.Numerics;
using ASToolkit.Core;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public sealed partial class Graphics
{
    private const int MaxSnowflakes = 1000;
    private const int SnowflakeVertexCount = 6;
    private const int MaxRaindrops = 250;
    private const int RaindropVertexCount = 2;
    private const float NoWind = 99999;
    private const int SnowGrowthShift = 7;
    private const int SnowAgeLimit = 0x100;
    private const int SnowfallSpawnRows = 0x32;
    private const uint SnowflakeColor = 0xffffffff;
    private const uint RainHeadColor = 0x70e0e0ff;
    private const uint RainTailColor = 0x308080ff;
    private const float DepthScale = 3.725404e-09f;
    private const float MinDepth = 0.015625f;
    private const float DepthPerLength = 0.00012207031f;
    private const float SnowFallRate = 150;
    private const float RainFallRate = 200;
    private const float WindScale = 1000;
    private const float SpawnBand = 40;
    private const float WrapMargin = 30;
    private const float RainSlant = 0.005f;
    private const float RainDepthPerLength = 0.0012207031f;
    private const float SnowDrift = 0.02f;
    private const int SnowflakeSizeRange = 3;
    private const int RaindropLengthRange = 0x33;
    private const int DriftRange = 0x65;
    private const int TwinkleOdds = 5;
    private const int SnowTextureSize = 256;
    private const int SnowCell = 4;
    private const int SnowCellMask = 3;
    private const int SnowEdgeReach = 6;
    private const int SnowAgeShift = 3;

    private readonly WeatherVertex[] _snowflakes = new WeatherVertex[MaxSnowflakes * SnowflakeVertexCount];
    private readonly WeatherVertex[] _raindrops = new WeatherVertex[MaxRaindrops * RaindropVertexCount];
    private int _snowflakeCount;
    private int _raindropCount;
    private int _groundSnowStart;
    private int _groundSnowAge;
    private float _snowWind;
    private Vector2 _snowShift;
    private byte _snowWindDirection;
    private float _snowWindSpeed = NoWind;
    private float _rainWind;
    private byte _rainWindDirection;
    private float _rainWindSpeed = NoWind;

    [EngineRoutine(0x004322a0)]
    private void UpdateGroundSnow(Map map)
    {
        if (map.IsPaused || Flags.HasFlag(GraphicsFlags.Software))
            return;

        if (!Environment.HasFlag(MapEnvironment.GroundSnow))
        {
            _groundSnowStart = map.CurrentTime;
            _groundSnowAge = 0;
            return;
        }

        if (_groundSnowAge < SnowAgeLimit)
        {
            _groundSnowAge = (map.CurrentTime - _groundSnowStart) >>> SnowGrowthShift;
        }

        if (Screen is null)
            return;

        var size = Screen.Size;
        var start = new Point(-(int)map.Shift.X & SnowCellMask, -(int)map.Shift.Y & SnowCellMask);
        var ramp = EnsureBuffers().ColorTable;
        var texture = TextureBuffer;
        var format = TextureFormat;
        bool isPalette = format == VidPixelFormat.P8;
        var depth = Screen.Depth;
        int depthWidth = Screen.DepthWidth;

        for (int y = start.Y; y < size.Height; y += SnowCell)
        {
            for (int x = start.X; x < size.Width; x += SnowCell)
            {
                int index = y * depthWidth + x;
                int level;
                int up = y < SnowCell ? int.MaxValue : Math.Abs(depth[index] - depth[index - SnowCell * depthWidth]);

                if (up > SnowEdgeReach)
                {
                    int down = Math.Abs(depth[index] - depth[index + SnowCell * depthWidth]);
                    level = down < SnowEdgeReach + 1 ? (SnowEdgeReach - down) * _groundSnowAge >> SnowAgeShift : -1;
                }
                else
                {
                    level = (SnowEdgeReach - up) * _groundSnowAge >> SnowAgeShift;
                }

                int column = x >> 2;
                int row = y >> 2;

                texture[row * SnowTextureSize + column] = level < 0 ? (ushort)0 : isPalette ? (byte)level : ramp[level];
            }
        }

        SetRenderState(RenderState.SpecularEnable, 0);
        SetAlphaBlend(BlendFactor.One, BlendFactor.InverseSourceColor);
        new Texture(texture, new Size(SnowTextureSize, SnowTextureSize), format, TexturePalette).DrawFlatQuad(Screen, new Rectangle(Point.Empty, Screen.Size),
            new Rectangle(0, 0, size.Width / SnowCell, size.Height / SnowCell), default, Blend);
    }

    [EngineRoutine(0x00432c10)]
    private void DrawSnow(Map map)
    {
        if (!Environment.HasFlag(MapEnvironment.StopSnow) && !Environment.HasFlag(MapEnvironment.BornSnow))
        {
            _snowflakeCount = 0;

            return;
        }

        if (map.IsPaused || Flags.HasFlag(GraphicsFlags.Software))
            return;

        var view = map.Session.Graphics.Viewport;
        float screenRight = view.Right;
        float screenBottom = view.Bottom;
        var flakes = _snowflakes.AsSpan();

        if (_snowWindDirection != WindDirection.Value || !_snowWindSpeed.IsExactly(WindSpeed))
        {
            _snowWind = WindDirection.Sine * WindSpeed * WindScale;
            _snowWindDirection = WindDirection.Value;
            _snowWindSpeed = WindSpeed;
        }

        _snowflakeCount = StepCount(_snowflakeCount, MaxSnowflakes, MapEnvironment.Snow, MapEnvironment.BornSnow, MapEnvironment.StopSnow);

        int spawned = 0;
        for (int i = 0; i < _snowflakeCount; i++)
        {
            var flake = flakes.Slice(i * SnowflakeVertexCount, SnowflakeVertexCount);

            if (flake[0].Color == 0 || flake[0].Position.Z < MinDepth)
            {
                spawned++;
                float size = map.Random.Next() % SnowflakeSizeRange + 2f;
                float x = map.Random.Next() * (screenRight - 1f) * GameRandom.UnitScale;
                float reach = spawned < SnowfallSpawnRows ? SpawnBand : screenBottom;
                float y = map.Random.Next() * reach * GameRandom.UnitScale;
                float depth = map.Random.Next() * (screenBottom + 50f) * DepthScale + MinDepth;
                flake[0] = new WeatherVertex(new Vector3(x - size, y, depth), SnowflakeColor);
                flake[1] = new WeatherVertex(new Vector3(x + size, y, size * DepthPerLength + depth), SnowflakeColor);
                float left = x - size * 0.5f;
                float right = size * 0.5f + x;
                flake[2] = new WeatherVertex(new Vector3(left, y - size, depth), SnowflakeColor);
                flake[3] = new WeatherVertex(new Vector3(right, y + size, depth), SnowflakeColor);
                flake[4] = new WeatherVertex(new Vector3(left, y + size, depth), SnowflakeColor);
                flake[5] = new WeatherVertex(new Vector3(right, y - size, depth), SnowflakeColor);

                continue;
            }

            float fall = (flake[1].Position.Z - flake[0].Position.Z - DepthPerLength) * map.FrameTime * SnowFallRate;
            var offset = new Vector2((_snowWind * fall + 50f - map.Random.Next() % DriftRange) * SnowDrift, fall) - (_snowShift + map.Shift);
            float dz = fall * DepthPerLength;

            if (offset.X + flake[0].Position.X < view.Left - WrapMargin)
            {
                offset.X = screenRight - view.Left + offset.X;
            }

            if (screenRight + WrapMargin < offset.X + flake[0].Position.X)
            {
                offset.X -= screenRight - view.Left;
            }

            if (offset.Y + flake[0].Position.Y < view.Top - WrapMargin)
            {
                offset.Y = screenBottom - view.Top + offset.Y;
            }

            if (screenBottom + WrapMargin < offset.Y + flake[0].Position.Y)
            {
                offset.Y -= screenBottom - view.Top;
            }

            var step = new Vector3(offset, -dz);
            for (int vertex = 0; vertex < SnowflakeVertexCount; vertex++)
            {
                flake[vertex].Position += step;
            }
        }

        _snowShift = -map.Shift;

        int slot = 0;
        for (int i = 0; i < _snowflakeCount; i++)
        {
            if (map.Random.Next() % TwinkleOdds != 0)
                continue;

            var flake = flakes.Slice(slot * SnowflakeVertexCount, SnowflakeVertexCount);
            float centerX = (flake[1].Position.X + flake[0].Position.X) * 0.5f;
            float centerY = (flake[1].Position.Y + flake[0].Position.Y) * 0.5f;
            for (int vertex = 0; vertex < SnowflakeVertexCount; vertex++)
            {
                var position = flake[vertex].Position;
                flake[vertex].Position = position with { X = centerX + position.Y - centerY, Y = position.X + centerY - centerX };
            }

            slot++;
        }

        if (_snowflakeCount > 0)
        {
            SetAlphaBlend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        }

        if (Screen is not null)
        {
            ScreenPrimitives.DrawLines(Screen, _snowflakes.AsSpan(0, _snowflakeCount * 2));
        }
    }

    [EngineRoutine(0x00432770)]
    private void DrawRain(Map map)
    {
        if (!Environment.HasFlag(MapEnvironment.StopRain) && !Environment.HasFlag(MapEnvironment.BornRain))
        {
            _raindropCount = 0;

            return;
        }

        if (map.IsPaused || Flags.HasFlag(GraphicsFlags.Software))
            return;

        var view = map.Session.Graphics.Viewport;
        float screenWidth = ScreenSize.Width;
        float screenBottom = view.Bottom;
        var drops = _raindrops.AsSpan();

        if (_rainWindDirection != WindDirection.Value || !_rainWindSpeed.IsExactly(WindSpeed))
        {
            float wind = WindDirection.Sine * WindSpeed * WindScale;
            if (0 < _raindropCount)
            {
                float change = wind - _rainWind;
                for (int i = 0; i < _raindropCount; i++)
                {
                    var drop = drops.Slice(i * RaindropVertexCount, RaindropVertexCount);
                    var tail = drop[1].Position;
                    drop[1].Position = tail with { X = (tail.Y - drop[0].Position.Y) * change * RainSlant + tail.X };
                }
            }

            _rainWind = wind;
            _rainWindDirection = WindDirection.Value;
            _rainWindSpeed = WindSpeed;
        }

        _raindropCount = StepCount(_raindropCount, MaxRaindrops, MapEnvironment.Rain, MapEnvironment.BornRain, MapEnvironment.StopRain);

        int spawned = 0;
        for (int i = 0; i < _raindropCount; i++)
        {
            var drop = drops.Slice(i * RaindropVertexCount, RaindropVertexCount);

            if (drop[0].Color == 0 || drop[0].Position.X < view.Left || view.Right <= drop[0].Position.X || drop[0].Position.Y < view.Top || screenBottom <= drop[0].Position.Y || drop[0].Position.Z < MinDepth)
            {
                spawned++;
                float length = map.Random.Next() % RaindropLengthRange + 15f;
                float slant = _rainWind * length;
                float x = map.Random.Next() * (view.Right - 1f) * GameRandom.UnitScale;
                float reach = spawned < SnowfallSpawnRows ? SpawnBand : screenBottom;
                float y = map.Random.Next() * reach * GameRandom.UnitScale;
                float depth = (length + 10f) * RainDepthPerLength + MinDepth;
                drop[0] = new WeatherVertex(new Vector3(x, y, depth), RainHeadColor);
                drop[1] = new WeatherVertex(new Vector3(x + slant * -RainSlant, y - length, length * DepthPerLength + depth), RainTailColor);

                continue;
            }

            float fall = (drop[1].Position.Z - drop[0].Position.Z) * map.FrameTime * RainFallRate;
            float shift = _rainWind * fall * RainSlant;
            if (shift + drop[0].Position.X < 0f)
            {
                shift += screenWidth;
            }

            if (screenWidth < shift + drop[0].Position.X)
            {
                shift -= screenWidth;
            }

            var step = new Vector3(shift, fall, -(fall * DepthPerLength));
            for (int vertex = 0; vertex < RaindropVertexCount; vertex++)
            {
                drop[vertex].Position += step;
            }
        }

        if (_raindropCount > 0)
        {
            SetAlphaBlend(BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha);
        }

        if (Screen is not null)
        {
            ScreenPrimitives.DrawLines(Screen, _raindrops.AsSpan(0, _raindropCount * RaindropVertexCount));
        }
    }

    private int StepCount(int count, int full, MapEnvironment weather, MapEnvironment born, MapEnvironment stop)
    {
        var phase = Environment & weather;
        if (phase == weather)
            return full;

        if (phase == born)
        {
            if (count < full)
                return count + 1;

            SetEnvironment(weather);
            return full;
        }

        if (phase != stop)
            return count;

        if (count >= 1)
            return count - 1;

        Environment &= ~weather;
        return count;
    }
}
