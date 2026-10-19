# Alien Shooter file formats

This document describes how the game stores its data, with enough detail to open the files by hand or with the toolkit.

All integers are little-endian. Text is cp1251 (Cyrillic) unless noted. Offsets are in bytes and written in decimal unless they start with `0x`.

## Contents

1. [The game folder](#the-game-folder)
2. [RES container](#res-container)
3. [objects.res](#objectsres)
4. [Maps](#maps)
5. [Sprites (.vid)](#sprites-vid)
6. [Menus (.men)](#menus-men)
7. [Scripts and text](#scripts-and-text)
8. [Drawing a level](#drawing-a-level)

## The game folder

```
AlienShooter.exe        the engine
AlienShooter.cfg        ini file (title, registry path)
objects.res             every object: stats, sprite path, weapons, sounds
strings.ini             interface strings
Maps/
  Level_NN.map          campaign level layout     RES "MAP "
  Level_NN.lgc          campaign level script     text
  ADDON/, ADDON2/       the two add-ons (same file layout)
  survive_01.map        survival level
  *.men                 menu layouts              RES "MENU"
  *.LGC                 shared scripts            text
  logo.map, mainmenu.map, shop.map, end.map
                        host maps for the menus (no sprites)
Vid/                    sprites                   RES "VID "
Text/                   mission briefings         text
Wav/  Music/            sounds (wav) and music (ogg)
Font/                   bitmap fonts              tga
cursores/               animated cursors          ani
Screens/                screenshots the game writes
```

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

An `OBJ ` record has a name, a fixed 688-byte body and the path of the sprite.

```
 0    4                  4+n+1      +688          +688+m+1
 +----+------------------+----------+-------------+-----------+
 | id | name, cp1251, 0  | body     | sprite path, 0          |
 +----+------------------+----------+-------------+-----------+
                           688 bytes   "vid\040.vid" or "font\..."
```

Known fields of the body, counted from its first byte.

```
 body +0     u32   (unused here)
      +4     u32   type
      +8     u32   flags (see below)
      +16    f32   extent x   \
      +20    f32   extent y    > collision and footprint size
      +24    f32   extent z   /
      +28    i32   hit points
      +112   u32   direction count (at least 1)
      +116   u32   frames per direction in the first animation
      +0x294 i32 x3   color tint: red, green, blue
```

Object flags.

```
 0x00000020   solid, blocks movement
 0x00000040   breakable, blocks until shot
 0x00008000   drawn over everything below it (items)
 0x00010000   bobs up and down
 0x00020000   visibility depends on the level (only object 631)
 0x01000000   starts its animation on frame 0 instead of a random frame
```

The entries set 16 other bits whose meaning is not known, and `ObjectFlags` leaves them unnamed.

The tint changes the palette of the sprite. A negative value scales the channel to `(256 + t) / 256` of itself and a positive value adds `t`. Yellow and red monsters, and the blue survival bosses, are the same sprite with different tints.

Ids that matter are named in `KnownObject` in the source (for example 9 is the player start, 419 a crate and 705 a menu button).

## Maps

Kind `MAP `. A level is a list of sprites placed in a world, plus the small chunks that go with it.

```
 RES "MAP "
 +------+-----------------------------------------------------------+
 | GRPH | ambient light and grid data                               |
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
 | w   | h   | x      | y      |      | = 12    |
 +-----+-----+--------+--------+------+---------+
```

`SPR ` has one record. It is an array of 28-byte entries closed by `0xFFFFFFFF`.

```
 0        4     8   12   16   20    21   24     28
 +--------+-----+---+----+----+-----+----+------+
 | handle | id  | x | y  | z  | dir | .. | army |
 | u32    | u32 |f32|f32 |f32 | u8  |pad | u32  |
 +--------+-----+---+----+----+-----+----+------+
          object id
          from objects.res
```

Maps of version 9 and 10 (survival, the menu host maps and a few add-on maps) store x, y and z as `i32`.

`SPRD` has one record per sprite, matched by handle, and ends with an `0xFFFFFFFF` record. The payload is what makes a sprite behave.

```
 handle   payload
 +------+---------------------------------------------------------+
 | u32  | 8 zero bytes          most sprites                      |
 |      | 12 bytes              monsters                          |
 |      | 20 bytes              the player (object 9)             |
 |      | 220-268 bytes         script spawners (object 908)      |
 |      | other sizes           crates, teleports, spawn walls    |
 +------+---------------------------------------------------------+
```

A spawner payload is a small program. See [NOTES.md](NOTES.md#spawners-908).

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

Kind `VID `. A sprite is a set of frames, laid out as animations and facings.

```
 RES "VID "
 +------+---------------------------------------------+
 | HEAD | 10 bytes                                    |
 | PAL  | 1,024 bytes, 256 entries of B G R X         |
 | DATA | one record per frame                        |
 | SURF | (texture sprites) the surfaces              |
 +------+---------------------------------------------+

 HEAD
 +------+-------+--------+-------+--------+
 |flags | speed | frames | width | height |
 | u16  | u16   | u16    | u16   | u16    |
 +------+-------+--------+-------+--------+
          ms per frame
```

Flags. A sprite kind is a combination of them, and the values quoted in this section are such combinations.

```
 0x01   unknown; on every sprite with frames except 0x36 and 0xB0
 0x02   blends with what lies under it, using the alpha of the palette
 0x04   per-pixel depth values
 0x08   palette
 0x10   current format version
 0x20   unknown; on every sprite with a SURF chunk, and on 0x36 and 0xB0
 0x40   unknown; only on 0x53, which has a CADR chunk
 0x80   unknown; only on 0xB0
 0x100  surfaces are range coded
 0x800  DXT compressed surfaces
```

The animation of an object is a table of start frames and frame counts. Frame `n` of facing `f` in animation `a` is `start(a) + f * count(a) + n`.

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
               count * ( u16 width | u16 height | u32 raw size )
               surface data

 DATA entry (36 bytes):  8 bytes unused | x | y | w | h | hotspot x | hotspot y | surface
```

Surfaces are ARGB4444. With bit 0x100 they are compressed by a carry-less range coder with an order-1 model of 257 symbols. A file with several surfaces shares the coder models across them.

Other flag combinations in use:

```
 0x36   runs of u16 z + u16 ARGB4444; alpha is always 0 and means additive blending
 0x11   (UI sprites) plain RGB565, no palette
 0xB0   omni-lights, 2 x 2 header, frames of BGRA colors; created by effects, never placed
```

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

The engine draws every sprite into a 16-bit z-buffer, so file order does not matter.

```
 screen position = ( x - cameraX - width / 2,
                     y - z - cameraY - height / 2 )

 depth of a pixel = 8 * z + its own z value          sprites with depth data
                  = 8 * z + 0x400                    flat sprites
                  standing sprites add 8 per row up from the bottom
                  flag 0x8000 adds 0x3FFF            items stay visible
```

A pixel is written where the buffer is at or below its depth (strictly below for sprites with depth data). Sprites with the flag 0x02 blend with the alpha byte of the palette and do not write depth.
