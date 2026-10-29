# Level generation

This document describes how `LevelGenerator` builds a level, stage by stage, with the types and methods that do each step. It is meant as a map for finding where a level property comes from and which code to change when a level or a rule goes wrong.

The rules a level is checked against are in [RULES.md](RULES.md), the engine and level facts the generator follows are in [NOTES.md](NOTES.md), and the file layouts it writes are in [FORMATS.md](FORMATS.md). All code lives under `src/ASToolkit.Core/Generation/` unless a path says otherwise.

## Contents

1. [Overview](#overview)
2. [Reference data](#reference-data)
3. [Coordinates](#coordinates)
4. [Target and budget](#target-and-budget)
5. [Layout plan](#layout-plan)
6. [Fine layout and structure](#fine-layout-and-structure)
7. [Breaker spot](#breaker-spot)
8. [Dressing and clutter](#dressing-and-clutter)
9. [Content](#content)
10. [Script](#script)
11. [Checking and selection](#checking-and-selection)
12. [Roof](#roof)
13. [Add-on style and installation](#add-on-style-and-installation)
14. [Debugging](#debugging)

## Overview

A generated level replaces one campaign level from 2 to 10. It takes that level's theme (lab or military), its gate style and its measured targets, and builds a new level around a main route that meets them. Several candidates are built from different seeds, each is checked with the same route analysis and rules that measure the originals, and the best one is kept.

```
 ascli generate / ascli preview
        |
        v
 GeneratorFactory.Create          (src/ASToolkit.Cli/Commands)
   ObjectTable.Load               objects.res
   ReferenceLevelSet.Load         analyzes campaign 2-10 and add-on levels 2-5
   SideTable.Load                 side field per object id
        |
        v
 LevelGenerator.Generate
   level target from ReferenceLevelSet.GetTarget
        |
        v
 +-- candidate loop, batches of 6 in parallel, at most 40 ----------------------+
 |                                                                              |
 |  Build, one seed per candidate                                               |
 |    1  gate request and budget         ChooseGate, BudgetPlanner              |
 |    2  layout plan in units            LayoutPlanner.Plan                     |
 |    3  cells, walls, floors            FineLayout, StructureBuilder           |
 |    4  breaker spot                    ContentPlacer.ChooseBreakerSpot        |
 |    5  decor, character, clutter       Dresser, RoomDecorator, ClutterPlanner |
 |    6  player start, content, script   PlacementContext, ContentPlacer        |
 |                                                                              |
 |  Check                                                                       |
 |    map and script bytes -> RouteAnalyzer -> RuleSet.Default                  |
 |                                                                              |
 |  scored by hard rules broken, then soft rules met                            |
 +------------------------------------------------------------------------------+
        | best candidate
        v
 RoofBuilder.Build, its own seed
        |
        v
 GeneratedLevel.Write / RandomAddOnInstaller.Install
```

`GeneratedLevel` carries everything a stage produced, so a test or a debugger can look at the plan, the fine layout, the budget, the map builder, the route analysis and the rule results of the chosen level.

| member | stage | content |
|---|---|---|
| `Plan` | layout plan | regions, links, stations, gate |
| `Layout` | fine layout | cells, wall edges, lattice, fake doors |
| `Budget` | budget | monster groups, ambushes, items, weapons |
| `Builder`, `Map` | all | the placed objects and the map they make |
| `Script` | content | doors, blackout and checkpoint for the `.lgc` |
| `Analysis`, `Results` | check | route tour and rule results |
| `RoofResidual` | roof | target pixels the roof could not cover |

### Randomness

One `Random` runs through every stage of a candidate. The same seed and level always give the same files, which `LevelGeneratorIntegrationTests` checks. Any added or removed call to the random source changes everything after it, so a code change that touches one stage also changes the output of the later stages for the same seed. Compare rule results across seeds when judging a change, and don't expect a fixed seed to keep its layout.

## Reference data

`LevelGenerator` loads its sources once, on first use, and shares them across candidates and levels. Each one is read from the game folder.

| source | built by | from | read by |
|---|---|---|---|
| level targets | `ReferenceLevelSet.Load` | route analysis and measures of campaign 2 to 10 and add-on levels 2 to 5 | `GetTarget`, every stage |
| theme decor | `ReferenceLevelSet.GetThemeDecor` | decor ids and heights of the campaign (and the add-ons in add-on style) | dressing filter, rule A13 |
| wall clearance | `WallClearanceTable.Build` | distance of each decor object to the nearest wall in the originals | dressing, clutter, breaker props |
| facings | `FacingTable.Build` | the directions the originals give decor against each wall direction | dressing, clutter, boss props |
| content pool | `OriginalContentSet.Load` | placed groups, spawner lists, pickups, crate loot and bosses of each original | `BudgetPlanner` |
| dressing libraries | `DressingLibrary.Build` | decor of the originals of each theme, indexed by wall windows | `Dresser.Dress` |
| breaker scenes | `OriginalContent.LoadBreakerScenes` | props within 170 px of the breakers of levels 2 to 4 | `ChooseBreakerSpot` |
| dynamite sites | `OriginalContent.LoadDynamiteSites` | 179 markers with their pads, holes and endless generators, levels 3 and 4 (lab) and 7 to 9 (military), plus add-on levels in add-on style | `PlaceSites` |
| keypads | `KeypadTable.Load` | keypad offsets next to each door kind and direction | structure, `PlaceGate` |
| sprites | `SpriteLibrary` | `.vid` files of floors, walls and the earth tile | `RoofBuilder` |
| original map | `LevelGenerator.GetOriginal` | the replaced campaign map, its cell model and its music lines | theme, unit count, clutter palette, script |
| sides | `SideTable.Load` | the most common side value of each object id | `MapBuilder.Add` |

## Coordinates

Generation works on three grids, from coarse to fine.

```
 unit         LayoutPlanner          rooms and halls are rectangles of units
   |  x UnitCells (lab 1, military 2)
   v
 cell         FineLayout             floor cells and the edges between them
   |  Lattice.ToWorld
   v
 map pixel    MapBuilder             object positions written to the map
```

`ThemeSettings` gives the sizes per theme. A lab unit is one lab cell on the (40, 28) lattice. A military unit is 2 by 2 cells of the (45, 32) fine lattice. Every size range in `ThemeSettings` (hall width and length, room size, arch width) is in units, and `ThemeSettings.UnitPixels` is the walking length of one unit step.

A `Cell` (U, V) spans [U, U+1) by [V, V+1). U runs along the `/` direction and V along the `\` direction. An `EdgeKey` names the border between two cells.

```
                       vertex (U+1, V)
                        /          \
  'v' edge (U, V)      /            \     'u' edge (U+1, V)
  a '/' wall          /              \    a '\' wall
                     /                \
   vertex (U, V)    <    cell (U, V)   >   vertex (U+1, V+1)
                     \                /
  'u' edge (U, V)     \              /    'v' edge (U, V+1)
  a '\' wall           \            /     a '/' wall
                        \          /
                       vertex (U, V+1)

   +U goes up and right on screen, +V goes down and right.
```

The two upper edges of a cell are its far walls, the ones that face the camera. Fake doors, the breaker and lab dynamite holes go on far walls. `Lattice` converts between lattice coordinates and map pixels, and `EdgeKey.GetNeighbors` returns the four neighbors of a cell with the edge crossed to reach each.

### Edge kinds

Three stages describe edges with different letters. `FineLayout` holds the generated walls, `RouteGrid` holds what the route analysis reads back from the map, and `DressingLibrary` reduces both kinds of model to three classes to match windows.

| letter | `FineLayout.Edges` (generation) | `RouteGrid.Edges` (rules) | `DressingLibrary.GetWallKey` |
|---|---|---|---|
| W | wall | wall | 1 |
| D | single door | door | 2 |
| O | open doorway frame (an arch) | | 0 |
| E, e | the two halves of a double door | | 2 |
| S | breakable wall | secret or spawn wall, monster hole | 1 |
| T | spawn wall | | 1 |
| F | | closed or jammed door | |
| O | | open doorway | |
| B | | broken wall | |
| G | | military fence frame | |
| none | open | open | 0 |

`RouteGrid.IsBlocking` treats only W and F as blocking. Generation uses it for its own distances, paths and connectivity checks, so every door, breakable wall and spawn wall counts as open while content is placed. The rules see the level as the player meets it, with doors locked until the breaker and S edges used only as a last resort (see [NOTES.md](NOTES.md#route-model)).

## Target and budget

### Level target

`ReferenceLevelSet.GetTarget` returns a `LevelTarget` measured from the campaign level being replaced. Most values are the original's own (style, blocked share, breakables, gate shares, dynamite sites, secret closets, checkpoint, boss, cannon, generators, box fill). A few are blended with the add-ons, through `Blend`, which interpolates the four fighting levels of each add-on over campaign levels 2 to 10 and mixes them with the campaign value at the weight `AddOnWeight` (2/3).

| target | source |
|---|---|
| `HitPoints` | the original's monster hit points times `HitPointFactor` (1.15) |
| `ReferenceTourPixels`, `SpineShare`, `Elongation` | blended with the add-ons |
| `OriginalSpineShare`, `OriginalElongation` | the original's own |
| `RevisitCeiling` | the original's revisit share plus 0.03, clamped to 0.06 to 0.16 |
| `ContentLevel` | the level whose pickups and weapons the generated level copies, the original by default |

### Gate request

`LevelGenerator.ChooseGate` turns the target's style into a `GateRequest`.

| style | gate | share behind the gate | notes |
|---|---|---|---|
| `LockedDoors` | locked doors | the original's locked share, or 0.5 | |
| `Blackout` | blackout | 0 | `SearchPixels` 5,200, the walk from the trigger to the breaker |
| `Fenced` | fences | the original's fenced-off share, or 0.4 | |
| `Ungated`, lab | locked doors or blackout, at random | 0.2 to 0.3 | `IsWing`, a gated branch off the route |
| `Ungated`, military | fences | 0.2 to 0.3 | `IsWing` |

A wing gate closes a side branch and leaves the main route open, which is what rule A12 asks of ungated originals.

### Budget

`BudgetPlanner.Plan` returns the `LevelBudget`, the monsters, pickups and counts the content stage places.

1. **Hit points.** The aim is the target's hit points times 0.9 to 1.1.
2. **Source levels.** Original levels (the campaign, plus the add-ons in add-on style) whose hit points lie within a third and three times the aim. When fewer than two qualify, the three closest on a log scale.
3. **Pools.** Spawner lists from the sources worth at most 22% of the aim, and placed groups worth at most 12%. When none is small enough, the whole pool is used. Both pools are shuffled.
4. **Drawing.** Until the aim is reached, each draw is an ambush with the chance of the replaced level's spawned share (clamped to 0.05 to 0.95, rule A5), otherwise a placed group. Pools are taken in order and wrap around.
5. **Cleaning.** Divers become red tank-weapon monsters, the diver boss and the bosses in `DroppedFromGroups` are dropped from groups and ambushes, and bats are capped at 2 × (level − 1).
6. **Boss ambushes.** Spawner lists of the replaced level that contain a campaign boss are added as they are.
7. **Items.** The content level's floor pickups and crate loot, each count varied by up to 10%, with each item passed through `ItemSchedule.Replace` so nothing appears before the level the campaign introduces it. Weapons and dynamite are left out here. Every weapon allowed on the level gets at least 2 of its ammunition, and dynamite equals the target's site count.
8. **Weapons.** The content level's weapons, loose and in crates, kept in a separate list for `PlaceWeapons` and the closets.
9. **Counts.** Bosses, mounted cannons and fence generators are the replaced level's.

`ItemSchedule` holds the first level of every pickup, the stash shares of secret closets and the weapon to ammunition mapping (ammo id is weapon id + 40).

## Layout plan

`LayoutPlanner.Plan` builds a `LayoutPlan` on the unit grid. A plan is a list of `PlanRegion` rectangles joined by `PlanLink`s. Regions have a `RegionKind`, links a `LinkKind`.

The room sizes and connections come from a `LayoutStyleProfile`, chosen by `--layout room|open` (default `room`) and passed by `LevelGenerator`.

| style | rooms | connections |
|---|---|---|
| `Room` | the theme's room size | the theme's weights, given in each step below |
| `Open` | 2.4 times the theme's room size, and half of the rooms 28 to 44 units | labs door 0.08, arch 0.40, open 0.52; military arch 0.45, open 0.55 |

`RoomCap`, the square root of the station area over 5 and at least 6 units, caps every room.

```
              side room           side room
                 |                    |
  [start]==[hall]==[room]==[hall]==[room]==[hall]==[final]     stations, the main route
                     |        \                |
               branch room   closet       key room (breaker or generator)
                     |
               branch hall
                     |
               branch room (tip)

  ==  chain link, door, arch or open
  |   link to a side room or branch
```

`LevelGenerator.Build` calls the planner with these inputs and retries with a new unit count when it returns null. After 200 failed attempts it throws "No layout for level N", which ends the whole `generate` or `preview` run.

| input | value |
|---|---|
| `targetUnits` | the original's floor in units (lab cells, or military cells / 4) times 0.9 to 1.05 |
| `fill` | the original's box fill times 0.9 |
| `elongation` | the original's for a blackout gate on the route, otherwise the mean of the original's and the blended one |
| `maxElongation` | lab, the larger of the two; military, no limit |
| `secretClosets` | the original's count on labs, 0 on military |
| `spawnClosets` | labs, the number of ambushes / 3, at most 6; military, 0 |
| `shortcuts` | labs, 0 to 2; military, 0 |

The planner runs these steps in order. Each returns null when it boxes itself in, and `Plan` then returns null.

### 1. Stations

`GrowStations` lays the main route as a chain of stations.

- The first room is a random size from the low half of the room range. A bounding box of `targetUnits / fill` units with an aspect of 1 to 1.6, turned at random, is placed around it. Every later region must stay inside it (`LayoutPlan.Bounds`, rule A15).
- Each next station is a hall after a room, and after a hall a hall with a 30% chance, otherwise a room. It goes on in the current heading, turning left or right with a 35% chance, and tries the other headings when blocked.
- `FindStationRect` slides the rectangle along the previous station until it shares at least `MinShared` (3) unit edges and lies on free units.
- Chain links are drawn from the style profile's connection weights. An arch drawn on a boundary too narrow for two units becomes a door on labs and an opening on military levels.
- Growth stops once stations hold 58% of the target, once the chain walk reaches the spine length (elongation × √targetUnits, times 0.65 for labs and 0.9 for military) or after 400 tries. Fewer than 5 stations fails. On a locked-door level the chain runs at least the original's walk to the breaker over 0.55.

### 2. Wing

For a wing gate, `GrowWing` grows a chain of alternating halls and rooms off a station about 45% along the route. Its first link is a door. It is kept when it reaches 60% of its share and rolled back otherwise, and the next of four hosts is tried.

### 3. Walk cap

The walk from the start to any unit may not exceed `maxElongation × √targetUnits / 0.8` unit steps. For a blackout on the route the cap is at least the trigger reach (1,700 px) plus the search walk, so the breaker room fits. `MeasureWalk` measures walks over unit links (secret and spawn walls excluded), and `TryExtendWalk` removes a just-added region again when any of its units lies past the cap. This is what keeps rule A4 (little walking back) and A16 (spine share) in range.

### 4. Branches

On a locked-door level, `FindGateStation` first picks the station whose exit lies at 0.8 of the original's walk to the breaker along the chain. Branches, and side rooms for their first 4,000 tries, grow only from the stations after it.

`GrowBranches` spends 60% of the remaining area on one to three dead-end branches off random stations. Each branch grows from its last region, or with a 35% chance forks from an earlier one. Branch links are drawn from the style profile's connection weights.

### 5. Side rooms

`AddSideRooms` attaches rooms until the plan reaches the target area or 6,000 tries. Hosts are drawn with stations three times as likely as branch regions or side rooms of stations. `TryAttachRoom` places a room flush against a random side of the host with at least 3 shared edges. Side-room links are drawn from the style profile's connection weights.

### 6. Gate

`PlaceGate` cuts the main route at a chain link for a gate on the route. `PlaceWingGate` uses the wing's first link.

| gate | cut | key room |
|---|---|---|
| locked doors | among the links that leave a share within 0.05 of the gate share behind them, the one whose chain walk is closest to the original's walk to the breaker, or else the link whose share is closest. The plan fails when the chain walk to the cut is under half the original's walk to the breaker | a 4 to 6 unit side room off the last open station or one of the two before it, at least half the original's walk to the breaker from the start |
| blackout | the trigger is the last chain link within 1,700 px of the start; the cut is the link whose walk from the trigger is closest to `SearchPixels` | a 4 to 6 unit room off one of the six open regions whose farthest point lies closest to the search walk from the trigger |
| fences | the largest cut whose behind share is within 0.08 of the gate share | `PlaceGeneratorRoom`, a room (lab 5 to 7, military 3 to 5 units) off a station at least 900 px of chain walk before the fence, preferring the station halfway to the cut |
| wing | the wing's first link, and for a blackout the trigger is the last chain link before the host within 1,700 px of the start | a 4 to 6 unit room off the wing's host station |

Every region past the cut, or in the wing, is marked `IsGated`. For fences the gate link becomes a one-edge `Fence`. For locked doors and blackouts every door link outside the gated part becomes an arch, so the doors that lock are the gate and those behind it, and the gate link becomes a door. A blackout also records `BlackoutLink`, the link the trigger sits in.

### 7. Closets, shortcuts and loops

- `AddClosets` adds secret closets (2 by 2 to 3 by 3 units, off stations and side rooms) and spawn closets (off the first half of the stations after the start). Each is linked by one edge of `SecretWall` or `SpawnWall`, and only the host may touch it.
- `AddShortcuts` adds breakable walls between two open rooms that share at least 3 edges and lie at least 3 links apart, on the same side of the blackout trigger. Rule D4.4 allows at most 2.
- `AddLoops` links touching lab rooms that no link joins yet, one loop per 8 rooms and at most 6. Both rooms lie on the same side of the gate and of the blackout trigger, and neither is the key room or the last station. The link is an arch in the open part and is drawn from the side-room weights behind the gate. A link that would shorten the walk from the start to the key room is dropped. The originals have 1 to 6 loops on campaign labs.

### 8. Clean-up

- `FillPockets` gives enclosed holes of up to 2 units to the neighbor that touches them most, since a walled-in hole cannot take a roof tile.
- `AlignLinks` moves each doorway along its boundary to the nearest span where no other wall meets it, narrowing it when no span is wide enough. It keeps one edge of wall at both ends of the boundary when a span allows it, as every door of the campaign labs has.
- `SpaceDoors` moves a lab door or arch that stands closer than 160 px to another one to the clear span of its boundary that lies farthest from the others. The closest doorways of the campaign labs are 119 px apart.

## Fine layout and structure

### Fine layout

`FineLayout.Build` turns the plan into cells. Each unit becomes `UnitCells`² cells holding its region id. Each link's unit edges split into cell edges, sorted, and stored in `LinkEdges` by link index. The edge kinds come from the link.

| link | cell edges |
|---|---|
| `Door`, two edges | `E` on the second, `e` on the first (a double door) |
| `Door`, one edge | `D` |
| `SecretWall` | `S` |
| `SpawnWall` | `T` |
| `Arch` | `O`, an open doorway frame |
| `Open`, `Fence` | open |

Every other border between two regions, or between a region and empty space, becomes a `W` edge. Edges inside a region do not exist.

### Placement on the map

- `StructureBuilder.PlaceLattice` picks the lattice origin so every cell lies at least 500 px into the map. On labs the origin snaps to the (40, 28) grid with the parity the floor tiles use (see [NOTES.md](NOTES.md#lab-lattice)).
- `StructureBuilder.AddFakeDoors` turns 2 to 5 far walls of hall stations into closed doors that lead nowhere, where no other wall meets them and at least 160 px from any door, arch or other fake door. On labs half of those with a matching neighbor become double. On military levels only even edges qualify.

### Visible edges

`LevelGenerator.Build` builds only the edges with a non-closet cell on one side. Closet interiors stay bare and the roof hides them.

### Lab structure

`StructureBuilder.BuildLab` places objects for every visible edge and returns the map index of each spawn wall, which the content stage programs later.

| edge | objects |
|---|---|
| floor cell | 500 floor, random direction |
| `E` | 608 frame and 607 door |
| `D` | 606 frame and 605 door |
| `S` | 609 breakable wall |
| `T` | 618 (`/`) or 619 (`\`, dir 64) spawn wall |
| `O` | per run of edges, 608 frames with a raised 611 at z −70 on each pair and a 606 frame with a raised 617 at z −63 on a single edge left over |
| `W` with a fake door | 608 or 606 frame and 611 or 617 jammed door, at z 0 or, half of the time, stuck at z −13 to −35 |
| `W` | 601 (`/`) or 602 (`\`) wall, direction from `LabWallDirs` |
| corner | 604 beam where walls meet and do not run straight through (`FindPosts`) |

`ContentPlacer.PlaceBrokenKeypads` then puts a broken keypad (425) next to jammed doors, as often as the originals do, using `KeypadTable`.

### Military structure

`StructureBuilder.BuildMilitary` places 651 and 650 walls on every second fine edge, 654 where a fake door is, 652 corner posts on even vertices and 503 floor tiles on their own (80, 56) lattice wherever a tile reaches a cell. `StructureBuilder.BuildFence` puts a 653 emitter and a 164 fence 14 px above it on each unit edge of the gate link.

`MapBuilder` collects the objects. `Add` drops a repeated object at the same rounded position unless `unique` is false, hands out handles, takes the side from `SideTable` and fills the default payloads, 20 bytes with two items for the player start, 12 bytes for monsters and bosses and 8 zero bytes for everything else.

## Breaker spot

Locked-door and blackout levels need a breaker. `ContentPlacer.ChooseBreakerSpot` looks for it in the key room before any decor is placed.

1. Candidate walls are the room's `\` far walls (`u` edges with room floor below them and none above).
2. The wall's straight run must end at a corner or run on for at least 3 edges (`WallRun.IsEnclosed`), and runs of 4 or more are preferred.
3. At least 3 open cells must lie in front of the wall and 2 to its side (`FloorScanner.CountOpenCells`). When no wall qualifies, one less of each.
4. The breaker hangs at the wall piece's position plus (−8, +3). For every wall and every original breaker scene, each prop is kept when its footprint lies in the room, a solid one stays out of the doorways and the cell in front of the breaker, and it passes the wall clearance and facing tables.
5. The fit with the most transformers wins, then the one with the most props, then a random pick.

When no wall qualifies, `Build` starts the whole candidate over with the same random source, up to 20 times. After that the candidate goes on without a breaker. `PlaceGate` then places no breaker or keypads and the doors stay open, so `LevelMeasurer` reads the level as ungated and rule B1 or B2 fails. A locked-door or blackout candidate reported as ungated comes from this path.

The props replace whatever decor the dressing put in the key room. Rule D1 checks the result.

## Dressing and clutter

### Window copying

`Dresser.Dress` fills the floor (closets excluded) with decor copied from the originals.

```
 generated level                         original of the same theme
 +---+---+---+                           +---+---+---+
 |   |   |   |  wall key of the window   |   | # |   |
 +---+---+---+  "0111...2..0"  ------>   +---+---+---+   any window with the
 |   |   |   |  (0 open, 1 wall,         |   |   |   |   same key, picked at
 +---+---+---+   2 door, per edge)       +---+---+---+   random
 |   |   |   |                           | # |   |   |
 +---+---+---+  <---- objects copied ----+---+---+---+
```

- `DressingLibrary.Build` indexes every window of the originals of the theme by `GetWallKey`, a string of the edges around and inside it. Window sizes are 3, 2 and 1 cells on labs and 4 and 2 on military levels. Sources with fewer than 100 edges are left out, and so are decor objects the theme filter rejects (anything the reference levels do not use at that height, and broken keypads).
- `Dress` walks the cells in (U, V) order and covers each uncovered one with the largest window that lies inside the floor, touches no covered cell and has a match.
- `DressingLibrary.TryCopy` copies the objects of the matched window. An object is dropped when it would stand closer to a wall than 95% of the originals keep it and than it stood in its source (rule A14), or when a floor object would face the walls in a way no original does (rule A17). Crates lose their loot and get the 12 zero bytes of an empty crate, as do the crates `RoomDecorator` adds.
- `Dresser.KeepSupported` drops stacked objects whose support was not copied, such as a monitor without its desk.

### Character

`RoomDecorator` adds to the copied decor. It draws from its own random stream, seeded from the layout, and places only decor the theme's height table allows.

Before the first `ClearAndConnect`, `RoomDecorator.Decorate`:

- Thins the dried blood to 5% of the floor cells and the corpses to 0.2%.
- Gives each room of at least 20 cells, except the start, the last station and the key room, a cluster of 1 to 8 pieces of one decor family near a random cell. The family is the one whose pieces the room already holds most of (office, lab or storage on labs; storage, armory or office on military), or the next one in turn.
- Puts 2 or 3 dried blood pools in the last two stations and a corpse in the last one.

Before the second `ClearAndConnect`, `RoomDecorator.PlaceComputerTables` puts a table under every computer that has none next to it (rule A18). The clearing pass removes a table that blocks a keep-clear cell or the way, and `RemoveComputersOffTables` then removes the computers left without a table next to them.

### Keeping the way clear

`LevelGenerator.GetKeepClear` marks the cells that must stay empty of solid objects. They are the cells around every doorway, the cell in front of each fake door and a 3 by 3 (lab) or 5 by 5 (military) square around the start.

`Dresser.ClearAndConnect` runs twice, after dressing and after clutter.

1. Solid objects whose footprint (`Dresser.GetFootprint`, the collision box grown by the player radius as in the route model) touches a keep-clear cell are removed.
2. A flood from the start finds the first floor cell that is not blocked and not reached. A path to it that ignores objects is found, and every solid object on the path is removed. A cell that no path reaches is dropped from the floor.
3. Step 2 repeats until every unblocked cell is reached, which keeps rule A2.

### Clutter

`ClutterPlanner.AddClutter` adds rows of solid and breakable objects along walls until the level matches the original's clutter (rule A1).

- The palette comes from the replaced original (`GetPalette`), solid and breakable floor decor weighted by how often the original uses each.
- The start station, the key room and, on boss levels, the last station get no clutter.
- A row is 2 to 5 objects along a wall from a random wall cell, as far from the wall as the clearance table asks (at least half a cell). Wide objects take every second cell.
- Solid rows go in until the blocked share of the half-cell grid (measured with `SolidObjectMapper`, as the rules do) reaches the target times 0.95 to 1.15. A row is rejected when it touches a keep-clear or blocked cell or would cut any floor off from the start. The loop gives up after 5,000 tries or 1,500 rejections in a row.
- Breakable rows then go in until the level has the original's number of breakables.

The second `ClearAndConnect` pass returns the blocked cells, and the kept objects are added to the map.

## Content

### Placement context

`PlacementContext` holds the state the content stage shares.

| member | meaning |
|---|---|
| `Taken` | cells already holding something, seeded with the blocked and keep-clear cells |
| `Distance` | walking distance in cells from the start, every door open, blocked cells excluded |
| `GetProgress` | `Distance` over the longest distance, from 0 at the start to 1 at the farthest cell |
| `IsInWall` | whether a point lies inside a wall piece's collision box plus a margin |
| `GetFreeCells` | region cells that are not taken, have no wall on their edges and are reachable |
| `Place` | places an object on a free spot of one of up to 25 random cells |

`GetProgress` is distance from the start, which is not the same as the tour progress `p` the rules use. On branched or gated levels a cell near the start can come late in the tour, and the reverse. When an object lands where the code intended and a timing rule (A6, A10, C2, D3, B4.2) still fails, this difference is a likely cause.

The player start (9, facing 96) goes on the start room cell nearest its middle that is not blocked, has no wall on its edges and lies at least 20 px from any wall box.

### Order

`ContentPlacer.Place` runs these steps in order. Each step takes cells from `Taken`, and the stashes take items and weapons out of the budget, so a step that finds no room is often starved by an earlier one.

```
 PlaceGate -> PlaceBosses -> PlaceMountedCannons -> PlaceSites -> PlaceCharges
   -> PlacePlacedGroups -> PlaceAmbushes -> PlaceStashes -> PlaceItems -> PlaceCheckpoint
```

### Gate

- **Breaker.** The 418 goes on the chosen spot. Every door gets a keypad from `KeypadTable`, 424 (off) on locked-door levels and 423 (on) on blackout levels. `LevelScript.DoorsOpen` is false on locked-door levels. A blackout's trigger point is the middle cell of `BlackoutLink` on its far side.
- **Generators.** On fence levels the original's number of 415 generators (at least one) go in the back third of the key room, farthest from its entrance, each with a 912 marker. Up to two transformers join them. Level 10's script treats a generator near (7541, 4501) in a special way, so that spot is avoided.

### Bosses

`PlaceBosses` puts each boss in the fifth of the last station's inner cells farthest from its entrance (rule D3), with corpses and blood around it.

### Mounted cannon

`PlaceMountedCannons` looks through the stations at progress 0.2 to 0.65, largest first, for a cell whose 3 by 3 ring of points 60 px and 45 px apart is free floor clear of walls (rule D5). The hall it lands in later gets two ambushes.

### Dynamite sites

`PlaceSites` places the target's number of 179 sites.

1. Templates are filtered by `SiteRankCap`, the highest endless generator a level may use.
2. Without a key room the stations are ordered by progress. With one, the rooms past progress 0.3 are ordered by `MeasureWalkOrder`, a nearest-room walk that starts at the key room, so sites land in the rooms a player reaches last after the gate opens.
3. Regions at or past progress 0.6 (0.65 for more than 4 sites) are candidates, and `n` of them are picked evenly spread (rule C2).
4. On labs, `PlaceLabSite` tries the nearest regions in order for a far wall with void behind it and wall on both sides. The wall piece becomes a 616 or 612 hole, the 179 goes at an offset of (±25, +20) from it, and the template's endless generators (or an 800 when it has none) hide at about (∓30, −42), behind it under the roof (see [NOTES.md](NOTES.md#dynamite)).
5. Otherwise `StampSite` copies a template around a free inner cell clear of walls, its pads and generators at their original offsets (shrunk to 0.3 on labs), pulled in when they would land in a wall.

Sites stay clear of solid decor through `IsCrowded`, which uses the same gap as rule C7. Rule C6 checks the shape.

### Charges

`PlaceCharges` groups the sites, in order of distance, into runs of up to three (four sites make two runs of two) and puts one 301 charge per site in a small cluster for each run (rule C3).

- The cluster lies between the previous run's last site and this run's first site, ideally 1,200 px of walking before the site, 400 to 2,800 px in a straight line from the nearest site (rule C4). Stations are preferred, then any room, then a 300 px minimum.
- With a key room, charges stay off the way to it, more than 600 px of detour from the shortest walk.

### Placed groups

`PlacePlacedGroups` sorts the budget's groups by hit points.

- When the level has no boss the strongest group goes in the last station.
- The tip of each branch takes the group whose rank best matches the tip's progress.
- Every other group goes in a room whose progress is within 0.2 of the group's rank, from 0 for the weakest to 1 for the strongest (rule A10).
- `TryPlaceGroup` anchors the group's original shape at a free cell at least 900 px from the start and keeps it when 80% of the members land on reachable floor outside closets and walls and at least 12 px from any monster already placed. After 40 failed anchors the members are scattered one by one, each on a free cell of its own.

### Ambushes

`PlaceAmbushes` sorts the ambushes by hit points and hands them out, weakest first.

1. **Spawn closets.** `PlaceClosetAmbush` puts the spawner inside the closet, the trigger two cells beyond the floor cell in front of its wall, or on that cell when the farther one is not floor (radius 180, 220 or 260), and a waypoint 500 to 800 px on along the route. The spawn wall gets a payload that blows it open on the same trigger. With a 30% chance a closet also holds two stash items.
2. **Blackout trap.** On blackout levels one ambush triggers at the key room's doorway (rule D1.3).
3. **Military sites.** One ambush per site, triggered at the site room's entrance, its spawner within 260 px of the site.
4. **Cannon hall.** Two ambushes from the middle of the list trigger at the cannon hall's entrance.
5. **The rest.** Triggers go to station doorways at target progress 0.04 to 0.46, spread evenly (rule A6). `PlaceAmbush` puts the spawner on a cell at least 5% of the route further on, 1,000 to 3,200 px from the trigger and off the screen around it (rule A7), falling back to 900 to 3,500 px and then to the farthest cells. The monsters walk back along the shortest path and stop 40% of the way to the trigger. Trigger radius is 250 to 350 px on labs and 300 to 450 px on military levels.

`SpawnerProgram.Create` writes the 908 payload, the trigger, the monsters and up to six waypoints (see [NOTES.md](NOTES.md#spawners-908)).

### Stashes and items

- `PlaceStashes` fills each secret closet with `ItemSchedule.CreateStash`, a money pile, an ammunition cache, a weapon with some ammunition or a perk with some money. Stash items count against the budget (rule D4), and `TakeWeapon` gives closets the weapons that are early for the level first.
- `PlaceItems` shuffles the remaining items. `PlaceWeapons` first replaces weapons the level may not have yet, puts about 35% of the weapons in empty crates and the rest in side rooms at progress 0.25 to 0.75, keeping weapons 600 px apart where it can (rule D7). Crates then take 32% of the ammunition, 45% of the armor and 20% of the health (rule A8). The rest of the loot, dynamite excepted, goes in piles by category (money 8, ammo 3, bonus 2, and armor, health and implants one by one) in the side rooms whose progress lies closest to the category's (0.48 to 0.60), deeper side rooms first.

### Checkpoint

`PlaceCheckpoint` runs when the original has a checkpoint, except on blackout levels, where the blackout trigger already moves the respawn point. It picks the station closest to progress 0.3 and a cell in it with nothing around and at least 24 px from walls (rule A9).

## Script

`LevelScriptWriter.Format` writes the `.lgc` from the `LevelScript`.

- The music definitions are copied from the original's script.
- `main()` includes the common scripts, calls `InitGame` and sets `DoorsIsOpen`.
- A blackout adds a trigger within 120 px of its point that moves the respawn point there, locks the doors, plays the quake and turns the lights off, and swaps every 423 keypad for a 424.
- A checkpoint adds a trigger within 120 px that moves the respawn point.

## Checking and selection

`LevelGenerator.Check` reads the candidate as the game would. It writes the map with the original as template, parses it back with `MapResourceBuilder.Parse`, formats and parses the script with `LevelScriptReader`, and runs `RouteAnalyzer.Analyze`. A level with no route fails A2 and nothing else is checked. Otherwise `RuleSet.Default.Evaluate` runs every rule on a `RuleContext`, which adds the cell model and `LevelMeasurer`'s measures.

Selection in `LevelGenerator.Generate` works on (hard rules broken, soft rules met).

1. The first 6 candidates are built in parallel. The one with the fewest broken hard rules wins, then the one with the most soft rules met.
2. When the best still breaks a hard rule, more batches of 6 are built and better candidates replace it. The search stops as soon as the best breaks no hard rule.
3. After 40 candidates the best so far is kept even when it breaks hard rules. `generate` installs it anyway, lists the broken rule ids and exits with code 1.

Each candidate's seed is `seed × 100000 + level × 100 + k`, where `k` is the candidate's index.

## Roof

`RoofBuilder.Build` runs once, on the chosen level, with its own seed. The rules never see the earth tiles. It hides everything outside the walkable floor under 501 earth tiles.

1. **Ground earth.** Earth tiles at z 0 fill the tile lattice positions whose tile does not touch any floor pixel.
2. **Raster.** Floors and walls are drawn into a depth buffer in 128 px strips, in parallel, as the engine sorts them.
3. **Classes.** Each pixel becomes a target, a forbidden pixel or neither.

| pixel | class |
|---|---|
| floor of a shown cell, or up to 96 px above it and 8 px below it | forbidden |
| face of a wall seen from shown floor | forbidden |
| floor of a closet or of a cell that is not shown | target |
| wall seen only from outside | target |
| empty pixel near floor that the ground earth does not cover | target |
| wall cap above the roof height | neither, and it ends the 96 px zone |

4. **Cover.** A greedy cover places roof tiles at z 90 (lab) or 95 (military), shifted down by their height so they appear on the same screen spot. Candidates lie on a 2 px grid, never overlap a forbidden pixel and are taken best first by how many target pixels they cover, with stale scores re-queued.

`RoofResidual` is the number of target pixels left uncovered. `generate` and `preview` print it as "roof misses". A large value means the walls leave gaps a tile cannot fill without covering visible floor.

## Add-on style and installation

`GenerateAddOn` builds add-on level 1 to 5 from campaign levels 2, 3, 5, 8 and 10 (`AddOnSlots`), so the five levels run from locked labs to fenced military bases. Its hit point target is the geometric mean of the same level in both add-ons, and its pickups come from one of them, chosen by the seed's parity. The generator must be created in add-on style, which also adds the add-on levels to the content pool, the dressing libraries, the theme decor and the dynamite sites.

`RandomAddOnInstaller.Install` writes the levels to `Maps/RANDOM`, their briefings, money table, script branches and play menu button. The [README](../README.md#the-random-add-on) describes the result.

## Debugging

### Reproducing a level

- `ascli preview --game <folder> --seed <s> --level <n> --out <file.map>` builds one level with the same selection as `generate` and prints every rule it fails, with the measured value against the target.
- `ascli analyze --levels <folder>` draws the tour over the generated maps, with objectives, gates and spawner triggers, and writes `routes.json`.
- `ascli check --levels <folder>` runs the rule set on a folder of maps and scripts.
- `LevelGeneratorIntegrationTests` generates every campaign level for one seed and fails on any broken hard rule.

### Where each rule is met

The table lists the stage and code that a level relies on to pass each rule. Thresholds are in [RULES.md](RULES.md).

| rule | topic | stage | code |
|---|---|---|---|
| A1 | clutter | dressing | `ClutterPlanner.AddClutter`, `ClutterPlanner.GetPalette` |
| A2 | reachability | dressing, layout | `Dresser.ClearAndConnect`, clutter connectivity, `LevelGenerator.GetKeepClear` |
| A3 | tour length | layout | target units in `Build`, `LayoutPlanner.GrowStations` |
| A4 | revisit | layout | walk cap in `LayoutPlanner.TryExtendWalk`, branch shares |
| A5 | monster split | budget | ambush chance in `BudgetPlanner.Plan` |
| A6 | ambush timing | content | trigger progress in `ContentPlacer.PlaceAmbushes` |
| A7 | ambush placement | content | spawner search in `ContentPlacer.PlaceAmbush` |
| A8 | crate loot | content | crate shares in `ContentPlacer.PlaceItems` and `PlaceWeapons` |
| A9 | checkpoint | content, script | `ContentPlacer.PlaceCheckpoint`, blackout block in `LevelScriptWriter` |
| A10 | placed groups | content | `ContentPlacer.PlacePlacedGroups` |
| A11 | difficulty | budget | `BudgetPlanner.Plan`, `ReferenceLevelSet.HitPointFactor` |
| A12 | mild gate | gate | wing gates in `LevelGenerator.ChooseGate`, `LayoutPlanner.GrowWing`, `PlaceWingGate` |
| A13 | theme decor | dressing | `LevelGenerator.GetDressingDecor`, clutter palette |
| A14 | wall clearance | dressing | `DressingLibrary.TryCopy`, `ClutterPlanner.PlanRow`, breaker prop fit |
| A15 | layout fill | layout | bounding box in `GrowStations`, `FillPockets` |
| A16 | spine share | layout | elongation inputs, chain length in `GrowStations`, walk cap |
| A17 | wall facing | dressing | `FacingTable` in `TryCopy`, `AddClutter` and `PlaceBosses` |
| A18 | decor context | dressing | `RoomDecorator.PlaceComputerTables` |
| B1 | locked doors | gate | `LayoutPlanner.PlaceGate`, key room near the cut |
| B2 | blackout | gate | `BlackoutReach`, `SearchPixels`, blackout branch of `PlaceGate` |
| B4 | fences | gate | `PlaceGate` fence cut, `PlaceGeneratorRoom` |
| C1 | dynamite count | budget, content | dynamite items, `PlaceSites`, `PlaceCharges` |
| C2 | dynamite timing | content | `EarliestSite`, `MeasureWalkOrder` |
| C3 | charge batches | content | runs in `PlaceCharges` |
| C4 | charge distance | content | spot search in `PlaceCharges` |
| C5 | final site | content | site regions in `PlaceSites` |
| C6 | site shape | content | `PlaceLabSite`, `StampSite` |
| C7 | site clearance | content | `ContentPlacer.IsCrowded` |
| D1 | breaker room | breaker spot, content | `ChooseBreakerSpot`, blackout trap in `PlaceAmbushes` |
| D2 | generators | content | generator count in `PlaceGate` |
| D3 | boss | content | `ContentPlacer.PlaceBosses` |
| D4 | secret closets | layout, content | `LayoutPlanner.AddClosets`, `AddShortcuts`, `PlaceStashes`, `ItemSchedule.CreateStash` |
| D5 | mounted cannon | content | `ContentPlacer.PlaceMountedCannons` |
| D6 | spawn walls | structure | spawn wall directions in `StructureBuilder.BuildLab` |
| D7 | weapons | content | `ContentPlacer.PlaceWeapons`, `TakeWeapon` |

### Common causes

- **A rule fails while the object sits where the code put it.** The rules read the tour progress `p`, and content placement reads distance from the start. See [Placement context](#placement-context).
- **A step places nothing.** Earlier steps took the cells (`Taken`), the keep-clear cells cover the room, or `GetFreeCells` finds only cells with walls on their edges. With the `inner` option, a cell also needs all four neighbors in the region.
- **An object stands inside a wall.** Wall boxes come from `PlacementContext.AddWallBox` for walls and fences only. Dressing checks copied objects against the clearance table alone.
- **A level breaks the same hard rule for every seed.** The layout inputs or a constant in `LayoutPlanner` or `ContentPlacer` leave no room for it. `preview` with a few seeds shows whether the measured value is always on the same side of the threshold.
- **Output changed for a seed after an unrelated edit.** See [Randomness](#randomness).
