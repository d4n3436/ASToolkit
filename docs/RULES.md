# Level rules

This document describes the rules the level generator follows and checks its levels against. Each rule is one class in `src/ASToolkit.Core/Rules/Checks/`, listed in `RuleSet` and run by `ascli check`. The numbers come from measuring the original levels (the campaign and both add-ons) with the route analysis described in [NOTES.md](NOTES.md).

## General information

- **Hard** rules reject a level, and the generator builds more candidates until one breaks none, keeping the one that breaks the fewest after 40. **Soft** rules add to a score that decides between candidates that break the same number of hard rules.
- **Originals** gives the measured values and **n**, the number of cases behind them. A rule resting on 4 bosses is weaker than one resting on 2,130 monsters.
- **Tour** is the walk through every objective. **p** is how far through the tour a point lies, from 0 at the start to 1 at the last objective. **px** are map pixels, and a screen is 1280 by 800.
- IDs start with A for all levels, B for a level style, C for dynamite levels and D for uncommon items.

Where the originals vary a lot from level to level (monster split, clutter, fences, closets), the target is per level. It is taken from the campaign level that the generated level replaces (section E). Other ranges pool all 17 originals, which are campaign levels 2 to 10 and both add-ons.

The placement of loose loot off the main route, the density of the dried blood and the props of each room have no rule. They are described in [GENERATION.md](GENERATION.md) and stay within the decor rules A13, A14 and A17.

---

## A. All levels

**A1 Clutter (Soft, Hard below 60%).** Solid objects (flag 0x20, such as containers, shelves, cabinets, desks and machines) block the original's share of the floor, within 30%. The count of breakables (flag 0x40, such as crates, barrels and chairs) is the original's, within 30%. Stacks stand against walls or form lanes, with no single props scattered in a hall.
- Originals blocked 2 to 16% (level 4 16%, levels 2 and 3 7 to 8%, military 2 to 8%) and have 69 to 237 breakables per campaign level (n = 9 levels).

**A2 Everything reachable (Hard).** Every objective can be reached on foot. Walls and solid objects block. Secret walls, spawn walls and monster holes do not count as a way through, so the tour must finish without its fallbacks.

**A3 Compact tour (Soft).** The tour is 0.7 to 1.3 times the level's reference length (section E).
- Originals run from 3,345 px (level 2) to 43,732 px (level 9) in the campaign and 6,714 to 34,886 px in the add-ons.

**A4 Little walking back (Hard above the ceiling).** The share of the tour that re-walks covered ground is at most the original's plus 3 points, with the ceiling never below 6% or above 16%. Objectives lie along a main route with short side rooms.
- Originals re-walk 1 to 15% in labs (mean 5%) and 4 to 15% in military levels (mean 10%).

**A5 Monster split (Soft).** For each monster group the generator draws whether it is placed or comes from an ambush. The chance of an ambush is the replaced original's share of monster hit points that come from ambushes, kept between 5% and 95% so that every level varies a little. The share of hit points from ambushes in the level must be within 30 points of the chance.
- Chances by level are 5% (2), 15% (3), 65% (4), 89% (5), 94% (6 and 7), 86% (8), 88% (9) and 95% (10).

**A6 Ambushes early (Soft).** The middle half of the ambush triggers lies within p 0.05 to 0.50, and at most 10% lie past p 0.75.
- Originals have the middle half at p 0.01 to 0.40 over all 17 levels and 0.10 to 0.40 in the campaign (n = 271 spawners).

**A7 Ambush spawners out of sight and ahead (Soft, with Hard caps per level).**
- A spawner is 900 to 3,500 px in a straight line from its trigger.
- At least 85% lie ahead on the tour or in a side room that opens onto it.
- At most 10% lie behind on cleared ground over the campaign, and at most 25% on any one level.
- At most 5% sit right at their trigger, except lab spawn closets and the military pad ambushes of C6.
- At most 10% are within a screen of their trigger (|dx| < 640 and |dy| < 400), not counting spawn closets.
- Originals have 57% ahead, 31% in a side room, 8% behind and 4% at the trigger. The distance runs from 978 px (10th percentile) to 2,944 px (90th) in the campaign (n = 132).

