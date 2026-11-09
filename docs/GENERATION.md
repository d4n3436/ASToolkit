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
12. [Designer values](#designer-values)
13. [Script](#script)
14. [One pass and guarantees](#one-pass-and-guarantees)
15. [Level 1](#level-1)
16. [Add-on style and installation](#add-on-style-and-installation)
17. [Debugging](#debugging)

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
 |     fights                          FightPlanner                           |
 |  3  cells, walls, floors, roof      FineLayout, StructureBuilder           |
 |  4  breaker spot                    ContentPlacer.ChooseBreakerSpot        |
 |  5  furniture, set pieces and gore  SceneFurnisher                         |
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
| wall clearance, facings | `StyleModel`, `WallClearanceTable`, `FacingTable` | dressing |
| encounter model | `EncounterModel` | `FightPlanner`, `EncounterPlacer`, spawn closets, `PlaceGate` |
| economy model | `EconomyModel` | `PickupPlacer`, `PlaceSites`, `PlaceCharges`, the checkpoint |
| furniture grammar | `FurnitureGrammar.Default`, tables in `Generation/Dressing/FurnitureGrammar.Data.cs` with numbers measured once on the originals | `SceneFurnisher` |
| object relations | `ObjectRelationTable.Build` | occupancy |
| set pieces | `FurnitureGrammar.Scenes.cs`, `SceneGrammar.Default` | `SceneFurnisher`, `ScenePlanner` |
| armies | `ArmyTable.Load` | `MapBuilder.Add` |

The encounter model holds these values, fitted to the style's originals:

- the spacing of standing monsters;
- how many monsters a spawner holds;
- spawn closets per room;
- endless generators per dynamite site, and fence generators per generator room;
- how many fights a level holds for its floor;
- how far apart a fight's spawners stand, and how far from their trigger;
- how big each phase's largest fight is, and where in their phases the originals' largest fights come.

A fight is everything the player meets in one phase within 1,000 px of walk from its first encounter. Groups holding under 2% of the level's hit points don't count.

The economy model holds these values:

- the share of each category's pickups in crates;
- the sizes of piles and the walls each category's piles stand against;
- how much denser pickups lie in rooms off the walks from the start to the objectives than on them;
- what secret closets hold and how many pickups, and how many spawn closets hold any;
- how far along the walk from the start dynamite sites stand, how far ahead of a site with no gate before it its charges lie, and how many levels hold charges in the start room;
- the share of levels with a checkpoint.

A value with fewer than 8 samples behind it falls back to a default.

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

`CampaignFitter` fits lines by level to the style's own campaigns for hit points (by the log of the level), spawned share, floor, regular rooms per floor, end money, weapon levels by price order, affordability, repeated weapons, pickups per floor and their ids, ammunition by weapon age, secret closets and dynamite sites per floor (sites by gate style), the shares of buildings and gate styles, each monster kind's first level, the mean hit points of a level's monsters, and the bosses with their share of levels. A line with fewer than 8 samples stays flat at the mean.

`CampaignPlanner.Plan` draws one `LevelBrief` per level (10 in campaign style, 5 in add-on style):

- **Buildings.** Level 1 is the grounds. Campaign style builds labs on levels 2 to 5 and warehouses on 6 to 10; add-on style draws every building but the grounds and the prison. Each building has its originals' wall set.
- **Gates.** The campaign opens with locked, locked and blackout labs on levels 2 to 4; other levels draw a gate per wall set, in the order the campaigns introduce them. An office block is always a blackout level.
- **Second wings.** Each lab level draws its second wing among those its gate builds (`BuildingGrammar.GetSecondWings`), never one already drawn in the campaign.
- **Threat, money, size.** Hit points and end money never fall from one level to the next. A lab's room count fits its two wings.
- **Weapons and pickups.** Weapons come in price order, none before the money can pay its share of the price.
- **Sites and cannons.** Each indoor level draws its dynamite sites from its gate style's fitted rate per floor. Campaign style stands a mounted cannon on warehouse levels 7 and 8, add-on style on its last warehouse level only.
- **Monsters and bosses.** A level draws the kinds it has reached, weighted over log hit points so that their mean meets the level's fitted mean; kinds first seen on the level are its new kinds, none on the style's first level with monsters. A level holds a boss at the fitted share, the newest boss kind it has reached.
- **Donor.** Each level names the campaign original of its wall set nearest in hit points. The donor gives the template map and the music lines.

### Budget

`BudgetPlanner` sets the level's budget. It takes the brief's hit points within a tenth, its pickups and weapons, one charge per site, endless generators at the fitted rate per dynamite site of its wall set, and the brief's cannons.

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

#### Grammar values

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
- **Walk.** Regions carry their order on the critical walk; links every walk from the start to the end crosses are chokes (`MissionWriter`).
- **Doorways.** `AlignLinks` moves each doorway off wall junctions and `SpaceDoors` keeps basement doors 160 px apart.

## Fights

`FightPlanner` plans the level's fights on the layout plan as `PlannedFight`s.

- **How many.** The level holds the fitted number of fights for its floor. Each phase gets fights in proportion to its floor and spreads them evenly along its walk. Rooms without a fight stay empty.
- **Phases.** A phase gets hit points in proportion to its rooms' floor, less if none of its rooms can hold an ambush. One fight in each phase is its climax. It gets the fitted share of the phase's hit points and comes at a point of the phase drawn from where the originals' climaxes come. Its room is the one nearest that point among the rooms that can hold an ambush, if the phase has any. The other fights split the rest by floor.
- **Monsters.** Each fight draws one monster family. At the style's measured share a fight holds one variant of it; otherwise its variants mix, none over the style's measured cap. A family too strong for a fight is kept for the next one.
- **Ambushes.** A fight in a room that every walk to the end enters through one door can be an ambush. Climaxes come first, and ambushes stop once the brief's spawned share is used. The trigger covers that door. Rooms entered through the blackout door get no ambush, and when two neighboring fights have doors close together, only one of them becomes an ambush. Spawners stand in the room's spawn closet if it has one. Otherwise they stand out of sight in later fight rooms, spread apart and within reach of the trigger.
- **New kinds.** Each new monster kind shows up first in the fight nearest a rank drawn from the style's measured opening ranks (the level's smallest fight 0, its biggest 1), an ambush at the style's measured share where the room allows one. The campaign's first monster level holds placed fights only.
- On a lab every fight stands in the second wing.

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
- **Basement.** `BuildBasement` places floor 500, doors 605 to 608, breakable walls 609, spawn walls 618 and 619, doorway frames 611 and 617, walls 601 and 602 and beams 604. Closets get sunk outer walls. Basement site walls become monster holes (616 or 612). Working doors get keypads, off on locked-door levels; jammed frames mostly get broken ones.
- **Concrete.** `BuildConcrete` places walls 650 and 651, frames 653, posts 652 and floor 503; `BuildFence` places fence emitters and fences on fence links.
- **Roof.** `BuildRoof` covers every cell not shown with tile 501.

`MapBuilder` collects the objects, gives them handles and armies, and fills their payloads.

## Breaker spot

`ContentPlacer.ChooseBreakerSpot` picks a `\` far wall of the key room with a run of 4 pieces or more ending in corners and open floor in front and beside, and draws one of the walls that qualify. When no wall qualifies, `Build` starts over, up to 20 times, then builds the level without a breaker.

## Dressing

Every piece goes through the level's occupancy ([Keeping the way clear](#keeping-the-way-clear)).

1. `StructureBuilder` hangs an electric panel beside some small doors and wall objects beside some double doors, scatters shadows over the closet side of breakable and spawn walls and piles rubble at some monster holes.
2. `SceneFurnisher.Furnish` furnishes each room from the furniture grammar, C# tables of each room type's arrangements with their shares, counts per area and modules per run, the type's signature family and whether its far walls stay covered, the families' pieces, wall distances and steps, each piece's companions by room type and facing, and the closest two families stand. A room stands its signature first with its full module (a desk with its computer and chair, a lab bench with its labware), holds each arrangement by its share and places it as a wall run on far or near walls, a grid on open floor in rooms large enough, a heap of stock with each piece at a measured offset for its two families and by a wall at its family's measured wall share for the room size, or a lone piece on open floor, grows with its far, grid and heap arrangements until it holds the piece count drawn for its type or each has stopped landing, then fills far wall stretches left bare. A piece takes the measured facing for the wall of its cell. Every piece goes through the level's occupancy. Walked-over pieces (shadows, blood, stripes, pads) take no spacing.
3. After its furniture a room hangs its wall fixtures and lays its victims by room type, a corpse beside the furniture it belongs with or seated against a far wall or a container, on its own blood, never by a near wall or behind a piece; gibs lie where victims' blood runs together, and rubble levels strew rock piles. Chairs and tables carry paper.
4. `PlacementContext` lays shadows under pickups at a count per kind.
5. Set pieces stand first, with their whole module.
   - Breaker: 2 to 4 transformers, a pointer and a corpse in blood. Cannon: a stripe box, crates and a pointer. Generators: 0.7 cells off a far wall with transformers, a shoot-here marker and a corpse. Missile racks: three missiles with a stripe box.
   - Concrete sites: pads between the site and the walls, one by a wall, one in a corner, a line of three from a far corner or two straddling the site. A site takes the middle of an outer side, shifted 0.6 cells toward its far end to look centered, or a corner; in a long room only its ends.
   - Several sites in a room pair up symmetrically, both corners of one side or the middles of facing sides, never two on one side otherwise. A site with no spot moves to the planner's next room.

### Keeping the way clear

`LevelGenerator.GetKeepClear` reserves both sides of every doorway, the front of each fake door and the start. `LevelOccupancy` keeps blocking pieces off doorways by the originals' door clearance, out of each other's boxes, and leaves the open floor joined to the start; `FitsAll` takes a group whole.

## Content

`PlacementContext` holds the taken cells, the occupancy, and the walking distance and progress from the start. The player start goes on the start room's free middle cell. Content placement then runs these steps:

```
 before the walls:  ScenePlanner.PlanSites
 before the decor:  ScenePlanner.PlaceCannon -> StandBasementSites -> GetRequests (handed to the furnisher)
 after the decor:   StackEndlessGenerators -> PlaceGate -> PlaceBosses -> PlaceCharges -> EncounterPlacer.Place -> PickupPlacer.Place -> PlaceCheckpoint
```

`ScenePlanner` decides where the set pieces go; the furnisher stands them ([Dressing](#dressing)).

- **Gate.** The breaker (418) on locked-door and blackout levels; a blackout's trigger is one point in the trigger room, well inside its door.
- **Bosses.** The brief's bosses, in the largest late dead-end room, else the last station.
- **Mounted cannon.** At the entry end of a large late station, with a kill zone at the far end.
- **Sites and charges.** Sites go to rooms near positions drawn along the walk, before the walls are built; concrete sites only in rooms of 96 cells or more. A basement site stands before a far wall hole with a spawner behind it that builds endless generators once the site's phase opens. Concrete sites stack 2 to 4 endless generators on a pad. Everything the blast must kill stands within its 100 px. The charges of each phase's sites lie together, 150 px or more from every site. They go beside the last key of the phase before, the breaker room or the generator room the player reaches last. A phase with no key before it puts them in the start room on the fitted share of levels for the first phase, and otherwise on the walk to its first site, the fitted lead before it.
- **Fights.** `EncounterPlacer` places the planned fights. Standing groups stand in clusters on visible floor and spill into the nearest rooms of their phase. Ambush spawners stand out of sight of a player inside the trigger circle on the room's door. Spawn closets blow their wall open, and only they may be seen spawning. An ambush with nowhere out of sight to stand becomes a standing group.
- **Pickups.** `PickupPlacer` lays the budget's pickups. A new weapon lies off the walks to the objectives or in a secret closet, with one of its ammunition pickups earlier on its walk. Secret closets and some spawn closets then take their stashes, crates take each category's share, and the rest lie in small piles in rooms drawn by floor, rooms off the walks weighted by the fitted density.
- **Checkpoint.** On the walk to the biggest fight, just before its room, on the fitted share of levels that are not blackouts.

## Designer values

The generator's other thresholds are game facts, search budgets or values measured once on the originals. The building grammars' values are listed under each building.

| value | setting |
|---|---|
| campaign shape | first indoor level 2, first warehouse level 6, mounted cannons on levels 7 and 8 |
| campaign draws | drawn values kept within 1.5 spreads of their fitted line |
| mounted cannon | in a hall reached after 3,600 px of walk, by 0.75 of the level, near 0.6 along its hall; furniture 100 px off its lane, monsters 600 px off it |
| breaker | a far-wall run of 4 steps, 3 steps clear in front and at its open end, 2 to the side, about 400 px from the gate |
| blackout trigger | 94 px past its door |
| bosses | the fitted share of levels, the newest kind, in a dead end from 0.65 of the level; gore 1 lattice unit off walls |
| divers | never placed |
| endless generators | the 9 ranks below the level's top rank |
| site holes | 3 edges apart |
| site spots | doorways 3 cells away, sides at most 1.25 times the room's depth, middles 1.5 cells of slack |
| seats | 0.3 cells of slack, nothing within 60 px in front |
| furniture | a far wall bare for 6 cells takes a run, heaps reach 3 cells |
| gore and rubble | gibs where 48 small pools run together, rubble on a quarter of basement levels and a quarter of monster holes |
| first monster level | placed fights only, in campaign style |
| screen | 1280 by 720, spawners a cell past it |

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
| one charge per site, enough charges before every site in play order, sites clear of solid decor | `PlaceCharges`, `LevelOccupancy` |
| every pickup and weapon of the budget, a new weapon after some of its ammunition, the checkpoint before the biggest fight | `PickupPlacer`, `PlaceCheckpoint` |
| blocking pieces off doorways and apart as the originals keep them, no pickup under them | `LevelOccupancy` |
| decor of the level's floor set, at its measured facings and wall clearances | `SceneFurnisher` |
| open-floor furniture only in rooms at least its measured open-floor size, no two pieces closer than their families' measured spacing for pieces facing the same way or turned, two pieces of one object, or two module anchors, no closer than the mean of their frames' measured spacings, a piece lining a wall as far from the walls at its ends as its frame stands in the campaign, spacings measured on the campaign and backing off to all originals below 8 samples, a stacked piece only on the tier under it, the room type's signature held in rooms of any size by its measured share | `SceneFurnisher` |
| no piece nearer a far or near wall than its family's measured distance for that side, nor nearer any wall than the least clearance measured for the piece in its wall set, every piece and companion inside its own room, no family in a room under the style's measured smallest room for it, no arrangement past its highest measured rate, a room's piece count drawn from the measured density for its size | `SceneFurnisher` |
| transformers only beside a breaker or generator and only on far walls, along the breaker's wall or the far wall nearest the generator at the measured clearance, never in a corner against a near wall, flanking the key and turned one way | `ContentPlacer` |
| computer rooms only as big rooms of at least 15 by 18 lattice units | `BuildingGrammar` |
| furniture carries its shadows at the campaign's measured share and offset per piece; corpses lie on their own blood | `SceneFurnisher` |
| room programs of halls, offices, depots and storage from the campaign's rooms, the rarer types from all originals | `FurnitureGrammar` |
| linked pickups at the height that rests their glow on the floor | `PlacementContext`, `PickupPlacer` |
| each family's main piece per room drawn by the style's own weights, a second piece mixed in at the measured share, every anchor's footprint (its family's side-by-side spacing) inside the room, no furniture footprint on a room's openings or the cells inside them, a room's openings and reserved cells joined past its furniture on the layout cells, no add-on piece in a campaign room | `SceneFurnisher` |
| sites, generators and the cannon's lane kept clear | `LevelGenerator`, `SceneFurnisher` |
| spawners and transformers clear of the solid furniture around them | `EncounterPlacer`, `ContentPlacer` |
| no spawner in an earlier phase than its trigger, every spawn trigger on every walk from the start to the end, no mixed family with one variant over the style's cap | `FightPlanner` |
| fights' hit points within a tenth of the budget's, each phase's placed hit points within a tenth of its fights', the campaign's first monster level bringing its kinds in placed fights no harder than its median | `FightPlanner`, `EncounterPlacer` |
| spawners but closets' off the 1280 by 720 screen around their trigger, monsters 12 px apart | `EncounterPlacer` |
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
