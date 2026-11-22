using System.Drawing;
using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Enumerates the runs of a frame without allocating.
/// </summary>
public struct VidFrameEnumerator
{
    private readonly VidFrame _frame;
    private int _position;
    private int _row;
    private int _x;
    private int _pixel;
    private int _pending;
    private int _sample;

    internal VidFrameEnumerator(VidFrame frame)
    {
        _frame = frame;
    }

    /// <summary>
    /// Gets the current run.
    /// </summary>
    public VidRun Current { get; private set; }

    /// <summary>
    /// Moves to the next run that has pixels.
    /// </summary>
    /// <returns><see langword="true"/> if there is another run; otherwise, <see langword="false"/>.</returns>
    public bool MoveNext() => _frame.Area.Width > 0 ? MoveNextLinear() : MoveNextRows();

    private bool MoveNextRows()
    {
        var stream = _frame.Stream.Span;
        while (_row < _frame.Area.Height)
        {
            if (_position + 2 > stream.Length)
            {
                return false;
            }

            var header = MemoryMarshal.Read<VidRunHeader>(stream[_position..]);
            byte count = header.Count;
            _position += 2;
            if (header.IsEnd)
            {
                _row++;
                _x = 0;
                continue;
            }

            _x += header.Skip;
            int depthLength = _frame.HasDepth ? 2 * count : 0;
            int colorLength = _frame.PixelSize * count;
            if (_position + depthLength + colorLength > stream.Length)
            {
                return false;
            }

            bool found = count > 0;
            if (found)
            {
                Current = new VidRun(new Point(_x, _frame.Area.Y + _row), count, _frame.Stream.Slice(_position, depthLength), 2,
                    _frame.Stream.Slice(_position + depthLength, colorLength), _frame.PixelSize, _frame.IsPaletted);
            }

            _position += depthLength + colorLength;
            _x += count;
            if (found)
            {
                return true;
            }
        }

        return false;
    }

    private bool MoveNextLinear()
    {
        var stream = _frame.Stream.Span;
        while (true)
        {
            if (_pending == 0)
            {
                if (_position + 2 > stream.Length)
                {
                    return false;
                }

                var header = MemoryMarshal.Read<VidRunHeader>(stream[_position..]);
                if (header.IsEnd)
                {
                    return false;
                }

                _position += 2;
                _pixel += header.Skip;
                _pending = header.Count;
                _sample = _position;
                _position += header.Count * _frame.Stride;
                if (_position > stream.Length)
                {
                    return false;
                }

                continue;
            }

            int row = Math.DivRem(_pixel, _frame.Area.Width, out int column);
            if (row >= _frame.Area.Height)
            {
                return false;
            }

            int length = Math.Min(_pending, _frame.Area.Width - column);
            int x = _frame.Area.X + column;
            int visible = Math.Min(length, _frame.CanvasWidth - x);
            bool found = visible > 0;
            if (found)
            {
                var samples = _frame.Stream.Slice(_sample, visible * _frame.Stride);
                Current = new VidRun(new Point(x, _frame.Area.Y + row), visible, _frame.HasDepth ? samples[(_frame.Stride - 2)..] : default, _frame.Stride, samples, _frame.Stride, _frame.IsPaletted);
            }

            _pixel += length;
            _pending -= length;
            _sample += length * _frame.Stride;
            if (found)
            {
                return true;
            }
        }
    }
}
