# Level generation

This document describes how `LevelGenerator` builds a level, stage by stage, with the types and methods that do each step. It is meant as a map for finding where a level property comes from and which code to change when a level goes wrong.

`LevelGeneratorIntegrationTests` checks the guarantees every level keeps, the engine facts and the original levels' measures the generator follows are in [NOTES.md](NOTES.md), and the file layouts it writes are in [FORMATS.md](FORMATS.md). All code lives under `src/ASToolkit.Core/Generation/` unless a path says otherwise.

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
14. [Read back](#read-back)
15. [Level 1](#level-1)
16. [Add-on style and installation](#add-on-style-and-installation)
17. [Debugging](#debugging)

## Overview

Each generated level is one level of a campaign drawn from the style's campaign model ([Campaign plan](#campaign-plan)). The style is either campaign style or add-on style ([Add-on style and installation](#add-on-style-and-installation)), and a brief is the set of targets the campaign planner draws for one level. Levels 2 and later take their building, gate, hit points, pickups and weapons from their brief, and their template map and music from a donor, the original level the brief names. Level 1 is an outdoor level ([Level 1](#level-1)). Each layout is read back with the route analysis that measures the originals, and when its tour misses an objective, the next layout is drawn ([Read back](#read-back)).

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

`GeneratedLevel` carries the output of each stage in `Plan` (regions, links, stations, gate), `Layout` (cells, wall edges, lattice), `Budget`, `Map`, `Script` and `Analysis`, with the sprites' `Provenance` and the building's `LayoutCounts`.

### Terms

- **Phase.** The part of the building the player can reach between two state changes, such as a gate opening.
- **Gate.** Locked doors that open when the breaker (418) is pulled, a blackout that turns off the lights and locks the doors at a trigger until then, or electric fences that drop when a generator is destroyed. Ungated levels don't have one.
- **Key.** The breaker or generator that opens the next phase. The room that holds it is a key room.
- **Tour.** The route analysis's walk from the start through every objective ([NOTES.md](NOTES.md#route-model)). Content placement reads the walking distance from the start instead.
- **Program.** The rooms and wings a building grammar lays out for the level's gate, with their phases.
- **Dynamite site.** A target the player blows up with a dynamite charge. Basement sites stand in front of a monster hole, and concrete sites can have endless generators, which make monsters until a blast kills them, stacked beside them.
- **Closet.** A small room behind a wall of its host room. A secret closet sits behind a breakable (secret) wall and holds a stash, and a spawn closet sits behind a spawn wall that blows open to release its monsters.
- **Jammed door.** A door that stays shut. The building marks the walls that may hold one as jammed walls.

### Randomness

Each level draws from five `Random` streams. The layout seed drives the building, doors, plan, structure and breaker spot; streams seeded from it drive the budget, the fights, the roof and the content. The same seed and level always give the same files (`LevelGeneratorIntegrationTests`).

## Reference data

`StyleFitter.Fit` fits a `StyleModel` to the style's originals (campaign levels 1 to 10 in campaign style, every original in add-on style). `LevelGenerator` builds its other sources once from the reference levels and shares them.

| source | built by | read by |
|---|---|---|
| campaign model | `CampaignFitter.Fit` | `CampaignPlanner` |
| wall clearance, directions | `StyleModel`, `WallClearanceTable`, `DirectionTable` | dressing |
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

Fights are everything the player meets in one phase within 1,000 px of walk from their first encounter. Groups holding under 2% of the level's hit points don't count.

The economy model holds these values:

- the share of each category's pickups in crates;
- the sizes of piles and the walls each category's piles stand against;
- how much denser pickups lie in rooms off the walks from the start to the objectives than on them;
- what secret closets hold and how many pickups, and how many spawn closets hold any;
- how far along the walk from the start dynamite sites stand, how far ahead of a site with no gate before it its charges lie, and how many levels hold charges in the start room;
- the share of levels with a checkpoint.

Values with fewer than 8 samples behind them fall back to a default.

## Coordinates

```
 unit         BuildingBuilder        one grid unit, one wall step
   |  x UnitCells (basement 1, concrete 2)
 cell         FineLayout             floor cells and the edges between them
   |  Lattice.ToWorld
 map pixel    MapBuilder             object positions
```

`WallSetSettings` gives the sizes per wall set. Basement units are one cell on the (40, 28) lattice, concrete units 2 by 2 cells of the (45, 32) lattice. A `Cell` (U, V) spans [U, U+1) by [V, V+1); U runs along `/` and V along `\`. An `EdgeKey` names the border between two cells.

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

The two upper edges of a cell are its far walls, facing the camera, and the two lower ones are its near walls. Fake doors, the breaker and the monster holes of basement sites go on far walls.

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

`CampaignFitter` fits lines by level to the style's original campaigns for hit points (by the log of the level), the spawned share (hit points in spawned monsters), floor, regular rooms per floor, end money, weapon levels by price order, affordability (the share of a weapon's price earned before it appears), repeated weapons, pickups per floor and their IDs, ammunition by weapon age, secret closets and dynamite sites per floor (sites by gate style), the shares of buildings and gate styles, each monster kind's first level, the mean hit points of a level's monsters, and the bosses with their share of levels. Lines with fewer than 8 samples stay flat at the mean.

`CampaignPlanner.Plan` draws one `LevelBrief` per level (10 in campaign style, 5 in add-on style):

- **Buildings.** Level 1 is outdoor. Campaign style builds labs on levels 2 to 5 and warehouses on 6 to 10; add-on style draws every building but the outdoor level and the prison. Each building has its originals' wall set.
- **Gates.** The campaign opens with locked, locked and blackout labs on levels 2 to 4; other levels draw a gate per wall set, in the order the campaigns introduce them. Office blocks, which only add-on style draws, are always blackout levels.
- **Second wings.** Each lab level draws its second wing among those its gate builds (`BuildingGrammar.GetSecondWings`) that the campaign has not drawn yet.
- **Hit points, money, size.** Hit points and end money never fall from one level to the next. Lab room counts fit their two wings.
- **Weapons and pickups.** Weapons come in price order, and a weapon never comes before the end money and cash earned from level 2 on reach its affordability share.
- **Sites and cannons.** Each indoor level draws its dynamite sites from its gate style's fitted rate per floor. Campaign style stands a mounted cannon on warehouse levels 7 and 8, add-on style on its last warehouse level only.
- **Monsters and bosses.** Each level draws the kinds it has reached, weighted over log hit points so that their mean meets the level's fitted mean; kinds first seen on the level are its new kinds, and the style's first level with monsters doesn't have any new kinds. Levels hold a boss at the fitted share, the newest boss kind they have reached.
- **Donor.** Each level names the campaign original of its wall set nearest in hit points. The donor gives the template map, whose chunks other than the objects and the header the level keeps, and its script's music; add-on style takes add-on 1's map of the same number as the template.

### Budget

`BudgetPlanner` sets the level's budget. It takes the brief's hit points within a tenth, its pickups and weapons, one charge per site, endless generators at the fitted rate per dynamite site of its wall set, and the brief's cannons.

## Building

`BuildingBuilder.Build` lays out the building from the brief with the building's grammar, phase by phase, and hands the doors stage the rooms with their phases, the start, keys, gate room, trigger, locks, shortcuts, closed and jammed walls, and the end when the program names one. One grid unit is one wall step. Rooms share a wall or stand two units apart. Once the program's rooms are laid, closets stand behind host walls, and one lab in four hides a secret room.

Layouts come from the layout solver (`LayoutSolver`, CP-SAT). Each room takes a position and one of its shapes and turns. Rooms never overlap, joined rooms meet on a door segment of each, and touches, door sides, obstacles, a screen band (how far the building may reach across and down the screen) and a minimum number of contacts are optional constraints. The solver takes the first layout it finds within 0.5 s of CP-SAT's deterministic time, which doesn't depend on the machine.

### Underground lab

Labs are two wings, the hub with its office pods and one second wing behind the gate.

- **Office pods.** Rings (two hall rooms bridged across a small court) and clumps (rooms packed against each other off one hall room) on both sides of the hub; the breaker ends a clump on a level with a key. Dark levels lock the pods past the first two; lit levels jam a ring's second hall door.
- **Locked doors.** The start opens onto the hub; the breaker stands in a pod; a locked passage at the hub's far end leads to the second wing.
- **Blackout.** The hub is quiet, and a depot wing past its far end holds the dark run and, nested inside it, the last phase.
- **Ungated.** The hub carries rings, and halls of big rooms follow, walked in sequence.

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

Campaign warehouses are laid from their level's template (levels 6 to 9; level 10 takes 9's). Each area (hall, strip, leg, pocket, block, depot) is drawn with its rooms, then the layout solver places the areas as single rooms. Doors join each area's rooms along its walk and its entry room to its host; a door into a later phase is a fence.

### Office block

The office block (`OfficeGrammar`, `OfficeBlock`) is a blackout level drawn as one level graph and laid in one solve:

- **Graph.** The start hall with two offices; a dark run of four to six rooms to the breaker's room, whose largest office holds the breaker; then the rooms the breaker opens (halls, big rooms, Ls, rooms with rows of offices, and the suite) joined in a tree, one run room opening onto them.
- **Solve.** Every room shares a wall with at least two others, and the building keeps to a band of the screen. The solver starts from a greedy placement.
- **After the solve.** Rows of offices are cut from their rooms, and pockets become rooms or join a neighbor. Rooms of the last phase get a door on every wall they share; the suite opens only along itself. The end is the last phase's large room farthest from the breaker.

### Add-on buildings

Add-on warehouses, the conference building, the school and the training complex are built as labs for their gate.

## Doors and progression

`DoorStage.Choose` lays the building's locks and shortcuts as given, and a door in every other wall the building leaves open between two rooms, locked when it leads into another phase. `MissionWriter` turns the result into the `MissionGraph`. Without a named end, the building ends in its last phase's room farthest from the last key.

The doors of every building keep every room reachable and every gate lock seen before its key. They keep the key reachable inside its phase, leave no unlocked door between phases and keep the goal reachable after every state change. Buildings that can't carry their gate are laid out ungated.

## Layout plan

`PlanWriter.Write` turns the building and its mission into a `LayoutPlan` of `PlanRegion`s joined by `PlanLink`s on the unit grid.

- **Stations.** The rooms on the main path from the start to the end, in the order the player reaches them. Locked links set the gate, and the rooms of the keys are the key rooms.
- **Closets.** Closets become regions behind their host's wall, linked by a `SecretWall` or `SpawnWall`.
- **Openings.** Locked links are doors, open links arches; jammed doors stand only on the building's jammed walls.
- **Walk.** Each region carries its order on the critical walk, and the links that every walk from the start to the end crosses are chokes (`MissionWriter`).
- **Doorways.** `AlignLinks` moves each doorway off wall junctions and `SpaceDoors` keeps basement doors 160 px apart.

## Fights

`FightPlanner` plans the level's fights on the layout plan as `PlannedFight`s. On a lab only the rooms of the second wing hold fights, triggers and dynamite sites, and the hub stays empty.

- **How many.** The level holds the fitted number of fights for its floor. Each phase gets fights in proportion to its floor and spreads them evenly along its walk. Rooms without a fight stay empty.
- **Phases.** Each phase gets hit points in proportion to its rooms' floor, weighted down by the brief's spawned share when none of its rooms can hold an ambush. One fight in each phase is its climax. It gets the fitted share of the phase's hit points and comes at a point of the phase drawn from where the originals' climaxes come. Its room is the one nearest that point among the rooms that can hold an ambush, if the phase has any. The other fights split the rest by floor.
- **Monsters.** Each fight draws one monster family. At the style's measured share a fight holds one variant of it; otherwise its variants mix, none over the style's measured cap. Families too strong for a fight are kept for the next one.
- **Ambushes.** Fights in a room that every walk to the end enters through one door can be ambushes. Climaxes come first, and ambushes stop once the brief's spawned share is used. The trigger covers that door. Rooms entered through the door into the dark phase get no ambush, and when two neighboring fights have doors close together, only one of them becomes an ambush. Spawners stand in the room's spawn closet if it has one. Otherwise they stand in the fight's room or a later room, off the 1280 by 720 screen around the trigger, spread apart and within reach of it.
- **New kinds.** Each new monster kind shows up first in the fight nearest a rank drawn from the style's measured opening ranks (the level's smallest fight 0, its biggest 1), and the fight is an ambush at the style's measured share where the room allows one. The campaign's first monster level holds placed fights only.

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
2. `SceneFurnisher.Furnish` furnishes each room from the furniture grammar, a set of C# tables. For each room type they hold its arrangements with their shares, the counts per area and the modules per run, the type's signature family and whether its far walls stay covered. For the families they hold the pieces, wall distances and steps, each piece's companions by room type and direction, and how close two families stand. Rooms that hold a set piece are furnished first, and each room is furnished in this order:
   - Set pieces stand first with their whole module ([Set pieces](#set-pieces)).
   - The signature follows with its full module (a desk with its computer and chair, a lab bench with its labware).
   - Each arrangement is held by its share and placed as a wall run on far or near walls, a grid on open floor in rooms large enough, a heap of stock, or a lone piece on open floor. In a heap, each piece stands at a measured offset for its two families, and the heap stands by a wall at its family's measured wall share for the room size.
   - The far, grid and heap arrangements grow until the room holds the piece count drawn for its type or none of them lands another piece.
   - Far wall stretches left bare are then filled.

   Pieces take the measured direction for the wall of their cell. Walked-over pieces (shadows, blood, stripes, pads) take no spacing in the occupancy.
3. After its furniture, a room hangs its wall fixtures and lays its victims by room type. A corpse lies beside the furniture it belongs with, or seated against a far wall or a container, on its own blood and away from near walls and from behind pieces. Gibs lie where the blood of victims runs together, and rubble levels (a quarter of basement levels) strew rock piles. Chairs and tables carry paper.
4. `PlacementContext` lays shadows under pickups at a count per kind.

### Set pieces

- Breaker: 2 to 4 transformers, a pointer and a corpse in blood. Cannon: a stripe box, crates and a pointer. Generators: 0.7 cells off a far wall with transformers, a shoot-here marker and a corpse. Missile racks: three missiles with a stripe box.
- Concrete sites: pads between the site and the walls, one by a wall, one in a corner, a line of three from a far corner or two straddling the site. Each site takes the middle of an outer side, shifted 0.6 cells toward its far end to look centered, or a corner; in a long room only its ends.
- Several sites in a room pair up symmetrically, on both corners of one side or the middles of facing sides. Sites with no spot move to the planner's next room.

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

- **Sites.** `PlanSites` puts the sites in rooms near positions drawn along the walk, before the walls are built, and concrete sites only in rooms of 96 cells or more. `StandBasementSites` stands each basement site in front of a monster hole with as much floor below it on screen as the originals keep below a site, and puts a spawner behind the hole that builds endless generators once the site's phase opens. A site that has no such hole takes the cell nearest its room's middle that keeps that floor, and otherwise moves to the next room planned for it. `StackEndlessGenerators` stacks 2 to 4 endless generators on a pad by each concrete site. Everything the blast must kill stands within its 100 px.
- **Mounted cannon.** `PlaceCannon` stands it at the entry end of a large late station, with a kill zone at the far end.
- **Gate.** The breaker (418) on locked-door and blackout levels; a blackout's trigger is one point in the trigger room, well inside its door.
- **Bosses.** The brief's bosses, in the largest late dead-end room, else the last station.
- **Charges.** The charges of each phase's sites lie together, 588 px or more from every site. They go beside the last key room of the phase before. When no key comes before the phase, they go in the start room on the fitted share of levels for the first phase. Otherwise they go on the walk to the phase's first site, the fitted distance ahead of it, then in the start room, then on the free cells nearest the start before that site.
- **Fights.** `EncounterPlacer` places the planned fights. Standing groups stand in clusters on visible floor and spill into the nearest rooms of their phase. Spawn closets blow their wall open, and only they may be seen spawning. Ambushes with nowhere out of sight to stand become standing groups.
- **Pickups.** `PickupPlacer` lays the budget's pickups. New weapons lie off the walks to the objectives or in a secret closet, with one of their ammunition pickups earlier on their walk. Secret closets and some spawn closets then take their stashes, crates take each category's share, and the rest lie in small piles in rooms drawn by floor, rooms off the walks weighted by the fitted density.
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

`LevelScriptWriter.Format` writes the `.lgc` with the donor's music, `InitGame` and `DoorsIsOpen`, a blackout trigger (respawn there, doors locked, lights off, keypads swapped), a checkpoint trigger, and an exit trigger that wins the level. Locked-doors levels start dark (ambient `C0C0C0`), and every other indoor level starts lit.

## Read back

`LevelGenerator.Generate` builds each layout in one pass, writes it, parses it back and runs `RouteAnalyzer.Analyze`. Steps that can't meet their guarantee draw themselves again. When the tour still misses an objective, forces a gate (opens a locked door or a fence without its key) or needs a secret wall (goes through a breakable wall, a spawn wall or a monster hole) outside level 1, `Generate` takes the next layout, up to 8, and otherwise keeps the one with the fewest unreached objectives, then forced gates, then secret walls. Layout `k`'s seed is `seed × 100000 + level × 100 + k`.

`LevelGeneratorIntegrationTests` checks the guarantees over seeds 1 to 10 of the campaign and the add-ons. Likeness measures (revisits, gated share, key walks, blocked floor) aren't guaranteed; `ascli inspect design` writes them beside the originals' in `design.tsv`.

## Level 1

`OutdoorLevelBuilder` builds level 1 on a map the size of the original it follows:

- **Campaign.** The fenced outdoor level has a shut gate by the start, one or two small buildings, the exit behind a breakable wall, storage clusters, lamps and money. It doesn't have monsters.
- **Compound (add-on 1).** The same outdoor level with a dynamite site at the exit, a charge outside, a shotgun, a cluster of monsters and two spawners at the gate.
- **City (add-on 2).** Roofed blocks and streets form a grid, with spawners along them and bats in the far corner. Killing every monster wins.

`LayGround` lays the original's ground rows with grass clumps and tufts. Level 1 gets no roof.

## Add-on style and installation

In add-on style the campaign model is fitted to both add-ons and plans 5 levels; levels 2 to 5 are written over add-on 1's map of the same number. `RandomAddOnInstaller.Install` writes the levels to `Maps/RANDOM` with their briefings, money table, script branches and menu button ([README](../README.md#the-random-add-on)).

## Debugging

- `ascli preview --game <folder> --seed <s> --level <n> --out <file.map>` builds one level as `generate` does.
- `ascli analyze --levels <folder>` draws the tour with objectives, gates and triggers.
- `ascli inspect design --seeds 10` measures decor, pickups, pacing, room graph, phases, beats, architecture and novelty of the originals and of generated levels, and writes them side by side to `design.md`, with `campaign.md`, `provenance.md` and `layout.md` beside it.
- Content placement reads distance from the start (`PlacementContext.GetProgress`), not the tour progress `p` the route analysis reads.
- Steps that place nothing usually found their cells taken (`Taken`) or reserved by the keep-clear cells.
- Output that changes for a seed after an unrelated edit comes from the random streams ([Randomness](#randomness)).
