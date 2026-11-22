# ASToolkit

This repo provides a toolkit for Sigma Team's Alien Shooter (GOG version 1.2) with a level generator, a game simulator, a widescreen patch and level analysis tools, all in one command-line program called `ascli`.

It is written in C# on .NET 11 and works on an installed copy of the game.

> [!NOTE]
> While AI was used in sections of code that were hard to get right, like level generation, I made sure everything is working as intended.

## Features

- **Level generation:** Builds new levels in the style of the originals and installs them as a new add-on called RANDOM, with a button on the play menu
- **Map viewer:** Displays Alien Shooter levels using a faithful, direct port of Alien Shooter's engine.
- **Widescreen patch:** Adds more resolutions, a borderless window mode and a HUD that fits wide screens
- **Level analysis:** Works out the route a player takes through a level and draws it over an image of the map

## Usage

```
dotnet run --project src/ASToolkit.Cli -- <command> --game <game folder>
```

| Command | Description |
|---|---|
| `generate` | Generates random levels and installs them as the RANDOM add-on |
| `restore` | Removes the RANDOM add-on and the changes it made |
| `widescreen apply` | Applies the widescreen patch |
| `widescreen restore` | Removes the widescreen patch |
| `preview` | Generates one level into a map file |
| `analyze` | Draws the route through original or generated levels |
| `simulate` | Plays the original campaigns with a bot player, in order, keeping equipment and shopping between levels |
| `inspect level` | Surveys levels into their places, connections and gates, with a render of the places |
| `inspect design` | Measures furniture, pickups, walls and pacing in the original levels and in generated ones, and compares them |
| `inspect object`, `sprites`, `stacks`, `vid` | Prints the objects, sprites, action stacks and sprite files of a game |

Run a command with `--help` to see its options.

## Random level generation

The generator draws a whole campaign at random, builds each level as a building laid out phase by phase, and fills it along the route the player takes. A phase is the part of the building the player can reach before the next gate opens.

1. **Campaign:** It draws the whole campaign first, from curves fitted to the original campaigns, giving each level its wall set (basement or concrete walls), gate, monster hit points, weapons, pickups and end-of-level money.
2. **Building:** It lays out the level's building with the grammar for its building type. Labs are a hub of office pods with a second wing behind the gate, campaign warehouses follow the layout of the original level with the same number, and office blocks are one packed floor. The doors and locks follow the building's phases.
3. **Structure:** Walls, doors and floors are built around the layout in the look of the original basement and concrete levels, and a roof hides everything outside the walkable floor.
4. **Content:** Monsters, bosses, items, ammo and dynamite are placed along the route to the hit points and pickups the campaign gives the level, with difficulty rising as the level goes on. Fights, furniture, pickups and gore are spread and arranged the way the originals spread and arrange them.
5. **Read back:** Each finished level is read back with the route analysis that measures the original levels. When its route can't reach every objective, the generator lays the level out again, up to 8 times.

### The RANDOM add-on

`generate` writes the levels into the game as a new add-on and adds a button for it to the play menu. Both styles install as the RANDOM add-on.

- **Campaign style** has ten levels, like the main campaign.
- **Add-on style** (`--addon-style`) has five levels, shorter and harder like the official add-ons.

`--seed` picks the campaign, with the hit points of its monsters, its weapons, pickups and end-of-level money, and the same seed always gives the same campaign. In campaign style, `--first` and `--last` choose which of the ten levels are generated, and the others are copies of the campaign's levels. `restore` returns the game to its original state.

## Widescreen patch

`widescreen apply` patches the executable and a few game scripts. It requires the stock GOG 1.2 executable and keeps backups for `widescreen restore` to put back.

## Toolkit GUI

The repo includes a GUI for the toolkit (`ASToolkit.Gui` project). It has a RES file inspector, a map viewer and bot replay.

## Project layout

```
src/ASToolkit.Core    File formats, engine runtime, simulation, level generation, route analysis and patchers
  Formats/            Readers and writers of the game's files
  Engine/             Port of the Alien Shooter engine
    Objects/          Sprites and the sprite definitions that draw them
    Graphics/         Drawing a frame into the back buffer and depth buffer
    Systems/          Movement, enemy AI, animation effects, doors and weapons
    Spatial/          Collision and spatial queries, also used by the route analysis
    Resources/        The sprite catalog and the geometry read from sprite files
    State/            Screen effect states and the 16-bit color layout
  Simulation/         The player bot, navigation, campaign play, replay capture and results
  Scripting/          Level scripts and the common table
  Levels/             Cells, walls and objects of a level, shared by analysis and generation
  Routes/             Route analysis
  Survey/             Surveys of levels into places, connections and gates
  Design/             Design measures of furniture, pickups, walls and pacing
  Reference/          Models fitted to the original levels and campaigns
  Rooms/              The room atlas built from the original levels
  Generation/         The level generator
  Patching/           The RANDOM add-on installer and the play menu patch
  Widescreen/         The widescreen patch
  Rendering/          Map images
src/ASToolkit.Cli     The ascli program
src/ASToolkit.Gui     A GUI for the toolkit
tests/                Unit tests
docs/                 Documentation
```

## Documentation

- [docs/GENERATION.md](docs/GENERATION.md) explains how the generator builds a level, stage by stage, the guarantees every level keeps, and where to look when a level goes wrong.
- [docs/FORMATS.md](docs/FORMATS.md) describes the game's file formats.
- [docs/NOTES.md](docs/NOTES.md) describes how the game's scripts behave and how the original levels are built.

## Tests

The tests run using the standard .NET test driver:
```
dotnet test
```

Tests that need the game files are skipped unless the `ALIEN_SHOOTER_TEST_GAME` environment variable points to the game folder.
