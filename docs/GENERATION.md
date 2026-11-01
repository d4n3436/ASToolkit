# Level generation

This document describes how the original levels are built and how `LevelGenerator` builds a level, stage by stage, with the types and methods that do each step. It is meant as a map for finding where a level property comes from and which code to change when a level goes wrong.

The guarantees every level keeps are in [One pass and guarantees](#one-pass-and-guarantees), the engine and level facts the generator follows are in [NOTES.md](NOTES.md), and the file layouts it writes are in [FORMATS.md](FORMATS.md). All code lives under `src/ASToolkit.Core/Generation/` unless a path says otherwise.

## Contents

1. [Original levels](#original-levels)
2. [Overview](#overview)
3. [Reference data](#reference-data)
4. [Coordinates](#coordinates)
5. [Target and budget](#target-and-budget)
6. [Layout plan](#layout-plan)
7. [Fine layout and structure](#fine-layout-and-structure)
8. [Breaker spot](#breaker-spot)
9. [Dressing](#dressing)
10. [Content](#content)
11. [Script](#script)
12. [One pass and guarantees](#one-pass-and-guarantees)
13. [Roof](#roof)
14. [Level 1](#level-1-1)
15. [Add-on style and installation](#add-on-style-and-installation)
16. [Debugging](#debugging)

## Original levels

This section describes the layout, fights, loot and pacing of the original levels, read through the level survey (`LevelSurveyor`), the design report (`ascli inspect design`) and route images (`ascli analyze`). Places (`P`), encounters and landmarks are the ones the survey lists. **p** is the tour progress, the share of the route analysis's tour walked before a point, and areas are in square map pixels.

### Measures

| level | style | tour px | places | loops | revisit | start area | first fight p | hit points by fifth of the tour (%) |
|---|---|---|---|---|---|---|---|---|
| 2 | locked lab | 3,345 | 13 | 1 | 0.02 | 80,640 | 0.88 | 0 / 0 / 0 / 0 / 100 |
| 3 | locked lab | 11,792 | 50 | 6 | 0.04 | 80,640 | 0.41 | 0 / 0 / 20 / 60 / 20 |
| 4 | blackout lab | 19,799 | 36 | 2 | 0.15 | 117,600 | 0.07 | 30 / 66 / 0 / 1 / 2 |
| 5 | ungated lab | 22,484 | 36 | 1 | 0.01 | 62,720 | 0.04 | 14 / 8 / 7 / 65 / 5 |
| 6 | military | 12,949 | 12 | 1 | 0.07 | 103,680 | 0.05 | 32 / 49 / 0 / 14 / 5 |
| 7 | military | 26,698 | 17 | 0 | 0.14 | 86,400 | 0.06 | 33 / 37 / 6 / 0 / 24 |
| 8 | military | 22,639 | 7 | 0 | 0.11 | 866,160 | 0.02 | 27 / 48 / 9 / 9 / 8 |
| 9 | military | 43,843 | 7 | 0 | 0.14 | 972,000 | 0.02 | 54 / 44 / 2 / 0 / 0 |
| 10 | military | 20,789 | 28 | 0 | 0.04 | 103,680 | 0.40 | 0 / 100 / 0 / 0 / 0 |
| add-on 1 level 2 | blackout lab | 34,886 | 36 | 3 | 0.09 | 474,320 | 0.00 | 27 / 37 / 20 / 11 / 5 |
| add-on 1 level 3 | ungated lab | 25,707 | 23 | 0 | 0.05 | 2,236,640 | 0.00 | 65 / 12 / 2 / 16 / 4 |
| add-on 1 level 4 | ungated lab | 18,603 | 12 | 1 | 0.02 | 485,520 | 0.00 | 77 / 9 / 4 / 7 / 3 |
| add-on 1 level 5 | ungated military | 22,004 | 5 | 0 | 0.03 | 1,000,080 | 0.00 | 72 / 1 / 19 / 1 / 6 |
| add-on 2 level 2 | ungated lab | 20,019 | 18 | 0 | 0.08 | 491,120 | 0.00 | 99 / 0 / 0 / 0 / 1 |
| add-on 2 level 3 | ungated lab | 6,323 | 23 | 4 | 0.02 | 5,246,080 | 0.24 | 0 / 84 / 4 / 4 / 8 |
| add-on 2 level 4 | fenced military | 28,189 | 8 | 0 | 0.07 | 339,120 | 0.00 | 46 / 34 / 16 / 4 / 0 |
| add-on 2 level 5 | ungated lab | 33,715 | 18 | 2 | 0.05 | 7,963,200 | 0.01 | 53 / 5 / 26 / 0 / 16 |

The start area is the area of the place holding the player start. The three level 1 maps are described on their own below.

### Level 1

Each campaign has a level 1 of its own, on an outdoor map with a different win.

| map | size | ground | win | contents |
|---|---|---|---|---|
| campaign | 2,000 by 2,000 | grass tiles 505, 506 and 507 | the player comes within 70 px of (1550, 1295) | no monsters and no spawners; 19 crates, 4 money, 8 street lamps, two walled buildings and a chain-link fence (660) |
| add-on 1 | 2,000 by 2,000 | grass tiles around lab buildings on 500 floor | the player comes within 40 px of (120, 320) | 55 placed monsters (50 and 53), 2 spawners, 3 spawn walls, 1 charge, 1 site, a shotgun and 13 doors |
| add-on 2 | 4,000 by 3,000 | city streets on 503 tiles between buildings under 5,499 earth tiles | every monster dead (`CompaignGameTact`) | 16 spawners, 42 bats (880) in one corner, 2 shotguns, a grenade launcher and two implants |

- The campaign level is a walk. The player starts at (145, 1908) on a dirt road that ends at the fence gate (661), which stays shut. The way in is the torn piece (662) on the far side of the compound. The exit lies inside the small building, 46 px from a breakable wall (625), and its door (617 at z 0) stays shut. The tour runs 4,248 px around the fence and through the wall. Four money pickups lie in a row by the first building.
- On add-on 1 the tour enters the compound through its torn piece next to the shut gate and runs 7,983 px. The exit is 70 px from the only dynamite site and 36 px from a spawn wall (618). The charge lies 1,235 px from the site at (1404, 499), and the shotgun lies at (407, 1201). Most placed monsters stand around (1300, 800), in the middle of the compound.
- Add-on 2 starts the player in the top left corner of a city block, with spawners spread over the streets and buildings that open by doors. Its tour of 16,406 px follows the streets to every monster group.
- The ground is a grid of tiles over the whole map, 505 on the campaign and add-on 1 (132 px apart in a row, rows 47 px apart and shifted by 66 px in turn) and 501 on add-on 2 (80 px apart in a row, rows 28 px apart and shifted by 40 px). Alone it reads as dirt. Clumps of 506 lie over it wherever the ground is grass, and tufts of 507 line the edge where the grass meets the dirt. On the campaign the grass covers everything but the yard inside the fence, the road from the start to the gate and the strip in front of the buildings.
- The campaign's street lamps (663) stand on the fence posts halfway between two pieces and face into the compound, 64 on the upper `\` side, 192 on the lower `\` side and 128 on the lower `/` side. Its torn piece lies on a `/` side with facing 0, and the two gate pieces lie on a `\` side with facings 0 and 128.
- The campaign's crates (419), containers (400, some stacked at z 60), barrels (401), trucks (440) and containers 442 stand in clusters of 2 to 6 against the fence and the building walls. Three gas cylinders (402) lean on the breakable wall and two barrels stand just outside it.

### Locked labs (levels 2 and 3)

- **Level 2.** The start room (P1) opens on a hall (P2) with two locked doors in view. The breaker room (P3) opens off the hall at p 0.46, with 68 blood decals, 2 transformers and a corpse. Behind the locked doors two rooms hold all 56 monsters, 14 in a line (P5) and 42 in a grid (P6). The first fight is at p 0.88. Five health pickups lie ahead of the first group, in its room and the one before, and the last room holds crates of money. The only shotgun lies in a secret closet off a locked storage room that the tour does not need.
- **Level 3.** The start hall (P2) shows 4 locked keypads and a breakable wall. The breaker room (P6, p 0.24) also holds the level's only charge. Behind the gate the tour runs through 14 rooms with 6 loops, and the fights peak in the fourth fifth with 48 placed monsters in the furnished room P12, which also holds 19 pickups and the minigun. The boss 850 stands in a dead-end lab (P16, p 0.73) with 12 escorts and the dynamite site, whose hole hides 4 endless generators. The breaker also opens 13 monster-free office and storage rooms in three branches off the start hall. 73% of the level's pickups lie off the tour, in these rooms and in the secret closets. Three spawn closets release streams of 10 to 13 monsters, and 9 secret closets hold money, ammo, the grenade launcher, an implant and an extra life.

### Blackout labs (level 4 and add-on 1 level 2)

- **Level 4.** The blackout trigger and the checkpoint lie in P3 at p 0.07 to 0.08. The next room (P4) holds the grenade launcher in a crate, and the one after it (P5) holds 188 placed monsters with 1,886 hit points, the largest placed group of the lab levels. The breaker room (P11, p 0.41) has 4 transformers, and coming within 100 px of it sets off 107 monsters from 7 spawners. The three charges lie in a dead end next to it (P12, p 0.47). The three sites lie at p 0.57, 0.69 and 0.94, back across the level. The tour re-walks 15% of itself, the most of any original. Ten office and storage rooms off the first hall hold loot and no monsters.
- **Add-on 1 level 2.** The trigger is 69 px from the start. The breaker (p 0.15) sets off 3 ambushes. 63% of the floor lies behind 34 locked doors, and four sites follow at p 0.45 to 1.0.

### Ungated lab (level 5)

The start room is the smallest of all originals (62,720). The first ambush comes at p 0.04 with 75 monsters from 2 spawn closets. The first half is office rooms with placed groups of monsters 50, 53 and 56. The second half is large lab rooms of 300,000 to 770,000 where every fight is a slug ambush (40, 43 and 46) from side rooms and closets, from p 0.39 on. The checkpoint (p 0.48) comes after the first of them, and the peak is in the fourth fifth. The boss 860 stands at p 0.81 in a 770,000 dead-end room with four yellow slugs. Eleven secret closets hold money with a perk, the rocket launcher and the minigun.

### Fenced military (levels 6 to 10)

- **Level 6.** A small start room opens on a 1,416,960 yard (P2), where two ambushes of about 100 monsters come at p 0.05 and 0.07. The generator room (P5, p 0.29) ends a chain of three rooms, and a 108-monster ambush triggers in the room before it. The fences open onto the last office (P10), where 3 red weapon monsters guard 10 money.
- **Level 7.** The generator is at p 0.10, and a 119-monster ambush triggers in the room before it. 67% of the base lies behind the fences. The largest hall (P10) holds the cannon (p 0.51), 3 pads, 47 placed monsters and the site at p 1.0. The charge lies in a far dead end (P13, p 0.77), and the tour walks back to the site, re-walking 14% of itself.
- **Level 8.** The start hall is itself a site, and three pad ambushes come at p 0.02 to 0.05. The checkpoint (p 0.19) stands before the generator room, whose two generators (p 0.21 and 0.22) share it with three charges. The fenced yard P5 (2,257,920) holds the cannon and a 135-monster pad ambush with 85,300 hit points, and the boss 867 waits in a dead-end hall beside a site and 3 pads (p 0.59). Six sites run from p 0.39 to 1.0.
- **Level 9.** The start yard holds 110 placed monsters, most of them weapon monsters, and 6 charges, and four waves of 254 to 360 monsters come by p 0.26. 98% of the hit points lie in the first two fifths. The nine sites lie in three places (P1, P2 and P7) from p 0.59 to 1.0, with endless generators behind their pads, and the tour of 43,843 px is the longest of all originals.
- **Level 10.** No monster appears until p 0.40. The first generator (p 0.13) drops 21 fences, and the central arena (P6, 4,534,560) holds 20 large health pickups, 23 boxes of magma minigun ammo and 7 implants and perks. The checkpoint (p 0.31) is in the arena. The second generator stands in a dead-end hall (P7, p 0.42), and entering the hall sets off one ambush of 1,408 monsters from 25 spawners in 8 fenced pens around the arena, which is 99.9% of the level's hit points and brings the boss 866. Two small lairs of placed monsters end the level. A gory secret lab (539 blood decals) lies off the start hall.

### Add-on levels

- Every add-on level except add-on 2 level 3 has its first fight by p 0.01, and six of the eight have the largest share of their hit points in the first fifth.
- Their start areas are large halls and yards of 339,120 to 7,963,200. The campaign labs and levels 6, 7 and 10 start in rooms of 62,720 to 117,600.
- Add-on 2 level 2 starts in an office of 491,120, and six ambushes start at p 0.00 to 0.002, the largest with 763 monsters from 12 spawners in side rooms. A seventh of 30 bats comes at p 0.88. Every monster of the level comes from ambushes, and the rest of the tour collects money and ammo from office rooms and 6 secret closets.
- Add-on 2 level 4 is a fenced military level with a blackout trigger at p 0.03. A 1,393-monster ambush starts at the start room, four generators share one room at p 0.18, and a breaker at p 0.84 opens nothing. The cannon stands in the fourth largest place on the tour.
- Add-on 1 level 3 starts in a 2,236,640 hub hall that opens by doors onto four large rooms, and three ambushes with 37,482 hit points start at p 0.00. Each of its sites has its own charge.
- Add-on 1 level 4 is a ring of 12 storage rooms joined by openings, with 9 sites in order around it and the cannon in the largest room.
- Add-on 1 level 5 starts in a yard where three pad ambushes with 215,609 hit points start at p 0.00.
- Add-on 2 levels 3 and 5 are a single yard of 5,246,080 and 7,963,200 with buildings around it, each joined by doors. Level 5 ends with the diver boss 890 in a dead-end storage room.

### Room graph

Measured on the ten lab originals (campaign levels 2 to 5, add-on 1 levels 2 to 4 and add-on 2 levels 2, 3 and 5) from the survey's places and connections. Rooms and halls are the nodes, and doorways, doors and openings of every kind are the edges. Closets, secret walls, spawn walls and monster holes are left out. The main body is every room on some path from the start to the end of the tour, and a branch is a group of rooms joined to the main body through one room. Cycles count the independent cycles of the door graph, including loops the tour does not walk. An objective's depth is the number of rooms between its room and the main body. A room is square when its length is within 15% of its width, and irregular when it fills under 80% of its bounding box.

| level | rooms | under 70,000 | cycles | main body | hub doors, rooms from the start | branches of 2+ rooms, largest, deepest | loot rooms | objective depths | square | no notch, small, mid, large | notches open outside |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 2 | 11 | 4 | 1 | 65% | 6, 1 | 2, 2, 2 | 5 | 1 | 45% | 60, 0, 0% | 73% |
| 3 | 37 | 13 | 8 | 60% | 7, 1 | 3, 9, 6 | 9 | 1, 1, 3, 3 | 16% | 65, 29%, none | 31% |
| 4 | 28 | 8 | 6 | 76% | 6, 1 | 2, 7, 6 | 13 | 0, 0, 1, 1, 1 | 25% | 53, 17, 0% | 42% |
| 5 | 22 | 7 | 2 | 61% | 5, 2 | 2, 6, 6 | 1 | 1 | 14% | 100, 12, 0% | 74% |
| add-on 1 level 2 | 27 | 2 | 3 | 69% | 5, 2 | 2, 7, 5 | 3 | 0 (5), 2, 4, 5 | 0% | 88, 57, 43% | 40% |
| add-on 1 level 3 | 8 | 1 | 0 | 58% | 4, 0 | 2, 3, 2 | 0 | 0 (3), 1 (3), 2 | 12% | 100, 100, 0% | 100% |
| add-on 1 level 4 | 12 | 0 | 1 | 100% | 2, 0 | none | 0 | 0 (14) | 33% | none, 67, 20% | 50% |
| add-on 2 level 2 | 11 | 0 | 0 | 80% | 5, 1 | 1, 2, 2 | 5 | none | 18% | none, 33, 50% | 82% |
| add-on 2 level 3 | 19 | 11 | 4 | 71% | 8, 0 | 4, 9, 9 | 13 | none | 11% | 100, 50, 0% | 100% |
| add-on 2 level 5 | 16 | 8 | 2 | 72% | 5, 0 | 2, 6, 4 | 2 | 0, 0 | 12% | 100, 100, 50% | 83% |

- Every level but the ring of add-on 1 level 4 has a hub of 4 to 8 doors at most two rooms from the start. The branches of two or more rooms hang off the hub on 8 of the 9 levels, and add-on 1 level 2 hangs its two from rooms farther on.
- The main body holds 58 to 80% of the floor, and no branch holds more than 20%. It runs through the large rooms, a median of 144 units against 36 in the branches, and 11 of the 17 halls lie on it. The cycles lie in the main body, apart from one in level 3's key branch and two in a branch of add-on 2 level 5.
- Branches are trees of up to 9 rooms and 9 rooms deep that split up to 4 ways. The loot branches (the monster-free branches of the patterns below) add up to 0 to 13 rooms a level, entered through locked or jammed doors on levels 3 and 4.
- The campaign keeps objectives in the main body or one room off it. Level 3's key branch is the exception, with the breaker and the charge 3 rooms in. Add-on 1 level 2 puts its breaker and its charges and site at the ends of branches 2 to 5 rooms deep.
- The gate splits the level into two sections. Once the tour enters the second, it never walks back into a room of the first on levels 2, 3 and 4, and re-enters one room (6% of the rest of the tour) on add-on 1 level 2. On level 3 and add-on 1 level 2 the tour walks back out of the key branch (4 and 5 rooms) before the second section starts. Level 4's blackout search runs through the main body (7 of its 8 rooms), and the locked labs keep the key in a branch off the hub.
- Ambushes on levels 2 to 4 spawn only into rooms the tour walks after the trigger, as level 4's breaker trap does into P9. Level 5 spawns two small ones (10% of its ambush hit points) into rooms the tour has left for good. Every triggered spawner stands in a closet, on the main body or one room off it, apart from two 2 rooms off on add-on 2 level 5. Their monsters stop a median 17% of the way to the trigger (quartiles 9 and 31%).
- A notch is a part of a room's bounding box outside the room at least 2 cells across and 4% of the box. Narrower jogs are wall slivers. Notches sit at corners (143 of 149) and take 6 to 23% of the box (deciles 1 and 9), with a short side of 2 to 11 units.
- Shape follows size. Over the ten levels, rooms under 100,000 px² have no notch 83% of the time, rooms of 100,000 to 400,000 px² 42% and larger rooms 26%, and 63% of the large rooms have two or more notches. Room areas run from 15 to 298 units (deciles 1 and 9).
- 60% of the notches open outside where the building's outline steps, 23% hold a neighboring room, 13% a solid block and 3% a closet.

### Rooms

Every room of every original has a role and a type, read by `RoomAtlas` (`src/ASToolkit.Core/Rooms/`) from the survey and the sprites. `ascli inspect rooms` writes a cropped image of each room with its role and type, sheets of them per level, `rooms.tsv` and the tables below.

- **Role** is where the room stands on the door graph (`RoomRoleMap`). The roles are start, hub (the first hall on the way to the end), neck (the room after the hub), other halls of the main body, rooms on the sides of the hub (hub row) and of the second hall (hall row), other main body rooms, and branch roots and inner branch rooms. The key room of a gate, when it stands alone off the main body, is its own role. Closets and nooks, rooms behind breakable walls and ground outside the walls are left out of the tables.
- **Type** is what the room is furnished as. The classifier scores each room by its defining group. A desk (a table with a chair within 70 px or a computer within 40 px) scores 4, plus one for each cabinet in a room with desks. Lab tubes, flasks and equipment score 1 each, big computers and 443 server banks 2, transformers and panels 2, containers, trucks and missiles 2, and shelves, cabinets, boxes and cylinders 1. Crates and barrels count as stock, toward depot when the room holds a container and toward storage when not. A room with fewer than 3 pieces is a passage when it has 2 doors or more and bare when not. Halls and corridors are halls.
- **Read by eye.** 35 rooms the classifier cannot type from their pieces are typed in `RoomAtlas.Corrections`, each keyed by a point inside the room. They are split rooms (a walkway with an office, lab or depot side, add-on 1 levels 2 and 3), room-shaped halls (level 5's P12, level 10's corridor maze), level 10's arena bays, add-on 2 level 3's gym and washrooms, and a handful of misreads.
- **Setting.** Add-on 1 level 4 and add-on 2 level 5 are built from lab walls on military floor. The survey keeps their theme as lab (the walls and grid are the lab ones) and gives them a military setting, and segments them as open plan like the military levels.

| type | rooms | area (k px², min / median / max) | fill | where |
|---|---|---|---|---|
| passage | 37 | 26 / 51 / 334 | 0.96 | branch roots, necks, level 10's airlocks |
| bare | 7 | 35 / 86 / 620 | 0.95 | four start rooms and three one-door branch roots |
| hall | 30 | 120 / 468 / 5,079 | 0.78 | hubs and halls of the main body |
| office | 64 | 24 / 145 / 1,839 | 0.86 | branches and the main body of labs |
| lab | 8 | 80 / 239 / 1,346 | 0.94 | branches and the second hall's rows |
| computer room | 8 | 172 / 506 / 1,656 | 0.87 | level 5's pods, military key rooms |
| machine room | 5 | 35 / 91 / 103 | 0.91 | key rooms (the breaker) and small branch rooms |
| storage | 27 | 33 / 103 / 470 | 0.92 | small branch rooms |
| depot | 66 | 35 / 465 / 2,880 | 0.83 | the main body of level 4, military levels and the military-setting add-ons |
| quarters | 2 | 591 | 1.00 | add-on 2 level 3 only |
| arena | 10 | 305 / 408 / 599 | 0.86 | level 10 only |

Lab rooms by role and type (campaign levels 2 to 5, add-on 1 levels 2 and 3, add-on 2 levels 2 and 3):

| role | passage | bare | hall | office | lab | computer | machine | storage | depot | quarters |
|---|---|---|---|---|---|---|---|---|---|---|
| start | 0 | 2 | 3 | 2 | 0 | 0 | 0 | 0 | 1 | 0 |
| hub | 0 | 0 | 5 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| neck | 2 | 0 | 3 | 1 | 0 | 0 | 0 | 0 | 1 | 0 |
| hall | 0 | 0 | 2 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| hub row | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 1 | 0 |
| hall row | 3 | 0 | 0 | 2 | 1 | 2 | 0 | 1 | 0 | 0 |
| body | 7 | 0 | 0 | 10 | 1 | 1 | 0 | 3 | 7 | 0 |
| branch root | 5 | 1 | 3 | 12 | 2 | 1 | 0 | 6 | 1 | 0 |
| branch | 4 | 0 | 1 | 29 | 2 | 1 | 1 | 9 | 4 | 2 |
| key | 0 | 0 | 0 | 0 | 0 | 0 | 2 | 0 | 0 | 0 |

| lab level | passage | bare | hall | office | lab | computer | machine | storage | depot | quarters |
|---|---|---|---|---|---|---|---|---|---|---|
| 2 | 1 | 0 | 1 | 5 | 0 | 0 | 1 | 2 | 1 | 0 |
| 3 | 9 | 1 | 4 | 12 | 3 | 0 | 1 | 4 | 2 | 0 |
| 4 | 3 | 1 | 1 | 7 | 0 | 0 | 1 | 7 | 8 | 0 |
| 5 | 6 | 1 | 3 | 5 | 0 | 4 | 0 | 1 | 1 | 0 |
| add-on 1 level 2 | 0 | 0 | 3 | 14 | 3 | 0 | 0 | 3 | 3 | 0 |
| add-on 1 level 3 | 1 | 0 | 2 | 5 | 0 | 0 | 0 | 0 | 0 | 0 |
| add-on 2 level 2 | 0 | 0 | 2 | 9 | 0 | 0 | 0 | 0 | 0 | 0 |
| add-on 2 level 3 | 1 | 0 | 1 | 0 | 1 | 1 | 0 | 2 | 0 | 2 |

| military level | passage | bare | hall | office | lab | computer | machine | storage | depot | arena |
|---|---|---|---|---|---|---|---|---|---|---|
| 6 | 2 | 0 | 1 | 2 | 0 | 1 | 1 | 3 | 2 | 0 |
| 7 | 2 | 2 | 2 | 4 | 1 | 0 | 0 | 3 | 3 | 0 |
| 8 | 0 | 0 | 3 | 0 | 0 | 1 | 1 | 0 | 2 | 0 |
| 9 | 0 | 0 | 2 | 0 | 0 | 1 | 0 | 0 | 4 | 0 |
| 10 | 10 | 1 | 4 | 0 | 0 | 0 | 0 | 0 | 1 | 10 |
| add-on 1 level 4 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 1 | 10 | 0 |
| add-on 1 level 5 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 4 | 0 |
| add-on 2 level 4 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 7 | 0 |
| add-on 2 level 5 | 2 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 18 | 0 |

- The only empty rooms are passages (median 51k px²), the bare rooms and level 10's arena bays.
- Offices make up half the lab branch rooms and a third of the main body. Storage rooms are small (median 103k) and stand in branches. Depots are big (median 465k) and fill the main body of level 4 and the military levels.
- The hub and the halls carry hall dressing only. A hall with desks on one side is a split room (add-on 1 level 2's start, P2 and the six-office walkway P5), and its walkway stays bare.
- The breaker stands in a small machine room on campaign levels 2, 3, 4 and 6, and the add-ons put it in an office or a depot.

Two examples of each type, with the room outlined in yellow, walls inside it in orange and its floor tinted:

| type | examples |
|---|---|
| office | ![Level 2 P10 and add-on 2 level 2 P8](images/rooms/office.jpg) |
| lab | ![Level 3 P22 and add-on 1 level 2 P20](images/rooms/lab.jpg) |
| computer room | ![Level 5 P19 and level 8 P7](images/rooms/computer.jpg) |
| machine room | ![Level 4 P11 and level 8 P4](images/rooms/machine.jpg) |
| storage | ![Level 4 P12 and level 7 P17](images/rooms/storage.jpg) |
| depot | ![Level 3 P12 and level 4 P5](images/rooms/depot.jpg) |
| hall | ![Level 5 P3 and level 3 P2](images/rooms/hall.jpg) |
| hall with an office side | ![Add-on 1 level 2 P1 and P5](images/rooms/split.jpg) |
| quarters | ![Add-on 2 level 3 P12 and P18](images/rooms/quarters.jpg) |
| passage | ![Level 5 P9 and P10](images/rooms/passage.jpg) |
| arena | ![Level 10 P9 and P21](images/rooms/arena.jpg) |

### Recipes

A recipe says how the originals furnish a type of room. `PartFinder` groups each room's furniture into parts by the pairs the originals build, a table with its chair (within 70 px), computer (45) or labware (50), cabinets and shelves into bands (60 to 100), crates and boxes into stacks (55), containers into rows (90), server banks in spaced rows (150), a wall computer with its chair (60), barrels and cylinders (45). A part stands against a wall, in a corner (two walls within 2 cells) or in the open. Papers and corpses are scattered and counted per area. `RoomRecipe.Measure` pools the parts of a type's rooms, kept apart for labs and military settings, into each kind of part's rate per 100,000 px² of floor, the share of rooms that hold one, where it stands, how far apart two stand in a room and how often the nearest one stands along a lattice axis (in a row), and its examples with their geometry. A kind held by 75% of the rooms or more is required. `ascli inspect rooms` writes the full tables to `recipes.md`.

The defining parts of the lab types (campaign and add-ons):

| type | rooms | required parts | common parts |
|---|---|---|---|
| office | 57 | desk of table, chair and computer (86%, 57% in the open), cabinets (75%, 63% against a wall, rows of up to 17) | plants (40%), table and chair (40%), wall screens (39%), water cooler (33%), cabinet row with a cooler (21%) |
| lab | 6 | labware (83%) | lab tables with chairs (67%), boxes (50%), shelves (50%), crates (50%) |
| computer room | 5 | server banks (100%, 86% in the open), wall computers (100%), chairs (80%) | machines (40%) |
| machine room | 3 | machines (100%, 62% in corners), shelves (100%) | |
| storage | 19 | shelves (79%, in corners and along walls) | crates (53%), cabinet and shelf bands (53%, all against walls), boxes (37%) |
| depot | 15 | containers (100%, 60% against a wall) | shelves (67%), crates (67%), barrels (60%), container and crate groups (47%) |
| hall | 13 | | wall screens (54%), cabinets, crates and barrels against walls (31% each) |

In military depots (51 rooms) barrels against walls are the one required part (76%), and containers (73%), crates (57%) and barrels (53%) also stand in the open, with forklifts in 31% of them.

### Furniture

| object | stands next to | originals |
|---|---|---|
| chair 407 | a table 404, within 60 px (median 38) | 476 of 510 |
| chair 678 (add-on 2 levels 2 and 4) | a table 404, within 60 px | 21 of 21 |
| big computer 135 on level 5 | a chair, within 200 px | 52 of 52 |
| computer 410 and dead computer 411 | a table, within 60 px | 107 of 107 and 231 of 231 |
| test tubes 426 and lab equipment 427 | a table, within 60 px | 98% of 205 and 99% of 85 |
| scattered paper 408 | a table, within 60 px | 70% of 458 |
| explosion sparks 174 | a big explosion 157, within 60 px | 186 of 186 |

- The table's frame sets the side its chair stands on. Frames 192 and 21 put it to the lower left (71 of 86 and 42 of 45), and frames 0 and 85 to the lower right (56 of 67 and 52 of 62).
- Level 5 has 17 of its 59 chairs away from tables, in front of big computers. Level 6's machine room has 15 big computers and 2 chairs.
- Level 3's three lab rooms, 36 to 47 units, hold 1, 4 and 5 tables. The two larger carry 24 test tubes and lab equipment each, 5 or 6 a table (19 and 5, 12 and 6).
- A furnished room is never smaller than 31,000 px² and always holds at least three pieces.
- Room floors are boxes with corner cuts. Rooms under 18 units have none and 80% of those from 18 to 45 units have none. Rooms from 45 to 180 units have 0 to 4 cuts with 41, 24, 26, 6 and 2% and the larger ones with 24, 15, 44, 13 and 5%. A cut spans a median of 30 to 39% of the box's width and 33 to 42% of its length, and 11% of its area. The box's long side is a median 1.33 to 1.54 times its short side, and fills a median of 100, 87 and 77% of its bounding box by size.

### Arrangements

| arrangement | originals |
|---|---|
| A square of hazard stripes (131, frames 128 and 144) around the mounted cannon, with a median distance of 139 to 178 px | 6 of 6 cannons |
| Open floor around the cannon, with crates beside the square | 6 of 6, crates at 5 |
| Hazard stripes (131, frames 96 and 144, z 40 to 60) across door thresholds and around missile bays | levels 3, 10 |
| Stains on walls (131, z 25 to 63, frames 16 to 112), one every 190 to 300 px along a wall | add-on 1 levels 2 to 5 |
| A dynamite site in the middle of a pad pair | 2 pairs, 3 and 13 px from the middle |
| A single pad 54 to 121 px from its site | every site with one pad |
| A pointer (180) next to the breaker | 5 of 6 breakers |
| Rows of desk units (table, chair, computer) with cabinets and plants along the walls | add-on office rooms |
| A gore scene of flying gibs (151) and heavy blood | level 2 (last room), level 10 (secret lab) |

### Walls, pickups and gore

Measured on campaign levels 2 to 10. A far wall is one of the two upper edges of a cell, the side that faces the camera, and a near wall is one of the two lower edges.

| object | originals |
|---|---|
| containers 400, 441 and 442 against a wall | 47%, 37%, 35% |
| test tubes 426 against a wall | 51% |
| soldier corpses 430 against a wall | 20% |
| monitors 405 and 406 on far walls | 91%, 100% |

- The campaign keeps its desks against walls, computers included. The add-on offices put rows of desks in the middle of rooms.
- The frame of a wall-side object depends on which of the four walls it stands against. Shelves take frame 51 against the upper `\` wall and frame 0 against both `/` walls (76 to 97%). Cabinets take 0 or 42 against the lower `\` wall and 21 against the lower `/` wall (73%), where they use 128, 170 and 0 against the upper `\` wall. Crates, barrels, boxes and containers 442 take any frame (18 to 38%).
- Office and lab decor stays in rooms of its kind. On the campaign no computer, test tube rack or lab equipment stands in a hall, corridor or yard, and 0 to 4% stand in rooms filled mostly with storage.
- Placed monsters face random directions (a median of 12% of a group share one facing).
- No room of an original repeats the decor of another room of the same level.
- Add-on 1 level 5 reuses a room of level 8 (42 of 55 objects at the same offsets) and the stripes around its cannon.

### View and pacing

Measured on campaign levels 2 to 10. "On screen" means within the 1,280 by 800 screen around a point of the tour, neighboring rooms included.

| measure | originals |
|---|---|
| room length over width, median and 90th percentile | 1.6 and 2.7 |
| tour between fights that hold 5% or more of the hit points, quartiles (share of the tour) | 0.03, 0.08, 0.14 |
| walk from the start to a dynamite site, 10th, 50th and 90th percentile | 1,850, 4,883 and 9,829 px |
| monsters first on screen more than 1,000 px of tour before they are reached | 39% |
| breakers first on screen more than 1,000 px before they are reached | 2 of 4 |

- On levels 4, 7, 8 and 9 the tour passes half the sites before it carries the charges and comes back to them later, as with the two sites in level 8's start hall (on screen at p 0.03 and 0.06, planted at 0.87 and 0.93) and three sites of level 9 (on screen at p 0.31, planted at 0.94 to 1.0). Most add-on sites are planted as soon as they come into view.
- A breakable wall (609) shows hairline cracks of its own, and nothing else marks it. Decor near the 33 campaign breakable walls is only slightly richer than near ordinary walls. Wall objects 130 stand near 21% of them against 3% of ordinary walls, barrels near 21% against 3%, and blood pools near 18% against 3%.

### Patterns

| pattern | levels |
|---|---|
| Locked doors the player sees from the start long before the breaker opens them | 2, 3, add-on 1 level 2 |
| Blood and corpses in the rooms before the first fight | 2, 3, 4, 5 |
| A monster-free branch that opens with the gate and holds money and ammo | 2, 3, 4 |
| A weapon or a pile of supplies in or right before the largest placed group | 3 (minigun), 4 (grenade launcher), 10 (arena stockpile) |
| A large ambush triggers at or just before the key | 4 (breaker, 107 monsters), 6 and 7 (room before the generator), 10 (second generator, 1,408 monsters), add-on 1 level 2 (breaker) |
| The checkpoint comes before the encounter with the most hit points | 4, 5, 8, 10 |
| The boss stands in a dead-end room or hall late in the tour, beside a site on levels 3 and 8 | 3, 5, 8, add-on 2 level 5 |
| Charges lie in batches away from the sites, and the tour walks back across the level to plant them | 4, 7, 8, 9 |
| The cannon stands in the largest place on the tour | 7, 8, add-on 1 levels 4 and 5, add-on 2 level 5 |

### Where generated levels differ

`ascli inspect design --seeds 10` measures the decor, pickups, pacing and room graph of campaign levels 2 to 10, add-on levels 2 to 5 and 10 seeds of each generated campaign level, and writes this table to `design.md`. Frames count as rare when the campaign uses them under 5% of the time against that wall side.

| measure | campaign | add-ons | generated (90 levels) |
|---|---|---|---|
| chairs within 60 px of a table | 154 of 178 (87%) | 343 of 353 (97%) | 1,444 of 1,703 (85%) |
| tables with a chair within 60 px | 152 of 172 (88%) | 353 of 411 (86%) | 1,440 of 1,583 (91%) |
| test tubes within 60 px of a table | 71 of 74 (96%) | 130 of 131 (99%) | 395 of 419 (94%) |
| lab equipment within 60 px of a table | 27 of 27 (100%) | 57 of 58 (98%) | 178 of 178 (100%) |
| scattered paper within 60 px of a table | 83 of 92 (90%) | 239 of 366 (65%) | 168 of 965 (17%) |
| computers away from walls | 18 of 122 (15%) | 170 of 209 (81%) | 384 of 1,160 (33%) |
| lab big computers with a chair within 200 px | 52 of 52 (100%) | 19 of 31 (61%) | 210 of 378 (56%) |
| corpses on blood (40 px) | 155 of 164 (95%) | 235 of 251 (94%) | 1,349 of 1,349 (100%) |
| office and lab decor in passages | 0 of 322 (0%) | 186 of 764 (24%) | 65 of 2,722 (2%) |
| office and lab decor in storage rooms | 10 of 322 (3%) | 35 of 764 (5%) | 118 of 2,722 (4%) |
| Cabinet against far walls only | 173 of 207 (84%) | 442 of 588 (75%) | 1,347 of 1,914 (70%) |
| Cabinet against near walls only | 15 of 207 (7%) | 14 of 588 (2%) | 282 of 1,914 (15%) |
| Table against far walls only | 106 of 163 (65%) | 49 of 410 (12%) | 800 of 1,583 (51%) |
| Table against near walls only | 20 of 163 (12%) | 20 of 410 (5%) | 187 of 1,583 (12%) |
| Shelves against far walls only | 125 of 214 (58%) | 89 of 159 (56%) | 890 of 2,153 (41%) |
| Shelves against near walls only | 56 of 214 (26%) | 9 of 159 (6%) | 750 of 2,153 (35%) |
| BigComputer against far walls only | 49 of 71 (69%) | 23 of 31 (74%) | 418 of 584 (72%) |
| BigComputer against near walls only | 3 of 71 (4%) | 1 of 31 (3%) | 38 of 584 (7%) |
| Cabinet in a frame used under 5% against its wall | 6 of 171 (4%) | 117 of 430 (27%) | 93 of 1,506 (6%) |
| Table in a frame used under 5% against its wall | 0 of 125 (0%) | 21 of 68 (31%) | 6 of 980 (1%) |
| Container in a frame used under 5% against its wall | 0 of 32 (0%) | 4 of 15 (27%) | 13 of 293 (4%) |
| Shelves in a frame used under 5% against its wall | 3 of 176 (2%) | 24 of 94 (26%) | 33 of 1,634 (2%) |
| loose money against a wall | 87 of 100 (87%) | 99 of 226 (44%) | 1,128 of 1,510 (75%) |
| loose money in a corner | 43 of 100 (43%) | 34 of 226 (15%) | 652 of 1,510 (43%) |
| loose health against a wall | 84 of 128 (66%) | 51 of 156 (33%) | 664 of 1,293 (51%) |
| loose ammo against a wall | 88 of 164 (54%) | 128 of 287 (45%) | 1,555 of 2,143 (73%) |
| loose armor against a wall | 9 of 19 (47%) | 5 of 18 (28%) | 158 of 225 (70%) |
| pickup piles of two or more (45 px) | 120 of 217 (55%) | 111 of 544 (20%) | 1,224 of 3,077 (40%) |
| health placed after a fight | 70 of 163 (43%) | 93 of 174 (53%) | 758 of 1,573 (48%) |
| ammo placed before a fight | 68 of 257 (26%) | 107 of 361 (30%) | 856 of 3,028 (28%) |
| loose money over 200 px of walk off the route | 91 of 119 (76%) | 172 of 226 (76%) | 1,015 of 1,510 (67%) |
| health in crates | 35 of 163 (21%) | 18 of 174 (10%) | 280 of 1,573 (18%) |
| ammo in crates | 93 of 257 (36%) | 74 of 361 (20%) | 885 of 3,028 (29%) |
| armor in crates | 18 of 39 (46%) | 12 of 30 (40%) | 163 of 388 (42%) |
| loose pickups per pile (45 px), median of levels | 2.174 | 1.475 | 2.077 |
| solid floor pieces within 30 px of a doorway | 6 of 1,155 (1%) | 2 of 2,506 (0%) | 105 of 10,783 (1%) |
| hittable floor pieces within 30 px of a doorway | 2 of 965 (0%) | 2 of 1,922 (0%) | 166 of 9,767 (2%) |
| monitors within 40 px of a doorway | 1 of 46 (2%) | 9 of 189 (5%) | 82 of 492 (17%) |
| solid or hittable floor pieces standing inside another | 0 of 2,120 (0%) | 40 of 4,428 (1%) | 26 of 20,550 (0%) |
| loose pickups under a solid or hittable piece | 1 of 469 (0%) | 2 of 750 (0%) | 119 of 5,617 (2%) |
| monitors within 25 px of a cabinet or shelves | 2 of 46 (4%) | 0 of 189 (0%) | 46 of 492 (9%) |
| monitors standing on a piece | 18 of 46 (39%) | 106 of 189 (56%) | 270 of 492 (55%) |
| boxes on shelves, a cabinet or a table | 73 of 92 (79%) | 104 of 121 (86%) | 196 of 958 (20%) |
| test tubes, lab equipment and computers standing on a piece | 221 of 230 (96%) | 393 of 398 (99%) | 1,686 of 1,757 (96%) |
| shelves with another shelf within 70 px | 14 of 217 (6%) | 11 of 160 (7%) | 33 of 2,153 (2%) |
| longest cabinet run | 5 | 32 | 8 |
| missiles in triplets | 9 of 9 (100%) | 18 of 73 (25%) | 12 of 98 (12%) |
| pad groups mirrored about their middle | 5 of 5 (100%) | 2 of 4 (50%) | 45 of 55 (82%) |
| wall inscriptions within 230 px of the cannon | 16, 27 | 23, 27, 30, 67 | 21 to 27 |
| open floor around the cannon (px) | 137, 213 | 174, 181, 259, 339 | 132 to 289 |
| hit points within 1,200 px of the cannon | 54,759 of 235,784 (23%) | 1,096,014 of 2,356,627 (47%) | 391,977 of 2,848,727 (14%) |
| walk from the start to the cannon (px) | 4,114, 5,329 | 2,148, 3,672, 4,279, 6,006 | 3,534 to 4,804 |
| room length over width, median of levels | 1.36 | 1.4 | 1.34 |
| levels with repeated room layouts | 0 of 9 | 0 of 8 | 0 of 90 |
| tour gap between fights with 5% of the hit points, median of levels | 0.102 | 0.096 | 0.056 |
| dynamite sites on screen 0.2 of the tour before they are reached | 10 of 20 (50%) | 4 of 28 (14%) | 94 of 200 (47%) |
| lab door-graph cycles, median of levels | 6 | 1 | 3 |
| lab main body share of the floor, median of levels | 0.647 | 0.69 | 0.587 |
| lab largest branch share of the floor, median of levels | 0.143 | 0.142 | 0.203 |
| lab hub rooms from the start, median of levels | 1 | 1 | 1 |
| lab deepest objective in rooms off the main body, median of levels | 1 | 0 | 1 |
| lab square rooms, median of levels | 0.25 | 0.125 | 0.167 |
| lab small rooms with no notch, median of levels | 0.7 | 1 | 0.864 |
| lab mid rooms with no notch, median of levels | 0.167 | 0.75 | 0.2 |
| lab large rooms with no notch, median of levels | 0 | 0.3 | 0 |
| lab notches open outside, median of levels | 0.733 | 0.818 | 0.688 |
| lab ambush hit points into rooms the tour has left, median of levels | 0 | 0 | 0 |
| lab first-section rooms re-entered after the gate, median of levels | 0 | 1 | 1 |
| lab bare rooms, median of levels | 8 | 1 | 2 |
| floor off the tour, median of levels | 0.12 | 0.086 | 0.177 |
| tour share re-walked, median of levels | 0.066 | 0.047 | 0.048 |
| first fight, share of the tour, median of levels | 0.058 | 0.001 | 0.077 |
| fifth of the tour with the most hit points, median of levels | 2 | 1 | 3 |

Generated levels hang monitors beside doorways (17% within 40 px against 2%) and against cabinets or shelves (9% against 4%), stand boxes away from the furniture they belong on (20% on shelves, a cabinet or a table against 79%), and leave hittable pieces in doorways and pickups under pieces eight to ten times as often as the campaign. They also leave scattered paper away from tables (17% by one against 90%) and lab big computers without a chair (56% have one, against all of level 5's), rarely stand missiles in triplets, put fewer hit points around the cannon, space their big fights closer together and leave more floor off the tour. Their labs have fewer door-graph cycles and a larger largest branch than the campaign's.

## Overview

A generated level replaces one campaign level from 1 to 10. Levels 2 to 10 take the original's theme (lab or military), its gate style and its measured targets, and are built around a main route that meets them. Level 1 is an outdoor level built after the original level 1 ([Level 1](#level-1-1)). Every level is built once, each step keeping its own guarantees, and read back with the same route analysis that measures the originals. A layout whose tour cannot reach every objective is drawn again.

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
 +-- each layout k until the tour reaches every objective, at most 8 ----------+
 |                                                                              |
 |  Build                                                                       |
 |    1  gate request and budget         ChooseGate, BudgetPlanner              |
 |    2  layout plan in units            LevelProfile.Sample, LayoutBuilder     |
 |    3  cells, walls, floors            FineLayout, StructureBuilder           |
 |    4  breaker spot                    ContentPlacer.ChooseBreakerSpot        |
 |    5  room types and furniture        RoomFurnisher, Dresser, RoomDecorator  |
 |    6  player start, content, script   PlacementContext, ContentPlacer        |
 |                                                                              |
 |  Analyze                                                                     |
 |    map and script bytes -> RouteAnalyzer                                     |
 |    fenced: build again with the generator moved when it misses its band      |
 +------------------------------------------------------------------------------+
        |
        v
 RoofBuilder.Build, its own seed
        |
        v
 GeneratedLevel.Write / RandomAddOnInstaller.Install
```

`GeneratedLevel` carries what each stage produced.

| member | stage | content |
|---|---|---|
| `Plan` | layout plan | regions, links, stations, gate |
| `Layout` | fine layout | cells, wall edges, lattice, fake doors |
| `Budget` | budget | monster groups, ambushes, items, weapons |
| `Builder`, `Map` | all | the placed objects and the map they make |
| `Script` | content | doors, blackout and checkpoint for the `.lgc` |
| `Analysis` | analysis | route tour, objectives, legs and fallbacks |
| `RoofResidual` | roof | target pixels the roof could not cover |

### Randomness

A level draws from three `Random` streams. The layout seed drives the gate, the layout plan, the fake doors, the structure and the breaker spot. A second stream seeded from it drives the budget, and a third drives the closets' outer walls, the keypads, the door marks, the dressing and the content. The same seed and level always give the same files, which `LevelGeneratorIntegrationTests` checks. A change to the dressing or the content keeps every layout unless it leaves an objective out of reach and `Generate` takes the next one. A change to the budget can move the layouts through the number of spawn closets it sets.

## Reference data

`LevelGenerator` loads its sources once, on first use, and shares them across levels. Each one is read from the game folder.

| source | built by | from | read by |
|---|---|---|---|
| level targets | `ReferenceLevelSet.Load` | route analysis and measures of campaign 2 to 10 and add-on levels 2 to 5 | `GetTarget`, every stage |
| theme decor | `ReferenceLevelSet.GetThemeDecor` | decor ids and heights of the campaign (and the add-ons in add-on style) | dressing filter |
| wall clearance | `WallClearanceTable.Build` | distance of each decor object to the nearest wall in the originals | dressing, breaker props |
| facings | `FacingTable.Build` | the directions the originals give decor against each wall side | dressing, boss props |
| content pool | `OriginalContentSet.Load` | placed groups, spawner lists, pickups, crate loot and bosses of each original | `BudgetPlanner` |
| room atlases | `ReferenceLevelSet.GetAtlas` | each original's rooms with their roles, types and parts | `RoomFurnisher` |
| lab recipes | `RoomRecipe.Measure` | the parts of each room type over the lab-setting originals | `RoomFurnisher` |
| dressing libraries | `DressingLibrary.Build` | decor of the originals of each theme, indexed by wall windows | `Dresser.Dress` |
| breaker scenes | `OriginalContent.LoadBreakerScenes` | props within 170 px of the breakers of levels 2 to 4 | `ChooseBreakerSpot` |
| dynamite sites | `OriginalContent.LoadDynamiteSites` | 179 markers with their pads, holes and endless generators, levels 3 and 4 (lab) and 7 to 9 (military), plus add-on levels in add-on style | `PlaceSites` |
| keypads | `KeypadTable.Load` | keypad offsets next to each door kind and direction | structure, `PlaceGate` |
| sprites | `SpriteLibrary` | `.vid` files of floors, walls and the earth tile | `RoofBuilder` |
| original map | `LevelGenerator.GetOriginal` | the replaced campaign map, its cell model and its music lines | theme, unit count, dressing, script |
| sides | `SideTable.Load` | the most common side value of each object id | `MapBuilder.Add` |

## Coordinates

Generation works on three grids, from coarse to fine.

```
 unit         LayoutBuilder          rooms and halls are pieces of units
   |  x UnitCells (lab 1, military 2)
   v
 cell         FineLayout             floor cells and the edges between them
   |  Lattice.ToWorld
   v
 map pixel    MapBuilder             object positions written to the map
```

`ThemeSettings` gives the sizes per theme. A lab unit is one lab cell on the (40, 28) lattice. A military unit is 2 by 2 cells of the (45, 32) fine lattice. `ThemeSettings.ArchWidth` is in units, and `ThemeSettings.UnitPixels` is the walking length of one unit step.

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

| letter | `FineLayout.Edges` (generation) | `RouteGrid.Edges` (route analysis) | `DressingLibrary.GetWallKey` |
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

`RouteGrid.IsBlocking` treats only W and F as blocking. Generation uses it for its own distances, paths and connectivity checks, where every door, breakable wall and spawn wall counts as open. The route analysis sees the level as the player meets it, with doors locked until the breaker and S edges used only as a last resort (see [NOTES.md](NOTES.md#route-model)).

## Target and budget

### Level target

`ReferenceLevelSet.GetTarget` returns a `LevelTarget` measured from the campaign level being replaced, with its style, gate shares, walk to the breaker, dynamite sites, secret closets, checkpoint, weapons, pens and survey, and the setting's decor, wall clearance and facing tables.

| target | source |
|---|---|
| `HitPoints` | the original's monster hit points times `HitPointFactor` (1.15) |
| `ContentLevel` | the level whose pickups and weapons the generated level copies, the original by default |

### Gate request

`LevelGenerator.ChooseGate` turns the target's style into a `GateRequest`.

| style | gate | share behind the gate | notes |
|---|---|---|---|
| `LockedDoors` | locked doors | the original's locked share, or 0.5 | |
| `Blackout` | blackout | 0 | `SearchPixels` 5,200, the walk from the trigger to the breaker |
| `Fenced` | fences | the original's fenced-off share, or 0.4 | |
| `Ungated`, lab | locked doors or blackout, at random | 0.2 to 0.3 | `IsWing`, a gated branch off the route; a blackout has `SearchPixels` 5,200 |
| `Ungated`, military | fences | 0.2 to 0.3 | `IsWing` |

A wing gate closes a side branch and leaves the main route open.

### Budget

`BudgetPlanner.Plan` returns the `LevelBudget`, the monsters, pickups and counts the content stage places.

1. **Hit points.** The aim is the target's hit points times 0.9 to 1.1.
2. **Source levels.** Original levels (the campaign, plus the add-ons in add-on style) whose hit points lie within a third and three times the aim. When fewer than two qualify, the three closest on a log scale.
3. **Pools.** Spawner lists from the sources worth at most 22% of the aim, and placed groups worth at most 12%. When none is small enough, the whole pool is used. Both pools are shuffled.
4. **Drawing.** Until the aim is reached, each draw is an ambush while the ambushes hold less than the replaced level's spawned share of the aim, with that share as the chance (at least 0.5, at most 0.95), otherwise a placed group. A level whose original spawns nothing gets placed groups only. Pools are taken in order and wrap around.
5. **Cleaning.** Divers become red tank-weapon monsters, the diver boss and the bosses in `DroppedFromGroups` are dropped from groups and ambushes, and bats are capped at 2 × (level − 1).
6. **Boss ambushes.** Spawner lists of the replaced level that contain a campaign boss are added as they are.
7. **Endless generators.** Every endless generator (800 to 814) in an ambush list becomes a monster 50.
8. **Opening group.** When the draws give the level no placed group, it gets the smallest of the pool.
9. **Items.** The content level's floor pickups and crate loot, each count varied by up to 10% and each item passed through `ItemSchedule.Replace`, which swaps an item the campaign has yet to introduce for an allowed one. Weapons and dynamite are left out here. Every weapon allowed on the level gets at least 2 of its ammunition, and dynamite equals the target's site count.
10. **Weapons.** The content level's weapons, loose and in crates, kept in a separate list for `PlaceWeapons` and the closets.
11. **Counts.** Bosses, mounted cannons and fence generators are the replaced level's.

`ItemSchedule` holds the first level of every pickup, the stash shares of secret closets and the weapon to ammunition mapping (ammo id is weapon id + 40).

## Layout plan

A `LayoutPlan` lies on the unit grid and holds `PlanRegion`s of one or more rectangles joined by `PlanLink`s, each region with a `RegionKind` and each link with a `LinkKind`. The roles are drawn first, and every edge between them is laid as a contact. The roles come from an original's survey (`LevelProfile`), the counts and areas are drawn around them (`LevelProfile.Sample`), and `LayoutBuilder.Build` places the rooms with three primitives, a row packed along a side of a hall, a block that divides one rectangle into rooms, and a chain grown outward. A contact the graph did not ask for stays a wall. Labs and military levels go through the same builder, each in its own units.

`LevelGenerator.Build` draws a profile and builds it, up to 200 times, with a new floor target each time (the original's floor times 0.9 to 1.05). After that it throws "No layout for level N". When the first built plan's rooms miss the original's room floor by more than 15%, it draws again with the target scaled by the miss.

| input | labs | military |
|---|---|---|
| unit | one lab cell, 2,240 px², sides of 48.8 px | 2 by 2 military cells, 11,520 px², sides of 110 px |
| secret closets | the original's count | none |
| spawn closets | the number of ambushes / 3, at most 6 | none |
| shortcuts | 0 to 2 | none |
| pens | none | the original's (level 10 has 8) |

### Profile

`LevelProfile.Measure` reads the original's door graph (`RoomRoleMap`), its main body and its branches, with areas in units:

| role | measured as |
|---|---|
| start | the player's start place |
| hub | the first hall on the shortest path from the start to the end of the tour |
| neck | the room after the hub on that path |
| second hall | the largest other hall in the main body, and whether it runs across the hub's axis or along it |
| hub rooms, hall rooms | main-body rooms with a door to the hub or to the second hall |
| block | the other main-body rooms |
| block first | the second hall lies more than one room past the neck |
| branches | components off the main body with a door to the hub, ordered from the rooms that open on it, with how many of them do |
| dead ends | the other components, each a chain of rooms from the one that joins the body out |
| key | the breaker's or generator's place, kept out of the branches when it is a single room (then it stands apart) |
| shapes | each room's area with the share of its bounding box its floor fills |

| level | start | halls | neck | hub, hall, block rooms | branches (entrances) | dead ends | key |
|---|---|---|---|---|---|---|---|
| 2 | 36 | 23 × 8 | 28 | 0, 0, 2 | 2 (1), 2 (2), 1 (1) | 1 | apart |
| 3 | 36 | 32 × 8, 37 × 6 across | 24 | 0, 5, 7 | 9 (2), 5 (1), 4 (2) | 3 | in a branch |
| 4 | 52 | 17 × 6 | 76 | 0, 0, 9 | 7 (2), 3 (2) | 6 | apart |
| 5 | 28 | 26 × 6, 59 × 10 along | 161 | 1, 3, 2, block first | 6 (2), 3 (1) | 3 | none |
| 6 | 9 | 24 × 7 | 9 | 0, 0, 1 | 6 (2), 1 (1) | 1 | in a branch |
| 7 | 9 | 13 × 5, 16 × 6 across | 29 | 0, 1, 2, block first | 3 (1), 2 (1), 1 (1), 1 (1) | 3 | in a branch |
| 8 | 76 | 22 × 5, 15 × 5 across | 63 | 0, 0, 0 | 5 (1) | 0 | in a branch |
| 9 | 85 | 10 × 3, 10 × 3 along | 156 | 1, 1, 0 | none | 0 | 144, apart |
| 10 | 9 | 13 × 5, 11 × 3 along | 33 | 1, 1, 2 | none | 18 | 50, apart |

In the lab originals every room that touches a hub has a door to it, a branch's entrances stand side by side on the hub, and rooms of different branches never join. Single rooms hang off rooms past the neck on 13 of 14 lab dead ends. Level 5 is the same grammar with the block before the second hall and gaps between the rooms on that hall. The military originals use the same roles with fewer, larger rooms. Level 10's hall room is its 394-unit arena, a ring around a walled court, and its dead ends are the bays around it.

### Sampling

Each body role keeps the original's count within one and draws its areas from the same role of every profile in the pool, scaled to the original's floor for that role and to the floor target. Branches keep the original's shapes (rooms and entrances), one shape fewer or one more, with roots and tails drawn from the pool's branch roots and tails. For a wing gate the largest branch grows to the asked share and the other rooms off the halls shrink to keep the floor.

- Labs pool campaign levels 2 to 5. Add-on levels first sample their own original's profile (50 draws) when it has a hub hall, or when it is a compound with a blackout on the route, and fall back to the campaign slot's profile. A compound is an original with no hall on its main path (add-on 1 level 2, add-on 2 levels 3 and 5). Its start stands in for the hub, and every main-body room past the neck goes into the block.
- Military levels pool every military original, campaign levels 6 to 10, add-on 1 level 5 and add-on 2 level 4. The originals with lab walls on military floor (add-on 1 level 4, add-on 2 level 5) survey as labs and serve only their own add-on slots.

### Building

`LayoutBuilder` works in units, and joins need a contact of 4. Every room but the start, the halls and the breaker's room first grows to the box an original room of 0.6 to 1.6 times its area fills on average (`LevelProfile.Inflate`). A room laid against another tries several sizes and offsets and keeps the one touching the fewest other rooms. On labs it also weighs the distance from its middle to the middle of the floor laid so far, half a point per floor side's length.

1. **Hub and start.** The hub hall runs from the origin, as long as the original's or as the rows on its longer side need, the last room of a side overhanging its end, and at least 4 units wide. The start room stands at its near end, behind the rooms the original's main path passes between its start and hub (level 10's passage).
2. **Key room.** On a locked-door level the breaker's room stands at the hub's far end on one side, beside the neck it opens, and on a fenced level whose original's generator room stands apart (level 10's depot) the generator's room stands there at the original's size. For a wing gate it joins the hub's rows right after the wing's entrances on the same side.
3. **Hub rows.** The branch entrances and the hub rooms, shuffled, alternate sides and pack end to end with gaps of 0 to 1 unit. A row room that would overrun the hall narrows and deepens to fit when it stays within 2.5 to 1. Half of the branches with 3 or more further rooms enter through a corridor 4 units wide and 8 to 16 long running away from the hub, as level 3's P27 and P23 do, with the further rooms in rows on both of its sides and the rest in a block at its tip. Another branch with more than 3 further rooms grows them as a block of tree doors (one extra door with a 30% chance) off its last entrance, trying every side, then as two blocks of half the rooms, the second off a room of the first. Three further rooms or fewer grow as a chain that touches nothing but its parent and forks off an earlier room with a 30% chance.
4. **Neck, second hall and block.** The neck stands at the hub's far end. Then, in the original's order, the second hall, turning off the neck's long side when the original's runs across the hub and going on along the hub's axis otherwise, with its rooms in rows on both sides (gaps of 1 to 5 units when the original's stand apart; a room that misses the near side tries the far one), and the block. The block rooms under 0.4 of the median block room hang off the others afterwards. With 5 or more big rooms the block is a ring around a court, as level 3's P9 to P17 and level 4's body are, a band as deep as the square of the mean room area (0.85 to 1.1 of it) around a rectangle 1 to 1.5 times longer than wide, each side of the band divided into rooms. Otherwise, or when the ring finds no room, the block is one rectangle divided by guillotine cuts in proportion to the areas. Either block takes the original's depth along the hub's axis over its breadth across it, within 15% (level 2 1.31, level 3 1.07, level 4 0.57, level 5 0.76; military 0.8 to 2.5), and stands with the neck entering it as far along its breadth as the original's does (level 4 a quarter of the way, level 2 the middle), its longer part reaching toward the side where most of the floor already laid lies. When neither fits, the layout is drawn again. No block room is more than 2.5 times longer than wide. Every block contact is opened (63% of them past a tree of doors in a compound, as in add-on 1 level 2). The end of the tour is the block's farthest room, or the second hall's farthest room when the block comes first.
5. **Body contacts.** Contacts of 4 units or more between main-body rooms of one section, or between the entrances of one branch, become doors with a 90% chance.
6. **Dead ends.** Each chain's first room stands alone against a random room past the neck other than the end, three tries, and then against each of those rooms, largest first. Its other rooms follow in line, out from the side the first one left its host (level 10's bays, a 9-unit passage and a 30 to 52 unit arena).
7. **Shapes.** Every room but the halls, the corridors, the start and the breaker's room draws the fill of an original room whose box is 0.6 to 1.6 times its size (rooms under 60 units fill their box; mid and large ones 0.56 to 0.85 of it) and loses corners, 25 to 50% of a side each, up to six times until it is within 0.05 of that fill. Repeated cuts make steps and crosses. A cut opens outside, or with a 40% chance goes to the room across it. It is undone when it would leave a join short of 4 units, split the room, or leave a strip narrower than 3 units. A room that corner cuts cannot bring down to its fill, such as level 10's arena with its bays joined all around, leaves a court of void in its middle as large as the fill asks, with a band at least 4 units wide around it.
8. **Pillars.** Halls 8 units wide or more keep a void pillar 2 by 1 units down their middle every 11 units of length, as level 3's hub does, with 2 units of floor around it.
9. **Regions.** The shortest path of joins from the start to the end is the chain of stations, and every other room hangs off the room the walk from the chain reaches it through. Each join becomes a link on the longest straight run of the shared wall.

### Gates by role

- **Locked doors.** The start, the hub and the key room are open and every other room locked, with a door on every link between the two. Loot branches then open, smallest first, while that brings the locked share closer to the gate share (within 0.05). A draw that still misses it by more than 0.1 is rejected. The gate is the hub's link to the neck.
- **Blackout.** The trigger is the hub's link to the neck. A key room stands alone against the block room farthest from the neck by walk through the joins, middle to middle. The gate is the chain link past the key room that locks the share closest to the original's, counting the hub's branches, whose entrances are doors. Every link across it is a door. Cycle links among the open rooms that most shorten the walk from the trigger to the key room become walls until that walk reaches the search length less 1,000 px.
- **Fences.** The fenced part is a room with every room the walk from the start reaches only through it, either a station with every later one (levels 6, 7, 9 and 10 fence the chain past the hub or the neck), or a side room with the rooms hung off it (level 8 fences a branch). Parts within 0.06 of the original's share are tried nearest first, every link across one becomes a one-edge `Fence` and every other link an arch. The generator room is an open room past the hub whose back, the unit farthest from its entrance where content stands the generator, lies at least 1,200 px of walk from the start and 900 px from every fence (the originals have 828 to 3,258). Among those it is the one whose walk is nearest the original's share of the tour walked before its first generator, clamped to 0.12 to 0.22, times 20 px per unit of floor. A part with no such room is skipped. Cuts whose first room plays the role of the original's first room behind a fence go first (level 8 fences its branch past the generator).
- **Wing.** The branch nearest the asked share (within 0.1, and at least 18% of the floor) closes behind doors on every link into it, with the breaker in the leaf nearest one of those doors (within 600 px of walk). A military wing closes behind fences, with the generator in the leaf nearest one of them that is at least 900 px away.
- **Pens.** Gated leaves, as many as the original has, become pens behind one `Fence` edge each.

A draw that cannot place its neck, key room or gate returns null and the next attempt draws again.

### Closets and clean-up

- Each room of the original that a breakable wall hides (the Secret role, level 3's office, level 10's lab and depot) becomes a secret room of its area behind a `SecretWall`, furnished as its type. The secret closets the target asks for beyond them follow.
- `AddClosets` adds secret closets (2 by 2 to 3 by 3 units, off stations and side rooms) and spawn closets (off the stations past the hub). Each is linked by one edge of `SecretWall` or `SpawnWall`, and only the host may touch it. Secret closets go off the host's upper walls (+u and −v) for the first 60 tries per closet, where the camera sees the breakable wall.
- `AddShortcuts` adds breakable walls between two open rooms that share at least 3 edges and lie at least 3 links apart, on the same side of the blackout trigger, none on a blackout level. The originals have at most 2.
- `FillPockets` gives enclosed holes of up to 2 units to the neighbor that touches them most.
- `AlignLinks` moves each doorway along its boundary to the nearest span where no other wall meets it, narrowing it when no span is wide enough. It keeps one edge of wall at both ends of the boundary when a span allows it.
- `SpaceDoors` moves a lab door or arch that stands closer than 160 px to another one to the clear span of its boundary that lies farthest from the others.
- A military arch is 1 or 2 units wide with at least one unit of wall beside it, and becomes an opening when the shared wall is too short. A lab arch on a wall too short for two units becomes a door.

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

The closets' outer edges, which border no shown cell, then get 601 and 602 walls from the content stream, sunk to z −7 so the roof covers their tops, with no beams. Each secret and spawn closet is walled on every side.

`ContentPlacer.PlaceBrokenKeypads` then puts a broken keypad (425) next to jammed doors, as often as the originals do, using `KeypadTable`.

### Military structure

`StructureBuilder.BuildMilitary` places 651 and 650 walls on every second fine edge, 654 where a fake door is, a 653 frame in seven of ten arches one wall piece wide, 652 corner posts on even vertices and 503 floor tiles on their own (80, 56) lattice wherever a tile reaches a cell. `StructureBuilder.BuildFence` puts a 653 emitter and a 164 fence 14 px above it on each unit edge of every fence link, the gate's and the pens'.

`ContentPlacer.PlaceDoorMarks` copies the wall marks (131) the original has within 60 px of its doors onto doors of the same ID and facing, as often as the original marks its doors.

`MapBuilder` collects the objects. `Add` drops a repeated object at the same rounded position unless `unique` is false, hands out handles, takes the side from `SideTable` and fills the default payloads, 20 bytes with two items for the player start, 12 bytes for monsters and bosses and 8 zero bytes for everything else.

## Breaker spot

Locked-door and blackout levels need a breaker. `ContentPlacer.ChooseBreakerSpot` looks for it in the key room before any decor is placed.

1. Candidate walls are the room's `\` far walls (`u` edges with room floor below them and none above).
2. The wall's straight run must end at a corner or run on for at least 3 edges (`WallRun.IsEnclosed`), and runs of 4 or more are preferred.
3. At least 3 open cells must lie in front of the wall and 2 to its side (`FloorScanner.CountOpenCells`). When no wall qualifies, one less of each. Walls with no floor behind them come first.
4. The breaker hangs at the wall piece's position plus (−8, +3). For every wall and every original breaker scene, each prop is kept when its footprint lies in the room, a solid one stays out of the doorways and the cell in front of the breaker, and it passes the wall clearance and facing tables.
5. The fit with the most transformers wins, then the one with the most props, then a random pick.

When no wall qualifies, `Build` starts the level over with the same random sources, up to 20 times, and then goes on without a breaker. `PlaceGate` then places no breaker or keypads and leaves the doors open, and the level reads as ungated.

## Dressing

Labs and military levels are dressed the same way, each from its own setting's atlas and recipes (see [Rooms](#rooms) and [Recipes](#recipes)).

1. `Dresser.Dress` covers the floor with windows copied from the originals with the same walls (`DressingLibrary.TryCopy`), keeping only wall dressing, blood, rubble and marks (no furniture or papers) that face and clear the walls as the originals do.
2. `RoomDecorator.Decorate` thins blood and corpses to the original's rates per floor cell, lays blood in the last two stations with a corpse and gibs in the last, and puts every corpse on blood.
3. `RoomFurnisher.Furnish` types and furnishes every room, below. The breaker's surroundings in its room and 100 px around every fence gate stay clear of it.
4. The breaker's scene stands in the key room, `Dresser.ClearAndConnect` checks the floor joins up, `RoomDecorator.MarkMissiles` lays the originals' floor stripes in front of missile threes as often as they do, and `KeepPlacedAsOriginals` drops any piece that faces or stands off its walls in a way the originals do not.

### Furnishing

- **Type.** Roles come from the plan's link graph, read the same way as the originals' (`RoomRoleMap`). Halls stay halls, and the key room takes the type of the original's breaker room. Every other room takes the type of the original room of its role nearest it in area, within a factor of 3 and each original room once. An empty type (passage, bare or the level 10 arena) goes only to a room with two doors or more. The rooms left over, once every room has had its own role's pick, take the furnished original room of any role nearest them in area, unused ones within the factor of 3 first.
- **Density.** A room holds as many pieces for its area as the original's rooms of its type, or the setting's when the original has none, give or take 20%, and at least 3.
- **Parts.** Each room draws its parts up front, the required parts of the type first and then parts by their rate until they reach the room's density, three in four from the original level's own rooms of the type and the rest from every original of the setting. They are placed largest first, the rooms taking turns one part each. A part is copied whole from an example. Its pieces keep their offsets, facings and heights, and crates lose their loot. No group of three or more pieces is used twice in a level, a room takes at most two parts from one original room, and no piece goes past the original's count for the floor.
- **Spot.** A part's anchor, the piece against the most walls, stands in a cell with walls on the sides it stood by, at the same distance from them, and a wall part farther than 60 px from every wall keeps its nearest one. As often as its kind stands in rows in the originals (the recipe's row share, 83% of office wall screens, 76% of server banks, 25% of lab barrels, every military missile), a part whose kind already stands against the same walls tries the spots one measured spacing from those along the walls first, and then up to 30 distinct cells by its walls. Each piece must keep its distance to the walls the part stands against and stay no closer than in its source to any other, turn and stand as the facing and clearance tables allow (judged against the walls as built and read back, `LevelExtractor.ReadLabEdges` and `ReadMilitaryEdges`, as the route analysis reads them), keep its footprint off the keep-clear cells, stay clear of the pieces already placed, and leave the room's free floor joined.
- **Footprints.** A piece takes a box along the lattice axes as wide as the originals mostly stand two of a kind side by side (the median distance to the nearest one along an axis, the wider axis, 67 px for tables, 42 for cabinets, 87 for shelves and 77 for containers), or its collision box when no original stands two side by side. Toward a wall the box reaches no farther than the piece stood from that wall in its source. Pieces of different parts never overlap their boxes. Against the keep-clear cells and for the joined floor a piece counts only the cells its collision box covers, without the player's 14 px margin.
- **Scatter.** Papers lie on free floor at the type's rate, where the facing and clearance tables allow them.

### Keeping the way clear

`LevelGenerator.GetKeepClear` marks the cells on both sides of every doorway, the cell in front of each fake door and a square around the start, and the military content stage adds the floor around sites, pads and the cannon's line of fire. `Dresser.ClearAndConnect` removes solid objects whose footprint touches one, then, from the start, removes the solid objects on the least-blocked path to every floor cell not reached, until every free cell is reached. It returns the blocked cells the content stage avoids.

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
| `Fights` | the fights placed so far, with their progress, hit points and region |

`GetProgress` is distance from the start, which is not the same as the tour progress `p` the route analysis measures. On branched or gated levels a cell near the start can come late in the tour, and the reverse.

The player start (9, facing 96) goes on the start room cell nearest its middle that is not blocked, has no wall on its edges and lies at least 20 px from any wall box.

### Order

`ContentPlacer.Place` runs these steps in order. Each step takes cells from `Taken`, and the stashes take items and weapons out of the budget.

```
 PlaceGate -> PlaceBosses -> PlaceMountedCannons -> PlaceSites -> PlaceCharges
   -> PlaceEncounters -> PlaceStashes -> PlaceItems -> PlaceCheckpoint
```

On military levels `PlaceMilitarySites` places the cannon and the sites before the decor, on a `PlacementContext` with no blocked cells, and `Place` takes them as they are. The cells within 150 px of every site and pad, and within 100 px of the line from the cannon to its kill zone, join the keep-clear cells, which the dressing leaves free.

### Content by role

`EncounterProfile` reads the content level's original by the roles (`RoomRoleMap`) and gate zones of its rooms:

| member | what it holds |
|---|---|
| `Encounters` | each fight (the standing monsters of a room, or one spawner) with its role, zone, share of the level's hit points and tour progress, and for a spawner the trigger room's role and zone and whether it is the fight's room or a neighbor |
| `SiteRooms`, `ChargeRooms` | each room holding 179 sites or 301 charges, with its role, zone, count and the tour progress of its first one |
| `PickupRooms` | the same per item category, for loose pickups outside secret closets |
| `CheckpointRoom` | the checkpoint's room and progress |
| `GeneratorProgress` | the tour progress of each generator |
| `IsGated` | whether any room lies behind a gate |

The generated rooms get their roles from `LayoutPlan.MeasureRoles`, the same map read over the plan's link graph, and their zones from `LayoutPlan.GetZone`. Each step matches what it places to the original's rooms and puts it in a generated room of the same role and zone at a similar progress. When the original has no gated rooms, zones are left out of the match. The measured shares per setting:

| | labs | military |
|---|---|---|
| fights | body rooms 62% of the hit points (locked body 38%, all standing), branch roots 14%, closets 7% | fenced branches 32%, open branches 16%, hub 10%, hall rows 9%, almost all spawned |
| triggers | a neighbor 25%, the key room 8% | far 32% (level 10's bay hall), own room 20%, neighbor 14%, the hub 13%, the neck 8% |
| sites | body and branch-root rooms, locked or blacked out | 2 to 4 per room in fenced branches, hall rows, the hub row and the start |
| charges | the key branch or body rooms | open branches, the start and the neck |
| health | body rooms 35%, branches | fenced hall rows 41%, necks |

### Gate

- **Breaker.** The 418 goes on the chosen spot. Every door gets a keypad from `KeypadTable`, 424 (off) on locked-door levels and 423 (on) on blackout levels. `LevelScript.DoorsOpen` is false on locked-door levels. A blackout's trigger point is the middle cell of `BlackoutLink` on its far side.
- **Generators.** On fence levels the original's number of 415 generators (at least one) go in the back third of the key room, farthest from its entrance, each with a 912 marker. Up to two transformers join them. The spot near (7541, 4501), which level 10's script treats in a special way, stays free. A generator the original reaches more than 0.15 after its first one goes instead in the dead-end room whose progress is closest to the original's, far from its entrance.

### Bosses

`PlaceBosses` puts each boss in the largest dead-end room outside a wing at progress 0.65 or later (on labs a station or a room one off one), or else the last station, in the fifth of its inner cells farthest from its entrance. Blood pools go around it, at least one cell from walls, with two corpses each on a pool of their own.

### Mounted cannon

The originals stand the cannon at one end of their largest place, with 1 to 4 of the level's sites, the pads and 5 to 8 spawners 950 to 1,600 px away at the other end.

`PlaceMountedCannons` looks through the stations at progress 0.15 to 0.8, those with floor 3,600 px of walk from the start first and then largest first. `HallAxis` measures each along its longer side, with the end the walk enters from at 1 and the far end at −1. A cell qualifies at progress 0.75 or earlier, at least 3,600 px of walk from the start, on the entry half at 0.3 or more, at least 1,000 px from the kill zone (the hall cell nearest −0.85 on the axis), with a 5 by 5 ring of points out to 180 px of free floor clear of walls. Cells nearest 0.6 go first. A second pass drops the entry half and kill zone distance and shrinks the ring to 90 px. The full square of floor stripes (131) around the level 8 cannon goes around it. Placed groups keep 600 px from the cannon.

### Dynamite sites

`PlaceSites` places the target's number of 179 sites.

1. Templates are filtered by `SiteRankCap`, the highest endless generator a level may use, and narrowed to those of the replaced original when it has any.
2. Each site takes the original's site room of the same rank. The rooms outside a wing other than the key room are ordered by the room the original room's earlier sites landed in, then by zone, then by role, then rooms holding no other original room's sites first, then by progress nearest the original's. The sites of one original room stay together, and different original rooms get different rooms. Without an original site room the target progress spreads from 0.6 to 1.
3. On military levels with a cannon, the 35% of the sites (1 to 4) whose target progress lies nearest the kill zone's move into the kill zone, within 450 px of it.
4. On labs, `PlaceLabSite` tries the ordered rooms in turn for a far wall with void behind it and wall on both sides. The wall piece becomes a 616 or 612 hole, the 179 goes at an offset of (±25, +20) from it, and the template's endless generators (or an 800 when it has none) hide at about (∓30, −42), behind it under the roof (see [NOTES.md](NOTES.md#dynamite)).
5. On military levels `PlaceWallSite` builds each site from the walls back. The originals' pad stands behind its site as seen from the way in (in 85% of sites), 0.9 to 1.1 cells off a far wall (+u or −v) and about 2.2 off a near one, diagonally into a corner, straight against a wall, or as a flanking pair or a line of three across the end of a room, and all the sites of a room share one arrangement.
   - For each quarter turn of the template, the pads' mean offset names the walls they back onto. Each room cell with a wall on that side (two walls for a corner) gives the site position that puts the outermost pad its gap off the wall. A pair with no mean offset stands in line against the wall across its spread.
   - A candidate needs the room's floor 0.9 cells around the site and every pad, every pad at least its gap off every wall and 2.5 cells from any doorway or fake door, the pads behind the site as seen from the room's entry (or the cannon in the kill zone), and 250 px from other sites.
   - The best candidate scores corners, the approach and, most, the room's far end. Every site of a room reuses its first template.
   - A room with no candidate tries its cells with pads 0.85 to 1.5 cells off a far wall, then the walls of the next ordered rooms, and only then open floor.
6. Otherwise `StampSite` copies a template around a free cell clear of walls, its generators at their original offsets (shrunk to 0.3 on labs) and its pads at theirs, mirrored about the site. Other members are pulled in to 0.3 of their offset when they would land off the floor or in a wall, and left out when they still would. `GetSiteCells` ranks the room's cells from its far end, or from the kill zone. `OrientPads` turns the template a quarter at a time in lattice space until every pad stands 0.85 to 1.5 cells off a far wall. Every cell is tried that way before pads stand in the open, and a site without pads comes last. A site keeps 250 px from other sites, and a site with pads outside the kill zone first looks for a spot 520 px from any other pad. `IsPadSpot` puts every pad, at a site or in a placed group, on reachable floor 20 px clear of the wall boxes, in view, at least 80 px from any other pad, and in the open or at least 0.85 cells off a far wall. A placed group that finds no spot leaves its pads out.

Sites stay clear of solid decor through `IsCrowded`, by the originals' gap: 16 px, or 30 px for pieces 40 px or larger.

### Charges

`PlaceCharges` puts one 301 charge per site. The charges map to the original's charges by rank, and those of one original room form a batch. A batch goes on a free inner cell 150 px or more from every site, in the first room when they are ordered by the room the batch's original room already landed in, the original room's zone, lying on the walk from the start to the key room or to a site, its role, holding no other batch, and progress nearest the original's. The charges of a batch stand on the cell and its free neighbors.

### Fights

`PlaceEncounters` places the budget's placed groups and ambushes on the original's fights.

- **Standing groups.** The groups, sorted by hit points, take the original's standing fights of the same rank. Each fight holds one room. The rooms are ordered by the room the fight already holds, then by role (a mismatch counts 2) and zone (1), then rooms no other fight holds first, then progress nearest the fight's. The first of up to four rooms with a free cell 900 px from the start where `TryPlaceGroup` succeeds takes the group, or else the group is scattered in the first.
- `TryPlaceGroup` anchors the group's original shape at a free cell at least 900 px from the start and keeps it when 80% of the members land on reachable floor outside closets and walls, within a walk of 1.4 times their straight distance from the anchor plus two cells, and at least 12 px from any monster already placed. After 40 failed anchors the members are scattered one by one, each on a free cell of its own.
- **Ambushes.** The ambushes, sorted by hit points, take the original's spawned fights by rank, and ambushes that share a fight merge into one spawner. A closet fight takes the next spawn closet through `PlaceClosetAmbush`: the spawner inside the closet, the trigger two cells beyond the floor cell in front of its wall, a waypoint 500 to 800 px on along the route, and a spawn wall payload that blows the wall open on the same trigger. Any other fight takes the first of its six best rooms, ordered as standing groups' are, that has free floor the walk from the start reaches no sooner than the trigger, and its spawner stands only there. With none, the first room keeps the fight and the trigger moves to its entrance.
- **Triggers.** A fight triggered in its own room keeps its trigger there. One triggered next door takes the neighbor that best plays the trigger room's role and zone, the nearest first. One triggered from afar takes one shared room per trigger role and zone (level 10's bays all wake from one hall). The trigger goes on the trigger room's free cell whose progress lies nearest the fight's.
- **Spawners** stand off the screen around the trigger, at least 640 px across or 400 px up or down, on one of the four such cells farthest from it, or else on the room's farthest cell. `PlaceAmbush` writes the program. The monsters walk back along the shortest path and stop 40% of the way to the trigger. Trigger radius is 250 to 350 px on labs and 300 to 450 px on military levels.

`SpawnerProgram.Create` writes the 908 payload, the trigger, the monsters and up to six waypoints (see [NOTES.md](NOTES.md#spawners-908)).

### Stashes and items

- `PlaceStashes` fills each secret closet with `ItemSchedule.CreateStash`, a money pile, an ammunition cache, a weapon with some ammunition or a perk with some money. Stash items count against the budget, and `TakeWeapon` gives closets the weapons that are early for the level first.
- `PlaceItems` shuffles the remaining items. `PlaceWeapons` first replaces weapons the level may not have yet, puts about 35% of the weapons in empty crates and the rest in side rooms at progress 0.25 to 0.75, keeping weapons 600 px apart where it can. Crates then take 32% of the ammunition, 45% of the armor and 20% of the health. The rest, dynamite excepted, goes in piles by category (money 8, ammo 3, armor, health and bonus 2, implants one by one). The piles map to the original's pickups of the category by rank, and each takes the first of four rooms, ordered as charges are (without the walk), where `PlacePile` finds a spot. `PlacePile` lays a pile within 16 px around a spot clear of the wall boxes in a cell with a wall on at least one side, two for money where it can. Every spot of a pile, and the fallback cell of an item no pile takes, must be visible. `PlacementContext.IsVisible` takes a point as visible when the ground straight below it on screen, up to the roof height of 90 px, is shown floor with no wall box in the way. The originals leave 18 of about 1,000 loose pickups outside secret closets under a roof, most of them money.

### Checkpoint

`PlaceCheckpoint` runs when the original has a checkpoint, except on blackout levels, where the blackout trigger already moves the respawn point. It takes the rooms in the order charges use, with the walk from the start to the strongest fight in place of the walk to the key, and puts the checkpoint on the first room's free inner cell with nothing around and at least 24 px from walls, cells on that walk first, then progress nearest the original's.

## Script

`LevelScriptWriter.Format` writes the `.lgc` from the `LevelScript`.

- The music definitions are copied from the original's script.
- `main()` includes the common scripts, calls `InitGame` and sets `DoorsIsOpen`.
- A blackout adds a trigger within 120 px of its point that moves the respawn point there, locks the doors, plays the quake and turns the lights off, and swaps every 423 keypad for a 424.
- A checkpoint adds a trigger within 120 px that moves the respawn point.
- An exit point plays the wind ambience at the start, comments out `CompaignGameTact`, and adds a trigger within its radius that wins the level and loads the shop (`LoadNextMap("maps\\shop.map")`).

## One pass and guarantees

`LevelGenerator.Generate` builds a level once. `BuildAnalyzed` builds it, reads it as the game would (the map written with the original as template and parsed back with `MapResourceBuilder.Parse`, the script formatted and parsed with `LevelScriptReader`) and runs `RouteAnalyzer.Analyze`. On a fenced level whose tour reaches the first generator outside 0.08 to 0.30, it builds the level again with the generator in another of the plan's `GeneratorRooms`: the one whose walk from the start, scaled by the measured route to the first generator, comes nearest the original's share of the tour (kept within 0.12 to 0.26). It keeps the build whose first generator lies nearer the band. The walls and fences stay as they were.

A step that cannot meet its guarantee draws itself again. The layout builder starts over on a failed placement. `Build` redraws the layout when the key room has no wall for the breaker (up to 20 times) and once when the built floor misses the original's by more than 15%. The outdoor builder redraws a campaign compound whose exit lies under 1,300 px from the start. When the tour still leaves an objective out of reach, forces a gate or needs a secret wall outside level 1, `Generate` takes the next layout, up to 8.

Layout `k`'s seed is `seed × 100000 + level × 100 + k`. The budget's stream is seeded with `layoutSeed × 7919 + 17` and the content's with `layoutSeed × 7919 + 101`.

### Guarantees

Every level keeps these, each in the step that creates it. `LevelGeneratorIntegrationTests` asserts the route ones over seeds 1 to 10 of the campaign and the add-ons.

| guarantee | stage | code |
|---|---|---|
| every objective reachable, no gate forced, no secret wall needed outside level 1 | layout, dressing | `LayoutBuilder` joins, `Dresser.ClearAndConnect`, `GetKeepClear`; `Generate` takes the next layout otherwise |
| locked doors: the breaker's room open before the gate, the breaker on the wall nearest a door into the locked part (within 400 px, or 100 px of the nearest wall) | layout, breaker spot | `LayKey`, `LockSections`, `ChooseBreakerSpot` |
| blackout: the trigger at the hub's exit, the breaker off the block room farthest by walk | gate | `PlaceBlackout`, `LengthenSearch` |
| fences: the fenced part within 0.06 of the original's share, every generator 900 px of walk or more from every fence where its room allows | gate, content | `PlaceFences`, `PlaceGate` |
| the breaker on a back wall of 4 pieces or more that ends in corners, judged on the walls as built, with open floor in front and beside | breaker spot | `ChooseBreakerSpot` |
| one charge per site, on a free spot 150 px or more from every site | content | `PlaceCharges` |
| sites clear of solid decor by the originals' gap | content | `IsCrowded`, `MeasureSiteGap` |
| sites shaped as their templates, pads off far walls | content | `PlaceLabSite`, `StampSite` |
| decor faces and stands off its walls as the originals turn and stand it, judged on the walls as built (add-on 2's gym equipment fails on 2 of 50 add-on levels, read back on a lattice a fraction of a cell off the layout's) | dressing, content | `RoomFurnisher`, `KeepPlacedAsOriginals` over all decor at the end of `Dress`, the breaker props' fit, the bosses' gore |
| decor of the level's setting only | dressing | `GetDressingDecor` |
| no wave from floor the walk passes before its trigger | content | `PlaceEncounters` |
| a checkpoint on the walk to the strongest fight when the original has one | content, script | `PlaceCheckpoint` |
| the budget's bosses, generators and weapons | content | `PlaceBosses`, `PlaceGate`, `PlaceWeapons` |
| spawn walls blow open toward their room | structure | `StructureBuilder.BuildLab` |
| level 1: the campaign exit 1,300 px or more from the start, the compound's 55 monsters on open ground | level 1 | `OutdoorLevelBuilder` |
| every sprite payload 8 bytes or more | all | the tests |

### Likeness

These measure how close a level comes to its original and are not guaranteed. `ascli inspect design` writes each one in `design.tsv`, the originals' rows beside the generated ones.

| measure | column | originals |
|---|---|---|
| share of the tour walked again | revisit | 0.01 to 0.15 |
| share behind the gate | gated share | level 2 0.53, level 3 0.70, level 4 0.32, fenced 0.14 to 0.84 |
| walk from the key to its gate | key to gate | 391 to 3,258 px |
| walk with the gate shut | key walk | level 2 1,538, level 3 2,881, level 4 6,274 px |
| first generator | first generator | 0.10 to 0.29 of the tour |
| floor blocked by solid decor | blocked share | 0.02 to 0.16 |
| floor's mean distance from its middle over a disc's | spread | 1.20 to 1.81 |

## Roof

`RoofBuilder.Build` runs once, after the route analysis, with its own seed. The analysis never sees the earth tiles. It hides everything outside the walkable floor under 501 earth tiles.

1. **Ground earth.** Earth tiles at z 0 fill the tile lattice positions whose tile does not touch any floor pixel.
2. **Raster.** Floors and walls are drawn once into a depth buffer in 128 px strips, as the engine sorts them, and the strips are kept for the classes.
3. **Classes.** Each pixel becomes a target, a forbidden pixel or neither.

| pixel | class |
|---|---|
| floor of a shown cell, or up to 96 px above it and 8 px below it | forbidden |
| face of a wall seen from shown floor | forbidden |
| floor of a closet or of a cell that is not shown | target |
| wall seen only from outside | target |
| empty pixel near floor that the ground earth does not cover | target |
| wall cap above the roof height | neither, and it ends the 96 px zone |

4. **Lattice.** Roof tiles go on the ground tiles' lattice wherever they cover target pixels and no forbidden pixel, which covers most of the void.
5. **Cover.** A greedy cover of the pixels left places roof tiles at z 90 (lab) or 95 (military), shifted down by their height so they appear on the same screen spot. Candidates lie on a 2 px grid in the rows that still hold target pixels, never overlap a forbidden pixel and are taken best first by how many target pixels they cover, with stale scores re-queued.

`RoofResidual` is the number of target pixels left uncovered. `generate` and `preview` print it as "roof misses". A large value means the walls leave gaps a tile cannot fill without covering visible floor.

## Level 1

`LevelGenerator.Build` hands level 1 to `OutdoorLevelBuilder`, which builds the level on a map the size of the original level 1 it follows, on the lab lattice centered on the map. The original sets the style.

- **Campaign.** A chain-link fence (660) rings a yard of 14 to 20 by 14 to 18 cells, with a shut gate (two 661, facings 0 and 128) on the `\` side facing the start in the bottom-left corner and a torn piece (662, facing 0) at the far end of the upper `/` side. Inside stand one or two buildings of lab walls 620 and 621, with posts 622 at the corners at the lattice offset of lab posts (0.25, 0.25), a shut door (623 and 617 at z 0) on the lower u wall and roof tiles (503 at z 90) laid two cells apart from the back corner, one cell in from the walls. The breakable wall (625) lies on the smaller building's upper v wall, with the original's barrels and gas cylinders copied around it and the exit at the original's offset behind it (46 px, radius 70). The original's storage clusters go in the yard, each against the fence or a building wall and clear of the gate and the torn piece. Street lamps stand on every third fence post away from the openings, facing into the yard, and a row of 4 money completes it, with no monsters.
- **Compound (add-on 1).** The same yard, its exit just inside the breakable wall with a dynamite site in front of it, one charge 900 to 1,600 px from the site outside the yard, a shotgun by the start, a cluster of 55 monsters 50 and 53 in the yard, and two spawners 900 to 1,600 px from the gate that trigger at it.
- **City (add-on 2).** A grid of roofed blocks with streets between them on a 4,000 by 3,000 map, the player start (549) in the top-left corner, 16 spawners along the streets 1,200 px or more from the start, each triggered near itself, 42 bats in the far corner, and two shotguns, a grenade launcher and two implants. Every monster dead wins.

`LayGround` runs each row of the original's ground tiles (505, or 501 for the city) across the whole map. Clumps of 506 go on a 50 by 48 px grid, shifted by half a step in turn and jittered by 8 px, wherever the ground is grass, and tufts of 507 at z 1 go on 6 in 10 points of a 25 px grid where grass meets dirt within 30 px. The campaign's dirt is the yard and a 180 px road from the gate through the start to 700 px past it. The compound's grass is a 140 px band along the map's edge, and the city's is a strip along its buildings. Every style also keeps grass within 1.3 cells of a building's upper walls. Level 1 gets no roof pass.

## Add-on style and installation

`GenerateAddOn` picks one add-on by the seed's parity and follows its levels. Level 1 is outdoor in that add-on's style, and levels 2 to 5 take their structure from the campaign level closest to that add-on's level (`AddOnSlots`, add-on 1 4, 5, 3 and 9, add-on 2 5, 5, 8 and 5) and their pickups and design targets from that add-on level. The hit point target is the geometric mean of the same level in both add-ons. Each level is written over that add-on's map of the same number (`GeneratedLevel.Template`). The generator must be created in add-on style, which also adds the add-on levels to the content pool, the dressing libraries, the theme decor and the dynamite sites.

`RandomAddOnInstaller.Install` writes the levels to `Maps/RANDOM`, their briefings, money table, script branches and play menu button. The [README](../README.md#the-random-add-on) describes the result.

## Debugging

### Reproducing a level

- `ascli preview --game <folder> --seed <s> --level <n> --out <file.map>` builds one level as `generate` does.
- `ascli analyze --levels <folder>` draws the tour over the generated maps, with objectives, gates and spawner triggers, and writes `routes.json`.
- `ascli inspect design --seeds <n>` measures the likeness numbers of generated levels beside the originals'.
- `LevelGeneratorIntegrationTests` generates seeds 1 to 10 of the campaign and the add-ons and fails on any objective out of reach.

### Common causes

- **A tour progress misses while the object sits where the code put it.** The route analysis reads the tour progress `p`, and content placement reads distance from the start. See [Placement context](#placement-context).
- **A step places nothing.** Earlier steps took the cells (`Taken`), the keep-clear cells cover the room, or `GetFreeCells` finds only cells with walls on their edges. With the `inner` option, a cell also needs all four neighbors in the region.
- **An object stands inside a wall.** Wall boxes come from `PlacementContext.AddWallBox` for walls and fences only. Dressing checks copied objects against the clearance table alone.
- **A likeness number misses on every seed.** The layout inputs or a constant in `LayoutBuilder` or `ContentPlacer` leave no room for it. `inspect design` over a few seeds shows whether the value is always on the same side of the original's.
- **Output changed for a seed after an unrelated edit.** See [Randomness](#randomness).