**A8 Loot in crates (Soft).** The share of pickups inside breakable crates is 25 to 40% for ammo, 40 to 50% for armor, 30 to 40% for weapons and 15 to 25% for health. Dynamite (301) is never in a crate.
- Originals have 27, 43, 29 and 16% over all 17 levels and 36, 46, 40 and 21% in the campaign.

**A9 Checkpoint (Hard where the original has one).** Levels 4, 5, 8 and 10 get a checkpoint at p 0.08 to 0.50, on the main route, in a cell that is safe to respawn in (never next to a wall). On level 4 it is the blackout trigger (B2). Other levels get none.
- Originals have it at p 0.08 (level 4), 0.48 (5), 0.20 (8) and 0.31 (10).

**A10 Placed monster groups (Soft).** Groups are laid out as in the originals (grids, clusters and lines), weakest early and strongest last when there is no boss. The median p of the placed monsters is within 0.25 of the original's, for levels with at least 4 placed monsters.
- Campaign medians are 1.00 (2), 0.70 (3), 0.15 (4), 0.18 (5), 0.57 (6), 0.45 (7), 0.28 (9) and 0.56 (10).

**A11 Difficulty (Soft, within 15%).** Each level's total monster hit points are the campaign level's own times 1.15. Money paid after a level is the campaign's own table. Pickups follow the campaign level's count (within 10% for each kind, stash contents included). Monsters, pickups, dressing, keypads and dynamite sites are drawn from campaign levels 2 to 10 only.

| level | campaign | target |
|---|---|---|
| 2 | 336 | 386 |
| 3 | 1,682 | 1,934 |
| 4 | 6,526 | 7,505 |
| 5 | 21,261 | 24,450 |
| 6 | 27,894 | 32,078 |
| 7 | 52,923 | 60,861 |
| 8 | 197,861 | 227,540 |
| 9 | 1,036,618 | 1,192,111 |
| 10 | 745,142 | 856,913 |

**A12 Every level has a gate (Hard).** A level whose original has no gate (level 5) draws a mild one at random from the kinds its theme supports. For labs that is a locked wing opened by a breaker, or a blackout with a breaker. Mild means:
- 15 to 35% of the level lies behind the gate.
- The key is reached by p 0.40.
- The first gate it opens is within 700 px of it (B1.3).

The mildest gate in the originals is level 6's fences, at 14%.

**A13 Decor matches the theme (Hard).** No decor that the campaign uses only on levels of the other theme. The set is computed from the campaign levels. The add-ons put the missile 437 (which sets off a nuke effect and a strong earthquake when broken) into labs, while the campaign uses it only on levels 6 and 10.

**A14 Decor keeps its distance from walls (Hard and Soft).** Every floor object (z below 5) stands at least as far from the nearest wall, door or fence edge as the closest instance in any original of the theme. At most 12% of them stand closer than 95% of the originals' instances.
- Measured per object id from the originals. The 5th percentile in lab cells is 0.62 for the red container 400, 0.86 for the bio-container 443 and 0.51 for shelves 403.

**A15 Layout fills its bounding box (Hard and Soft).** The floor area over the floor's bounding box is at least 0.25 and within 0.08 of the original's.
- Originals have 0.32 to 0.50 for campaign labs (level 2 0.41, 3 0.45, 4 0.50, 5 0.32), about 0.49 for add-on labs and 0.26 to 0.46 for military levels.

**A16 Short main way, deep branches (Soft).** Two numbers, each within the range spanned by the original's value and the blend of the campaign and add-on levels.
- Spine share, the walk from the start to the tour's farthest point over the tour length (within 0.08). Originals have 0.75 (level 2), 0.41 (3), 0.33 (4), 0.44 (5), and 0.16 and 0.12 for add-on 1 levels 2 and 3.
- Elongation, that walk over the square root of the floor area (within 0.4). Originals have 1.98 (level 2), 2.44 (3), 3.27 (4), 3.83 (5) and 1.14 for add-on 1 levels 2 and 3.
- Add-on 1 level 2 is the model. It is a compact block with a short main way and one large branch, which holds 69% of its floor at more than 1,000 px depth, and the tour enters it and backtracks out.

