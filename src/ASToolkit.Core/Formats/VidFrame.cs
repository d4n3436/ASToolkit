namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a read-only view of the pixels of one sprite frame, enumerated as runs from the top row down.
/// </summary>
public readonly struct VidFrame
{
    private readonly ReadOnlyMemory<byte> _stream;
    private readonly bool _hasDepth;
    private readonly bool _isPaletted;
    private readonly int _colorSize;
    private readonly int _stride;
    private readonly int _width;
    private readonly int _left;
    private readonly int _canvasWidth;

    internal VidFrame(ReadOnlyMemory<byte> rows, int top, int rowCount, bool hasDepth, bool isPaletted)
    {
        _stream = rows;
        Top = top;
        RowCount = rowCount;
        _hasDepth = hasDepth;
        _isPaletted = isPaletted;
        _colorSize = isPaletted ? 1 : 2;
        _stride = _colorSize;
    }

    internal VidFrame(ReadOnlyMemory<byte> stream, int left, int top, int width, int rowCount, int canvasWidth, bool hasDepth, bool isPaletted, int colorSize, int stride, VidMesh mesh)
    {
        _stream = stream;
        Mesh = mesh;
        _left = left;
        Top = top;
        _width = width;
        RowCount = rowCount;
        _canvasWidth = canvasWidth;
        _hasDepth = hasDepth;
        _isPaletted = isPaletted;
        _colorSize = colorSize;
        _stride = stride;
    }

    /// <summary>
    /// Gets the first row of the frame in canvas coordinates.
    /// </summary>
    public int Top { get; }

    /// <summary>
    /// Gets the triangles that give the pixels a height; empty for frames that carry none.
    /// </summary>
    public VidMesh Mesh { get; }

    /// <summary>
    /// Gets the number of rows the frame declares, including those without pixels.
    /// </summary>
    public int RowCount { get; }

    /// <summary>
    /// Gets the size in bytes of one color: one for a palette index, two for a 16-bit color, four for a 32-bit color.
    /// </summary>
    public int PixelSize => _colorSize;

    /// <summary>
    /// Returns an enumerator over the runs of the frame.
    /// </summary>
    public Enumerator GetEnumerator() => new(this);

    /// <summary>
    /// Enumerates the runs of a frame without allocating.
    /// </summary>
    public struct Enumerator
    {
        private readonly VidFrame _frame;
        private int _position;
        private int _row;
        private int _x;
        private int _pixel;
        private int _pending;
        private int _sample;

        internal Enumerator(VidFrame frame)
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
        public bool MoveNext() => _frame._width > 0 ? MoveNextLinear() : MoveNextRows();

        private bool MoveNextRows()
        {
            var stream = _frame._stream.Span;
            while (_row < _frame.RowCount)
            {
                if (_position + 2 > stream.Length)
                {
                    return false;
                }

                byte skip = stream[_position];
                byte count = stream[_position + 1];
                _position += 2;
                if (skip == 0 && count == 0)
                {
                    _row++;
                    _x = 0;
                    continue;
                }

                _x += skip;
                int depthLength = _frame._hasDepth ? 2 * count : 0;
                int colorLength = _frame._colorSize * count;
                if (_position + depthLength + colorLength > stream.Length)
                {
                    return false;
                }

                bool found = count > 0;
                if (found)
                {
                    Current = new VidRun(_x, _frame.Top + _row, count, _frame._stream.Slice(_position, depthLength), 2,
                        _frame._stream.Slice(_position + depthLength, colorLength), _frame._colorSize, _frame._isPaletted);
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
            var stream = _frame._stream.Span;
            while (true)
            {
                if (_pending == 0)
                {
                    if (_position + 2 > stream.Length)
                    {
                        return false;
                    }

                    byte skip = stream[_position];
                    byte count = stream[_position + 1];
                    if (skip == 0 && count == 0)
                    {
                        return false;
                    }

                    _position += 2;
                    _pixel += skip;
                    _pending = count;
                    _sample = _position;
                    _position += count * _frame._stride;
                    if (_position > stream.Length)
                    {
                        return false;
                    }

                    continue;
                }

                int row = Math.DivRem(_pixel, _frame._width, out int column);
                if (row >= _frame.RowCount)
                {
                    return false;
                }

                int length = Math.Min(_pending, _frame._width - column);
                int x = _frame._left + column;
                int visible = Math.Min(length, _frame._canvasWidth - x);
                bool found = visible > 0;
                if (found)
                {
                    var samples = _frame._stream.Slice(_sample, visible * _frame._stride);
                    Current = new VidRun(x, _frame.Top + row, visible, _frame._hasDepth ? samples[(_frame._stride - 2)..] : default, _frame._stride, samples, _frame._stride, _frame._isPaletted);
                }

                _pixel += length;
                _pending -= length;
                _sample += length * _frame._stride;
                if (found)
                {
                    return true;
                }
            }
        }
    }
}
