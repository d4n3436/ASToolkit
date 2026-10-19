using System.Runtime.InteropServices;

namespace ASToolkit.Core.Formats;

/// <summary>
/// Represents a read-only view of the pixels of one sprite frame, enumerated as runs from the top row down.
/// </summary>
public readonly struct VidFrame
{
    private readonly ReadOnlyMemory<byte> _rows;
    private readonly bool _hasDepth;
    private readonly bool _isPaletted;

    internal VidFrame(ReadOnlyMemory<byte> rows, int top, int rowCount, bool hasDepth, bool isPaletted)
    {
        _rows = rows;
        Top = top;
        RowCount = rowCount;
        _hasDepth = hasDepth;
        _isPaletted = isPaletted;
    }

    /// <summary>
    /// Gets the first row of the frame in canvas coordinates.
    /// </summary>
    public int Top { get; }

    /// <summary>
    /// Gets the number of rows the frame declares, including those without pixels.
    /// </summary>
    public int RowCount { get; }

    /// <summary>
    /// Returns an enumerator over the runs of the frame.
    /// </summary>
    public Enumerator GetEnumerator() => new(this);

    /// <summary>
    /// Enumerates the runs of a frame without allocating.
    /// </summary>
    public struct Enumerator
    {
        private readonly ReadOnlyMemory<byte> _rows;
        private readonly int _top;
        private readonly int _rowCount;
        private readonly bool _hasDepth;
        private readonly bool _isPaletted;
        private int _position;
        private int _row;
        private int _x;

        internal Enumerator(VidFrame frame)
        {
            _rows = frame._rows;
            _top = frame.Top;
            _rowCount = frame.RowCount;
            _hasDepth = frame._hasDepth;
            _isPaletted = frame._isPaletted;
        }

        /// <summary>
        /// Gets the current run.
        /// </summary>
        public VidRun Current { get; private set; }

        /// <summary>
        /// Moves to the next run that has pixels.
        /// </summary>
        public bool MoveNext()
        {
            while (_row < _rowCount)
            {
                byte skip = _rows.Span[_position];
                byte count = _rows.Span[_position + 1];
                _position += 2;
                if (skip == 0 && count == 0)
                {
                    _row++;
                    _x = 0;
                    continue;
                }

                _x += skip;
                int depthLength = _hasDepth ? 2 * count : 0;
                int colorLength = (_isPaletted ? 1 : 2) * count;
                bool found = count > 0;
                if (found)
                {
                    Current = new VidRun(_x, _top + _row, count, _rows.Slice(_position, depthLength), _rows.Slice(_position + depthLength, colorLength), _isPaletted);
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
    }
}
