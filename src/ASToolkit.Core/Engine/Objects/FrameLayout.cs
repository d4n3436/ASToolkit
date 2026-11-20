using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class FrameLayout
{
    private const int StateCount = (int)SpriteAnimation.Death2 + 1;

    private readonly int[] _counts = new int[StateCount];
    private readonly int[] _firstFrames = new int[StateCount];
    private readonly int[] _framesPerDirection = new int[StateCount];
    private readonly int[] _durations = new int[StateCount];

    public FrameLayout(in GameObject gameObject, int spriteSpeed, int totalFrames)
    {
        TotalFrames = totalFrames;
        Directions = gameObject.Directions;
        DirectionOffset = gameObject.Directions == 0 ? 0 : Angle.HalfTurn / gameObject.Directions;
        for (int state = 0; state < StateCount; state++)
        {
            _counts[state] = gameObject.FrameCounts[state];
            _durations[state] = gameObject.FrameDurations[state] == 0 ? spriteSpeed : gameObject.FrameDurations[state];
        }

        if (totalFrames > 0 && totalFrames < Directions)
        {
            Directions = totalFrames;
        }

        int end = 0;
        for (int state = 0; state < StateCount; state++)
        {
            end += _counts[state] * Directions;
        }

        if (totalFrames > 0 && totalFrames < end)
        {
            for (int state = StateCount - 1; state >= 0; state--)
            {
                int before = end - Directions * _counts[state];

                if (before <= totalFrames)
                {
                    _counts[state] -= (end - totalFrames) / Directions;
                    break;
                }

                _counts[state] = 0;
                end = before;
            }
        }

        int start = 0;
        int firstPopulated = -1;
        for (int state = 0; state < StateCount; state++)
        {
            if (_counts[state] == 0)
            {
                int borrowed = firstPopulated + 1;

                if (gameObject.SpriteClass == SpriteClass.Frame && (state & 1) != 0 && state < (int)SpriteAnimation.Fight && _counts[borrowed] != 0)
                {
                    _firstFrames[state] = _firstFrames[borrowed];
                    _framesPerDirection[state] = _framesPerDirection[borrowed];
                }
                else
                {
                    _firstFrames[state] = 0;
                    _framesPerDirection[state] = firstPopulated < 0 ? 0 : _framesPerDirection[firstPopulated];
                }
            }
            else
            {
                _firstFrames[state] = start;
                _framesPerDirection[state] = _counts[state];

                if (firstPopulated < 0)
                {
                    firstPopulated = state;
                    for (int previous = 0; previous < state; previous++)
                    {
                        if (_framesPerDirection[previous] == 0)
                        {
                            _framesPerDirection[previous] = _counts[state];
                        }
                    }
                }
            }

            start += _counts[state] * Directions;

            if (totalFrames > 0 && totalFrames < start)
            {
                _firstFrames[state] = 0;
                _framesPerDirection[state] = firstPopulated < 0 ? 0 : _framesPerDirection[firstPopulated];
            }
        }
    }

    public int Directions { get; }

    public int DirectionOffset { get; }

    public int TotalFrames { get; }

    public int GetDirectionIndex(Angle direction) => direction.GetDirectionIndex(Directions, DirectionOffset);

    public int GetFrameCount(SpriteAnimation state) => _counts[(int)state];

    public int GetFirstFrame(SpriteAnimation state) => _firstFrames[(int)state];

    public int GetFramesPerDirection(SpriteAnimation state) => _framesPerDirection[(int)state];

    public int GetFrameDuration(SpriteAnimation state) => _durations[(int)state];

    public void SetFrameDurations(int duration) => Array.Fill(_durations, duration);

    public void LoadFrameDurations(in GameObject gameObject, int spriteSpeed)
    {
        for (int state = 0; state < StateCount; state++)
        {
            _durations[state] = gameObject.FrameDurations[state] == 0 ? spriteSpeed : gameObject.FrameDurations[state];
        }
    }
}
