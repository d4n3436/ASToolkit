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
- Most cached sprites have the depth flag 0x04, with two depth bytes after the color of each pixel. The Alien Shooter 2 files dropped both from 580 sprites. Those have the flags 0x48 instead of 0x4C.
- Sprite `meshType` keeps the values 0 and -1 that the Alien Shooter 2 loader rewrites to 1.
- `Vid/fast_load.vid` and `Vid/fast_load_data.vid` are not `RES` containers. They are the sprite cache the header key refers to.
- In `1195.vid` and `1196.vid` the PAL chunk size is 20 bytes short, and the `CADR` chunk starts 20 bytes past where that size points.

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

Each chunk has an 8-byte header and includes a pad byte if its length is not even. The last chunk in the game's files has no pad byte. The chunk's `size` doesn't include the pad byte.

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

Without bit 31 the flags word is the record count and the reserved and count fields are absent. Tags can repeat, and the engine adds up their counts.

Chunks with `count` records are tables. Most chunks hold one record with a packed array inside. Others, such as `OBJ ` in objects.res, hold one record per entry.

## objects.res

Kind `DATA`. It defines every object the game knows by an id from 0 to 1000.

```
 RES "DATA"
 +------+--------------------------------------------------+
 | OBJ  | one record per object                            |
 | WEAP | one record per weapon                            |
 | CNST | one record of 104 bytes                          |
 | SFX  | one sound list per record                        |
 +------+--------------------------------------------------+
```

`OBJ ` records hold a name, a fixed-size body and the path of the sprite. The body is 688 bytes in Alien Shooter, 704 in Alien Shooter 2, 696 in Zombie Shooter and 708 in Zombie Shooter 2.

```
 0    4                  4+n+1      +body         +body+m+1
 +----+------------------+----------+------------+-----------+
 | id | name, cp1251, 0  | body     | sprite path, 0         |
 +----+------------------+----------+------------+-----------+
                           see above   "vid\040.vid" or "font\..."
```

Body fields:

```
 AS1    AS2    type     field
 +0     +0     u32      SpriteType: a U_ mask (see below)
 +4     +4     u32      SpriteClass: a B_ value (see below)
 +8     +8     u32      Property: P_ flags (see below)
 +12    +12    u32      MoveMask: two objects collide when their masks share a bit; 0 never collides
 +16    +16    f32      SizeXYZ[0]   \
 +20    +20    f32      SizeXYZ[1]    > footprint size (top-down)
 +24    +24    f32      SizeXYZ[2]: the height
 +28    +28    i32      MaxHP: 0 for non-damageable objects
 +32    +32    f32      MaxSpeed: highest speed along the facing, in milli-units; the initial speed of a spawned object (in Alien Shooter 2 the minimum of a random range); 999999 reaches the goal at once
 -      +36    f32      maximum initial speed, in milli-units
 +36    +40    f32      MaxZSpeed: vertical speed, in milli-units; in Alien Shooter 2 the minimum initial vertical speed
 -      +44    f32      maximum initial vertical speed, in milli-units
 +40    +48    f32      Acceleration[0]: acceleration, in millionths; 999999 is instant
 +44    +52    f32      Acceleration[1]: deceleration, in millionths; 999999 is instant
 +48    +56    f32      RotationPeriod: full turn in milliseconds; 0 turns instantly and 999999 never turns
 +52    +60    u32      NWeapon: index of the WEAP record of the object (31, 15 or 11 on bodies, 32 on bullets, 0 on effects)
 +56    +64    f32      DeathRange: blast radius added to the footprint of the object when it dies or hits something
 +60    +68    i32      DeathDamage: damage sent to everything the blast reaches; in Alien Shooter 2 the minimum of a random range
 -      +72    i32      VID_DAMAGE_MAX: maximum damage
 -      +76    f32      push: speed added to everything the blast hits, weaker with distance
 +64    +80    f32 x3   LinkCoor[3]: offset of the linked object from this one (x, y, z; Alien Shooter 2 flips the sign of y on load)
 +76    +92    i32      LinkVid: object id of the linked object (a player body part, a weapon, a muzzle flash)
 +80    +96    f32      TopZ: height above the terrain the object keeps when it is not falling; 0 for none
 +84    +100   f32      highest step up the object climbs; also how far a bobbing object rises and sinks
 +88    +104   f32      deepest drop the object walks off
 +92    +108   i32      VID_LIFETIME: lifetime in milliseconds; 999999 means forever
 +96    +112   -        16 bytes every reader skips (always 0)
 +112   +128   u32      NoDir: direction count (at least 1)
 +116   +132   i32 x17  NoFrame[17]: frames per direction of each animation state
 +184   +200   i32 x17  SFX[17]: sound id of each state (an index into the SFX chunk)
 +252   +268   i32 x17  VID_FRAME_SPEED: frame duration of each state in milliseconds; 0 takes the speed of the sprite header
 +320   +336   f32 x17  ChildX[17]: x offset of the object created in the state, rotated with the facing
 +388   +404   f32 x17  ChildY[17]: y offset of the object created in the state, rotated with the facing
 +456   +472   f32 x17  ChildZ[17]: height offset of the object created in the state
 +524   +540   i32 x17  ChildVid[17]: object created in each state, mostly an effect or a projectile
 +592   +608   i32 x17  NoChild[17]: number of objects created in the state, 1 on most; 2 makes a mirrored pair
 +660   +676   i32 x3   GammaRGBA[0..2]: red, green, blue
 +672   +688   i32      GammaRGBA[3]: alpha; negative on smoke, steam and shadows
 +676   +692   f32 x3   ScaleXYZ[3]: scale along x, y and the height; 1.0 by default, mostly used by fx and ui objects
```

The 696-byte body of Zombie Shooter (Alien Shooter spinoff) is the Alien Shooter 2 layout without `VID_DAMAGE_MAX` and the push (+72 and +76), with every later field 8 bytes earlier.

Zombie Shooter 2 inserts a four-byte field, read with the `BlackWhite` setting, between the alpha gamma and scale. Scale starts at +696.

```
 ZS2     type     field
 +692    i32      value retained when BlackWhite is enabled
```

Objects have 17 animation states, numbered 0 to 16 and named by the `ANI_` constants.

| state | names                                    |
|-------|------------------------------------------|
| 0     | `ANI_STAND`                              |
| 1     | `ANI_STOP_MOVE`, `ANI_BUILD`             |
| 2     | `ANI_GO`                                 |
| 3     | `ANI_START_MOVE`                         |
| 4     | `ANI_L_ROTATE`                           |
| 5     | `ANI_R_ROTATE`                           |
| 6     | `ANI_OPEN`                               |
| 7     | `ANI_CLOSE`, `ANI_HIT`                   |
| 8     | `ANI_FIGHT`                              |
| 9     | `ANI_SALUT`                              |
| 10    | `ANI_STAND_OPEN`                         |
| 11    | `ANI_LOAD`, `ANI_CLASH_VERT`, `ANI_LINK` |
| 12    | `ANI_UNLOAD`, `ANI_LAZY`, `ANI_CLASH`    |
| 13    | `ANI_WOUND`                              |
| 14    | `ANI_BIRTH`                              |
| 15    | `ANI_DEATH`                              |
| 16    | `ANI_DEATH2`                             |

