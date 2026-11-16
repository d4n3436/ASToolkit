using ASToolkit.Core.Formats;

namespace ASToolkit.Core.Engine;

internal sealed class FrameLayout
{
    private const int StateCount = (int)SpriteAnimation.Death2 + 1;

    private readonly int[] _counts = new int[StateCount];
    private readonly int[] _firstFrames = new int[StateCount];
    private readonly int[] _framesPerDirection = new int[StateCount];
    private readonly int[] _durations = new int[StateCount];

    [EngineRoutine(0x00413c30)]
    public FrameLayout(in GameObject obj, int spriteSpeed, int totalFrames)
    {
        Directions = obj.Directions;
        for (int state = 0; state < StateCount; state++)
        {
            _counts[state] = obj.FrameCounts[state];
            _durations[state] = obj.FrameDurations[state] == 0 ? spriteSpeed : obj.FrameDurations[state];
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

                if (obj.SpriteClass == SpriteClass.Frame && (state & 1) != 0 && state < (int)SpriteAnimation.Fight && _counts[borrowed] != 0)
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

    public int GetFrameCount(SpriteAnimation state) => _counts[(int)state];

    public int GetFirstFrame(SpriteAnimation state) => _firstFrames[(int)state];

    public int GetFramesPerDirection(SpriteAnimation state) => _framesPerDirection[(int)state];

    public int GetFrameDuration(SpriteAnimation state) => _durations[(int)state];

    public void SetFrameDurations(int duration) => Array.Fill(_durations, duration);

    public void LoadFrameDurations(in GameObject obj, int spriteSpeed)
    {
        for (int state = 0; state < StateCount; state++)
        {
            _durations[state] = obj.FrameDurations[state] == 0 ? spriteSpeed : obj.FrameDurations[state];
        }
    }
}
