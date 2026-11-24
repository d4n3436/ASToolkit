namespace ASToolkit.Engine;

/// <summary>
/// Represents an enumeration, row by row, of the sprites in the buckets of a <see cref="SpriteCollector"/> that a box covers.
/// </summary>
public struct HashQuery
{
    private readonly SpriteList[] _buckets;
    private readonly int _columns;
    private readonly int _firstColumn;
    private readonly int _lastColumn;
    private readonly int _lastRow;
    private int _column;
    private int _row;
    private int _index;

    internal HashQuery(SpriteList[] buckets, int columns, int firstColumn, int lastColumn, int firstRow, int lastRow)
    {
        _buckets = buckets;
        _columns = columns;
        _firstColumn = firstColumn;
        _lastColumn = lastColumn;
        _lastRow = lastRow;
        _column = firstColumn;
        _row = firstRow;
        _index = -1;
    }

    /// <summary>
    /// Gets the sprite at the current position of the enumerator.
    /// </summary>
    public readonly Sprite Current => _buckets[_row * _columns + _column][_index];

    /// <summary>
    /// Returns this query as its own enumerator.
    /// </summary>
    /// <returns>This query.</returns>
    public readonly HashQuery GetEnumerator() => this;

    /// <summary>
    /// Advances the enumerator to the next sprite in the box.
    /// </summary>
    /// <returns><see langword="true"/> if the enumerator moved to the next sprite; <see langword="false"/> if it passed the last one.</returns>
    public bool MoveNext()
    {
        while (_row <= _lastRow)
        {
            if (++_index < _buckets[_row * _columns + _column].Count)
            {
                return true;
            }

            _index = -1;

            if (++_column > _lastColumn)
            {
                _column = _firstColumn;
                _row++;
            }
        }

        return false;
    }
}
