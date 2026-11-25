using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.InteropServices;
using ASToolkit.Engine.IO;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

public partial class Picture
{
    private const int PageSide = 256;
    private const int PageValues = PageSide * PageSide;
    private const int MinTextureSide = 32;
    private const int ExtraPartCount = 901;

    [EngineRoutine(0x00429e40)]
    private static int ComparePartSizes(in VidTextureFrame first, in VidTextureFrame second)
    {
        int width = Math.Max(first.Source.Width, second.Source.Width);
        int height = Math.Max(first.Source.Height, second.Source.Height);
        return width > height ? second.Source.Width - first.Source.Width : second.Source.Height - first.Source.Height;
    }

    [EngineRoutine(0x00429920)]
    private int ReadPage(Span<byte> destination, ReadOnlySpan<ushort> page, Size size)
    {
        if (!Flags.HasFlag(PictureFlags.Palette) && Flags.HasFlag(PictureFlags.SurfaceVariant))
        {
            // TODO: Port 0x00429920 (DirectDraw: CreateSurface, Lock, Unlock and BltFast into a DXT1 surface, or a DXT3 one with alpha, and the surface error logs)
            return 0;
        }

        int length = 0;
        for (int row = 0; row < size.Height; row++)
        {
            var values = page.Slice(row * PageSide, size.Width);
            if (Flags.HasFlag(PictureFlags.Palette))
            {
                foreach (ushort value in values)
                {
                    destination[length++] = (byte)value;
                }
            }
            else
            {
                MemoryMarshal.AsBytes(values).CopyTo(destination[length..]);
                length += size.Width * sizeof(ushort);
            }
        }

        return length;
    }

