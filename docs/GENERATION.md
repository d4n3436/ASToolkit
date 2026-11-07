# Level generation

This document describes how `LevelGenerator` builds a level, stage by stage, with the types and methods that do each step. It is meant as a map for finding where a level property comes from and which code to change when a level goes wrong.

The guarantees every level keeps are in [One pass and guarantees](#one-pass-and-guarantees), the engine facts and the original levels' measures the generator follows are in [NOTES.md](NOTES.md), and the file layouts it writes are in [FORMATS.md](FORMATS.md). All code lives under `src/ASToolkit.Core/Generation/` unless a path says otherwise.

## Contents

1. [Overview](#overview)
2. [Reference data](#reference-data)
3. [Coordinates](#coordinates)
4. [Target and budget](#target-and-budget)
5. [Building](#building)
6. [Doors and progression](#doors-and-progression)
7. [Layout plan](#layout-plan)
8. [Fine layout and structure](#fine-layout-and-structure)
9. [Breaker spot](#breaker-spot)
10. [Dressing](#dressing)
11. [Content](#content)
12. [Script](#script)
13. [One pass and guarantees](#one-pass-and-guarantees)
14. [Level 1](#level-1)
15. [Add-on style and installation](#add-on-style-and-installation)
16. [Debugging](#debugging)

## Overview

A generated level is one level of a campaign drawn from the style's campaign model. Levels 2 and later take their building, gate, threat, pickups and weapons from their brief and the rest from a donor original. Level 1 is an outdoor level ([Level 1](#level-1)). Each level is built once and read back with the route analysis that measures the originals; a layout whose tour misses an objective is drawn again.

```
 ascli generate / ascli preview
        |
 GeneratorFactory.Create          (src/ASToolkit.Cli/Commands)
   ObjectTable.Load, ReferenceLevelSet.Load, StyleFitter.Fit
        |
 LevelGenerator.Generate          brief from CampaignPlanner.Plan
        |
 +-- each layout k until the tour reaches every objective, at most 8 ----------+
 |  1  budget                          BudgetPlanner                          |
 |  2  building, doors, layout plan    BuildingBuilder, DoorStage, PlanWriter |
 |  3  cells, walls, floors, roof      FineLayout, StructureBuilder           |
 |  4  breaker spot                    ContentPlacer.ChooseBreakerSpot        |
 |  5  room types and furniture        RoomFurnisher, Dresser, RoomDecorator  |
 |  6  player start, content, script   PlacementContext, ContentPlacer        |
 |  analysis                           RouteAnalyzer on the written bytes     |
 +------------------------------------------------------------------------------+
        |
 GeneratedLevel.Write / RandomAddOnInstaller.Install
```

`GeneratedLevel` carries each stage's output: `Plan` (regions, links, stations, gate), `Layout` (cells, wall edges, lattice), `Budget`, `Map`, `Script` and `Analysis`, with the sprites' `Provenance` and the building's `LayoutCounts`.

### Randomness

A level draws from four `Random` streams. The layout seed drives the building, doors, plan, structure and breaker spot; streams seeded from it drive the budget, the roof and the content. The same seed and level always give the same files (`LevelGeneratorIntegrationTests`).

## Reference data

`StyleFitter.Fit` fits a `StyleModel` to the style's originals (campaign levels 1 to 10 in campaign style, every original in add-on style). `LevelGenerator` builds its other sources once from the reference levels and shares them.

| source | built by | read by |
|---|---|---|
| campaign model | `CampaignFitter.Fit` | `CampaignPlanner` |
| decor heights, wall clearance, facings | `StyleModel`, `WallClearanceTable`, `FacingTable` | dressing |
| content pool | `OriginalContentSet.Load` | `BudgetPlanner` |
| room atlases, recipes | `ReferenceLevelSet.GetAtlas`, `RoomRecipe.Measure` | `RoomFurnisher` |
| dressing libraries | `DressingLibrary.Build` | `Dresser.Dress` |
| object relations | `ObjectRelationTable.Build` | occupancy, furnishing, companions |
| breaker scenes, dynamite sites | `OriginalContent` | `ChooseBreakerSpot`, `PlaceSites` |
| keypads | `KeypadTable.Load` | structure, `PlaceGate` |
| armies | `ArmyTable.Load` | `MapBuilder.Add` |

## Coordinates

```
 unit         BuildingBuilder        one grid unit, one wall step
   |  x UnitCells (basement 1, concrete 2)
 cell         FineLayout             floor cells and the edges between them
   |  Lattice.ToWorld
 map pixel    MapBuilder             object positions
```

`WallSetSettings` gives the sizes per wall set: a basement unit is one cell on the (40, 28) lattice, a concrete unit 2 by 2 cells of the (45, 32) lattice. A `Cell` (U, V) spans [U, U+1) by [V, V+1); U runs along `/` and V along `\`. An `EdgeKey` names the border between two cells.

```
                       vertex (U+1, V)
                        /          \
  'v' edge (U, V)      /            \     'u' edge (U+1, V)
  a '/' wall          /              \    a '\' wall
   vertex (U, V)    <    cell (U, V)   >   vertex (U+1, V+1)
  'u' edge (U, V)     \              /    'v' edge (U, V+1)
  a '\' wall           \            /     a '/' wall
                       vertex (U, V+1)

   +U goes up and right on screen, +V goes down and right.
```

A cell's two upper edges are its far walls, facing the camera; fake doors, the breaker and basement dynamite holes go on them.

### Edge kinds

| `EdgeKind` | generation (`FineLayout.Edges`) | route analysis (`RouteGrid.Edges`) |
|---|---|---|
| `Wall` | wall | wall |
| `Door` | single door | door |
| `Doorway` | open doorway frame | open doorway |
| `DoubleDoor`, `DoubleDoorEnd` | the halves of a double door | |
| `SecretWall` | breakable wall | secret or spawn wall, monster hole |
| `SpawnWall` | spawn wall | |
| `ShutDoor` | | closed or jammed door |
| `BrokenWall` | | broken wall |
| `FenceFrame` | | concrete fence frame |

`EdgeKind.BlocksWalking` blocks only walls and shut doors. Generation treats every door and breakable wall as open; the route analysis sees the level as the player meets it (see [NOTES.md](NOTES.md#route-model)).

## Target and budget

### Campaign plan

`CampaignFitter.Fit` fits lines by level to the style's own campaigns: hit points, spawned share, floor, regular rooms per floor, end money, weapon levels by price order, affordability, repeated weapons, pickups per floor and their ids, ammunition by weapon age, secret closets, and the shares of buildings and gate styles. A line with fewer than 8 samples stays flat at the mean (`FittedValue.IsBackedOff`).

`CampaignPlanner.Plan` draws one `LevelBrief` per level (10 in campaign style, 5 in add-on style):

- **Buildings.** Level 1 is the grounds. Campaign style builds labs on levels 2 to 5 and warehouses on 6 to 10; add-on style draws every building but the grounds and the prison. Each building has its originals' wall set.
- **Gates.** The campaign opens with locked, locked and blackout labs on levels 2 to 4; other levels draw a gate per wall set, in the order the campaigns introduce them. An office block is always a blackout level.
- **Second wings.** Each lab level draws its second wing among those its gate builds (`BuildingGrammar.GetSecondWings`), never one already drawn in the campaign.
- **Threat, money, size.** Hit points and end money never fall from one level to the next. A lab's room count fits its two wings.
- **Weapons and pickups.** Weapons come in price order, none before the money can pay its share of the price.
- **Donor.** Each level names the campaign original of its wall set nearest in hit points (`LevelBrief.DonorLevel`). The donor gives what later steps have not yet replaced, such as sites, checkpoint, pens, bosses, fights, pickup styles, room atlas, dressing, gore, door marks, template map and music.

### Budget

`BudgetPlanner.Plan` returns the `LevelBudget`: placed groups and ambushes drawn from originals of similar hit points until they reach the brief's (ambushes up to its spawned share), the brief's pickups and weapons, one charge per donor site, and the donor's bosses, cannons and generators.

## Building

`BuildingBuilder.Build` lays out the building from the brief with the building's grammar, phase by phase, and hands the doors stage the rooms with their phases, the start, keys, gate room, trigger, locks, shortcuts, closed and jammed walls, and the end when the program names one. A grid unit is one wall step. Rooms share a wall or stand two units apart, never across a one-unit slit. After the program, closets stand behind host walls, and one lab in four hides a secret room.

Layouts come from the layout solver (`LayoutSolver`, CP-SAT). Each room takes a position and one of its shapes and turns, rooms never overlap, joined rooms meet on a door segment of each, and touches, door sides, obstacles, a screen band and a least number of contacts are optional constraints. It takes the first layout it finds, within half a deterministic second.

### Underground lab

A lab is two wings, the hub with its office pods and one second wing behind the gate.

- **Office pods.** Rings (two hall rooms bridged across a small court) and clumps (rooms packed against each other off one hall room) on both sides of the hub; the breaker ends a clump on a level with a key. Dark levels lock the pods past the first two; lit levels jam a ring's second hall door.
- **Locked doors.** The start opens onto the hub; the breaker stands in a pod; a locked passage at the hub's far end leads to the second wing.
- **Blackout.** A quiet hub, then a depot wing past its far end holding the dark run and, nested inside it, the last phase.
- **Ungated.** A hub of rings, then halls of big rooms walked in sequence.

| second wing | rooms | built by |
|---|---|---|
| suite (first gated level) | 3 to 4 | template |
| research wing | 7 to 12 | drawn (`ResearchWing`): a hall with one block of rooms on its far side, a ring of passages round a court, a ladder, an office block |
| depot wing | 9 to 14 | drawn and composed (`DepotWing`): two sections of depots across a court, a loading dock or a two-row service corridor |
| halls of big rooms | 8 to 10 | template: halls chained through cross passages, lined with big rooms |
| gallery, echelon, airlock | 6 to 12 | templates |

Templates are records read by `BuildingGrammar.LayWing`. Drawn wings are laid on their own grid and placed past the joint.

#### Designer values

| value | setting |
|---|---|
| secret room | one lab in four |
| first wing | 12 to 14 regular rooms, 6 to 10 on the first gated level |
| office pods | rings at most 3 rooms, clumps at most 6 |
| lab room counts | 19 to 28; 19 to 24 locked, 19 to 22 ungated, 9 to 14 on the first gated level |
| depot sections | 3 to 5 depots |
| depot wing's last phase | a third of its rooms |
| loading dock | one room up to 704 units |

### Warehouse

A campaign warehouse is laid from its level's template (levels 6 to 9; level 5 takes 6's, level 10 takes 9's). Each area (hall, strip, leg, pocket, block, depot) is drawn with its rooms, then the layout solver places the areas as single rooms. Doors join each area's rooms along its walk and its entry room to its host; a door into a later phase is a fence.

### Office block

The office block (`OfficeGrammar`, `OfficeBlock`) is a blackout level drawn as one level graph and laid in one solve:

- **Graph.** The start hall with two offices; a dark run of four to six rooms to the breaker's room, whose largest office holds the breaker; then the rooms the breaker opens (halls, big rooms, Ls, rooms with rows of offices, and the suite) joined in a tree, one run room opening onto them.
- **Solve.** Every room shares a wall with at least two others, and the building keeps to a band of the screen. A greedy placement is the solver's starting point.
- **After the solve.** Rows of offices are cut from their rooms, and pockets become rooms or join a neighbor. Rooms of the last phase get a door on every wall they share; the suite opens only along itself. The end is the last phase's large room farthest from the breaker.

### Add-on buildings

Add-on warehouses, the prison and the remaining add-on buildings are built as labs for their gate until their own designs replace them.

## Doors and progression

`DoorStage.Choose` lays the building's locks and shortcuts as given and a door in every other wall the building leaves open, locked into another phase. `MissionWriter` turns the result into the `MissionGraph`. A building that names no end ends in its last phase's room farthest from the last key.

The doors of every building keep every room reachable, every gate lock seen before its key, the key reachable inside its phase, no unlocked door between phases, and the goal reachable after every state change. A building that cannot carry its gate is laid out ungated and counted.

## Layout plan

`PlanWriter.Write` turns the building and its mission into a `LayoutPlan` of `PlanRegion`s joined by `PlanLink`s on the unit grid.

- **Stations.** The critical path in flow order; locked links set the gate, the breaker or generator rooms are the key rooms.
- **Closets.** Closets become regions behind their host's wall, linked by a `SecretWall` or `SpawnWall`.
- **Openings.** Locked links are doors, open links arches; jammed doors stand only on the building's jammed walls.
- **Doorways.** `AlignLinks` moves each doorway off wall junctions and `SpaceDoors` keeps basement doors 160 px apart.

## Fine layout and structure

`FineLayout.Build` turns each unit into `UnitCells`² cells and each link into edges:

| link | cell edges |
|---|---|
| `Door`, two edges | `DoubleDoor` and `DoubleDoorEnd` |
| `Door`, one edge | `Door` |
| `SecretWall`, `SpawnWall` | the same |
| `Arch` | `Doorway` |
| `Open`, `Fence` | open |
| jammed door | `ShutDoor` |

Every other border of a region is a `Wall`.

- **Placement.** `StructureBuilder.PlaceLattice` keeps every cell 500 px into the map, on the basement floor tiles' parity. `AddFakeDoors` turns 2 to 5 far walls of hall stations into doors that lead nowhere.
- **Basement.** `BuildBasement` places floor 500, doors 605 to 608, breakable walls 609, spawn walls 618 and 619, doorway frames 611 and 617, walls 601 and 602 and beams 604. Closets get sunk outer walls. Broken keypads go by jammed doors.
- **Concrete.** `BuildConcrete` places walls 650 and 651, frames 653, posts 652 and floor 503; `BuildFence` places fence emitters and fences on fence links.
- **Roof.** `BuildRoof` covers every cell not shown with tile 501.

`MapBuilder` collects the objects, gives them handles and armies, and fills their payloads.

## Breaker spot

`ContentPlacer.ChooseBreakerSpot` picks a `\` far wall of the key room with a run of 4 pieces or more ending in corners and open floor in front and beside, and fits the originals' breaker scenes around it. When no wall qualifies, `Build` starts over, up to 20 times, then builds the level without a breaker.

## Dressing

Every piece goes through the level's occupancy ([Keeping the way clear](#keeping-the-way-clear)).

1. `Dresser.Dress` copies wall dressing, blood and rubble windows from originals with the same walls.
2. `RoomDecorator.Decorate` thins blood and corpses to the donor's rates and lays gore toward the end.
3. `RoomFurnisher.Furnish` types each room from its area and fills it with whole parts copied from the originals' rooms of its type, each anchored against walls as in its source, with its companions (`ObjectRelationTable.GetCompanions`).
4. The breaker scene stands in the key room, and missile stripes go in front of missile threes.

### Keeping the way clear

`LevelGenerator.GetKeepClear` reserves both sides of every doorway, the front of each fake door and the start. `LevelOccupancy` keeps blocking pieces off doorways by the originals' door clearance, out of each other's boxes, and leaves the open floor joined to the start; `FitsAll` takes a group whole.

## Content

`PlacementContext` holds the state content placement shares: the taken cells, the occupancy, walking distance and progress from the start, and the fights placed. The player start goes on the start room's free middle cell. `ContentPlacer.Place` then runs:

```
 PlaceGate -> PlaceBosses -> PlaceMountedCannons -> PlaceSites -> PlaceCharges
   -> PlaceEncounters -> PlaceStashes -> PlaceItems -> PlaceCheckpoint
```

Most steps match what they place to the donor's rooms by role (`RoomRoleMap`), gate zone and progress (`EncounterProfile`).

- **Gate.** The breaker (418) and keypads on locked-door and blackout levels; a blackout's trigger is one point in the trigger room, well inside its door. Fenced levels put generators (415) in the back of each generator room.
- **Bosses.** In the largest late dead-end room, else the last station.
- **Mounted cannon.** At the entry end of a large late station, with a kill zone at the far end.
- **Sites and charges.** Sites stand in the donor's site rooms' counterparts; on a lab only in the second wing, a third of them in locked rooms. Basement sites break a far wall into a hole with endless generators behind it; concrete sites stand pads off walls. One charge per site, 150 px or more from every site.
- **Fights.** Standing groups and ambushes take the donor's fights by rank. On a lab every fight stands in the second wing; on every level a spawn trigger stands on the main route. Spawners stand off screen from their trigger; closet ambushes blow their wall open (`SpawnerProgram.Create`).
- **Stashes and items.** Secret closets hold stashes; weapons and pickups go loose or in crates as the donor lays them.
- **Checkpoint.** On the walk to the strongest fight, when the donor has one and the level is not a blackout.

## Script

`LevelScriptWriter.Format` writes the `.lgc`: the donor's music, `InitGame` and `DoorsIsOpen`, a blackout trigger (respawn there, doors locked, lights off, keypads swapped), a checkpoint trigger, and an exit trigger that wins the level. A locked-doors level starts dark (ambient `C0C0C0`); every other indoor level starts lit.

## One pass and guarantees

`LevelGenerator.Generate` builds a level once, writes and parses it back as the game would, and runs `RouteAnalyzer.Analyze`. A step that cannot meet its guarantee draws itself again; when the tour still misses an objective, forces a gate or needs a secret wall outside level 1, `Generate` takes the next layout, up to 8, and otherwise keeps the one with the fewest unreached objectives, then forced gates, then secret walls. Layout `k`'s seed is `seed × 100000 + level × 100 + k`.

| guarantee | code |
|---|---|
| every objective reachable, no gate forced, no secret wall needed outside level 1 | `DoorStage`, `LevelOccupancy`, `Generate` |
| every room reachable, gate locks seen before their keys, no unlocked door between phases | `BuildingBuilder`, `DoorStage` |
| a blackout's trigger at the dark phase's entrance, its breaker in that phase's key room | `BuildingBuilder`, `DoorStage`, `PlanWriter` |
| a generator in every key room of a fenced mission | `PlaceGate` |
| no overlap, no one-unit step, every room joined to the start, closets against their hosts | `BuildingBuilder` |
| the breaker on a back wall of 4 pieces or more with open floor around it | `ChooseBreakerSpot` |
| one charge per site, sites clear of solid decor | `PlaceCharges`, `LevelOccupancy` |
| blocking pieces off doorways and apart as the originals keep them, no pickup under them | `LevelOccupancy` |
| decor of the level's floor set, facing and clearing walls as in the originals | `Dresser`, `RoomFurnisher` |
| no wave from floor walked before its trigger | `PlaceEncounters` |
| the roof covers no walkable floor | `BuildRoof` |
| every sprite payload 8 bytes or more | tests |

`LevelGeneratorIntegrationTests` asserts them over seeds 1 to 10 of the campaign and the add-ons. Likeness measures (revisits, gated share, key walks, blocked floor) are not guaranteed; `ascli inspect design` writes them beside the originals' in `design.tsv`.

## Level 1

`OutdoorLevelBuilder` builds level 1 on a map the size of the original it follows:

- **Campaign.** A fenced yard with a shut gate by the start, one or two small buildings, the exit behind a breakable wall, storage clusters, lamps and money; no monsters.
- **Compound (add-on 1).** The same yard with a dynamite site at the exit, a charge outside, a shotgun, a cluster of monsters and two spawners at the gate.
- **City (add-on 2).** A grid of roofed blocks and streets, spawners along them, bats in the far corner; every monster dead wins.

`LayGround` lays the original's ground rows with grass clumps and tufts. Level 1 gets no roof.

## Add-on style and installation

In add-on style the campaign model is fitted to both add-ons and plans 5 levels; levels 2 to 5 are written over add-on 1's map of the same number. `RandomAddOnInstaller.Install` writes the levels to `Maps/RANDOM` with their briefings, money table, script branches and menu button ([README](../README.md#the-random-add-on)).

## Debugging

- `ascli preview --game <folder> --seed <s> --level <n> --out <file.map>` builds one level as `generate` does.
- `ascli analyze --levels <folder>` draws the tour with objectives, gates and triggers.
- `ascli inspect design --seeds 10` measures decor, pickups, pacing, room graph, phases, beats, architecture and novelty of the originals and of generated levels, and writes them side by side to `design.md`, with `campaign.md`, `provenance.md` and `layout.md` beside it.
- Content placement reads distance from the start (`PlacementContext.GetProgress`), not the tour progress `p` the route analysis reads.
- A step that places nothing usually found its cells taken (`Taken`) or reserved by the keep-clear cells.
- Output that changes for a seed after an unrelated edit comes from the random streams ([Randomness](#randomness)).
