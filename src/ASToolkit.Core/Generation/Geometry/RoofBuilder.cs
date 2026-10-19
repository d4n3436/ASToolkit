using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Levels;

namespace ASToolkit.Core.Generation.Geometry;

/// <summary>
/// Provides the earth that covers everything around the walkable floor.
/// </summary>
public static class RoofBuilder
{
    private const int GroundDepth = 1024;
    private const int StepX = 40;
    private const int StepY = 28;
    private const int Margin = 300;
    private const int GroundProbe = 6;
    private const int CapProbe = 16;
    private const int UnitHeight = 96;
    private const int UnitDrop = 8;
    private const int CandidateStep = 2;
    private const int Seam = 3;
    private const int Block = 8;
    private const int StripWidth = 128;
    private const int LabRoofZ = 90;
    private const int MilitaryRoofZ = 95;
    private const byte None = 0;
    private const byte Floor = 1;
    private const byte Wall = 2;
    private const byte KindMask = 3;
    private const byte Ground = 4;
    private const byte Forbidden = 8;
    private const byte Target = 16;
    private const byte ShownFloor = 32;
    private const byte ClosetFloor = 64;

    /// <summary>
    /// Adds the earth tiles to a map and returns the number of pixels left uncovered.
    /// </summary>
    public static int Build(Random rng, MapBuilder map, Lattice lattice, IReadOnlySet<Cell> shown, IReadOnlySet<Cell> closets, LevelTheme theme, SpriteLibrary sprites)
    {
        var earth = sprites.Find(KnownObject.Earth) ?? throw new InvalidDataException("The earth sprite 501 is missing.");
        var tile = new TileMask(earth.View.GetFrame(0), earth.View.Width, earth.View.Height);
        var floors = map.Sprites
            .Where(s => IsFloor(s.Id) && s.Z >= 0)
            .ToArray();
        
        double minX = floors.Min(s => s.X) - Margin;
        double maxX = floors.Max(s => s.X) + Margin;
        double minY = floors.Min(s => s.Y) - Margin;
        double maxY = floors.Max(s => s.Y) + Margin;
        var area = new Area((int)Math.Floor(minX), (int)Math.Floor(minY - Margin), (int)Math.Ceiling(maxX - minX), (int)Math.Ceiling(maxY - minY + Margin));
        var structure = GetStructure(map, sprites, area);

        var floor = new BitMask(area.Width, area.Height);
        ForEachStrip(area, strip =>
        {
            using var raster = new Raster(strip, area);
            raster.Draw(structure);
            raster.CopyFloor(floor);
        });

        var grounds = AddGroundEarth(rng, map, floor, area, tile, floors, theme);
        int roofZ = theme == LevelTheme.Lab ? LabRoofZ : MilitaryRoofZ;
        var target = new BitMask(area.Width, area.Height);
        var forbidden = new BitMask(area.Width, area.Height);
        ForEachStrip(area, strip =>
        {
            using var raster = new Raster(strip, area);
            raster.Draw(structure);
            raster.MarkGround(tile, grounds);
            raster.Classify(lattice, shown, closets, GroundDepth + 8 * roofZ, target, forbidden);
        });

        return Cover(tile, target, forbidden, (cx, cy) => map.Add(KnownObject.Earth, cx + area.X0, cy + area.Y0 + roofZ, roofZ, rng.Next(256), unique: false));
    }

    private static bool IsFloor(KnownObject id) => id is KnownObject.BasementFloor or KnownObject.BasementFloorAlt or KnownObject.MilitaryFloor;

    private static StructureSprite[] GetStructure(MapBuilder map, SpriteLibrary sprites, Area area)
    {
        var structure = new List<StructureSprite>();
        foreach (var sprite in map.Sprites.Where(s => IsFloor(s.Id) || KnownObject.LabWalls.Contains(s.Id) || KnownObject.MilitaryWalls.Contains(s.Id)))
        {
            var found = sprites.Find(sprite.Id);
            if (!found.HasValue)
            {
                continue;
            }

            var (obj, file) = found.Value;
            int left = (int)Math.Round(sprite.X - file.Width / 2.0) - area.X0;
            int top = (int)Math.Round(sprite.Y - sprite.Z - file.Height / 2.0) - area.Y0;
            structure.Add(new StructureSprite(sprite, obj, file, file.GetFrame(obj.GetFrame(sprite.Dir, file.FrameCount)), IsFloor(sprite.Id) ? Floor : Wall, left, top));
        }

        return structure.ToArray();
    }