    [EngineRoutine(0x00429e80)]
    private void ConvertHardware(ResContainerBuilder builder)
    {
        RangeCoder? colorCoder = null;
        RangeCoder? depthCoder = null;
        if (Flags.HasFlag(PictureFlags.RangeCoded))
        {
            colorCoder = new RangeCoder(Flags.HasFlag(PictureFlags.SurfaceVariant) ? 1 : 2);
            depthCoder = new RangeCoder(2);
        }

        var size = Colors.Image.Size;
        int frameCount = Colors.Image.FrameCount;
        int pageLimit = (size.Height / PageSide + 1) * (size.Width / PageSide + 1) * frameCount + 1;
        var parts = new VidTextureFrame[pageLimit - 1 + ExtraPartCount];
        uint[] checksums = new uint[frameCount];
        int count = frameCount;
        for (int frame = 0; frame < frameCount; frame++)
        {
            int match = FindRepeatedFrame(checksums, frame);
            if (match >= 0)
            {
                parts[frame].Surface = -1;
                parts[frame].Source.Width = match;
                Advance();
                continue;
            }

            var bounds = GetBounds(true, true);
            parts[frame].Surface = 0;
            if (bounds is { Width: <= PageSide, Height: <= PageSide })
            {
                parts[frame].Surface = 0;
                parts[frame].Next = 0;
                parts[frame].Origin = bounds.Location;
                parts[frame].Source.Width = Math.Min(bounds.Right - bounds.Location.X, PageSide);
                parts[frame].Source.Height = Math.Min(bounds.Bottom - bounds.Location.Y, PageSide);
            }
            else
            {
                for (int x = bounds.Left; x < bounds.Right; x += PageSide)
                {
                    int next = count;
                    for (int y = bounds.Top; y < bounds.Bottom; y += PageSide)
                    {
                        if (x == bounds.Left && y == bounds.Top)
                        {
                            parts[frame].Surface = 0;
                            parts[frame].Next = next;
                            parts[frame].Origin = new Point(x, y);
                            parts[frame].Source.Width = Math.Min(bounds.Right - new Point(x, y).X, PageSide);
                            parts[frame].Source.Height = Math.Min(bounds.Bottom - new Point(x, y).Y, PageSide);
                            continue;
                        }

                        next++;
                        parts[count].Surface = 0;
                        parts[count].Next = next;
                        parts[count].Origin = new Point(x, y);
                        parts[count].Source.Width = Math.Min(bounds.Right - new Point(x, y).X, PageSide);
                        parts[count].Source.Height = Math.Min(bounds.Bottom - new Point(x, y).Y, PageSide);
                        count = next;
                    }
                }
            }

            parts[count - 1].Next = 0;
            Advance();
        }

        int[] order = new int[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        CRuntime.Sort(order, (first, second) => ComparePartSizes(parts[first], parts[second]));
        int limit = pageLimit * PageSide;
        int pageCount = 0;
        var place = Point.Empty;
        bool isFitting = false;
        for (int rank = 0; rank < order.Length; rank++)
        {
            ref var part = ref parts[order[rank]];
            if (part.Surface < 0 || part.Source.Height == 0)
                continue;

            if (rank == 0 || part.Source.Width < parts[order[rank - 1]].Source.Width || part.Source.Height < parts[order[rank - 1]].Source.Height)
            {
                place.Y = 0;
            }

            if (place.Y > limit - part.Source.Height)
            {
                if (!isFitting)
                {
                    ThrowPartDoesNotFit();
                }
            }
            else
            {
                while (true)
                {
                    int rest = place.Y & (PageSide - 1);
                    if (place.Y > place.Y - rest - part.Source.Height + PageSide)
                    {
                        int nextPage = place.Y - rest + PageSide;
                        if (place.Y < nextPage)
                        {
                            place.Y = nextPage;
                        }
                    }

                    place.X = 0;
                    bool isPlaced;
                    if (PageSide - part.Source.Width < 0)
                    {
                        isPlaced = isFitting;
                    }
                    else
                    {
                        while (true)
                        {
                            isFitting = true;
                            bool isOverlapping = false;
                            int right = 0;
                            for (int other = rank - 1; other >= 0; other--)
                            {
                                ref readonly var placed = ref parts[order[other]];
                                if (placed.Surface < 0)
                                    continue;

                                int distanceX = Math.Abs((placed.Source.Width + placed.Source.X * 2) / 2 - (part.Source.Width + place.X * 2) / 2);
                                if (distanceX >= (placed.Source.Width + part.Source.Width) / 2)
                                    continue;

                                int distanceY = Math.Abs((placed.Source.Height + placed.Source.Y * 2) / 2 - (part.Source.Height + place.Y * 2) / 2);
                                if (distanceY >= (placed.Source.Height + part.Source.Height) / 2)
                                    continue;

                                right = placed.Source.Width + placed.Source.X;
                                isOverlapping = true;
                                break;
                            }

                            if (!isOverlapping)
                            {
                                isPlaced = true;
                                break;
                            }

                            place.X = right;
                            isFitting = false;
                            if (place.X > PageSide - part.Source.Width)
                            {
                                isPlaced = false;
                                break;
                            }
                        }
                    }

                    if (isPlaced)
                        break;

                    place.Y++;
                    if (place.Y > limit - part.Source.Height)
                    {
                        ThrowPartDoesNotFit();
                    }
                }
            }

            int page = place.Y / PageSide;
            part.Surface = page;
            if (Flags.HasFlag(PictureFlags.Depth))
            {
                part.Surface *= 2;
            }

            if (pageCount <= page)
            {
                pageCount = page + 1;
            }

            part.Source.X = place.X;
            part.Source.Y = place.Y;
        }

        Rewind();
        byte[] scratch = ArrayPool<byte>.Shared.Rent(MaxRecordSize);
        try
        {
            ushort[] colorSheet = new ushort[pageCount * PageValues];
            ushort[] depthSheet = new ushort[pageCount * PageValues];
            for (int frame = 0; frame < frameCount; frame++)
            {
                if (parts[frame].Surface < 0)
                {
                    Advance();
                    continue;
                }

                int i = frame;
                do
                {
                    ref readonly var part = ref parts[i];
                    for (int x = part.Origin.X; x < part.Origin.X + part.Source.Width; x++)
                    {
                        for (int y = part.Origin.Y; y < part.Origin.Y + part.Source.Height; y++)
                        {
                            int cell = (part.Source.Y - part.Origin.Y + y) * PageSide - part.Origin.X + x + part.Source.X;
                            if (Flags.HasFlag(PictureFlags.Depth))
                            {
                                depthSheet[cell] = (ushort)GetDepth(new Point(x, y));
                            }

                            colorSheet[cell] = (ushort)GetHighColor(new Point(x, y));
                        }
                    }

                    i = part.Next;
                }
                while (i != 0);

                Advance();
            }

            for (int i = 0; i < count; i++)
            {
                parts[i].Source.Y %= PageSide;
            }

            for (int i = 0; i < count; i++)
            {
                if (parts[i].Surface < 0)
                {
                    parts[i] = parts[parts[i].Source.Width];
                }
            }

            var surfaces = new ResRecordBuilder();
            surfaces.Write((short)(Flags.HasFlag(PictureFlags.Depth) ? pageCount * 2 : pageCount));
            for (int page = 0; page < pageCount; page++)
            {
                int surface = Flags.HasFlag(PictureFlags.Depth) ? page * 2 : page;
                int right = 0;
                int bottom = 0;
                foreach (ref readonly var surfacePart in parts.AsSpan(0, count))
                {
                    if (surfacePart.Surface != surface)
                        continue;

                    right = Math.Max(right, surfacePart.Source.Right);
                    bottom = Math.Max(bottom, surfacePart.Source.Bottom);
                }

                var texture = new Size(MinTextureSide, MinTextureSide);
                while (texture.Width < right)
                {
                    texture.Width *= 2;
                }

                while (texture.Height < bottom)
                {
                    texture.Height *= 2;
                }

                if (texture.Width > PageSide)
                {
                    Colors.Image.Report(LogError.Invalid, "SurfSizeX"u8, texture.Width);
                }

                if (texture.Height > PageSide)
                {
                    Colors.Image.Report(LogError.Invalid, "SurfSizeY"u8, texture.Height);
                }

                surfaces.Write((short)texture.Width);
                surfaces.Write((short)texture.Height);
                int length = ReadPage(scratch, colorSheet.AsSpan(page * PageValues, PageValues), texture);
                surfaces.Write(length);
                surfaces.WriteBody(scratch.AsSpan(0, length), colorCoder);
                if (Flags.HasFlag(PictureFlags.Depth))
                {
                    length = 0;
                    for (int row = 0; row < texture.Height; row++)
                    {
                        MemoryMarshal.AsBytes(depthSheet.AsSpan(page * PageValues + row * PageSide, texture.Width)).CopyTo(scratch.AsSpan(length));
                        length += texture.Width * sizeof(ushort);
                    }

                    surfaces.Write(length);
                    surfaces.WriteBody(scratch.AsSpan(0, length), depthCoder);
                }
            }

            builder.Chunks.Add(CreateChunk("SURF", [surfaces.Bytes.ToArray()], surfaces.CompressionSavings));
            builder.Chunks.Add(CreateChunk("DATA", [MemoryMarshal.AsBytes(parts.AsSpan(0, count)).ToArray()]));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    [DoesNotReturn]
    private void ThrowPartDoesNotFit()
    {
        Colors.Image.Report(LogError.Text, "Can't replace rectangle"u8);
        throw new InvalidOperationException("A frame of the picture does not fit in its textures.");
    }
}