Menu buttons and fonts reuse the low states under their own names (`ANI_MENUSTANDDOWN` to `ANI_MENUDISABLEUP`, `ANI_FONT_NORMAL` to `ANI_FONT_NEXT_LINE`). States from `ANI_DEATH` up end the object. The monsters of the data use an id for the body, the next one for its death effect and the one after for its corpse, with the death effect named in `ANI_DEATH` of the body. The sprite needs at least `NoDir` times the sum of `NoFrame` frames.

`VID_BATTLERANGE`, `VID_DETECTRANGE`, `VID_AIM_RADIUS`, `VID_RELOAD_TIME` and `VID_RELOAD_TIME_IN_VOLLEY` are fields of the WEAP record the object points at, not of the object.

`WEAP` records are 612 bytes in Alien Shooter and 640 bytes in Zombie Shooter, Zombie Shooter 2 and Alien Shooter 2.

```
 AS1    AS2/ZS  type   field
 +0     +0      u32    target SpriteType mask
 +4     +4      u32    weapon flags (meanings below)
 +8     +8      f32    length of a train car along its track
 +12    +12     f32    weight; only a greater power pushes or pulls the object; 999999 for objects nothing moves
 +16    +16     f32    power with which the object pulls a train or pushes what it runs into; 0 in Alien Shooter
 -      +20     i32    Alien Shooter 2 and Zombie Shooter 2: milliseconds between target decisions; 999999 never, 0 every tick
 +20    +24     f32    VID_DETECTRANGE: distance at which the unit notices an enemy and chases it
 +24    +28     f32    VID_BATTLERANGE: farthest distance at which the unit attacks
 +28    +32     f32    VID_WEAPONAIM (Alien Shooter, Zombie Shooter), VID_AIM_RADIUS: aim spread
 -      +36     i32    attacks of a volley
 +40    +40     i32    VID_AMMO: most ammunition the weapon holds; 999999 or more means unlimited
 +32    +44     i32    VID_RELOAD_TIME (Zombie Shooter on): milliseconds between attacks, after a volley where there are volleys
 -      +48     i32    VID_RELOAD_TIME_IN_VOLLEY: milliseconds between the attacks of a volley
 +36    +52     i32    VID_BUILDTIME: build time in milliseconds
 +44    +56     i32    army of the objects the attack creates; 1 on monsters, 0 on the player
 +48    +60     i32    behavior mask (BEH_): 1 BEH_AGGRESSIVE attacks an enemy in weapon range, 2 BEH_ACTIVE moves toward an enemy it sees but cannot attack yet, 4 BEH_CAREFUL, 8 BEH_HAUNT, 16 BEH_BIGFIRE; 3 on normal monsters
 -      +64     -      20 bytes no reader uses (always 0)
 +56    +84     i32    rank: attackers prefer the higher-ranked of two equal targets; 0 in Alien Shooter, 5 or 6 (the player) in Alien Shooter 2
 +60    +88     f32    closest distance at which the unit attacks
 +64    +92     i32    period in milliseconds of the keyframe animation, 999999 to run it once over the death animation
 +68    +96     f32 x8 keyframe times as shares of the period
 +100   +128    i32 x8 red gamma at each keyframe, -255 to 255 (negative darkens, positive brightens)
 +132   +160    i32 x8 green gamma at each keyframe
 +164   +192    i32 x8 blue gamma at each keyframe
 +196   +224    i32 x8 alpha at each keyframe
 +228   +256    f32 x8 factor on the scale along x at each keyframe (1 by default)
 +260   +288    f32 x8 factor on the scale along y at each keyframe
 +292   +320    f32 x8 factor on the scale along the height at each keyframe
 +324   +352    f32 x8 horizontal screen offset in pixels at each keyframe (0 by default)
 +356   +384    f32 x8 vertical screen offset in pixels at each keyframe
 +388   +416    f32 x8 third component of the offset track; no reader found
 +420   +448    i32 x24 three keyframe tracks; the later games keep their low bytes; no reader found
 +516   +544    f32 x8 no reader found
 +548   +576    f32 x8 speed added at each keyframe, scaled by 0.001 on load unless 999999
 +580   +608    f32 x8 scaled by 0.001 on load unless 999999; no reader found
```

The weapon record drives the targeting and attacks of a unit. Units notice enemies within `VID_DETECTRANGE`, chase them with `BEH_ACTIVE` and attack between the closest distance and `VID_BATTLERANGE` once the reload time has passed, scattering their shots by the aim value. The damage of an attack belongs to the object the `ANI_FIGHT` state creates, or to the projectile that object creates in turn (a shotgun flash creates 10 pellets of 5 damage). Ammunition counts in 1/64 rounds up to `VID_AMMO`. Melee monsters have a battle range of 50 to 115 and ranged ones 260 to 750. The keyframe tracks animate the gamma, scale, screen offset and speed of the object over the period, and Alien Shooter uses only the gamma.

The later games also read these weapon flags:

```
 0x00000004   between two equal targets, takes the one ahead (ZS2, AS2 guardian robots 3020, 3021)
 0x00000100   shoves what the moving object runs into
 0x00000200   carries what the object touches along with its movement
 0x00000400   delays target decisions to the last frame of the animation
 0x00000800   a torso aiming apart from its legs (also Zombie Shooter)
 0x00001000   moves straight toward the target instead of along the facing
 0x00004000   a linked object whose facing shows its owner's health
 0x00008000   keeps moving after an attack
 0x00020000   deals 5 damage to what the object bumps into
```

Weapon flag 0x80 lets a unit target its own army, and 0x20 removes the scatter against units.

When an object dies or hits something, its blast sends `DeathDamage` to every object with hit points within its footprint plus `DeathRange`, less with distance with `P_RadialDamage`. Hit units play `ANI_CLOSE` when it has frames. Health below half selects `ANI_WOUND`, and fatal hits select `ANI_DEATH2` when the damage exceeds the maximum health and `ANI_DEATH` otherwise. The armor objects (200 to 202) linked to the player take 50, 70 and 90 percent of its damage. The endless monster generators carry no hit points and fall only to a dynamite blast.

Each animation state creates the objects of its `ChildVid`, `NoChild` times, at its child offset turned with the facing. Blood, corpses, casings and debris come from these chains. Alien 50 creates the blood 756 when hit and the death effect 51 when it dies, and 51 leaves the corpse 52. Linked objects (`LinkVid`) come with their owner, take its army and follow it at `LinkCoor` turned with the facing, unless they have the flag 0x2000000. Armed linked objects are upper bodies that fire for their owner. Objects with a lifetime other than 999999 are removed once it has passed.

`SpriteClass` is compared against literal values, `Property` is tested with bit masks, and `SpriteType` is a mask.

Alien Shooter uses the `MoveMask` values 7 (most monsters and some projectiles), 8 (projectiles), 15 (the player, scenery and some monsters), 16 (death effects) and 31 (walls and generators). Alien Shooter 2 adds many more, including 64 and 128 for turrets, vehicles and its other new groups.