    // Keep strips on 64-pixel boundaries to stop them from sharing a bit mask word
    private static void ForEachStrip(Area area, Action<Strip> body) =>
        Parallel.For(0, (area.Width + StripWidth - 1) / StripWidth, i => body(new Strip(i * StripWidth, Math.Min(StripWidth, area.Width - i * StripWidth))));

    // Use the other parity on military levels, where a 503 spans two earth tiles
    private static (int Cx, int Cy)[] AddGroundEarth(Random rng, MapBuilder map, BitMask floor, Area area, TileMask tile, MapSprite[] floors, LevelTheme theme)
    {
        var first = floors[0];
        double ox = first.X;
        double oy = first.Y;
        int parity = theme == LevelTheme.Lab ? 0 : 1;
        var taken = floors.Select(s => ((int)Math.Round((s.X - ox) / StepX), (int)Math.Round((s.Y - oy) / StepY))).ToHashSet();
        int i0 = (int)Math.Floor((area.X0 - ox) / StepX) - 2;
        int i1 = (int)Math.Ceiling((area.X0 + area.Width - ox) / StepX) + 2;
        int j0 = (int)Math.Floor((area.Y0 - oy) / StepY) - 2;
        int j1 = (int)Math.Ceiling((area.Y0 + area.Height - oy) / StepY) + 2;
        var grounds = new List<(int, int)>();
        for (int index = i0; index <= i1; index++)
        {
            for (int innerIndex = j0; innerIndex <= j1; innerIndex++)
            {
                if (((index + innerIndex) & 1) != parity || taken.Contains((index, innerIndex)))
                {
                    continue;
                }

                double x = ox + StepX * index;
                double y = oy + StepY * innerIndex;
                (int cx, int cy) = ((int)Math.Round(x), (int)Math.Round(y));
                if (!TouchesFloor(floor, area, tile, cx, cy))
                {
                    grounds.Add((cx, cy));
                    map.Add(KnownObject.Earth, x, y, 0, rng.Next(256), unique: false);
                }
            }
        }

        return grounds.ToArray();
    }

    // Test the tile without the seam it shares with its neighbors
    private static bool TouchesFloor(BitMask floor, Area area, TileMask tile, int cx, int cy)
    {
        foreach ((int dy, int x0, int x1) in tile.Inner)
        {
            int y = cy + dy - area.Y0;
            int from = Math.Max(0, cx + x0 - area.X0);
            int to = Math.Min(area.Width - 1, cx + x1 - area.X0);
            if (y >= 0 && y < area.Height && from <= to && floor.Count(y, from, to) > 0)
            {
                return true;
            }
        }

        return false;
    }

    // Greedy cover with lazy scores. Requeue candidates popped with a stale score.
    private static int Cover(TileMask tile, BitMask target, BitMask forbidden, Action<int, int> place)
    {
        var blocks = target.CountBlocks(Block);
        int rowCount = Math.Max(0, (target.Height - tile.Bottom + tile.Top + CandidateStep - 1) / CandidateStep);
        var found = new (int Cx, int Score)[rowCount][];
        Parallel.For(0, rowCount, i =>
        {
            int cy = -tile.Top + i * CandidateStep;
            var list = new List<(int, int)>();
            for (int cx = tile.HalfWidth; cx < target.Width - tile.HalfWidth; cx += CandidateStep)
            {
                if (blocks.Sum(cx - tile.HalfWidth, cy + tile.Top, cx + tile.HalfWidth, cy + tile.Bottom) == 0 || forbidden.Any(tile, cx, cy))
                {
                    continue;
                }

                int score = target.Count(tile, cx, cy);
                if (score > 0)
                {
                    list.Add((cx, score));
                }
            }

            found[i] = list.ToArray();
        });

        var queue = new PriorityQueue<(int Cx, int Cy), int>(found.Sum(l => l.Length));
        for (int index = 0; index < rowCount; index++)
        {
            foreach ((int cx, int score) in found[index])
            {
                queue.Enqueue((cx, -tile.Top + index * CandidateStep), -score);
            }
        }

        while (queue.TryDequeue(out var c, out int negative))
        {
            int score = target.Count(tile, c.Cx, c.Cy);
            if (score == 0)
            {
                continue;
            }

            if (score < -negative)
            {
                queue.Enqueue(c, -score);
                continue;
            }

            foreach ((int dy, int x0, int x1) in tile.Rows)
            {
                target.Clear(c.Cy + dy, c.Cx + x0, c.Cx + x1);
            }

            place(c.Cx, c.Cy);
        }

        return target.CountAll();
    }

    private readonly record struct Area(int X0, int Y0, int Width, int Height);

    private readonly record struct Strip(int X, int Width);