**A17 Decor faces its walls (Hard).** A floor object (z below 5) within 1.5 cells of a wall has a frame that the originals of the theme use for that object against walls of that direction (checked with at least 5 samples). A frame the originals lean on a wall must have a wall within 1 cell.
- Leaning frames are those the originals put within 1 cell of a wall at least 40% of the time, and four times as often as the object's other frames. That picks the soldier corpse 430 frames 0 and 1 (slumped on `/` and `\` walls) and one board variant (668 frame 3).
- A leaning corpse in the open is allowed when it lies on a blood pool (110 or 916). The campaign does this five times.

**A18 Decor context (Soft).** Every computer sits on a table, within 2 cells of one.
- The campaign labs keep every computer (410 or 411) on a table (404). The check allows 2 cells, the distance of one original dead computer from its table.

---

## B. Level styles

### B1 Locked doors, lab (levels 2 and 3)

The script starts with `DoorsIsOpen = 0`. The player explores the open part, finds the breaker, and the locked doors open.
- **B1.1 (Hard)** At least half of the level is behind the locked doors, within 10 points of the original (level 2 53%, level 3 70%).
- **B1.2 (Soft)** The walk from the start to the breaker is the original's within 30% (1,538 px on level 2, 2,881 px on level 3).
- **B1.3 (Hard)** The first locked door the breaker opens is within 700 px of walking from the breaker. The originals have 391 to 635 px.
- **B1.4** The breaker room follows D1.

### B2 Blackout, lab (level 4)

The lights go out at a trigger near the start. The doors lock, and the player searches for the breaker in the dark.
- **B2.1 (Hard)** The blackout trigger is within the first 2,000 px of the tour, on the main route, in a spot the player cannot miss (a doorway or corridor). The originals have 1,782 px (level 4) and 1,270 px (add-on 1 level 2).
- **B2.2 (Hard)** The search from the blackout to the breaker, with every door locked, is 4,000 to 6,500 px of walking. The originals have 6,274 px and 4,150 px.
- **B2.3 (Soft)** The checkpoint is the blackout trigger.
- **B2.4 (Soft)** The search passes jammed and raised embedded doors (611 and 617), so some ways are shut and some open. It ends in a dead-end breaker room (D1).

### B3 Ungated lab (level 5)

The original has no gate, so the generated level draws a mild one (A12). It has a boss (860) at the end of its wing (D3), one checkpoint near p 0.48, many secret closets (D4) and mostly ambushes (A5).

### B4 Fenced military (levels 6 to 10)

Electric fences (164 in 653 frames) shut off part of the base until a generator (415) is destroyed. Military levels have no lockable doors.
- **B4.1 (Hard)** The share of the level behind the fences is the original's within 10 points (level 6 14%, 7 67%, 8 52%, 9 26%, 10 84%).
- **B4.2 (Hard)** The first generator, the one that drops the fences, stands in the open part and is visited at p 0.08 to 0.30. A second generator in a wing of its own (level 10) may come later. The originals visit the first one at 0.10 to 0.29 and level 10's second at 0.43.
- **B4.3 (Hard)** The nearest generator is at least 800 px of walking from its fence. The originals have 828 to 3,258 px.
- **B4.4** Generator placement follows D2.
- **B4.5** Level 6 has a breaker (418) about 250 px from its generator. It opens nothing and is dressing in the generator's building, not an objective.

---

## C. Dynamite levels (3, 4, 7, 8 and 9)

The win needs a charge (301) planted at every site (179). A site shuts down a monster hole with endless generators (80x) behind it in labs, or teleport pads (450) with their generators in military levels.

- **C1 (Hard)** The number of sites is the original's (level 3 has 1, level 4 has 3, level 7 has 1, level 8 has 6, level 9 has 9). On campaign levels the number of charges equals the number of sites.
- **C2 (Hard)** Sites come late. The first is at p 0.40 or later and the median at p 0.60 or later. Every campaign original passes, with first sites at 0.41 to 1.0, a 10th percentile of 0.46 and a median of 0.73. The add-ons plant their first site as early as p 0.10, so the rule is for campaign levels.
  - On gated levels the generator ranks site rooms by a greedy nearest-room walk from the key room, which is how the tour sweeps after the key, and puts sites in the rooms that walk reaches last. Charges are placed by distance from the key room and keep off the way from the start to it.