Scripts look objects up by a mask over `SpriteType` (the `GETSPRITE_` constants), id, `SpriteClass`, army and state.

The two games number the `U_` constants differently. Alien Shooter 2 added `U_DUMMY` and moved the others one bit up:

| `U_`        | Alien Shooter | Alien Shooter 2 | objects of the data                                                 |
|-------------|---------------|-----------------|---------------------------------------------------------------------|
| `U_DUMMY`   | -             | 1               | lights, glows, helper arrows                                        |
| `U_TERRAIN` | 1             | 2               | corpses, casings, craters, shadows, dried blood, fx under pickups   |
| `U_OBJECT`  | 2             | 4               | pickups, props, wall decor, lights, frozen monsters                 |
| `U_UNIT`    | 4             | 8               | monsters, drones, generators, teleports, player legs                |
| `U_AVIA`    | 8             | 16              | the gun stand placeholders of Alien Shooter 2                       |
| `U_MENU`    | 16            | 32              | fonts, logos and other interface sprites                            |
| `U_RAILWAY` | 32            | 64              | none                                                                |
| `U_REGION`  | 64            | 128             | the region of Alien Shooter 2                                       |
| `U_TACTIC`  | 128           | -               | none; a map editor mode                                             |
| `U_CANNON`  | 512           | 512             | bullets, explosions, particles, monster death effects               |
| `U_SPRITE`  | 1024          | 1024            | player torso and head, cursor, linked effects                       |

`SpriteClass` values:

| `B_`               | value | objects of the data                                  |
|--------------------|-------|------------------------------------------------------|
| `B_TERRAIN`        | 0     | spawn walls, light volumes                           |
| `B_OBJECT`         | 1     | pickups, props, player torso and head, dummy blocks  |
| `B_UNIT`           | 2     | monsters, drones, turrets                            |
| `B_BUILDING`       | 3     | monster generators and teleports                     |
| `B_CANNON`         | 5     | bullets, monster death effects, placed flying gibs   |
| `B_Man`            | 7     | player legs, player vehicles                         |
| `B_BuildedTerrain` | 8     | scenery, corpses, casings, craters, wall drawings    |
| `B_SPRITE`         | 9     | lights, shadows, glows, cursor, loading              |
| `B_FRAME`          | 10    | logos, frames, faces, placed in screen space         |
| `B_LINKER`         | 12    | muzzle flashes, halos                                |
| `B_TEXT`           | 19    | font sheets, one glyph per frame                     |
| `B_REGION`         | 23    | regions (Alien Shooter 2), sized per placement       |
| `B_CREATURE`       | 25    | one monster weapon                                   |

Every placed object is live when the map loads. Placed cannons, such as the flying gibs (151) of levels 2 and 10 and the explosions (157) of levels 6 and 8, play as if just created.

`B_Monster` is another name for `B_UNIT` (2), and `U_Monster` for the `U_` value 4. No object uses `B_AVIA` or `B_Plane` (4), `B_PRIMITIVE` (6), `B_CIV_ROBOT` (20), `B_ENGINE` (21), `B_RAIL` (22), `B_DEPO` (24), `B_BALLOON` (26), or `B_MISSILE` or `B_BigMissile` (27).

Object ids index the object table.

`ScaleXYZ` multiplies the scale of each placement (a menu button of 1.1 x 1, a volume bar of 1 x 1.35, a small explosion of 0.65). On omni-lights it scales the radius of the light.

Object flags (`Property`). The Alien Shooter files set only part of them, and the Alien Shooter 2 files set the rest. 0x400 and 0x2000000 have no name.

```
 0x00000001   P_RandBirth: the object is born with a random facing (casings, craters, holes, dried blood)
 0x00000002   P_Gravity: leaves the ground and falls with the first gravity constant
 0x00000004   P_Gravity2: falls with the second gravity constant; this flag and 0x02 also mark the object as moving
 0x00000008   P_BuildSizeToGridZ: raises the height grid along the footprint outline
 0x00000010   P_Track: runs the queued actions on every frame instead of only after the last
 0x00000020   P_BuildVidZToGridZ: raises the height grid with the depth of the sprite
 0x00000040   P_Hash, P_Map: reachable by shots and blasts
 0x00000080   P_BirthAsSmoke: plays while it is alive (flame, gun smoke)
 0x00000100   P_Noise: scatters the spawn position by up to 8 units; in Alien Shooter 2 a footprint turned with the facing
 0x00000200   P_ZeroZ: the height comes from the level's height map
 0x00000400   random speed: starts with a random speed up to the record's speed; no object sets it
 0x00000800   P_Gamma: the ambient gamma does not apply to the object
 0x00001000   P_Wind: moved by the wind; in Alien Shooter 2 casts shadows, and omni-lights with it draw them
 0x00002000   P_SkipMapEd: hidden from the object list of the editor; Alien Shooter 2 turns one frame by the whole facing
 0x00004000   P_Crush: receives a touch message when another object overlaps it
 0x00008000   P_AlwaysTop: drawn over everything below it (items)
 0x00010000   P_Wave: bobs up and down by the field at +84 (+100)
 0x00020000   P_InvisibleForEnemy: hidden while its army differs from the army of the player
 0x00040000   P_CreateChildEnd: state children emitted at the last frame of the state instead of the first
 0x00080000   P_VertDir: picks the direction frame by the angle of its motion while moving vertically
 0x00100000   P_Hole: Alien Shooter moves the object straight toward the position of its goal instead of along its facing
 0x00200000   P_Blur: the sprite is stretched between the object and its target
 0x00400000   P_RandZSpeed: starts with a random vertical speed, up or down
 0x00800000   P_DblLight: doubles an Alien Shooter omni-light
 0x01000000   P_OnePhase: starts its animation on frame 0 instead of a random frame
 0x02000000   fixed offset: a spawned or linked object keeps its offset from the parent instead of rotating it with the parent's facing
 0x04000000   P_RadialDamage: damages everything inside its radius when it hits something, less with distance
 0x08000000   P_SelfMoving: flies toward its target and follows the height of the target
 0x10000000   P_SmoothMove: Alien Shooter bounces the object off the ground, reversing and halving its vertical speed, instead of stopping it
 0x20000000   P_HardwareDirect: turned on the ground plane by the facing between direction frames (Alien Shooter 2)
 0x40000000   P_Ground: drawn on the ground layer (blood, craters, floor tiles)
 0x80000000   P_NotDamageForFriend: a hit or blast from the object skips objects of its own army
```

The army is two bits (11 and 12) of the state of a placed object in Alien Shooter and three bits (17 to 19) in Alien Shooter 2, and it indexes the per-army tables of the object, such as its maximum health and unit count. Placed objects take it from the map, and spawned and linked objects from their parent. Sprites with the flag 0x400 pick their palette by it.