    private sealed record StructureSprite(MapSprite Sprite, GameObject Object, VidResource View, VidFrame Frame, byte Kind, int Left, int Top);

    private sealed class TileMask
    {
        public TileMask(VidFrame frame, int width, int height)
        {
            int left = (int)Math.Round(-width / 2.0);
            int top = (int)Math.Round(-height / 2.0);
            
            var rows = new List<(int Dy, int X0, int X1)>();
            foreach (var run in frame)
            {
                int last = run.X + run.Count - 1 + left;
                if (rows.Count > 0 && rows[^1].Dy == run.Y + top)
                {
                    rows[^1] = (rows[^1].Dy, rows[^1].X0, last);
                }
                else
                {
                    rows.Add((run.Y + top, run.X + left, last));
                }
            }

            Rows = rows.ToArray();
            
            Inner = Rows
                .Skip(Seam)
                .SkipLast(Seam)
                .Select(r => (r.Dy, r.X0 + Seam, r.X1 - Seam)).Where(r => r.Item2 <= r.Item3)
                .ToArray();
            
            HalfWidth = Rows.Max(r => Math.Max(-r.X0, r.X1)) + 1;
            Top = Rows.Min(r => r.Dy);
            Bottom = Rows.Max(r => r.Dy);
        }

        public (int Dy, int X0, int X1)[] Rows { get; }

        public (int Dy, int X0, int X1)[] Inner { get; }

        public int HalfWidth { get; }

        public int Top { get; }

        public int Bottom { get; }
    }

    private sealed class BitMask
    {
        private readonly ulong[] _bits;
        private readonly int _stride;

        public BitMask(int width, int height)
        {
            Width = width;
            Height = height;
            _stride = (width + 63) >> 6;
            _bits = new ulong[_stride * height];
        }

        public int Width { get; }

        public int Height { get; }

        public void Set(int x, int y) => _bits[y * _stride + (x >> 6)] |= 1UL << x;

        // Callers must keep the span inside the mask
        public int Count(int y, int x0, int x1)
        {
            ref ulong row = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_bits), y * _stride);
            int w0 = x0 >> 6;
            int w1 = x1 >> 6;
            ulong first = ~0UL << x0;
            ulong last = ~0UL >> (~x1 & 63);
            if (w0 == w1)
            {
                return BitOperations.PopCount(Unsafe.Add(ref row, w0) & first & last);
            }

            int total = BitOperations.PopCount(Unsafe.Add(ref row, w0) & first) + BitOperations.PopCount(Unsafe.Add(ref row, w1) & last);
            for (int index = w0 + 1; index < w1; index++)
            {
                total += BitOperations.PopCount(Unsafe.Add(ref row, index));
            }

