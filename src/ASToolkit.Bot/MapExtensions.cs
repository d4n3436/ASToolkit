using System.Drawing;
using System.Numerics;
using ASToolkit.Engine;
using ASToolkit.Formats;

namespace ASToolkit.Bot;

/// <summary>
/// Provides views of a map's update lists and a faster way to step the map.
/// </summary>
public static class MapExtensions
{
    private const int AimWindow = 16;

    extension(Sprite sprite)
    {
        /// <summary>
        /// Gets a value indicating whether the sprite is a placed object, such as a wall, a crate or a generator, and isn't a mobile unit.
        /// </summary>
        public bool IsBody => sprite.Vid.Class is not (SpriteClass.Unit or SpriteClass.Avia or SpriteClass.Man) || sprite.Vid.Id == KnownObject.Crate;
    }

    extension(Map map)
    {
        /// <summary>
        /// Gets the sprites of the update lists, layer by layer, in update order.
        /// </summary>
        public SpriteView UpdatedSprites => new(map, SpriteViewKind.Updated);

        /// <summary>
        /// Gets the units of the collision list other than the scripted ones, buildings, the player's body and its torso.
        /// </summary>
        /// <remarks>Deleting sprites while enumerating can skip a unit, because the collision list fills a removed slot with its last entry.</remarks>
        public SpriteView Monsters => new(map, SpriteViewKind.Monsters);

        /// <summary>
        /// Gets the bodies in the update lists that can take damage.
        /// </summary>
        public IEnumerable<Sprite> Bodies => map.UpdatedSprites.Where(sprite => sprite.IsBody && sprite.Vid.MaxHp > 0);

        /// <summary>
        /// Gets the scripted sprites in the update lists.
        /// </summary>
        public SpriteView ScriptedSprites => new(map, SpriteViewKind.Scripted);

        /// <summary>
        /// Steps the map one frame at the next real time of a pacer and draws only a small area of its screen around a cursor.
        /// </summary>
        /// <remarks>The frame plays as if the whole screen were drawn. If the map doesn't have a screen, one is created.</remarks>
        /// <param name="pacer">The pacer that gives the real time of the frame.</param>
        /// <param name="cursor">The cursor the frame's mouse messages move to, in map coordinates.</param>
        /// <returns><see langword="true"/> when the game is quitting; otherwise, <see langword="false"/>.</returns>
        public bool StepAimed(FramePacer pacer, Vector2 cursor) => map.StepAimed(pacer.TakeNextRealTime(map), cursor);

        /// <summary>
        /// Steps the map one frame of its played demo at a real time and draws only a small area of its screen around the cursor of the demo's next frame.
        /// </summary>
        /// <remarks>The frame plays as if the whole screen were drawn. If the map doesn't have a screen, one is created. When the demo has no next frame, the area surrounds the current cursor.</remarks>
        /// <param name="realTime">The real time of the frame in milliseconds.</param>
        /// <returns><see langword="true"/> when the game is quitting; otherwise, <see langword="false"/>.</returns>
        public bool StepAimed(uint realTime) =>
            map.StepAimed(realTime, map.PostNextDemoMouse(out var cursor) ? cursor : map.Input.Cursor);

        /// <summary>
        /// Steps the map one frame at a real time and draws only a small area of its screen around a cursor.
        /// </summary>
        /// <remarks>The frame plays as if the whole screen were drawn. If the map doesn't have a screen, one is created.</remarks>
        /// <param name="realTime">The real time of the frame in milliseconds.</param>
        /// <param name="cursor">The cursor the area surrounds, in map coordinates.</param>
        /// <returns><see langword="true"/> when the game is quitting; otherwise, <see langword="false"/>.</returns>
        public bool StepAimed(uint realTime, Vector2 cursor)
        {
            map.SetRealTime(realTime);

            return map.TactAimed(cursor);
        }

        private bool TactAimed(Vector2 cursor)
        {
            var screen = map.Session.Graphics.Screen ??= new Screen(Size.Round(map.Session.Graphics.ScreenSize), true);
            var fullViewport = new Rectangle(Point.Empty, screen.Size);
            var pixel = cursor - map.Camera;
            var corner = new Point(Math.Clamp((int)pixel.X - AimWindow / 2, 0, screen.Size.Width - AimWindow), Math.Clamp((int)pixel.Y - AimWindow / 2, 0, screen.Size.Height - AimWindow));
            screen.Viewport = new Rectangle(corner, new Size(AimWindow, AimWindow));
            bool isQuitting = map.Tact();
            screen.Viewport = fullViewport;

            return isQuitting;
        }