The helper blocks of the Alien Shooter 2 editor (630 to 634, the pillars 1357 to 1359, the teleport markers) and the depth blocks linked to walls (2706, 1799 and the like) carry the flag 0x20000 and army 2. They stay hidden and still raise the height grids. The blocks 630 to 632 are named dummies for Z, and 633 and 634 blocks for monsters and for the player. In Alien Shooter the only object with the flag 0x20000 is the depth blocker 631.

The `CNST` record is 104 bytes in every game. Speeds are stored in thousandths and gravities in millionths. Most fields serve repair, resupply, trains and the interface. The gammas at +80, +84 and +88 take the script form of `LightOff`, and every game stores zero.

```
 +0     f32   fastest edge scroll along x
 +4     f32   fastest edge scroll along y
 +8     f32   Gravitation: 174 in Alien Shooter, 174 in Alien Shooter 2
 +12    f32   Gravitation2: 84 in Alien Shooter, 800 in Alien Shooter 2
 +16    i32   hit points one repair step restores
 +20    i32   resupply steps: each one refills the ammunition capacity divided by this
 +24    f32   fastest train speed
 +28    f32   5 in every game; no reader found
 +32    f32   slowing of a pushed object's slide; 600 in Alien Shooter and Zombie Shooter, 150 in the later games
 +36    i32   factor on a unit's build time in seconds
 +44    i32   120 in every game; no reader found
 +48    i32   120 in every game; no reader found
 +52    i32   milliseconds the cursor rests before its hint shows, repeated every 500 ms
 +56    i32   2 in every game; no reader found
 +60    i32   2 in every game; no reader found
 +68    i32   2000 in every game, read by the train code
 +72    i32   3000 in every game; no reader found
 +76    i32   30 in every game; no reader found
 +80    u32   script gamma of the selected unit
 +84    u32   script gamma of an army 1 object under the cursor
 +88    u32   script gamma of any other object under the cursor
 +96    f32   speed above which a train crashes
 +100   i32   1000 in every game; no reader found
```

In Alien Shooter 2, `P_HardwareDirect` shows the direction frame closest to the facing and turns the sprite on the ground plane by the remainder. With `P_SkipMapEd` it turns one frame by the whole facing, and with `P_VertDir` by the angle of the motion. The soft black cover 1127 lines its edges up with the walls this way.

`GammaRGBA` tints the sprite. Negative values scale a channel to `(256 + t) / 256` of itself and positive ones add `t`. The gamma of a placement replaces it, and the scripts read and write it per army. Yellow and red monsters, and the blue survival bosses, are the same sprite with a different `GammaRGBA`.

## Maps

Kind `MAP `. Levels are lists of placed sprites with a few small chunks.

```
 RES "MAP "
 +------+-----------------------------------------------------------+
 | GRPH | ambient light, environment and wind                       |
 | HEAD | map size, starting camera, version                        |
 | GRID | (optional) the painted height grid, see Movement          |
 | SPR  | all placed sprites                                        |
 | SPRD | one extra-data record per sprite                          |
 | PLAY | a sprite handle for each player, zero in every map        |
 | GROU | groups of sprites                                         |
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
 | u32   | gamma - | gamma + | u32 | f32 | = -1 | = 32 | = 40 |          |
 +-------+--------+--------+-----+-----+------+------+------+----------+
```

The ambient gamma is packed like the gamma of a placed sprite and sets the ambient light.

`flags` is the environment the map starts with, loaded as the current environment that `SetEnvironment` later changes. Its bits are the `ENV_` values of `EXPORT.LGC`:

```
 0x00000001   ENV_SHADOW
 0x00000002   ENV_LONGSHADOW
 0x00000004   ENV_EARTHQUAKE, the camera shakes
 0x00000020   ENV_FOG
 0x00000040   ENV_GROUND_SNOW, snow on the ground
 0x00000080   ENV_SQUALL, a gust that raises the wind for 4 seconds and clears itself
 0x00000100   ENV_FOGINV, inverted fog
 0x00000200   ENV_FOGAPPEAR, fog that slowly appears and fades
 0x00000400   ENV_STOP_RAIN, rain slowly stopping
 0x00000800   ENV_BORN_RAIN, rain slowly starting
 0x00000C00   ENV_RAIN
 0x00004000   ENV_STOP_SNOW, snow slowly stopping
 0x00008000   ENV_BORN_SNOW, snow slowly starting
 0x0000C000   ENV_SNOW
 0x80000000   ENV_STOP, clears the given bits when passed to SetEnvironment
```

Only the earthquake, ground snow, squall, rain and snow bits take effect. Every map of the original games stores zero, Alien Shooter Revisited's level 1 starts with rain and two Alien Shooter 2 Conscription maps start with shadows.

`dir` and `len` are the wind as `SetWind` sets it, its direction in 256ths of a turn in the low byte of `dir` and its speed in pixels per millisecond. Every map has `dir` 64 or 200 and `len` 0.025. The fields from byte 20 on are unused.

`SPR ` has one record. It is an array of entries closed by `0xFFFFFFFF`, 28 bytes each before version 16 and 48 bytes from version 16.

```
 0        4     8   12   16   20    24     28       32       36    40    44    48
 +--------+-----+---+----+----+-----+------+--------+--------+-----+-----+-----+
 | handle | id  | x | y  | z  | dir | army | gamma - | gamma + | sx  | sy  | sz  |
 | u32    | u32 |f32|f32 |f32 | u32 | u32  | u32    | u32    | f32 | f32 | f32 |
 +--------+-----+---+----+----+-----+------+--------+--------+-----+-----+-----+
          object id                         version 16 and later
          from objects.res
```

Maps before version 10 (survival, the menu host maps and a few add-on maps of Alien Shooter) store x, y and z as `i32`.

The engines keep only the low byte of `dir`, the facing in 256ths of a turn clockwise from up the screen, and the other three bytes are zero in every map.

Alien Shooter keeps two bits of the army, 0 to 3, and the later games keep three, 0 to 7. The levels use 0 for the player and most scenery, 1 for monsters and generators, and 2 for breakable objects, walls and corpses. In Alien Shooter, monsters of army 1 hunt only the units of army 0, and units never target army 2 unless their weapon has the flag 0x80. The later games set how armies treat each other with `SetRelation`, and their `CONST.LGC` makes 3 the guards, 4 the hypnotized monsters, 5 the robots and 6 the monsters of looping generators, left out of the kill counts.

Alien Shooter maps place the player as the legs (9), or the female player legs (549) in the first level of the second add-on. The start script of `COMMON.LGC` replaces the placed player with object 9, and with the female objects 549 to 560 when the `PlayerSex` profile value is female.

The gamma of the placed object is packed in two `u32`, with alpha, red, green and blue from the high byte down. The first holds the magnitudes of the negative channels and the second the positive ones, the same packing the engine builds from `GammaRGBA`. When either is nonzero it replaces the gamma of the object for that placement.

`sx` and `sy` scale the sprite on the ground plane before it is turned, and `sz` scales the heights of its mesh. Maps before version 16 read as a scale of 1. About 3,000 placements of Alien Shooter 2 use another scale, from 0 to 8.

`SPRD` has one record per sprite, matched by handle, and ends with an `0xFFFFFFFF` record. The payload holds the behavior of the sprite.

