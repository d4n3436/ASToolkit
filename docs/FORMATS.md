# Alien Shooter file formats

This document describes how the games in the Alien Shooter series store their data, with enough detail to open the files by hand.

All integers are little-endian. Text is cp1251 (Cyrillic) unless noted. Offsets are in bytes and written in decimal unless they start with `0x`.

## Contents

1. [The game folder](#the-game-folder)
2. [Alien Shooter: Vengeance](#alien-shooter-vengeance)
3. [RES container](#res-container)
4. [objects.res](#objectsres)
5. [Maps](#maps)
6. [Sprites (.vid)](#sprites-vid)
7. [Menus (.men)](#menus-men)
8. [Scripts and text](#scripts-and-text)
9. [Drawing a level](#drawing-a-level)

## The game folder

```
AlienShooter.exe        the engine
AlienShooter.cfg        ini file (title, registry path, resource names)
objects.res             every object: stats, sprite path, weapons, sounds
strings.ini             interface strings                                   (AS1)
campaign.cfg            campaign map list and loot tables                   (AS2)
levels.cfg              per-level rewards                                   (AS2)
Weapon.cfg              ammunition costs                                    (AS2)
Survive.cfg             survival waves                                      (AS2)
input.cfg               key bindings                                        (AS2)
Maps/
  Level_NN.map          campaign level layout     RES "MAP "
  Level_NN.lgc          campaign level script     text
  ADDON/, ADDON2/       add-ons (same file layout)                          (AS1)
  survive_*.map         survival levels
  *.men                 menu layouts              RES "MENU"
  *.LGC                 shared scripts            text
  logo.map, mainmenu.map, shop.map, end.map, slide.map, nag.map
                        host maps for the menus (no sprites)
Vid/                    sprites                   RES "VID "
                        (AS2 also keeps tga textures and shaders here)
Images/                 tga images the menus use                            (AS2)
Text/                   mission briefings and dialogs                       text
Wav/  Music/            sounds (wav) and music (ogg)
Speech/                 voice lines                                         (AS2)
Font/                   bitmap fonts              tga                       (AS1)
cursores/               animated cursors          ani
Screens/                screenshots the game writes                         (AS1)
```

## Alien Shooter: Vengeance

Vengeance (2006) is an earlier build of Alien Shooter 2. Its files differ in these ways.

- Sprite headers are 10, 14, 22 or 34 bytes. The 426-byte header with the sprite tool's data is Alien Shooter 2 only.
- Most cached sprites have the depth flag 0x04, with two depth bytes after the color of each pixel. The Alien Shooter 2 files dropped both from 580 sprites, which have the flags 0x48 instead of 0x4C.
- Sprite `mode` keeps the values 0 and -1 that the Alien Shooter 2 loader rewrites to 1.
- `Vid/fast_load.vid` and `Vid/fast_load_data.vid` are not `RES` containers. They are the sprite cache the header key refers to.
- In `1195.vid` and `1196.vid` the PAL chunk size is 20 bytes short, so a chunk walk cannot reach the `CADR` chunk.

## RES container

Objects, maps, sprites and menus use the same container. Only the four-character kind changes.

```
 0        4        8       12
 +--------+--------+--------+-------+----+---------------------+
 | "RES " | size   | kind   | chunk | 00 | chunk | chunk | ... |
 +--------+--------+--------+-------+----+---------------------+
  "RIFF" also       DATA"
  works             "MAP " "VID " "MENU"

 size = file length minus 8 bytes (identifier and size fields)
```

Each chunk has an 8-byte header and includes a pad byte if its length is not even, except the last chunk, which has none in the game's files. The chunk's `size` doesn't include the pad byte.

```
 +--------+--------+----------------------------------------+
 | tag    | size   | body (size bytes)                      |
 | 4 chars| u32    |                                        |
 +--------+--------+----------------------------------------+

 body, when flags has bit 31 set (every file in the game):
 +------------+-----------+---------+---------+---------+-----+
 | flags      | reserved  | count   | record  | record  | ... |
 | 0x80000000 | 0         | u32     |         |         |     |
 +------------+-----------+---------+---------+---------+-----+

 record:
 +--------+---------------------------+
 | length | length bytes of data      |
 | u32    |                           |
 +--------+---------------------------+
```

Without bit 31 the flags word is the record count and the reserved and count fields are absent. A tag can repeat, and the engine adds up the counts.

A chunk with `count` records is therefore a table. Most chunks hold one record with a packed array inside. Others, such as `OBJ ` in objects.res, hold one record per entry.

## objects.res

Kind `DATA`. It defines every object the game knows by an id from 0 to 1000.

```
 RES "DATA"
 +------+--------------------------------------------------+
 | OBJ  | 504 records, one per object                      |
 | WEAP | 46 records of 612 bytes, one per weapon          |
 | CNST | 1 record of 104 bytes                            |
 | SFX  | 58 records, sound lists                          |
 +------+--------------------------------------------------+
```

An `OBJ ` record has a name, a fixed-size body and the path of the sprite. The body is 688 bytes in Alien Shooter 1 and games from around that era, 704 in Alien Shooter 2.

```
 0    4                  4+n+1      +body         +body+m+1
 +----+------------------+----------+------------+-----------+
 | id | name, cp1251, 0  | body     | sprite path, 0          |
 +----+------------------+----------+------------+-----------+
                          688/704 bytes   "vid\040.vid" or "font\..."
```

Body fields:

```
 AS1    AS2    type     field
 +0     +0     u32      group: category in byte 0, kind in byte 1, bytes 2 and 3 always 0 (see below)
 +4     +4     u32      type: engine behavior class (see below)
 +8     +8     u32      flags (see below)
 +12    +12    u32      collision mask: two objects collide when their masks share a bit; 0 never collides
 +16    +16    f32      extent x   \
 +20    +20    f32      extent y    > footprint size (top-down; no z extent in the file)
 +24    +24    f32      height (extent z), used for draw depth; the sprite draw routine orders sprites by it (extent y < extent z)
 +28    +28    i32      hit points; 0 for non-damageable objects
 +32    +32    f32      speed along the facing, in milli-units (x0.001 on load); the initial speed of a spawned object, in Alien Shooter 2 the minimum of its random range
 -      +36    f32      maximum initial speed, in milli-units
 +36    +40    f32      vertical speed, in milli-units; in Alien Shooter 2 the minimum initial vertical speed
 -      +44    f32      maximum initial vertical speed, in milli-units
 +40    +48    f32      acceleration, stored in micro-units (x1e-6 on load); ~1e6 = instant
 +44    +52    f32      deceleration, stored in micro-units (x1e-6 on load); ~1e6 = instant
 +48    +56    f32      rate, loaded as 256 / value; ~1e6 = no limit
 +52    +60    u32      tag (31, 15 or 11 on bodies, 32 on bullets, 0 on effects); when it is 0 the engine takes the sprite measures from the parent object (rest of the use not located)
 +56    +64    f32      blast radius added to the footprint of the object when it hits something
 +60    +68    i32      damage sent to everything the blast reaches; in Alien Shooter 2 the minimum of a random range
 -      +72    i32      maximum damage; the loader raises it to the minimum when it is lower
 -      +76    f32      push: stored divided by 50 on load, it adds a speed to everything hit, along the facing of the blast and weaker with distance
 +64    +80    f32 x3   offset of the attached object from this one (x, y, z; Alien Shooter 2 flips the sign of y on load)
 +76    +92    i32      object id of the attached object (a player body part, a weapon, a muzzle flash); the engine resolves it to the object itself after the load
 +80    +96    f32      height above the terrain the object keeps when it is not flying; 0 for none
 +84    +100   f32      highest step up the object can climb while following the terrain; also the height a bobbing object rises and sinks by
 +88    +104   f32      deepest drop the object can walk off while following the terrain
 +92    +108   i32      lifetime in milliseconds; 999999 means forever
 +96    +112   -        16 bytes the reader skips (always 0)
 +112   +128   u32      direction count (at least 1)
 +116   +132   i32 x17  frames per direction of each animation state
 +184   +200   i32 x17  sound id of each state (an index into the SFX chunk)
 +252   +268   i32 x17  frame duration of each state in milliseconds; the reader replaces 0 with the speed in the sprite header
 +320   +336   f32 x17  x offset of the object created in the state, rotated with the facing
 +388   +404   f32 x17  y offset of the object created in the state, rotated with the facing
 +456   +472   f32 x17  height offset of the object created in the state
 +524   +540   i32 x17  object id tied to each state, mostly the effect or projectile created in it
 +592   +608   i32 x17  number of objects created in the state, 1 on most; 2 makes a mirrored pair
 +660   +676   i32 x3   color tint: red, green, blue
 +672   -      i32      unused (always 0)
 -      +688   i32      alpha tint; negative on smoke, steam and shadows
 +676   +692   f32 x3   scale along x, y and the height; 1.0 by default, mostly used by fx and ui objects
```

An object has 17 animation states, numbered 0 to 16. The engine treats states below 15 as alive and 15 and above as the end of the object. The monsters of the data use an id for the body, the next one for its death effect and the one after for its corpse, with the death effect named in state 15 of the body.

A spawned object starts at the speed in the record, the movement step raises the speed by the acceleration until it reaches that value and lowers it by the deceleration when the object stops, and the vertical speed feeds the height. Alien Shooter 2 picks the initial speed and the initial vertical speed at random between the minimum and the maximum of the record.

When an object hits something, the blast sends every object within its footprint plus the blast radius a damage message. An object with the explosive flag scales the damage down with the distance, and one without it deals the full damage. Alien Shooter 2 rolls the damage between the minimum and the maximum, and the push adds a speed to each object it hits.

An attached object is created together with its owner and follows it. It can have an attached object of its own. It is placed at the position of the owner plus an offset:

- When the sprite of the owner has the flag 0x200, the offset is the vector of the frame the owner shows (Alien Shooter 2).
- Otherwise it is the offset in the record of the owner, with the sign of y flipped.
- Unless the attached object has the flag 0x2000000, the x and y of the offset turn with the facing of the owner.

The animation state of an object creates the objects named in its id array, each at its own offset from the owner and as many times as the count array says.

An object with a lifetime other than 999999 is removed that many milliseconds after it is created.

`type` is compared against literal values (an enum), `flags` is tested with bit masks, and `category` and `kind` are group masks.

A moving object stops at another object when the two masks share a bit, an object with a zero mask never collides, and the flag 0x4000 on the other object sends it a touch message instead. Alien Shooter uses the masks 7 (most monsters and some projectiles), 8 (projectiles), 15 (the player, scenery and some monsters), 16 (death effects) and 31 (walls and generators). Alien Shooter 2 adds many more, including 64 and 128 for turrets, vehicles and its other new groups.

The packed `category` and `kind` is the group of the object. Scripts and the engine look objects up with a mask over the low bits of the packed value. The lookup function takes a mask of 0x67f, which covers the category bits 1 to 64 and the kind values 2 and 4, and can also filter by exact id, exact type, side and state. The only fixed uses in the engine are the actor lists, which take the objects whose category matches 0xC in Alien Shooter and 0x18 in Alien Shooter 2, and a few exact tests in the draw and load paths. Bytes 2 and 3 of the packed value are never tested.

Groups seen in the data of Alien Shooter:

| kind | category | objects                                                     |
|------|----------|-------------------------------------------------------------|
| 0    | 1        | corpses, shadows and fx under pickups, dummies              |
| 0    | 2        | pickups, wall decor, lights, frozen monsters                |
| 0    | 4        | monsters, generators, teleports, player legs                |
| 0    | 16       | fonts, logos and other interface sprites                    |
| 2    | 0        | bullets, explosions, particles, monster death effects       |
| 4    | 0        | player torso and head, cursor, linked effects               |

Alien Shooter 2 moved the groups one bit up for monsters (8), turrets (16) and the interface (32), added 4 for props and doors and 128 for regions. Its kinds are the same.

The type tells the engine how to treat the object:

| type | objects of the data                                  | what the engine does                                                         |
|------|------------------------------------------------------|------------------------------------------------------------------------------|
| 0    | spawn walls, light volumes                           | no distinct branch located                                                   |
| 1    | pickups, props, player torso and head, dummy blocks  | no distinct branch located                                                   |
| 2    | monsters, drones, turrets                            | counted as a monster; spawning checks for an object in the way               |
| 3    | monster generators and teleports                     | no distinct branch located                                                   |
| 5    | bullets, monster death effects                       | treated differently when it follows a target                                 |
| 7    | player legs, player vehicles                         | samples the terrain over its whole footprint and skips some collision tests  |
| 8    | scenery, corpses, casings, craters, wall drawings    | has its own obstacle grid routine and is drawn on layer 1 or 2               |
| 9    | lights, shadows, glows, cursor, loading              | no distinct branch located                                                   |
| 10   | logos, frames, faces                                 | placed relative to the camera, as screen-space interface                     |
| 12   | muzzle flashes, halos                                | follows its parent and turns with it                                         |
| 19   | font sheets                                          | each frame is one glyph cell of a texture atlas                              |
| 23   | regions (Alien Shooter 2)                            | rectangle whose size is stored with each placed object                       |
| 25   | one monster weapon                                   | no distinct branch located                                                   |

An object id is its index in the object table. Unknown ids resolve to a fallback object. The lookup by id masks it to 11 bits.

The scale is kept per object and multiplied into the scale of the placement when the engine builds the world matrix of the sprite. It sizes the sprite along x, y and the height (a menu button of 1.1 x 1, a volume bar of 1 x 1.35, a small explosion of 0.65). Alien Shooter resets it to 1.0 for sprites with per-pixel depth and surfaces. On omni-lights the x and y values scale the radius of the light (see [Lights](#lights)).

Object flags. The engine tests them on the object class and never changes them at run time. The Alien Shooter files set only part of them, and the Alien Shooter 2 files set the rest.

```
 0x00000001   random facing: the object starts with a random facing (casings, craters, holes, dried blood)
 0x00000002   flying: leaves the ground and falls with the first gravity constant
 0x00000004   heavy: falls with the second gravity constant; this flag and 0x02 also mark the object as moving
 0x00000008   outline: blocks movement along its footprint outline, sampled every 8 units
 0x00000010   the animation step runs the queued actions of the object on every frame instead of only past the last frame (see the animation step under the sprite format)
 0x00000020   solid, blocks movement
 0x00000040   hittable: indexed for hit and blast queries, so shots and explosions can reach it
 0x00000080   stream: plays while it is alive (flame, gun smoke)
 0x00000100   oriented box: the footprint is a box that rotates with the facing (Alien Shooter 2); Alien Shooter scatters the spawn position by up to 8 units instead
 0x00000200   follows terrain: the height comes from the level's height map
 0x00000400   random speed: starts with a random speed up to the record's speed (only the original game reads it; no object sets it)
 0x00000800   unlit: the level lighting does not tint the object
 0x00001000   windblown: the level's wind moves the object (Alien Shooter); in Alien Shooter 2 the object casts shadows, and an omni-light with it draws them
 0x00002000   free rotation: the sprite keeps one frame and the draw rotates it by the facing
 0x00004000   touchable: receives a touch message when another object overlaps it
 0x00008000   raised: drawn over everything below it (items); Alien Shooter adds 0x3FFF to the depth, Alien Shooter 2 adds 1000 to the height and to y, which keeps the screen position
 0x00010000   bobs up and down by w = sin(2π * ((tick >> 3) & 255) / 256) times the field at +84 (+100); Alien Shooter 2 adds w to the height when it places the sprite, and Alien Shooter adds 8 * w, truncated, to the depth and moves the sprite up by an eighth of that
 0x00020000   side dependent: hidden while its side differs from the side of the player
 0x00040000   death animation: freezes on the death frame when killed
 0x00080000   tracks motion: the facing follows the movement or the target
 0x00100000   homing: steers toward its target while it moves
 0x00200000   beam: the sprite is stretched between the object and its target
 0x00400000   random lift: starts with a random vertical speed, up or down
 0x00800000   Alien Shooter 2 tests it in the movement and collision code next to the object scale; no confirmed purpose. Omni-lights are found by the sprite flag 0x80 instead
 0x01000000   starts its animation on frame 0 instead of a random frame
 0x02000000   fixed offset: a spawned or attached object keeps its offset from the parent instead of rotating it with the parent's facing
 0x04000000   explosive: damages everything inside its radius when it hits something
 0x08000000   rocket: flies toward its target and follows the height of the target
 0x10000000   bouncing: bounces off the ground instead of stopping
 0x20000000   rotated: the sprite is turned on the ground plane by the facing, which turns it smoothly between direction frames (Alien Shooter 2)
 0x40000000   ground decal: drawn on the ground layer (blood, craters, floor tiles)
 0x80000000   projectile: a hit or blast from the object skips objects of its own side
```

The side is a two-bit value in bits 11 and 12 of the state of a placed object in Alien Shooter, and a three-bit value in bits 17 to 19 in Alien Shooter 2. A placed object takes it from the `side` field of the map, and a spawned or attached object inherits the side of its parent. The flag 0x20000 hides the object while its side differs from the side of the player, which is 0 when a level starts, and the flag 0x80000000 skips objects with the same side. Sprites with the flag 0x400 pick the palette with it.

In Alien Shooter 2 the helper blocks of the editor (630 to 634, the pillars 1357 to 1359, the teleport markers) and the depth blocks attached to walls (2706, 1799 and the like) have the flag 0x20000 and side 2, so the game never shows them.

In Alien Shooter the only object with the flag 0x20000 is the depth blocker 631. The maps place it on side 0, which matches the side of the player, but it is type 8 scenery. Alien Shooter draws scenery only by baking it into the terrain when the level loads. The bake skips objects with the flag 0x20000, which keeps 631 off the screen.

The gravity constants are the third and fourth floats of the `CNST` record: 174 and 84 in Alien Shooter, 174 and 800 in Alien Shooter 2.

The flags 0x20000000, 0x2000 and 0x80000 work together in the Alien Shooter 2 draw. Without 0x2000 the draw shows the direction frame closest to the facing and turns the sprite by the remainder, `facing - index * 256 / directions` in 256ths of a turn. With 0x2000 it turns a single frame by the whole facing. With 0x80000 the angle comes from the movement or the target, and a still object takes the whole facing. No sprite turns when its `mode` is 0, or -1 with a standing footprint, which after the loader leaves only 101.vid.

The turn is part of the world matrix, so it acts on the ground plane before the view tilts it. A canvas point `(x, y)` with height `h`, measured from the center of the canvas, sits on the ground at `(x, (y + h) / 0.70706)`; that point turns by the angle and goes back to the screen as `(x', 0.70706 * y' - h)`. A flat sprite turned by 45 degrees therefore becomes a diamond as wide as its width plus its height. Object 1127, the soft black cover of Alien Shooter 2, is a single-frame sprite with this flag, and its turn is what lines its edges up with the walls.

The tint changes the colors of the sprite. A negative value scales the channel to `(256 + t) / 256` of itself and a positive value adds `t`. Alien Shooter 2 applies the tint when it draws the sprite, as a diffuse color of `255 - n` and a specular color of `p`, which turns a channel `c` into `c * (255 - n) / 255 + p`. The tint it uses is the tint of the placement (the per-side tint of the object when the placement has none) plus the tint of the object, added byte by byte and saturating. On placements without their own tint, the object tint counts twice. The alpha tint scales the alpha of the sprite the same way when it is negative and has no effect when it is positive, and it only shows on sprites that blend by alpha. Of the objects with a negative alpha tint, only the acid shadow 1879 is placed in a map (five times in Level_07_2). Yellow and red monsters, and the blue survival bosses, are the same sprite with different tints.

## Maps

Kind `MAP `. A level is a list of sprites placed in a world, plus the small chunks that go with it.

```
 RES "MAP "
 +------+-----------------------------------------------------------+
 | GRPH | ambient light and a direction vector                      |
 | HEAD | map size, starting camera, version                        |
 | GRID | (optional) a u16 matrix, only in a few maps               |
 | SPR  | all placed sprites                                        |
 | SPRD | one extra-data record per sprite                          |
 | PLAY | player slots, empty in every map                          |
 | GROU | groups of sprites, must be empty in a written level       |
 +------+-----------------------------------------------------------+
```

`HEAD` has one 24-byte record.

```
 0     4     8        12       16     20
 +-----+-----+--------+--------+------+---------+
 | f32 | f32 | f32    | f32    | u32  | u32     |
 | map | map | camera | camera | tick | version |
 | w   | h   | x      | y      |      |         |
 +-----+-----+--------+--------+------+---------+
```

The levels of Alien Shooter are version 10 and 12, and those of Alien Shooter 2 are 18 to 20.

`GRPH` has one record of 20 bytes, or 56 in Alien Shooter 2 levels. A 20-byte record ends after `len`.

```
 0       4        8        12    16    20     24     28     32         56
 +-------+--------+--------+-----+-----+------+------+------+----------+
 | flags | ambient| ambient| dir | len | u32  | u32  | u32  | zeros    |
 | u32   | tint - | tint + | u32 | f32 | = -1 | = 32 | = 40 |          |
 +-------+--------+--------+-----+-----+------+------+------+----------+
```

The ambient tint is packed like the tint of a placed sprite and sets the ambient light (see [Lights](#lights)). Alien Shooter 2 turns the low byte of `dir`, an angle in 256ths of a turn, into a vector of length `len`; every map has `dir` 64 or 200 and `len` 0.025. It tests the value 4 of `flags` while drawing the scene, and every map stores zero there. The three fields from byte 20 are loaded and saved without being read anywhere else (the engine keeps only the low byte of the last two), and it does not read past byte 32.

`SPR ` has one record. It is an array of entries closed by `0xFFFFFFFF`, 28 bytes each before version 16 and 48 bytes from version 16.

```
 0        4     8   12   16   20    24     28       32       36    40    44    48
 +--------+-----+---+----+----+-----+------+--------+--------+-----+-----+-----+
 | handle | id  | x | y  | z  | dir | side | tint - | tint + | sx  | sy  | sz  |
 | u32    | u32 |f32|f32 |f32 | u32 | u32  | u32    | u32    | f32 | f32 | f32 |
 +--------+-----+---+----+----+-----+------+--------+--------+-----+-----+-----+
          object id                         version 16 and later
          from objects.res
```

Maps before version 10 (survival, the menu host maps and a few add-on maps of Alien Shooter) store x, y and z as `i32`.

The engines keep only the low byte of `dir` as the facing, and the other three bytes are zero in every map.

The side goes from 0 to 7. The levels use 0 for the player and most scenery, 1 for monsters and generators, and 2 for breakable objects, walls and corpses. Alien Shooter 2 also uses 3 for allied soldiers and NPCs, and 5 and 6 for a few robots and monsters.

Alien Shooter maps place the player as the legs (9), or the female player legs (549) in the first level of the second add-on, which still attach the male torso (11). The start script in `COMMON.LGC` destroys the placed player and creates object 9. When the `PlayerSex` profile value is female, the script swaps the objects 9 to 20 for 549 to 560 and 352 for 358 with `VID_EXCHANGEVID`.

The tint of the placed object is packed in two `u32`, with alpha, red, green and blue from the high byte down. The first holds the magnitudes of the negative channels and the second the positive ones, the same packing the engine builds from the tint of objects.res. When either is nonzero it replaces the tint of the object for that placement.

`sx` and `sy` scale the sprite on the ground plane before it is turned, and `sz` scales the heights of its mesh. Maps before version 16 read as a scale of 1. About 3,000 placements of Alien Shooter 2 use another scale, from 0 to 8.

`SPRD` has one record per sprite, matched by handle, and ends with an `0xFFFFFFFF` record. The payload is what makes a sprite behave.

```
 handle   payload
 +------+------------------------------------------------------------+
 | u32  | 8 zero bytes          most sprites                         |
 |      | 12 bytes              monsters                             |
 |      | 20 bytes              the player (object 9)                |
 |      | 220-268 bytes         script spawners (object 908)         |
 |      | other sizes           crates, teleports, spawn walls       |
 |      | 12 bytes + name       Alien Shooter 2, see Drawing a level |
 +------+------------------------------------------------------------+
```

A spawner payload is a small program. See [NOTES.md](NOTES.md#spawners-908).

In Alien Shooter 2 the payload of a sprite with behavior starts with its action stacks:

```
 u16      last stack run
 u16      flags; 1 on some lamps, glows and doors
 u32      stack count, 4 (0 when the sprite has none)
 per stack:
   u32    entry count
   entry  (u32 code, i32 first, i32 second, i32 third) * entry count
```

Each update the engine steps to the next of the four stacks, wrapping around, and runs it from its last entry until it waits, which makes the stacks run side by side. Code 98 hides the object and its attached chain when the first argument is nonzero and shows it otherwise. Codes 40 (pause), 43 (wait for the player within a radius), 73 (stop), 77 (wait for a level script check) and 79 (wait until a script variable equals a value) make the stack wait, and the other codes run at once. A sprite that the stacks hide before they first wait, taken in that order starting after the last stack run, starts the level hidden. That covers 299 placements across the Alien Shooter 2 maps: red alarm lamps and lights, alarm glows, helper arrows and markers, exit zones, Spinner holes and mobile teleport generators, which the level scripts show later.

### Coordinates and direction

The world is isometric. A sprite stands at (x, y) and is lifted by z.

```
       +----> x          a lab floor tile covers one diamond
       |
       v y                    /\
                             /  \      40 px wide
                             \  /      28 px tall
                              \/
```

The lab lattice has walls at `x = 20a`, `y = 14b` with `a + b` even and floor tile centers at `x = 40i`, `y = 28j + 14` with `i + j` odd. The military levels use a finer lattice of 45 by 32. [NOTES.md](NOTES.md#level-construction-in-the-originals) has the measurements.

The direction byte `dir` (0 to 255) picks the frame of a sprite that has several facings.

```
 dirs = number of facings of the sprite
 facing = ((dir + 128 / dirs) & 0xFF) * dirs >> 8

 example, dirs = 4:       dir  0..31   -> 0
                          dir 32..95   -> 1
                          dir 96..159  -> 2
                          dir 160..223 -> 3
                          dir 224..255 -> 0
```

The half-step offset `128 / dirs` centers each facing on its angle.

## Sprites (.vid)

Kind `VID `. A sprite is a set of frames, laid out as animations and facings. On Alien Shooter 2 the file is read and its frames turned into surfaces by the loader listed in the key functions.

```
 RES "VID "
 +------+---------------------------------------------+
 | HEAD | 10 to 426 bytes                             |
 | PAL  | 1,024 bytes, 256 entries of B G R X         |
 | DATA | one record per frame                        |
 | SURF | (texture sprites) the surfaces              |
 | CADR | the cached surfaces                         |
 +------+---------------------------------------------+

HEAD
 +------+-------+--------+-------+--------+--------+------+-------+
 |flags | speed | frames | width | height | format | mode | blend |
 | u16  | u16   | u16    | u16   | u16    | u32    | i32  | u32   |
 +------+-------+--------+-------+--------+--------+------+-------+
 0      2       4        6       8        0x0a     0x0e   0x12
          ms per frame
```

Most Alien Shooter 1 sprites have a ten-byte record. The other Alien Shooter 1 records are 18 and 22 bytes, and the Alien Shooter 2 ones are 10, 14, 22, 34 and 426 bytes.

The Alien Shooter 1 loader of software sprites reads only the first ten bytes. The Alien Shooter 2 loader reads `format` and `mode` and, when the record goes on, `blend`. It loads no frames when `format` is zero.

`format` is a Direct3D 9 format code, which the engine maps to bits per pixel.

```
 0x14  R8G8B8, 24 bit            0x1a  A4R4G4B4, 16 bit
 0x15  A8R8G8B8, 32 bit          0x1c  A8, 8 bit
 0x16  X8R8G8B8, 32 bit          0x29  P8, 8-bit palette indices
 0x17  R5G6B5, 16 bit            0x32  L8, 8 bit
 0x18  X1R5G5B5, 16 bit          DXT1 4 bit, DXT3 and DXT5 8 bit (FourCC codes)
 0x19  A1R5G5B5, 16 bit
```

The files use 0x29 for the paletted sprites and 0x15, 0x17, 0x19 and 0x1a for the others.

`mode` is what the draw routines test when they build the quad of a frame.

```
 0, -1    the quad stands up, its depth growing with the height up the frame; the sprite is
          drawn without rotating by the object's facing. -1 takes this path or the flat one
          depending on the depth range stored for the sprite.
 1        a flat quad; the outline pass skips it
 2        eight vertices, two quads; no sprite of the game
 3        the frame is a triangle mesh built from the vertex data in its CADR record
 4, 5     no special handling, drawn like mode 1; 5 is on the omni-lights (flag 0x80)
 6        light glow; the draw routine can return a bounding rectangle widened up to 128 pixels
```

The Alien Shooter 2 loader replaces 0 and -1 with 1 unless the path contains "\101." (101.vid, the only file that keeps 0).

Mode 4 is on 22 Alien Shooter 1 sprites with flags 0x1D, and the Alien Shooter 1 loader does not read it. No draw routine tests for it. Every sprite with mesh frames has mode 3.

`blend` selects the Direct3D blend and depth setup of the draw routine. The pair is the source and destination blend factor.

```
 0      alpha test; with the blended flag (and a format other than A1R5G5B5) SRCALPHA, INVSRCALPHA
 1      DESTCOLOR, SRCCOLOR
 2      ONE, ONE
 4      ONE, ONE; objects with the flag 0x1000 are scaled to the render target
 5      DESTCOLOR, ONE
 6      like 0, drawn without writing depth
 7      like 0, drawn with depth writes; no sprite of the game
```

The depth test is ALWAYS for blend 4, blend 7 and mode 6, which draws them over everything already drawn, and GREATEREQUAL for the rest. Depth is written only for 0 (when the sprite has no flag 0x02) and 7. The files use 0, 1, 2, 4 and 6. Alien Shooter draws its blended sprites after the other sprites.

The palettes of the additive sprites already hold colors scaled by their alpha, so the color is added as it is. Object 1306 (fog clouds) has blend 2 and an R5G6B5 format without alpha, which is why it shows as dark blobs when drawn as an opaque sprite.

The Alien Shooter 2 engine skips most of the rest of the record, which the sprite tool wrote.

```
 +0x16  u32      offset of the sprite's frame table in fast_load.vid; 0 in 60 files
 +0x1a  u64      FILETIME of the cache build, the first 8 bytes of fast_load.vid;
                 2008-02-07 06:50:47 in 850 Alien Shooter 2 sprites, 0 in 66
 +0x22  i32      -1 in most files; read by the engine, the value is not used
 +0x26  i32      1 in most files, the frame count in 97; stored in the sprite
 +0x2a  i32[32]  all zero in 562 files, otherwise 71 (the usual speed) or a few other values
 +0xaa  i32[32]  tables that count up from 1, zero in most files
 +0x12a ...      mostly zero
 +0x1aa end
```

When the FILETIME of a sprite equals the first 8 bytes of `fast_load.vid`, the loader takes the frame table from that file instead of the sprite. It clears the flags 0x04 and 0x2000 (and 0x8000 in Alien Shooter 2) and reads the table at the u32 offset.

```
 fast_load.vid:  u64 FILETIME, then one table per sprite
 frame entry (24 bytes):  u16 * 6    the CADR record's index count, vertex count,
                                     width, height, x and y
                          u32        offset of the frame's data in fast_load_data.vid
                          f32, f32   lowest and highest vertex depth; 0.0012 and 10.0 without a mesh
```

Only Vengeance ships `fast_load.vid` and `fast_load_data.vid`, where 847 of 979 sprites match. The Alien Shooter 2 keys never match. No code that uses the stored value at +0x26 was found, and the rest of the tail has no known meaning.

Flags. A sprite kind is a combination of them, and the values quoted in this section are such combinations.

```
 0x01   frame records are runs of 8-bit values; sprites without it (0x36) use 16-bit runs
 0x02   blends with what lies under it, using the alpha of the palette
 0x04   per-pixel depth values
 0x08   palette
 0x10   current format version
 0x20   has a SURF chunk with 32-bit surfaces
 0x40   has a CADR chunk with cached surface data; the standard format in Alien Shooter 2
 0x80   omni-light: a 2 x 2 sprite whose frames are BGRA colors, one u32 per frame in a single DATA record (only 0xB0)
 0x100  surfaces are range coded
 0x200  each CADR record starts with a 12-byte vector, three floats that place the object attached to the one showing the frame (52 Alien Shooter 2 sprites)
 0x400  several palettes; Alien Shooter adds the palette size times the side of the placed object to the palette address; no sprite of the game
 0x800  DXT compressed surfaces; a CADR record header is 14 bytes (no sprite of the game)
 0x1000 never tested by the engine; no sprite of the game
 0x2000 a second image under the first: the surface is twice as tall, each CADR pixel has one more byte, and drawing offsets the texture by one frame height; no sprite of the game
 0x4000 text font the engine builds at run time from a system font, with no frames of its own; no file sets it
 0x8000 one more byte per CADR pixel; with 0x2000 the two bytes are the 16-bit color of the second image, alone the byte is skipped; no sprite of the game
```

The animation of an object is a table of start frames and frame counts. Frame `n` of facing `f` in animation `a` is `start(a) + f * count(a) + n`, where `start(a)` adds up the frame counts of the earlier states times the direction count.

In Alien Shooter, the animation step moves an object to its next frame each time the game tick (in milliseconds) passes a multiple of the frame duration of its state, so every object with the same duration changes frame at the same moment. After the last frame of the state the step goes back to the first. Past the last frame, or on every frame with the flag 0x10, the step also runs the next queued action of the object or sends it an end-of-animation message; what each object type does with that message was not traced.

### Software sprites

Sprites with 0x01 and 0x10 set and 0x20, 0x40 and 0x80 clear hold their pixels in runs, one `DATA` record per frame. The game has 0x11, 0x19 (0x08), 0x1B (0x08 and 0x02) and 0x1D (0x08 and 0x04). All but 0x11 are paletted.

```
 record: u32 length
         i16 N, then N * 6 bytes of extra data
         i16 first row, i16 row count
         per row:
           [ skip u8 | count u8 | count * u16 z | count * u8 palette index ]*
           00 00                       end of row
```

The z values are present only when the flags have 0x04. A record of two bytes is an alias of an earlier frame.

### Texture sprites

```
 SURF record:  u16 count
               count * ( u16 width | u16 height | u32 raw size | surface data )

 DATA entry (36 bytes):  8 bytes unknown | x | y | w | h | left | top | surface
```

Surfaces are ARGB4444 and their raw size is `width * height * 2`. The single `DATA` record holds one entry per frame. `x`, `y`, `w` and `h` select the frame in the surface, and `left` and `top` place it on the sprite canvas.

With bit 0x100 the surface data is compressed by a carry-less range coder with an order-1 model of 257 symbols, where symbol 256 ends the data. A file with several surfaces shares the coder models across them, and each compressed surface starts right after the end of the one before. The decoded bytes hold the low bytes of all pixels first and the high bytes second.

Other flag combinations in use:

```
 0x36   runs of u16 z + u16 ARGB4444; alpha is always 0 and means additive blending
 0x11   (UI sprites) plain RGB565, no palette
 0xB0   omni-lights (flag 0x80); created by effects, never placed
```

### Cached sprites

Alien Shooter 2 keeps the frames of most sprites in a `CADR` chunk instead of `DATA`, one record per frame. The engine builds each one into a surface and, for mode 3, a triangle mesh.

```
 record: [ 12 bytes: f32 x 3 ]          only with the flag 0x200
         u16 index count
         u16 vertex count
         u16 width, u16 height          the frame's region
         u16 x, u16 y                   the region's origin on the canvas
         u16 * index count              triangle indices
         u32 * vertex count             packed vertices
         runs: [ skip u8 | count u8 | count * pixel ]*
         00 00                          end of the frame
```

A vertex is one u32 with three 10-bit fields and a 2-bit scale in the top bits. The engine reads the field at bit 20 as `(v >> 20 & 0x3ff) / (1 << (v >> 30)) - 32` to find the lowest and highest values of the mesh. The frames of mode 3 sprites carry the mesh, apart from the empty ones, and the counts are zero in all other frames.

The runs describe the region as one raster, `width` pixels per row. A run skips `skip` pixels, which are transparent, then holds `count` pixels, and may continue onto the next row. The frame ends at the `00 00` pair, and the record has nothing after it. The engine blits the region at `(x, y)`.

A pixel is `format` sized (see `HEAD`), and a palette index when the sprite has a palette. After the color come one byte for each of the flags 0x2000 and 0x8000, and then two bytes of depth with the flag 0x04, which the CADR decoder skips. The 16-bit and 32-bit formats have no palette. Alien Shooter 2 tints them at draw time like the other sprites. About fifty Alien Shooter 2 sprites, mostly shadows, glows and level props, use them. A zero pixel is transparent.

The vertices of a mode 3 frame give each pixel its height. A vertex is the column and row on the canvas in its first two 10-bit fields, and the third field is the height described above. The pixel height is interpolated over the triangles, and the depth of the pixel is `0x400 + 8 * height`, which is what Vengeance stores per pixel for the same sprites (a fit over the 22,365 pixels of 1780.vid frame 0 gives 8.05 * height + 1023.9).

A record of four bytes is an alias, the u32 index of an earlier frame to repeat. All 240 in the games point backwards. A record that is only the 12-byte header, with every field zero, is an empty frame.

A path ending in `.bmp` or `.tga` instead of `.vid` is loaded directly, with black as the color key.

## Menus (.men)

Kind `MENU`. A menu is a list of sprites with screen coordinates.

```
 RES "MENU"
 +------+---------------------------------------------+
 | HEAD | 20 bytes                                    |
 | SPR  | one record per element                      |
 +------+---------------------------------------------+

 SPR record, 32 bytes + optional text
 0        4      8     12   16     20          24   28
 +--------+------+-----+----+------+-----------+----+----+----------+
 | handle | id   | x   | y  | scale| directions| .. | .. | text, 0  |
 | u32    | u32  | f32 | f32| f32  | u32       |    |    | (41-45 B |
 +--------+------+-----+----+------+-----------+----+----+  records)|
```

The text is a label or a binding such as `Play`, `PlayerAmmo[2]` or `ItemPrice[8]`. Coordinates are screen pixels.

A button is object 705, and its direction byte is the button number the script sees. A label is object 3 whose text is a key of the `[menu]` section in `strings.ini`.

```
 +-------------------+   object 705, direction byte 128 = button 5
 |     [ Play ]      |   object 3, text "PlayAddon3"  -> strings.ini [menu]
 +-------------------+
```

## Scripts and text

### Scripts (.lgc)

C-like scripting language in plain text in cp1251, compiled when a map loads.

```
 #define MUSIC0 "music\\mus01.ogg"
 #include "maps\export.lgc"        engine functions
 #include "maps\common.lgc"        shared code

 main()                            runs every tick
 {
   iff(1) { InitGame(); }          level setup block
   ...
 }

 F419_15() { ... }                 handler F<object id>_<event> of an object
```

Engine functions are declared in `EXPORT.LGC` as `extern Name(args) <builtin>;`. The registry values `GameType` and `LevelNumber` choose the level flow. [NOTES.md](NOTES.md#scripts) describes the handlers that matter.

### Strings and text

```
 strings.ini
 [strings]       window and error texts
 [menu]          one key per menu label
 ItemNNN=...     item names, by object id

 Text/level_NN.txt        mission briefing: MISSION, DESCRIPTION, TIPS
 Text/addonNlevel_NN.txt  the same for the add-ons
 AlienShooter.cfg         [common] Title, RegPath
```

## Drawing a level

Both engines test every sprite against a depth buffer in which a larger value is nearer, and Alien Shooter 2 sets the Direct3D depth test to GREATEREQUAL.

```
 screen position = ( x - cameraX - width / 2,
                     y - z - cameraY - height / 2 )

 depth of a pixel = 8 * z + its own z value          sprites with depth data (Alien Shooter)
                  = 8 * z + 0x400 + 8 * h            mesh frames, h interpolated over the triangles
                  = 8 * z + 0x400                    other sprites
                  standing sprites add 8 per row up from the bottom (Alien Shooter)
```

A pixel is written where the buffer is at or below its depth (strictly below for sprites with depth data). Sprites with the flag 0x02 blend with the alpha byte of the palette and do not write depth, so the order of the draw matters for them.

The shader file `Vid/Pseudo3Dnew.fx` shows how the Direct3D engine places a sprite. A world point (x, y, z) lands on the texture at `(x, y * 0.70706 - z)`, and the view matrix keeps the height as the depth, so the depth at one screen pixel grows with the absolute height of what is drawn there. A frame without a mesh is a flat quad at the height of the object, which is why Alien Shooter 2 has no standing rule. The engine also builds a G-buffer with a normal per pixel and adds point, spot and sun lights to it in the pixel shaders.

### Draw passes

Alien Shooter 2 sorts the object classes into passes when it loads their sprites and draws the passes in increasing order. The first rule that matches decides.

| pass | objects                                                                  |
|------|--------------------------------------------------------------------------|
| 10   | sprite `mode` 6                                                          |
| 0    | type 8 (scenery) without the flag 0x02 and with `blend` 0                |
| 2    | other type 8                                                             |
| 5    | type 23 (regions)                                                        |
| 11   | types 10 and 19 (interface and fonts)                                    |
| 7    | `blend` 4 or 5 (lights and glows)                                        |
| 3    | without the flag 0x02 and with `blend` 0 or 6                            |
| 9    | other sprites, unless they have the object flag 0x40000000 or `blend` 7  |
| 4    | the rest                                                                 |

Walls, floors and most props are type 8 and come first, blood and the soft black covers (1127) follow in pass 2, and monsters, pickups and other objects come in pass 3, so a cover never hides them. Inside a pass the engine goes through the cells of the level grid, rows from the top and cells from the left, and through the objects of each cell in list order. Depending on an engine setting, passes 0 and 2 are drawn directly or baked into cached ground textures of 256 x 256 pixels that keep the depth in their alpha channel. Objects hidden by their side (flag 0x20000) are skipped in every pass.

### Lights

Alien Shooter 2 lights the scene by the graphics detail level, which `SetGraphDetail` (script function 149) sets. The engine lowers any request above 5 to 5 before it picks a level, and it falls back to 3 when the card lacks a D24S8 depth-stencil format. The menu maps the High and Medium settings to 5 and Low to 3. The level 8 and 9 branches below exist in the code, but no setting reaches them.

```
 0       LOWEST, taken when the light texture cannot be created
 3       LOW, light buffer
 5       MEDIUM, light buffer
 8, 9    HIGH and HIGHEST, pixel shader 2.0 scene texture
```

The Low setting also hides the objects listed in `LowDetailVids` of `CONST.LGC`, such as the fog clouds 1306, and Low and Medium hide those in `MediumDetailVids`, such as the small shadow 108.

With the light buffer the engine draws passes 0 to 5 straight on the screen. It then clears the buffer to white through the ambient tint of `GRPH`, `255 * (256 - n) / 256 + p` with `n` and `p` the bytes of the negative and positive parts, times 0.3 (0.35 below level 5). Pass 7 goes into the buffer with the blend of each sprite, and the buffer is an 8-bit target whose channels stop at 1. A screen quad then multiplies the scene by twice the buffer (DESTCOLOR, SRCCOLOR), with the positive ambient added to the buffer as the specular color, and a second untextured white quad (DESTCOLOR, ONE) doubles the result again. Each step saturates, which leaves a scene color `d` under a buffer `b` at `min(1, 2 * min(1, 2 * d * min(1, b + p)))`. Passes 8 to 11 come last.

The draw adds the ambient tint, byte by byte and saturating, to the tint of every sprite in pass 7 and later whose object lacks the flag 0x800.

An omni-light is an object whose sprite has the flag 0x80. Its class always goes to pass 7, and an object with a light-only sprite is usually attached to a visible lamp. The light takes its color from the current frame, skipping frames whose color is black. Its tint is built like the tint of any sprite in pass 7 and applied to the color as `c * (256 - n) / 256 + p`. The light covers an ellipse on screen centered on `(x, y - z)`, with radii `ExtentX * ScaleX / 2` and `ExtentY * ScaleY / 2` of the object, each times the scale of the placement.

At levels 1 to 5 the engine draws the light as a quad from `(x - rx, y - z - ry + 10)` to `(x + rx, y - z + ry + 10)` and adds it to the buffer (ONE, ONE) without a depth test. The quad carries a 64 x 64 falloff texture and the color of the light times `0.2 / ((10 / ry)² + 0.2)`, with the largest channel as its alpha, and an alpha test drops texels whose alpha falls below 3 (4 below level 5). For a point `(u, v)` of the quad from -1 to 1 across each side:

```
 falloff = clamp((0.2 / (u² + (v / 0.70706)² + 0.04) - 0.2) * 256 / 5, 0, 255)
```

At levels 8 and 9 the engine draws passes 0 to 5 into a scene texture whose alpha channel keeps the height of each pixel over 512, copies it onto the screen through the ambient light as `c * (255 - n) / 255 + p` and draws pass 7 on top, where the omni-lights use the technique `DrawLight20` and add (ONE, ONE). For a pixel `(px, py)` of the scene color `s` and height `h`, and a light at height `z` with radii `rx` and `ry`:

```
 cx = (px - x) / rx
 cy = ((py - (y - z)) / 0.70706 - z + h) / ry
 cz = (z - h) / ry
 light = s * 0.2 * color / 255 * (1 / (cx² + cy² + cz² + 0.04) - 1)
```

Each light is clamped to 0..1 before it is added. The quad sits at the height `z + 150` with the depth test GREATEREQUAL, which leaves pixels higher than that unlit.

Shadows are on by default. The engine only switches them off when the card lacks a D24S8 depth-stencil format, and a debug key and a script command toggle them. An omni-light whose object has the flag 0x1000 draws them before its quad. It clears the stencil once per frame, gives each light its own reference value, and draws a card for every caster into the stencil only (no color, no depth write, depth test GREATEREQUAL, alpha test above 0, scissor on the light quad). The light then skips the pixels whose stencil equals its reference.

A caster is an object with the flag 0x1000 whose sprite has a `CADR` chunk, no blend, no flag 0x02 and no flag 0x80. The engine keeps casters in a separate list per grid cell and takes those in the cells under the light quad whose distance to the light is below `rx`. The distance is the octagonal estimate `0.961 * max + 0.398 * min`, applied to x and y and then to that result and z.

The card is the frame of the caster, mapped to the screen through its bounding rectangle in the canvas:

- The angle toward the light is `(int)(atan2(dx, -dy) * 256 / 2π)` truncated, with `dx` and `dy` from the caster to the light, and the shadow points the opposite way (the angle plus 128).
- Objects with the flag 0x20000000 use the direction frame of `facing - angle`, the side that faces the light. Others use their current frame.
- A frame is flat when its highest vertex times 1.8 is below its larger side, judged on the first frame for objects with the flag 0x20000000.
- Each corner starts at its canvas position minus half the canvas, raised by the height of the object above the terrain, and is scaled by the object scale times the placement scale. Upright cards turn by the shadow angle around the object. Flat cards only turn when the object has the flag 0x20000000, by what is left of its facing after the direction frame, and move `0.6 * highest` away from the light, with the y part times 0.70706.
- The corners land at `(x, y - z)` and then move down by the height above the terrain.
- The top corners get the height `0.3 * highest` and the bottom ones the lowest vertex, both times the z scale. The depth test then keeps the card on the floor around a caster and off the walls behind it.

### Black covers

Alien Shooter 2 maps hide the ground around the playable rooms with black objects: 1127 (a 350 x 350 sprite with soft edges), 1710 (solid blocks) and 1711 (1127's sprite with the flag 0x8000). 1127 and 1711 turn with their facing, which lines their edges up with the walls.

Many covers are placed at a reduced scale (see the sprite record), which keeps them inside the walls around an opening.

Covers over areas that open during play carry a name from `h1` to `h15` at the end of the payload, and the level script removes them with `EXT_KILLOBJECTS`. A map file shows the state before that. The unnamed covers stay for the whole level.

## Key functions

Addresses from Ghidra decompilation.

Alien Shooter (GOG):

- `FUN_00411b50` - loads the object table from `objects.res`
- `FUN_00413c30` - reads one object record
- `FUN_0040fc40` - finds a placed object by category and kind mask, id, type, side and state
- `FUN_0043f200` - creates a placed object, applying the spawn flags
- `FUN_00442ad0` - sets the side of an object and hides it when the flag 0x20000 asks for it
- `FUN_00419b80` - bakes a scenery object into the terrain tiles, skipping objects with the flag 0x20000
- `FUN_00447fe0` - creates a projectile, with its initial speed and vertical speed
- `FUN_0043fb10` - moves an object one step, applying gravity, homing and wind
- `FUN_00443810` - tests the collision mask of an object
- `FUN_00448130` - updates the motion of an object, with ground bounce and rocket height
- `FUN_00440110` - advances the animation of an object
- `FUN_00416860` - draws a software sprite into the z-buffer, applying the raised and bobbing flags
- `FUN_0043f820` - moves an object toward its rest height above the terrain
- `FUN_0044def0` - moves an attached object to its owner plus the offset in the record
- `FUN_00445770` - creates the attached object of a new object
- `FUN_0042d6a0` - inserts an object into the hit and blast index
- `FUN_00440830` - handles the messages of an object, including blasts

Alien Shooter 2 (Reloaded, GOG):

- `FUN_0041baa0` - bits-per-pixel for a sprite format code
- `FUN_0042b1e0` - steps through the chunks of a `RES ` container
- `FUN_0042b320` - reads the length of the next record in a chunk
- `FUN_004550e0` - decodes the RLE pixels of a `CADR` frame
- `FUN_00455ac0` - reads the vertices of a `CADR` frame and finds their depth range
- `FUN_00456cd0` - reads a `.vid` file and builds its frames into surfaces
- `FUN_00457890` - places and draws a sprite frame, using `mode` and `blend`
- `FUN_00458220` - sets the Direct3D blend and depth state of a sprite
- `FUN_00414b60` - sets the graphics detail level
- `FUN_0045a120` - puts the class of a sprite in its draw pass
- `FUN_00415840` - draws a frame of the level, with its passes and the light buffer it blends onto the scene
- `FUN_004493a0` - draws one pass, cell by cell of the level grid
- `FUN_00453c20` - draws an omni-light object and works out its color, tint and radius
- `FUN_00416d10` - draws the quad of one omni-light, per shader level
- `FUN_004168c0` - draws the stencil shadows of an omni-light
- `FUN_0044c690` - walks the casters under a light quad
- `FUN_00459a90` - draws the shadow card of one caster
- `FUN_00425ed0` - the angle from an object to a point
- `FUN_0043cac0` - runs one action of an object, by code
- `FUN_004143d0` - sets the view matrix
- `FUN_00414580` - sets the viewport and the projection matrix
- `FUN_00450f50` - the direction frame of a facing
- `FUN_004439d0` - creates the attached object of a new object
- `FUN_0043f2f0` - sets the side of an object and hides it when the flag 0x20000 asks for it
- `FUN_004514f0` - reads one object record
- `FUN_004422d0` - spawns the child objects of an animation state
- `FUN_0043b680` - applies the blast: damage roll, radius and push
- `FUN_00404170` - creates a placed object and rolls its initial vertical speed
- `FUN_00439a50` - rolls the initial speed of a placed object
- `FUN_0043f4e0` - sets the facing of an object
- `FUN_00440000` - tests the collision of an object
- `FUN_0040ecf0` - sends the touch message

Alien Shooter Vengeance:

- `FUN_00425358` - creates the sprite class for a `.vid` from its flags
- `FUN_00469800` - loads a cached sprite, including the fast load table
- `FUN_0046a5d0` - builds the quad and sets the blend state of a frame