        /// <summary>
        /// Sets the map's session clock to a real time.
        /// </summary>
        /// <param name="realTime">The real time in milliseconds.</param>
        /// <exception cref="InvalidOperationException">The map's session doesn't run on a <see cref="SteppedClock"/>.</exception>
        public void SetRealTime(uint realTime) => map.GetSteppedClock().Set(realTime);

        private SteppedClock GetSteppedClock() => map.Session.Time as SteppedClock ?? throw new InvalidOperationException("The map's session does not run on a stepped clock.");

        /// <summary>
        /// Sets the map's session clock to a real time and runs one frame.
        /// </summary>
        /// <param name="realTime">The real time of the frame in milliseconds.</param>
        /// <returns><see langword="true"/> when the game is quitting; otherwise, <see langword="false"/>.</returns>
        public bool TactAt(uint realTime)
        {
            map.SetRealTime(realTime);

            return map.Tact();
        }

        /// <summary>
        /// Sets the map's session clock to a real time and loads a map or demo file.
        /// </summary>
        /// <param name="name">The path of the map or demo file, relative to the game folder.</param>
        /// <param name="realTime">The real time in milliseconds, which seeds the random number generator unless a demo supplies the seed.</param>
        public void LoadAt(ReadOnlySpan<byte> name, uint realTime)
        {
            map.SetRealTime(realTime);
            map.Load(name);
        }

        /// <summary>
        /// Attempts to get the game time of the next frame of the played demo.
        /// </summary>
        /// <param name="time">The game time of the next frame in milliseconds when the demo has one; otherwise, 0.</param>
        /// <returns><see langword="true"/> when the demo has a next frame; otherwise, <see langword="false"/>.</returns>
        public bool TryGetNextDemoTime(out int time)
        {
            bool hasFrame = map.TryGetNextDemoFrame(out var frame);
            time = frame.Time;

            return hasFrame;
        }

        /// <summary>
        /// Returns the game time of every frame the played demo has left.
        /// </summary>
        /// <returns>The game times in milliseconds, in frame order.</returns>
        public IEnumerable<int> EnumerateDemoFrameTimes()
        {
            if (map.PlayedDemo is null)
                yield break;

            var demo = map.PlayedDemo.Value;
            int offset = map.PlayedDemoOffset;
            while (demo.TryReadFrame(ref offset, out var frame))
            {
                yield return frame.Time;
            }
        }

        /// <summary>
        /// Posts the mouse position recorded for the next frame of the played demo.
        /// </summary>
        /// <param name="cursor">The next frame's cursor in map coordinates when the demo has a next frame; otherwise, the zero vector.</param>
        /// <returns><see langword="true"/> when the demo has a next frame; otherwise, <see langword="false"/>.</returns>
        public bool PostNextDemoMouse(out Vector2 cursor)
        {
            cursor = default;

            if (!map.TryGetNextDemoFrame(out var frame))
                return false;

            var input = frame.Input;
            var screen = new Point((int)input.ScreenCursor.X, (int)input.ScreenCursor.Y) + new Size(map.Session.Graphics.WindowPosition);
            map.Input.PostMessage(WindowMessageId.HitTest, 0, (screen.Y & 0xFFFF) << 16 | (screen.X & 0xFFFF));
            cursor = input.Cursor;

            return true;
        }

        /// <summary>
        /// Determines whether a living monster has the given object ID.
        /// </summary>
        /// <param name="id">The object ID.</param>
        /// <returns><see langword="true"/> if a living monster has the object ID; otherwise, <see langword="false"/>.</returns>
        public bool HasLivingMonster(KnownObject id)
        {
            foreach (var sprite in map.Monsters)
            {
                if (sprite.Vid.Id == id && sprite.IsAlive)
                    return true;
            }

            return false;
        }

        private bool TryGetNextDemoFrame(out DemoFrame frame)
        {
            int offset = map.PlayedDemoOffset;
            frame = default;

            return map.PlayedDemo is not null && map.PlayedDemo.Value.TryReadFrame(ref offset, out frame);
        }
    }
}
