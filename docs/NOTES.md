# Alien Shooter (GOG 1.2) notes

This covers what the toolkit's code does not say by itself, which is how the game's scripts and engine behave and how the original levels are built. The file layouts are in [FORMATS.md](FORMATS.md), and the rules derived from the levels are in [RULES.md](RULES.md).

## Scripts

- Script syntax and handler names are described in [FORMATS.md](FORMATS.md#scripts-and-text).
- The registry value `GameType` selects the flow (0 campaign, 1 survival, 2 add-on, 3 add-on 2) and `LevelNumber` the level. `MAINMENU.LGC` sets them, `SHOP.LGC` loads `Maps/[addon/|addon2/]Level_NN.map`, and `COMMON.LGC` decides what follows a level. The campaign ends after level 10 with `Level_11`, and add-ons end after level 5 with their `Level_06`.
- Win check (`common_compaign.lgc`). No object 179 is left and no monster with side 1 is alive. Money paid after a level comes from `LevelMoney[]` in `common_table.lgc`.
- Checkpoints are the `StartPlayerX/Y` values set by a trigger in `Level_NN.lgc`.
- The logo, main menu and shop maps are identical menu-host maps with no sprites.

### Doors, power and fences

- Doors that open are 605 and 607. They sink in z when the player is near and `DoorsIsOpen` is set. Embedded doors 611 and 617 are decoration.
- The breaker (418, `F418_0`) sets `DoorsIsOpen = 1`, calls `LightOn`, swaps the 424 door panels for 423, removes paired 609 and 612 trap walls and removes nearby 180 pointers.
- A dark start calls `LightOff(0x606060)` after `InitGame`. The level 4 blackout is a trigger that sets `DoorsIsOpen = 0`, calls `LightOff` and swaps 423 for 424.
- The generator 415 (`F415_15`) destroys every 164 fence in the level. Level 10 has an extra hardcoded branch.
- The `side` field of a sprite decides what can be destroyed. Breakables (barrels, monitors, crates, chairs, 609 walls) and the generator 415 use 2 in every original. Decor uses 0, and monsters, 908 and 80x generators use 1. A 415 with side 0 cannot be destroyed, and neither can its fences.

### Spawners (908)

- The payload is an action stack that runs from the end. It is a u32 count, then (op, a, b, c) entries, then a trailer of `00000000 03000000`.
- `ACT_FLAGMAN_TRIGGER` (43, x, y, r) waits until the player is within r. `ACT_BUILD_UNIT` (35, id) builds a monster and `ACT_PAUSE` (40) waits between units. `ACT_STOP_STACK` (73) ends the stack. `ACT_MOVE` (33, x, y) entries are copied to the spawned units as waypoints, and the first one is their final destination.
- Most monsters in the originals come from spawners. Level 6 has 9 placed monsters and 462 spawned ones, and level 10 has 62 and 1,408.
- A crate (419) payload of (0, 1, item, 0) holds its loot.
- Spawn closets use a 618 (`/`, sprite 601) or 619 (`\`, sprite 602) wall whose payload is `[ACT_STOP_STACK, 15 (ANI_DEATH), ACT_FLAGMAN_TRIGGER x y r]` plus 4 zero bytes. It explodes through `F618_15` and has a 908 inside on the same trigger.

### Dynamite

- A player standing on a 179 marker with a charge (301) plants it (`F179_0` in `common_event.lgc`). The blast (`F184_15`, radius 100) kills the 80x generators, the 908 spawners and the 450 teleports, turns the 616 and 612 holes into rubble (615) and kills side 1 units within 150.
- In the originals there are as many charges as sites. A site is always within about 100 px of a blue 450 pad (military, with one to three invisible 80x generators on it and a 908 ambush next to it) or of a 618, 619, 616 or 612 spawn hole (labs).
- Lab sites on levels 3 and 4 use a far-wall piece of a deep room as the hole (616, or 612 for small monsters). The 179 lies about (±25, +20) in front of it and the 80x generators about (∓30, -42) behind it under the roof.

## Level construction in the originals

### Lab lattice

- Floor tiles (500) have centers at x = 40i, y = 28j + 14 with i + j odd.
- Walls sit on a lattice of x = 20a, y = 14b with a + b even. A `/` wall (601) steps by (40, -28) and a `\` wall (602) by (40, 28), offset 28 in y from the corner line. Beams (604) stand at corners.
- With V(a, b) = O + a(40, -28) + b(40, 28), cell (u, v) has its floor at V(u + .5, v + .5), `/` walls at V(u + .25, e + .25), `\` walls at V(e - .25, v + .75) and beams at V(a + .25, b + .25), which is the edge midpoint plus (0, 14).
- Original rooms leave the floor tile out under wall cabinets.
- A door is a wall with a gap (606 or 608) with the door (605 or 607) on the same spot. On the `/` axis both use dir 0. On the `\` axis the wall uses 128 and the door 128 (605) or 64 (607).
- Double doors have a 608 frame and a 607 or 611 door that span two wall edges, the anchor edge and the previous one along the run, which holds no wall piece. Single doors (606, 605, 617) cover one edge.
- The floor tiles fit two origins half a cell apart, and the two swap the `/` and `\` slots. The wall pieces decide which origin a level uses.

### Military lattice

- The fine lattice is (45, 32). Walls 650 and 651 are 2 units long and centered on integer points. The corner post 652 sits with the 650 that ends at the post.
- The energy-fence emitter 653 flanks fenced gaps (dir 0 for `\`, 128 for `/`). A gate is one piece wide, with the emitter pair where the wall piece would be, the fence 164 14 px above it and ordinary concrete walls on both sides. 654 is a decorative door wall. Military levels have no working doors.
- Floor 503 uses its own (80, 56) lattice and reaches under the walls. Otherwise a black band shows at the foot of far walls.

### Roofs and stacking

- Roof tiles (501) lie at z 90 on labs and 95 on military levels, on top of the walls. Their offsets vary by about 18 px between patches, so no lattice reproduces them.
- The engine sorts by y, so a tile at (y - 80, z - 80) lands on the same screen position but is drawn before the walls.
- Decor with z above 0 stands on whatever is under it, such as a monitor on a desk.

### Placement

- Wall collision reaches into the neighboring cell. A cell with no wall on its own edges can still lie inside a neighbor's wall box (±40 by 28 for lab walls), and the engine pushes anything spawned there through the wall. Players, monsters, items and spawners must clear the boxes.
- Originals keep spawners 1,000 to 3,000 px from their trigger, or behind 618 and 619 spawn walls when closer.
- Monsters are placed in tight 20 px grids, clusters and lines, plus about 100 singles.
- Breakable closets in labs are 4 to 9 cell pockets behind one 609 piece, under the roof. They hold 4 to 9 coins, ammo, a weapon or a perk.
- The breaker hangs on a `\` wall piece (anchor + (-8, +3), dir 0) facing the room. The room is small and has 429 transformers, a 430 corpse with blood, 403 shelves and a 180 pointer. The door panels 423 and 424 sit on the door frame with the door's dir at z about 5.
- The mounted cannon 357 appears once on each of levels 7 and 8, plus add-ons 1 and 2 levels 4 and 5. It has side 0 and needs about 120 by 90 px of free floor.

## Route model

The route analysis walks a model of the level the way a player would. It is used by `analyze` and by every rule that measures walking distance.

- **Cells.** Lab cells come from the 500 and 502 floors. Military cells come from the 503 floors and are split in two. Lab walls standing on 503 floor use the lab lattice.
- **Routing grid.** It is twice as fine as the cells. Pieces half a cell off the grid snap to its lines. A piece covers its real length, which is 2 fine edges, or 4 for double doors and frames.
- **Edge kinds in labs.** W is a wall. D is a door (605, 607) that blocks while doors are locked. F is a closed or jammed embedded door (z 0, or -13 to -35) that blocks. O is an open doorway, either a raised embedded door (z at or below -40) or an empty 606 or 608 frame. S is a last-resort passage (609 and 625 secret walls, 618 and 619 spawn walls, 612 and 616 holes). B is a broken wall (610, 626).
- **Military.** Frames 653 are open. The ones with a fence 164 standing in them are gates.
- **Solid objects.**
  - Every object with flag 0x20 blocks, and flag 0x40 objects block until shot but are passable in the model.
  - Flag 0 objects are walked through (truck 440, bio-container 443, corpses 430, boxes 428, lab kit 427).
  - The box is ±(0.33 · extent + 14) px around the object, player radius included. A cell is blocked when the boxes cover at least half of it.
  - The box size was fitted to rows of 400 (extent 90) and 413 (extent 20).
- **Doors in play.** A raised door lets the player walk through. A jammed one stops the player at the frame.
- **Gates.** `DoorsIsOpen = 0` locks doors until the breaker. A blackout block locks them once the route passes the trigger. Fences stay up until a generator dies.
- **Tour.** The walk goes to the nearest reachable objective and ticks off whatever it passes in range. While a gate is up it heads for the key first. The end is the last objective, and `p` is the distance walked at the first visit over the tour length.
  - Objectives are every charge pickup (40 px), every 179 site (while carrying a charge), the breaker (40 px), every generator (250 px), every 908 trigger (its radius), every placed monster (350 px), every spawner's group (350 px of the spawner or its waypoints) and the bosses.
  - A spawner's group opens when its trigger fires, or at the start for a spawner without a trigger.
  - S edges are used only when nothing else is reachable.
- The tour is greedy, so its lengths run longer than an expert's route.
- The door panels 423, 424 and 425 are decoration. 425 is the broken embedded-door panel.

## Alien Shooter 2 walk

The route analysis of the original levels does not fit Alien Shooter 2, whose floors follow no lattice. Its levels are walked over the engine's height grids instead (see [FORMATS.md](FORMATS.md#movement-and-collision)), by `analyze` and the map viewer.

- **Steps.** The walk visits the main-line quests (line 1) in `TimeLine` order.
  - A quest counts where a trigger area starts it, or where a script variable its starting stack waits for gets set, such as dynamite sites.
  - A start with no place, such as a dialog of an NPC parked off the map, is left out. A quest that a stack starts once another quest begins, with no trigger area, begins with that quest, so electric doors open as soon as the quest that shuts them down begins.
  - A later quest prefers a place the earlier ones have not used.
  - After a quest start come the sprites named by its `ChangeStateItem` entries, at the place their stacks move them to with `ACT_CHANGE_COOR`, such as monster generators that wait off the map. For a quest that no trigger starts, the entry is what starts it.
  - A `CSI_DEATH` entry becomes one step for each sprite of that name. The walk takes them nearest first from where the player stands, and numbers the steps in the order it reaches them. A sprite to destroy counts as reached within 450 pixels, about the reach of a shot on screen, and as destroyed once any walk passes that close, such as a device that the robot passes.
  - The level ends at the start of a quest with `WinOnBegin`, once a quest that a stack follows with such a quest begins, or else at the exits of the latest quest the map starts.
- **Ground.** The solid and outline objects, the objects linked to them and the dummy blocks 630 to 632 raise the two height grids with the heights of their current frames, and the ground under each footprint follows the scan of the engine, including its tolerance of 7. A step must keep to the climb and drop of the walker. The outline objects use their sprite heights instead of the outline. A step compares the ground of the footprint at the next cell with the height of the cell under the walker center, which is 8 pixels behind it instead of the 3 pixels of a game step, so the climb allowed over a slope is scaled by 17 / 12. A ledge must also keep to the climb on its own. A slope steeper than about 0.83 pixels of height per pixel, such as the barbed wire and the meshes of the generators, cannot be walked up.
- **Floor.** Only cells under some sprite can be walked on. The solid objects cover the cells they raise, and surface sprites and non-solid scenery cover their frame rectangles, and a sprite that has opened keeps covering its floor. This keeps the walk out of the empty space between the places of a map, such as the separate scenes of a map.
- **Boxes.** Objects and linked objects, such as the dummy blocks of the electric doors, whose `moveMask` shares a bit with the player's block their footprint where their heights meet the player's. Scenery (`spriteClass` 8), units, flying units, shells, the dummy markers of the editor and the objects the player can shoot down are left out.
- **Openings.** A sprite the scripts open, break or remove opens with the quest its stack or `if` block waits for, so the walk to a step only uses the openings of the quests begun by then. The grid is rebuilt whenever more of them open. Openings behind a trigger area or a script variable are described under Triggers. This keeps electric doors shut until a security computer is dealt with, and covers the boxes that play their death animation, such as the fang doors.
- **Open from the start.** The objects the player can shoot down (hittable, with hit points), such as barrels and glass partitions.
- **Triggers.** A sprite that opens, breaks or turns into something else when the player comes within the radius of a `FlagmanTrigger`, or once a trigger has set the variable it waits for, stays closed until the walk enters that area by the distance of the scripts. A trigger the walk passes fires on its own. When a step cannot be reached the walk goes to the trigger whose area it can reach and from which the step is reachable once everything is open, and only the triggers that set a variable, such as the button of a gate, become steps. A sprite no trigger opens is open from the start.
- **Downloads.** The question mark triggers that call `ScriptEvent16` are steps of the walk, in the order of their names, and the level ends with the last one when a quest that wins on begin follows. They happen while the quest before the winning one is active, so the sprites that open with a later quest are still closed.
- **Detours.** When a step cannot be reached, the walk begins the quests that open sprites by destroying their objects, such as one that begins when power units are destroyed, and tries again. Levels that need none are not changed.
- **Skips.** A key press that moves the player once a quest has begun, such as a space bar press, moves the player to the destination before the first step that comes after the quest, so the walk does not go back to the teleport marker.
- **Teleports.** Teleports, lifts and teleport triggers link their area to their goal point.
- **Search.** A* over 8 pixel cells, with 8 neighbors and no corner cutting, to a free cell within 64 pixels of the step, or inside the trigger area of a step that has one.
- **Controlled units.** A `ScriptEvent15` trigger gives the player control of a unit. When the player cannot reach a step, the player walks to such a trigger and the unit walks the steps with its own size, climb and drop until the player can reach one again. A panel next to the projection screen 2338 gives the player the robot 3040, which destroys the power units. A step nobody reaches gets a straight line.
- **Level scripts.** The `if` blocks of the level script are placed where their conditions come true: a `SizeTo(Flagman(0), x, y) < r` area, the places that start a quest it checks, or the places that set a variable it tests. This shows a water drain and a countdown timer.