            return total;
        }

        public int Count(TileMask tile, int cx, int cy)
        {
            int total = 0;
            foreach ((int dy, int x0, int x1) in tile.Rows)
            {
                total += Count(cy + dy, cx + x0, cx + x1);
            }

            return total;
        }

        public bool Any(TileMask tile, int cx, int cy)
        {
            foreach ((int dy, int x0, int x1) in tile.Rows)
            {
                if (Count(cy + dy, cx + x0, cx + x1) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public void Clear(int y, int x0, int x1)
        {
            var row = _bits.AsSpan(y * _stride, _stride);
            int w0 = x0 >> 6;
            int w1 = x1 >> 6;
            ulong first = ~0UL << x0;
            ulong last = ~0UL >> (~x1 & 63);
            if (w0 == w1)
            {
                row[w0] &= ~(first & last);
                return;
            }

            row[w0] &= ~first;
            row[w1] &= ~last;
            row[(w0 + 1)..w1].Clear();
        }

        public int CountAll()
        {
            int total = 0;
            foreach (ulong word in _bits)
            {
                total += BitOperations.PopCount(word);
            }

            return total;
        }

        public SummedArea CountBlocks(int size)
        {
            var area = new SummedArea((Width + size - 1) / size, (Height + size - 1) / size, size);
            for (int y = 0; y < Height; y++)
            {
                for (int index = 0; index < _stride; index++)
                {
                    ulong word = _bits[y * _stride + index];
                    while (word != 0)
                    {
                        area.Add(((index << 6) + BitOperations.TrailingZeroCount(word)) / size, y / size);
                        word &= word - 1;
                    }
                }
            }

            area.Accumulate();
            return area;
        }
    }

    private sealed class SummedArea
    {
        private readonly int[] _sums;
        private readonly int _width;
        private readonly int _height;
        private readonly int _size;

        public SummedArea(int width, int height, int size)
        {
            _width = width;
            _height = height;
            _size = size;
            _sums = new int[(width + 1) * (height + 1)];
        }

        public void Add(int bx, int by) => _sums[(by + 1) * (_width + 1) + bx + 1]++;

        public void Accumulate()
        {
            int stride = _width + 1;
            for (int y = 1; y <= _height; y++)
            {
                for (int x = 1; x <= _width; x++)
                {
                    int p = y * stride + x;
                    _sums[p] += _sums[p - 1] + _sums[p - stride] - _sums[p - stride - 1];
                }
            }
        }

        // Zero means the rectangle is empty
        public int Sum(int x0, int y0, int x1, int y1)
        {
            int bx0 = Math.Clamp(x0 / _size, 0, _width);
            int by0 = Math.Clamp(y0 / _size, 0, _height);
            int bx1 = Math.Clamp(x1 / _size + 1, 0, _width);
            int by1 = Math.Clamp(y1 / _size + 1, 0, _height);
            int stride = _width + 1;
            return _sums[by1 * stride + bx1] - _sums[by0 * stride + bx1] - _sums[by1 * stride + bx0] + _sums[by0 * stride + bx0];
        }
    }

    // Vertical strip of the level's pixels. Passes read only their own columns.
    private sealed class Raster : IDisposable
    {
        private static readonly int[] NearRows = [-StepY * 2, 0, StepY * 2];

        private readonly short[] _depth;
        private readonly byte[] _flags;
        private readonly int _offset;
        private readonly int _width;
        private readonly int _height;
        private readonly int _x0;
        private readonly int _y0;

        public Raster(Strip strip, Area area)
        {
            _offset = strip.X;
            _width = strip.Width;
            _height = area.Height;
            _x0 = area.X0 + strip.X;
            _y0 = area.Y0;
            int length = _width * _height;
            _depth = ArrayPool<short>.Shared.Rent(length);
            _depth.AsSpan(0, length).Fill(GroundDepth);
            _flags = ArrayPool<byte>.Shared.Rent(length);
            _flags.AsSpan(0, length).Clear();
        }

        public void Draw(StructureSprite[] structure)
        {
            foreach (var sprite in structure)
            {
                if (sprite.Left + sprite.View.Width >= _offset && sprite.Left < _offset + _width)
                {
                    Draw(sprite);
                }
            }
        }

        public void CopyFloor(BitMask floor)
        {
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    if ((_flags[y * _width + x] & KindMask) == Floor)
                    {
                        floor.Set(_offset + x, y);
                    }
                }
            }
        }

        public void MarkGround(TileMask tile, (int Cx, int Cy)[] grounds)
        {
            foreach ((int cx, int cy) in grounds)
            {
                if (cx + tile.HalfWidth < _x0 || cx - tile.HalfWidth >= _x0 + _width)
                {
                    continue;
                }

                foreach ((int dy, int x0, int x1) in tile.Rows)
                {
                    int y = cy + dy - _y0;
                    if (y < 0 || y >= _height)
                    {
                        continue;
                    }

                    int from = Math.Max(0, cx + x0 - _x0);
                    int to = Math.Min(_width - 1, cx + x1 - _x0);
                    for (int x = from; x <= to; x++)
                    {
                        _flags[y * _width + x] |= Ground;
                    }
                }
            }
        }

        // A wall pixel stands (depth - 1024) / 8 px above its ground point
        public void Classify(Lattice lattice, IReadOnlySet<Cell> shown, IReadOnlySet<Cell> closets, int roofDepth, BitMask target, BitMask forbidden)
        {
            for (int y = 0; y < _height; y++)
            {
                double wy = _y0 + y;
                var lastCell = new Cell(int.MinValue, int.MinValue);
                (bool Shown, bool Closet) lastIn = default;
                for (int x = 0; x < _width; x++)
                {
                    int p = y * _width + x;
                    double wx = _x0 + x;
                    var cell = lattice.GetCell(wx, wy);
                    if (cell != lastCell)
                    {
                        lastCell = cell;
                        lastIn = (shown.Contains(cell), closets.Contains(cell));
                    }

                    int kind = _flags[p] & KindMask;
                    if (kind != Wall && lastIn.Closet)
                    {
                        _flags[p] |= ClosetFloor | Target;
                        continue;
                    }

                    switch (kind)
                    {
                        case Floor when lastIn.Shown:
                            _flags[p] |= ShownFloor | Forbidden;
                            break;
                        case Floor:
                            _flags[p] |= Target;
                            break;
                        case Wall:
                            double ground = wy + (_depth[p] - GroundDepth) / 8.0;
                            bool cap = _depth[p] > roofDepth;
                            int probe = cap ? CapProbe : GroundProbe;
                            bool front = shown.Contains(lattice.GetCell(wx, ground + probe));
                            bool back = shown.Contains(lattice.GetCell(wx, ground - probe));
                            bool visible = cap ? front || back : front;
                            if (visible && !cap)
                            {
                                _flags[p] |= Forbidden;
                            }
                            else if (!visible && !cap)
                            {
                                _flags[p] |= Target;
                            }

                            break;
                        default:
                            if ((_flags[p] & Ground) == 0 && IsNearFloor(x, y))
                            {
                                _flags[p] |= Target;
                            }

                            break;
                    }
                }
            }

            MarkUnitZones(roofDepth);

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    byte f = _flags[y * _width + x];
                    if ((f & Forbidden) != 0)
                    {
                        forbidden.Set(_offset + x, y);
                    }
                    else if ((f & Target) != 0)
                    {
                        target.Set(_offset + x, y);
                    }
                }
            }
        }

        public void Dispose()
        {
            ArrayPool<short>.Shared.Return(_depth);
            ArrayPool<byte>.Shared.Return(_flags);
        }

        private void Draw(StructureSprite s)
        {
            var frame = s.Frame;
            int left = s.Left - _offset;
            int baseDepth = (int)(s.Sprite.Z * 8);
            if (s.Object.Flags.HasFlag(ObjectFlags.Raised) && baseDepth < 0x3fff)
            {
                baseDepth += 0x3fff;
            }

            bool hasDepth = s.View.HasDepth;
            bool standing = s.Object.ExtentY < s.Object.ExtentZ;
            int flat = Math.Min(baseDepth + 0x400, 0x7fff);
            foreach (var run in frame)
            {
                int y = s.Top + run.Y;
                if (y < 0 || y >= _height)
                {
                    continue;
                }

                int rowDepth = standing ? flat + (frame.RowCount - (run.Y - frame.Top)) * 8 : flat;
                for (int pixelIndex = 0; pixelIndex < run.Count; pixelIndex++)
                {
                    int x = left + run.X + pixelIndex;
                    if (x < 0 || x >= _width)
                    {
                        continue;
                    }

                    int depth = hasDepth ? baseDepth + run.GetDepth(pixelIndex) : rowDepth;
                    int p = y * _width + x;
                    if (hasDepth ? _depth[p] < depth : _depth[p] <= depth)
                    {
                        _depth[p] = (short)depth;
                        _flags[p] = s.Kind;
                    }
                }
            }
        }

        // Forbid rows up to a unit's height above shown floor and a few below it, stopping at caps
        private void MarkUnitZones(int roofDepth)
        {
            int[] sinceShown = new int[_width];
            int[] sinceCloset = new int[_width];
            sinceShown.AsSpan().Fill(int.MaxValue);
            sinceCloset.AsSpan().Fill(int.MaxValue);
            for (int y = _height - 1; y >= 0; y--)
            {
                for (int x = 0; x < _width; x++)
                {
                    int p = y * _width + x;
                    bool cap = IsCap(p, roofDepth);
                    sinceShown[x] = (_flags[p] & ShownFloor) != 0 ? 0 : cap || sinceShown[x] == int.MaxValue ? int.MaxValue : sinceShown[x] + 1;
                    sinceCloset[x] = (_flags[p] & ClosetFloor) != 0 ? 0 : cap || sinceCloset[x] == int.MaxValue ? int.MaxValue : sinceCloset[x] + 1;
                    if (sinceShown[x] <= UnitHeight && !cap)
                    {
                        _flags[p] |= Forbidden;
                    }
                    else if (sinceCloset[x] <= UnitHeight && !cap && (_flags[p] & Forbidden) == 0)
                    {
                        _flags[p] |= Target;
                    }
                }
            }

            sinceShown.AsSpan().Fill(int.MaxValue);
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int p = y * _width + x;
                    sinceShown[x] = (_flags[p] & ShownFloor) != 0 ? 0 : sinceShown[x] == int.MaxValue ? int.MaxValue : sinceShown[x] + 1;
                    if (sinceShown[x] <= UnitDrop && !IsCap(p, roofDepth))
                    {
                        _flags[p] |= Forbidden;
                    }
                }
            }
        }

        private bool IsCap(int p, int roofDepth) => (_flags[p] & KindMask) == Wall && _depth[p] > roofDepth;

        private bool IsNearFloor(int x, int y)
        {
            foreach (int dy in NearRows)
            {
                int yy = y + dy;
                if (yy >= 0 && yy < _height && (_flags[yy * _width + x] & KindMask) != None)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