```
 handle   payload
 +------+------------------------------------------------------------+
 | u32  | 8 zero bytes          most sprites                         |
 |      | 12 bytes              monsters                             |
 |      | 20 bytes              the player (object 9)                |
 |      | 220-268 bytes         script spawners (object 908)         |
 |      | 12-20 bytes           crates (object 419)                  |
 |      | other sizes           teleports, spawn walls               |
 |      | 12 bytes + name       Alien Shooter 2, see Drawing a level |
 +------+------------------------------------------------------------+
```

In Alien Shooter every payload has the same layout:

```
 u32      entry count
 entry    (u32 act, i32 a, i32 b, i32 c) * entry count
 u32      item count
 u32      item ids * item count
 u32      3 on most monsters, spawners and teleports (450), 0 on crates and player starts; absent on other sprites
```

The 8 zero bytes of most sprites are an empty stack and an empty item list. Monsters store `00000000 00000000 03000000`, and the player start carries two items (`02000000 05010000 05010000`). Crates hold their loot in the item list, one item in most and two in a few, and empty crates are 12 zero bytes. Spawners (908) and spawn walls keep their program in the stack. [NOTES.md](NOTES.md#spawners-908) describes the spawners.

Stacks run from their last entry, one entry at each animation tick. Entries below 0x11 play the animation state of that number, and the others run the action of the low byte of the act. The spawners use the proximity trigger (43), the pause (40), `BuildUnit` (35), `GotoStack` (71) and `StopStack` (73). Units a sprite builds inherit the entries under `StopStack` and walk their `Move` (33) entries as waypoints.

In Alien Shooter 2 the payload of a sprite with behavior starts with its action stacks and ends with its name:

```
 u16      last stack run
 u16      flags; 1 on some lamps, glows and doors
 u32      stack count, 4 (0 when the sprite has none)
 per stack:
   u32    entry count
   entry  (u32 act, i32 var1, i32 var2, i32 var3) * entry count
 u32      value count, 0 or 1
 u32      values * value count
 char[]   name, cp1251, NUL-terminated
```

The name is often a note of the level designer, such as "Установка динамита" (dynamite placement). Names in square brackets, such as `[gate]` or `[explosive]`, is the name a quest of `levels.cfg` refers to. Names from `h1` to `h20` mark the sprites that `EXT_KILLOBJECTS` removes. Pickups name the items they hold, such as `Obj_money2`.

Each update runs the last entry of the next of the four stacks, and waits that are not over go back on their stack. As in Alien Shooter, units a sprite builds inherit the entries under `ACT_STOP_STACK` (73).

| act | name | effect |
|---|---|---|
| 0 to 16 | | plays the `ANI_` state of that number on the sprite, or on the nearest sprite of the object `var1` |
| 35 | `ACT_BUILD_UNIT` | builds the object `var1` |
| 40 | `ACT_PAUSE` | waits `var1` plus a random part of `var2` milliseconds |
| 43 | `ACT_FLAGMAN_TRIGGER` | waits until the player is within `var3` of (`var1`, `var2`) |
| 77 | `ACT_WHILE_NOT_QUEST` | waits until quest `var1`\_`var2` is `QS_BEGUN` |
| 78 | `ACT_SET_QUEST` | starts quest `var1`\_`var2` |
| 79 | | waits until the script variable `Action<var1>` equals `var2` |
| 91 | `ACT_SET_GOAL_COOR` | sets the goal point of teleports and lifts |
| 98 | `ACT_SET_INVISIBLE` | hides the sprite when `var1` is nonzero, shows it otherwise |
| 115, 116 | `ACT_SET_DIALOG`, `ACT_SET_FLAGMAN_DIALOG` | plays dialog `var1` of `Text/dialogs.txt` on the sprite or on the player |
| 131 | `ACT_SCRIPT_VAR` | sets `Action<var1>` to `var2`, or adds `var2` when `var3` is 1 |
| 134 | `ACT_DESTROY_UNIT` | destroys the sprite itself when `var1` is -1, or the object `var1` at (`var2`, `var3`) |
| 136 | `ACT_SCRIPT_EVENT` | calls the script function `ScriptEvent<var1>(sprite, var2, var3)` |

The level scripts read the `Action<n>` variables each tick in `CompaignGameTact` (`common_compaign.lgc`) and reset them.

| variable | effect |
|---|---|
| `Action1` | plays music `n` |
| `Action2`, `Action3` | gives experience or money |
| `Action4` | gives the item with base id `n` |
| `Action5` | removes the sprites named `h<n>` with `EXT_KILLOBJECTS` |
| `Action6` | opens the shop |
| `Action8`, `Action9` | wins the level, after the win text or at once |
| `Action10` and up | read by single levels |

`ScriptEvent11` to `ScriptEvent13` (`util_teleport.lgc`) teleport the player, `ScriptEvent15` (`util_car.lgc`) hands the player a controlled unit, and `ScriptEvent16` (`common_compaign.lgc`) runs the data downloads of level 10.

In Alien Shooter 2 the player is object 250, and respawn posts (1359) set where it comes back. Sprites that their stacks hide before the first wait start the level hidden, 299 placements across the maps (alarm lamps, glows, helper arrows, exit zones and the like).

`PLAY` has one record of a `u32` sprite handle for each player, 4 in Alien Shooter and Zombie Shooter and 8 in the later games, their editions of Alien Shooter included. Every handle in every map is zero.

`GROU` has one record of `u32` sprite handles. Each group lists its sprites and ends with `0xFFFFFFFF`, and one more `0xFFFFFFFF` ends the record. Campaign level 7 groups 26 placed monsters, and the second add-on's levels 4 and 5 group 9 and 14 objects. Written levels keep the record a lone `0xFFFFFFFF`.

### Coordinates and direction

The world is isometric. Sprites stand at (x, y) and are lifted by z.

```
       +----> x          a basement floor tile covers one diamond
       |
       v y                    /\
                             /  \      40 px wide
                             \  /      28 px tall
                              \/
```

The basement lattice has walls at `x = 20a`, `y = 14b` with `a + b` even and floor tile centers at `x = 40i`, `y = 28j + 14` with `i + j` odd. The concrete levels use a finer lattice of 45 by 32. [NOTES.md](NOTES.md#level-construction-in-the-originals) has the measurements.

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

### Movement and collision

Solid objects raise height grids of 8 by 8 pixel cells over the map, and objects that follow the terrain (`P_ZeroZ`) walk on them, climbing and dropping no more than the steps in their record. Objects with `P_BuildVidZToGridZ` raise the cells with the depth of their sprite, a pixel of depth `d` in row `r` raising the cell of row `r + h` to `h = d / 8 - 128` above the object. Objects with `P_BuildSizeToGridZ` raise them along their footprint outline. Alien Shooter 2 keeps one grid for scenery (`SpriteClass` 8) and one for the rest. Its `GRID` chunk holds the first grid, one `i16` per cell and `map w / 8` cells per row, and only the menu, logo, slide and end maps have one.

Moving objects collide when their `MoveMask` values share a bit, their footprints overlap (half of `SizeXYZ[0]` and `SizeXYZ[1]` each way) and their heights overlap. Objects with `P_Noise` in Alien Shooter 2 use a box turned with the facing.

## Sprites (.vid)

Kind `VID `. Sprites hold their frames laid out by animation state and facing.

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
 +---------+-------+--------+-------+--------+--------+----------+-------+
 | VidType | speed | noCadr | width | height | format | meshType | blend |
 | u16     | u16   | u16    | u16   | u16    | u32    | i32      | u32   |
 +---------+-------+--------+-------+--------+--------+----------+-------+
 0         2       4        6       8        0x0a     0x0e       0x12
             ms per frame, VID_FRAME_SPEED
```

Most Alien Shooter 1 sprites have a ten-byte record, and the others 18 or 22 bytes. Alien Shooter 2 records are 10, 14, 22, 34 or 426 bytes. Pictures loaded from a `.bmp` file have no header and run at 71 ms per frame. Alien Shooter 1 reads only the first ten bytes. Alien Shooter 2 also reads `format`, `meshType` and, when present, `blend`, and loads no frames when `format` is zero.

`format` is a Direct3D 9 format code.

```
 0x14  R8G8B8, 24 bit            0x1a  A4R4G4B4, 16 bit
 0x15  A8R8G8B8, 32 bit          0x1c  A8, 8 bit
 0x16  X8R8G8B8, 32 bit          0x29  P8, 8-bit palette indices
 0x17  R5G6B5, 16 bit            0x32  L8, 8 bit
 0x18  X1R5G5B5, 16 bit          DXT1 4 bit, DXT3 and DXT5 8 bit (FourCC codes)
 0x19  A1R5G5B5, 16 bit
```

The files use 0x29 for the paletted sprites and 0x15, 0x17, 0x19 and 0x1a for the others.

`meshType` sets the geometry of each frame.

```
 0, -1    an upright quad whose depth grows up the frame, not turned by the facing;
          -1 is upright or flat by the depth range of the sprite
 1        a flat quad
 2        two quads of eight vertices; only Zombie Shooter 2's 3996.vid
 3        a triangle mesh from the vertex data of the CADR record
 4, 5     drawn like 1; 5 is on the omni-lights
 6        a light glow
```

The Alien Shooter 2 loader replaces 0 and -1 with 1, except in 101.vid. Every sprite with mesh frames has `meshType` 3.

`blend` is the `BlendProcedure`, the source and destination blend factors of the draw.

```
 0      alpha test; with the blended flag (and a format other than A1R5G5B5) SRCALPHA, INVSRCALPHA
 1      DESTCOLOR, SRCCOLOR
 2      ONE, ONE
 4      ONE, ONE; objects with the flag 0x1000 are scaled to the render target
 5      DESTCOLOR, ONE
 6      like 0, drawn without writing depth
 7      like 0, drawn with depth writes; no sprite of the game
```

Blends 4 and 7 and `meshType` 6 draw over everything already drawn. The files use blends 0, 1, 2, 4 and 6. The palettes of additive sprites hold colors already scaled by their alpha.

The rest of the 426-byte record holds the data of the sprite tool.

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

Only Vengeance ships `fast_load.vid` and `fast_load_data.vid`, where 847 of 979 sprites match. The Alien Shooter 2 keys never match.

`VidType` flags. The values quoted in this section combine them.

```
 0x01   frame records are runs of 8-bit values; sprites without it (0x36) use 16-bit runs
 0x02   blends with what lies under it, using the alpha of the palette
 0x04   ZBuffer: per-pixel depth values
 0x08   Palette: palette indices
 0x10   current format version
 0x20   VID_HARDWARE: has a SURF chunk with 32-bit surfaces; VID_HARDWARE_Z with both 0x02 and 0x04
 0x40   VID_SURFACE: has a CADR chunk with cached surface data; the standard format in Alien Shooter 2
 0x80   VID_LIGHT, an omni-light: a 2 x 2 sprite whose frames are BGRA colors, one u32 per frame in a single DATA record (only 0xB0)
 0x100  surfaces are range coded
 0x200  each CADR record starts with a 12-byte vector, three floats that place the object linked to the one showing the frame (52 Alien Shooter 2 sprites)
 0x400  one palette per army; no sprite of the game
 0x800  Shadow: each CADR record header has one more u16 after the index count, 14 bytes in all (no sprite of the game)
 0x1000 a sprite class of its own, always on HARDWARE_LAYER; no sprite of the game
 0x2000 Normal: a normal map under the image; the surface is twice as tall and each CADR pixel has one more byte; no sprite of the game
 0x4000 text font the engine builds at run time from a system font, with no frames of its own; no file sets it
 0x8000 Material: one more material byte per CADR pixel; with 0x2000 the two bytes make a 16-bit value under the image, alone the byte is skipped; no sprite of the game
```

Sprites with none of 0x20, 0x40 and 0x80 are VID_SOFTWARE sprites, drawn from their `DATA` runs. Sprites with the flag 0x20 and an empty `SURF` record have no pictures. The cursor, the bullets, the endless generators (800 to 814) and the invisible damage and spawner objects (900 to 923) use them.

Frame `n` of facing `f` in animation state `a` is `start(a) + f * count(a) + n`, where `count(a)` is the state's `NoFrame` and `start(a)` adds up the frame counts of the earlier states times the direction count. The engine moves to the next frame each time the game tick passes a multiple of the state's frame duration, and wraps to the first frame after the last.

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

The z values are present only with the flag 0x04. Records of two bytes alias an earlier frame.

### Texture sprites

```
 SURF record:  u16 count
               count * ( u16 width | u16 height | u32 raw size | surface data )

 DATA entry (36 bytes):  checksum | surface | x | y | w | h | left | top | next
```

Surfaces are ARGB4444 and their raw size is `width * height * 2`. The single `DATA` record holds one entry per frame, followed by the extra parts of frames drawn in several pieces. `surface` selects the surface, `x`, `y`, `w` and `h` the rectangle in it, and `left` and `top` place the piece on the sprite canvas. `next` is the index of the entry holding the frame's next piece, or zero for the last one. `checksum` hashes the piece's pixels, and a repeated piece is stored once, with surface -1 and the index of the first copy in `w` (no sprite of the game holds one). Frames wider than a surface, such as the menu backgrounds, continue in later entries.

With bit 0x100 the surface data is compressed by a carry-less range coder with an order-1 model of 257 symbols, where symbol 256 ends the data. Files with several surfaces share the coder models across them, and each compressed surface starts right after the end of the one before. The decoded bytes hold the low bytes of all pixels first and the high bytes second.

Other flag combinations in use:

```
 0x36   runs of u16 z + u16 ARGB4444 with alpha 0, drawn faded by depth (see below)
 0x11   (UI sprites) plain RGB565, no palette
 0xB0   omni-lights (flag 0x80); created by effects, never placed
```

Sprites with the flags 0x36 have no `SURF` chunk and keep their frames in `DATA` runs of depths followed by colors (the lamp beam 785, the shields 203, 220 and 221, the rescue flash 181). They fade by how far their depth rises above what is drawn under them.

### Cached sprites

Alien Shooter 2 keeps the frames of most sprites in a `CADR` chunk instead of `DATA`, one record per frame. Each record becomes a surface and, for `meshType` 3, a triangle mesh.

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

Each vertex is one u32 with three 10-bit fields and a 2-bit scale in the top bits. The first two fields are the column and row on the canvas, and the third is the height, `(v >> 20 & 0x3ff) / (1 << (v >> 30)) - 32`. Only the frames of `meshType` 3 sprites carry indices and vertices.

The runs describe the region as one raster, `width` pixels per row. Each run skips `skip` transparent pixels, then holds `count` pixels, and may continue onto the next row. The frame ends at the `00 00` pair.

Pixels are `format` sized (see `HEAD`), and palette indices when the sprite has a palette. After the color come one byte for each of the flags 0x2000 and 0x8000, then two bytes of depth with the flag 0x04. About fifty Alien Shooter 2 sprites, mostly shadows, glows and level props, use the 16-bit and 32-bit formats. Zero pixels are transparent.

The vertices give each pixel a height, interpolated over the triangles.

Records of four bytes alias an earlier frame by its u32 index, and all 240 in the games point backwards. Records of only the 12-byte header, every field zero, are empty frames.

Paths ending in `.bmp` or `.tga` instead of `.vid` load directly, with black as the color key.

## Menus (.men)

Kind `MENU`. Menus are lists of sprites with screen coordinates.

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

Buttons are object 705, and the button number the script sees is the direction frame (`ndir`) that `dir` picks among its 10 directions. Labels are object 3, their text a key of the `[menu]` section in `strings.ini`.

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

Event 14 runs once when the object is created or loaded with the map. Other events run at the first frame of their state, or every frame with flag 0x10.

Tables are integer arrays with an optional size, filled in braces and split across lines freely, with `//` and `/* */` comments anywhere:

```
static int LevelMoney[] = {100 ,100 , 350 ,1000, 2800, 5500, 9000,18000,29000,32000,   0,   0 };
static int WeaponPrice[11] = { 0,0,200,350,1000,2500,4800,8000,15800,25400,900 };
```

Engine functions are declared in `EXPORT.LGC` as `extern Name(args) <builtin>;`. The registry values `GameType` and `LevelNumber` choose the level flow. [NOTES.md](NOTES.md#scripts) describes the handlers that matter.

### Quests (levels.cfg)

Alien Shooter 2 keeps the quests of the campaign in `levels.cfg`, one `Key=Value` block per quest between `;****` lines. The engine writes the same keys when it saves the file. The map sprites start quests and wait for them with `ACT_SET_QUEST` and `ACT_WHILE_NOT_QUEST`, and the scripts read and set them with `EXT_GETQUESTSSTATE` and `EXT_SETQUESTSTATE`.

```
 Quest=1_1_3                         LevelNum, line 1, quest 3; line 1 is the main story
 LevelNum=1
 TimeLine=2                          the place of the quest in the story
 AddExperienceMAIN=100               also AddMoneyMAIN, AddItemMAIN, RemItem
 NextQuests=1004                     line * 1000 + quest
 ChangeStateItem=QS_BEGUN CSI_DEATH gate
 Comment=Найти ворота                designer note
```

The states are `QS_NOTBEGUN` (0), `QS_BEGUN` (1), `QS_COMPLETED` (2), `QS_FAILED` (4) and `QS_NOTACTUAL` (32). Starting a quest can set quests with an earlier `TimeLine` to `QS_NOTACTUAL`. `ChangeStateItem` takes a state, an event (`CSI_DEATH`, `CSI_TAKE`, `CSI_HAVE` or `CSI_BIRTH`) and a name. The name is the bracketed name of a map sprite (see [Maps](#maps)), an item name, an object id or `m0` to `m9`. When the event happens to that name, the quest goes to the state. `QS_BEGUN CSI_DEATH m3` starts the quest once the sprites named `m3` are destroyed. The names `m0` to `m9` only take `QS_BEGUN`. `LineQuests` takes a line and a quest as `line * 1000 + number`, like `NextQuests`, and links the quest to a quest of that line. The `TimeLine` of that quest is checked before the events apply. `SetFlagmanCoord` takes a state and four numbers, such as `QS_BEGUN 4177 6948 0 150`. `WinOnBegin`, when nonzero, wins the level as soon as the quest starts. Other keys are `BeginNext`, `UnactualNext`, `Accomplished`, `SetInfoQuest`, `SndBegun` and `SndComplete`.

The title the game shows is the section `<1_1_3>` of `Text/mission.txt`. The dialogs of `ACT_SET_DIALOG` are the sections `<n>` of `Text/dialogs.txt`, whose lines start with tags such as `<Font=10>`, `<Icon=391,3>` and `<Speech=Speech\001_2.ogg>`.

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

Both engines draw sprites against a depth buffer in which a larger value is nearer.

```
 screen position = ( x - cameraX - width / 2,
                     y - z - cameraY - height / 2 )

 depth of a pixel = 8 * z + its own z value          sprites with depth data (Alien Shooter)
                  = 8 * z + 0x400 + 8 * h            mesh frames, h interpolated over the triangles
                  = 8 * z + 0x400                    other sprites
```

Objects are drawn in 16 layers, from layer 0 up. The layer follows from the flags of the sprite and of the object. Alien Shooter 2 sorts the object classes into draw passes by the same rules. Within a layer the map's sprites come out in file order, and of two at one depth the later covers the earlier. Sprites with the flag 0x02 blend with what lies under them and do not write depth.

```
 layer  name                   objects                                         AS2 pass
 0      GROUND_LAYER           the prerendered ground
 1      BUILDED_LAYER          scenery (SpriteClass 8)                          0
 2      BUILDED_ALPHA_LAYER    blended scenery                                  2
 3      SOFTWARE_GROUND_LAYER  software sprites with P_Ground
 4      GROUND2_LAYER          hardware sprites with P_Ground
 5      SOFTWARE_LAYER_GRID    software sprites built into the height grid
 6      SOFTWARE_LAYER         other software sprites
 7      SOFTWARE_ALPHA_LAYER   blended software sprites
 8      HARDWARE_LAYER         other hardware sprites                           3
 9      ALPHA_GROUND_LAYER     blended hardware sprites with P_Wave             4
 10     REGION_LAYER           regions                                          5
 11     LIGHT_LAYER            omni-lights                                      7
 12     ALPHA_LAYER            other blended hardware sprites                   9
 13     SOFTWARE_TOP_LAYER     software sprites with P_AlwaysTop
 14                            cursor and fonts; light glows (meshType 6)       10
 15     MENU_LAYER             sprites without pictures, frames and text        11
```

### Camera

The camera is the map point at the top left of the screen. It starts at the camera of the map header and stays inside the map. `SetScrollType` sets how it follows the player. `SCROLL_TO_FLAGMAN_WITH_DIRECTION` (8) leads toward the mouse and `SCROLL_FLAGMAN_IN_CENTER` (16) keeps the player in the middle.

### Lights

The ambient gamma of `GRPH` lights the level, and objects with `P_Gamma` ignore it. `LightOff` and `LightOn` fade it to a target over about a second. Omni-lights are objects whose sprite has the flag 0x80. Each takes its color from its frame and lights an ellipse sized by the footprint and scale of its object. Alien Shooter adds the lights to what is already drawn. Alien Shooter 2 draws them into a light buffer that it multiplies over the scene, casts stencil shadows from objects with `P_Wind`, and picks its light method by the graphics detail level (`SetGraphDetail`). The Low setting also hides the objects listed in `LowDetailVids` of `CONST.LGC`.

## Key functions

Addresses from Ghidra decompilation.

Alien Shooter (GOG):

- `FUN_00411b50` - loads the object table from `objects.res`
- `FUN_00413c30` - reads one object record
- `FUN_00413710` - creates a sprite object with its default header
- `FUN_00433cd0` - reads the environment, ambient gamma and wind of the `GRPH` record
- `FUN_00431cf0` - `SetEnvironment`, sets or clears environment bits
- `FUN_0040c930` - `SetWind`, sets the wind speed and direction
- `FUN_0040bed0` - scrolls the camera each frame
- `FUN_0040bc20` - moves the camera and clamps it to the map
- `FUN_0040fc40` - finds a placed object by `SpriteType` mask, id, `SpriteClass`, army and state
- `FUN_0043f200` - creates a placed object, applying the spawn flags
- `FUN_00442ad0` - sets the army of an object and hides it when the flag 0x20000 asks for it
- `FUN_00419b80` - bakes a scenery object into the terrain tiles, skipping objects with the flag 0x20000
- `FUN_00447fe0` - creates a projectile, with its initial speed and vertical speed
- `FUN_0043fb10` - moves an object one step, applying gravity, homing and wind
- `FUN_00443810` - tests the collision mask of an object
- `FUN_00448130` - updates the motion of an object, with ground bounce and rocket height
- `FUN_00440110` - advances the animation of an object
- `FUN_00416860` - draws a software sprite into the z-buffer, applying the raised and bobbing flags
- `FUN_0043f820` - moves an object toward its rest height above the terrain
- `FUN_0044def0` - moves a linked object to its owner plus the offset in the record
- `FUN_00445770` - creates the linked object of a new object
- `FUN_00405bd0` - creates an object by `SpriteClass`, the pickup classes 0 and 1 through `FUN_0044bd60` and the others through `FUN_0040e870`
- `FUN_00445fb0` - gives a unit an attack order from its stack
- `FUN_0043f720` - lets a unit drop a deleted object from its goal and stack
- `FUN_00414d10` - applies gamma to the color of an omni-light and draws it through `FUN_00433670`
- `FUN_00433670` - fills an omni-light's texture from the height buffer and draws its quad
- `FUN_0041b0e0` - draws a sprite of runs with depth (flags 0x36) as a quad at the farthest depth
- `FUN_0042d6a0` - inserts an object into the hit and blast index
- `FUN_00440830` - handles the messages of an object, including blasts

Alien Shooter 2 (Reloaded, GOG):

- `FUN_0041baa0` - bits-per-pixel for a sprite format code
- `FUN_0042b1e0` - steps through the chunks of a `RES ` container
- `FUN_0042b320` - reads the length of the next record in a chunk
- `FUN_004550e0` - decodes the RLE pixels of a `CADR` frame
- `FUN_00455ac0` - reads the vertices of a `CADR` frame and finds their depth range
- `FUN_00456cd0` - reads a `.vid` file and builds its frames into surfaces
- `FUN_00457890` - places and draws a sprite frame, using `meshType` and `blend`
- `FUN_00458220` - sets the Direct3D blend and depth state of a sprite
- `FUN_00414b60` - sets the graphics detail level
- `FUN_0045a120` - puts the class of a sprite in its draw pass and samples the height points of a `CADR` sprite
- `FUN_00415840` - draws a frame of the level, with its passes and the light buffer it blends onto the scene
- `FUN_004493a0` - draws one pass, cell by cell of the level grid
- `FUN_00453c20` - draws an omni-light object and works out its color, gamma and radius
- `FUN_00416d10` - draws the quad of one omni-light, per shader level
- `FUN_004168c0` - draws the stencil shadows of an omni-light
- `FUN_0044c690` - walks the casters under a light quad
- `FUN_00459a90` - draws the shadow card of one caster
- `FUN_00425ed0` - the angle from an object to a point
- `FUN_0043cac0` - runs one action of an object, by code
- `FUN_004143d0` - sets the view matrix
- `FUN_00414580` - sets the viewport and the projection matrix
- `FUN_00450f50` - the direction frame of a facing
- `FUN_004439d0` - creates the linked object of a new object
- `FUN_0043f2f0` - sets the army of an object and hides it when the flag 0x20000 asks for it
- `FUN_004514f0` - reads one object record
- `FUN_004422d0` - spawns the child objects of an animation state
- `FUN_0043b680` - ends an animation step of an object, running its action stacks and applying the blast with its damage roll, radius and push
- `FUN_00448300` - finds the placed object of an id or mask nearest to a point
- `FUN_004686d0` - finds a quest by line and number
- `FUN_0046ec30` - sets the state of a quest
- `FUN_0046ee20` - starts a quest with its sound, info text, starting quests and win
- `FUN_0046f950` - applies a `ChangeStateItem` event (a sprite name, an object id or a named monster) to the quests
- `FUN_00470240` - reads one key of a quest of `levels.cfg`
- `FUN_00468cb0` - writes the quests to `levels.cfg`
- `FUN_00404170` - creates a placed object and rolls its initial vertical speed
- `FUN_00439a50` - rolls the initial speed of a placed object
- `FUN_0043f4e0` - sets the facing of an object
- `FUN_00440000` - tests the collision of an object
- `FUN_00403fb0` - tests whether the footprints of two objects overlap
- `FUN_00444f90` - tests the footprints of two objects when one is an oriented box
- `FUN_00403f60` - tests whether the heights of two objects overlap
- `FUN_00480640` - tests a step of an object against the height grids
- `FUN_00480050` - returns the ground height under the footprint of an object
- `FUN_0047fc80` - tests whether a point is below the height grids
- `FUN_0047fb90` - creates the two height grids of a map
- `FUN_00453060` - samples the height points of a sprite with depth
- `FUN_004523b0` - raises the height grids with the points of the current frame of an object
- `FUN_00480e30`, `FUN_00480ec0` - raise one cell of the first or the second height grid
- `FUN_0047c830` - loads a map, reading the `GRID` chunk into the first height grid
- `FUN_0047e320` - saves a map, writing the first height grid as the `GRID` chunk
- `FUN_0040ecf0` - sends the touch message

Alien Shooter Vengeance:

- `FUN_00425358` - creates the sprite class for a `.vid` from its flags
- `FUN_00469800` - loads a cached sprite, including the fast load table
- `FUN_0046a5d0` - builds the quad and sets the blend state of a frame