- **C3 (Soft)** Charges come in batches. Each batch lies in one spot and holds as many charges as the run of sites that follows it on the tour. Level 8 has 3 charges, 3 sites, 3 charges and 3 sites.
- **C4 (Hard)** A charge lies 250 to 3,200 px in a straight line from the nearest site. The originals have 276 to 3,072 px, with a median near 1,200.
- **C5 (Soft)** The last site is the tour's last objective, as in 6 of 10 originals.
- **C6 (Hard)** Site shapes. Labs use a 616 or 612 hole in a back wall with the 179 about 25 px in front and generators behind. Military levels use blue pads with an ambush right at the pad. Every site is within 150 px of a hole or spawn wall (labs) or 260 px of a pad (military). The originals are 27 to 44 px and 55 to 76 px.
- **C7 (Hard)** Solid decor keeps clear of a site. Its footprint stays at least 16 px from the 179 (30 px for objects 40 px or wider). The generator also keeps standing non-solid objects of 20 px or more (crates, barrels, chairs, the bio-container 443) that far away. Within 35 px of a site 88% of the originals' 49 sites have no clutter.

---

## D. Uncommon items

**D1 Breaker room (418), levels 2, 3 and 4.** n = 5 working breakers (levels 2, 3 and 4, add-on 1 level 2 and add-on 2 level 4) plus level 6's dressing one.
- A small room with a single doorway, off the main way, which the tour enters and leaves. The breaker hangs on the far wall, facing the door.
- **D1.1 (Hard)** In view. The breaker hangs on a back `\` wall, with open floor in front and beside it on the +v side. The wall run it hangs on ends in a corner at both ends, or runs at least 3 edges past it. The originals have 3 to 6 cells of floor in front and 2 to 22 beside. The generator wants 3 in front and 2 beside, accepts 2 and 1 when no wall has more, and the check needs 2 and 1.
- **D1.2 (Soft)** At least 2 transformers (429) within 400 px.
- **D1.3 (Soft)** Ambush triggers within 400 px of the breaker, some on blackout levels and none on locked-door levels. On level 4 (7 ambushes) and add-on 1 level 2 (3), restoring the power releases a fight.
- Dressing is 2 to 4 transformers, a corpse (430) with blood, sometimes shelves (403) and a pointer (180), reused from the original rooms.

**D2 Generators (415), levels 6 to 10.** n = 11 on 6 levels.
- They stand against the back wall of a dead-end room or corridor in the open part of the base, usually next to power equipment (transformers 429, containers). Several can share a room (level 8 has 2, add-on 2 level 4 has 4). Level 10's two are in separate wings.
- Counts are 1 on levels 6, 7 and 9 and 2 on levels 8 and 10. Ammo or armor often lies nearby, up to 7 pickups.
- **D2.1 (Soft)** The original's number of generators, and no ambush trigger within 300 px of one unless the original has it there (level 6 has 5).

**D3 Bosses.** Level 3 has 850, level 5 has 860, level 8 has 867, and level 10 has 866, released by an ambush. n = 4.
- A boss stands at the far end of a dead-end room or wing, facing its entrance, with open floor to fight on (at least 600 by 400 px clear of solid objects) and corpses and blood around.
- The escort follows the original. Level 3 has about 50 placed monsters within 600 px, level 8 has three teleport pads beside the boss, and level 5 has a few small monsters and ambushes around a large control room.
- It is visited at p 0.55 to 1.0 (originals 0.58 to 0.99). When it is not the last objective, the strongest placed group comes after it.

**D4 Secret closets (breakable wall 609 or 625), lab levels only.** n = 42 closets in 7 lab levels.
- **D4.1 (Soft)** The count is the original's within 2 (level 2 has 2, level 3 has 9, level 4 has 5, level 5 has 11). Military levels have none.
- **D4.2 (Soft)** A closet is 2 by 2 to 3 by 3 floor tiles behind one breakable wall piece, under the roof, opening onto a room or corridor that the tour passes. Closets spread along the whole tour (p 0.05 to 0.96). With 3 or more, the first is at p 0.3 or earlier and the last at 0.7 or later.
- **D4.3 (Soft)** Contents in the originals (38 closets with contents):

  | content | share |
  |---|---|
  | money pile of 3 to 12 | 42% |
  | ammo cache of 2 to 4 boxes of 1 to 3 kinds | 21% |
  | one weapon, with up to 4 of its ammo | 26% |
  | one perk, implant, armor or health, sometimes with money | 11% |

  A weapon may be up to two levels early (original level 4 hides the flamethrower 267, normally from level 6). A closet's weapon comes out of the level's weapon count (D7). At most half the closets hold a weapon.
- **D4.4 (Soft)** Up to 2 breakable walls per lab level may join two areas that are both reachable without them, so that breaking through saves a walk of at least 600 px. The tour must not need them, and they do not count as closets. The originals have 2 on level 4 and 8 on add-on 1 level 2.

**D5 Mounted cannon (357), levels 7 and 8 only.** n = 6.
- Exactly one, standing in the open middle of the largest hall or yard on the main route, never in a side room or dead end.
- Free floor around it, with the nearest solid object at least 100 px away (116 px on level 7 and 231 px on level 8 in the originals).
- It faces a long open approach, and at least two ambush groups or a large placed horde cross that floor towards it. A striped floor marking is common.
- It is visited at p 0.15 to 0.80 and is never the last objective.

**D6 Spawn walls (618 and 619) and monster holes (612 and 616).** The first lab ambushes burst out of closets. The tour treats these only as last-resort passages.
- **(Hard)** A spawn wall that blows open (one with a trigger program) faces along its wall, with dir 0 for 618 (`/`) and dir 64 for 619 (`\`). Every triggered 619 in the originals has dir 64 (13 of 13). A 619 at dir 0 leaves its broken frame standing across the wall.

**D7 Weapons.** n = 9 campaign levels.
- The count is the replaced original's, loose and in crates (level 2 has 1, level 3 has 2, level 4 has 4, level 5 has 2, level 6 has 2 and levels 7 to 10 have 1).
- The kinds are the original's own. One that is not yet allowed on the level goes to a secret closet or is swapped for an allowed one.
- **D7.1** The count is within 1 of the target (Soft) and at most 2 over (Hard).
- **D7.2** No 600 px circle holds more than 2 weapons (Hard), and the closest two are at least 500 px apart (Soft). Originals keep two weapons at least 592 px apart.

---

## E. Per-level targets

| level | style | blocked | breakables | ambush chance | fenced | locked | sites | closets | checkpoint p | boss | cannon | revisit ceiling | tour reference px | HP target |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2 | locked lab | 7% | 69 | 5% | | 53% | 0 | 2 | | | | 6% | 13,300 | 5,200 |
| 3 | locked lab | 8% | 190 | 15% | | 70% | 1 | 9 | | 850 | | 6% | 17,300 | 12,700 |
| 4 | blackout lab | 16% | 237 | 65% | | | 3 | 5 | 0.08 (blackout) | | | 16% | 17,000 | 28,300 |
| 5 | lab, mild gate | 6% | 149 | 89% | | | 0 | 11 | 0.48 | 860 | | 6% | 16,600 | 61,100 |
| 6 | military | 7% | 73 | 94% | 14% | | 0 | 0 | | | | 10% | 16,300 | 103,100 |
| 7 | military | 8% | 168 | 94% | 67% | | 1 | 0 | | | 1 | 16% | 24,500 | 196,600 |
| 8 | military | 7% | 90 | 86% | 52% | | 6 | 0 | 0.20 | 867 | 1 | 12% | 24,700 | 365,800 |
| 9 | military | 8% | 194 | 88% | 26% | | 9 | 0 | | | | 16% | 32,100 | 671,800 |
| 10 | military | 2% | 121 | 95% | 84% | | 0 | 0 | 0.31 | 866 (ambush) | | 7% | 25,800 | 705,400 |

The revisit ceiling is the original's value plus 3 points, floored at 6% and capped at 16%. The tour reference blends the campaign and add-on lengths (A3). The hit point target is the campaign's own (A11).

## Limits

- The tour is a nearest-next walk, so its lengths run higher than a player's.
- With branches, a late point in the tour is not necessarily far from the start. On gated levels the dynamite sites go to the rooms farthest from the breaker room.
- Rules resting on 6 cases or fewer (breaker rooms, cannons, bosses, the blackout checkpoint) are weaker than the others.
- The add-ons expect a player carrying more gear than the campaign's item rules hand out, so money and ammo may need raising after play-testing.
