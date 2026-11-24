using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ASToolkit.Formats;

namespace ASToolkit.Engine;

internal ref struct RunWalker
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly Rectangle _viewport;
    private readonly int _left;
    private readonly int _pixelSize;
    private int _position;
    private int _x;
    private bool _isInRun;

    public RunWalker(ReadOnlySpan<byte> data, FrameRows rows, int pixelSize)
    {
        _data = data;
        _pixelSize = pixelSize;
        _position = rows.Position;
        Top = rows.Top;
        RowCount = rows.Count;
        End = rows.Top + rows.Count;
        Row = Top;
    }

    public RunWalker(ReadOnlySpan<byte> data, FrameRows rows, int left, int width, Rectangle viewport, int pixelSize)
    {
        _data = data;
        _viewport = viewport;
        _left = left;
        _pixelSize = pixelSize;
        _position = rows.Position;
        _x = left;
        IsClipped = left < viewport.Left || viewport.Right < left + width;
        Top = rows.Top;
        RowCount = rows.Count;
        End = Math.Min(rows.Top + rows.Count, viewport.Bottom);
        if (Top < viewport.Top)
        {
            int skipped = viewport.Top - Top;
            Top += skipped;
            RowCount -= skipped;
            do
            {
                while (!CurrentHeader.IsEnd)
                {
                    _position += CurrentHeader.Count * pixelSize + Unsafe.SizeOf<VidRunHeader>();
                }

                _position += Unsafe.SizeOf<VidRunHeader>();
                skipped--;
            }
            while (skipped != 0);
        }

        Row = Top;
    }

    public bool IsClipped { get; }

    public int Top { get; }

    public int RowCount { get; }

    public int End { get; }

    public int Row { get; private set; }

    public int Start { get; private set; }

    public int Length { get; private set; }

    public int Pixels { get; private set; }

    public int First { get; private set; }

    public int Offset { get; private set; }

    public int Count { get; private set; }

    public bool IsVisible { get; private set; }

    private readonly ref readonly VidRunHeader CurrentHeader => ref MemoryMarshal.AsRef<VidRunHeader>(_data[_position..]);

    public bool MoveNext()
    {
        if (_isInRun)
        {
            _position = Pixels + Length * _pixelSize;
            _x = Start + Length;
            _isInRun = false;
        }

        while (Row < End)
        {
            ref readonly var run = ref CurrentHeader;
            if (!run.IsEnd)
            {
                Start = _x + run.Skip;
                Length = run.Count;
                Pixels = _position + Unsafe.SizeOf<VidRunHeader>();
                Clip();
                _isInRun = true;
                return true;
            }

            _position += Unsafe.SizeOf<VidRunHeader>();
            _x = _left;
            Row++;
        }

        return false;
    }

    private void Clip()
    {
        First = Start;
        Offset = 0;
        Count = Length;
        IsVisible = true;
        if (!IsClipped)
            return;

        int runEnd = Start + Length;
        if (Start < _viewport.Left)
        {
            First = _viewport.Left;
            Offset = _viewport.Left - Start;
            Count = _viewport.Right < runEnd ? _viewport.Right - _viewport.Left : runEnd - _viewport.Left;
            IsVisible = _viewport.Left < runEnd;
        }
        else if (_viewport.Right < runEnd)
        {
            Count = _viewport.Right - Start;
            IsVisible = Start < _viewport.Right;
        }
    }
}
