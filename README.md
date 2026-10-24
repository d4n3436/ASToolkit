# ASToolkit

This repo provides a toolkit for Alien Shooter (GOG version 1.2) with a level generator, a widescreen patch and level analysis tools, all in one command-line program called `ascli`.

It is written in C# on .NET 10 and works on an installed copy of the game.

> [!NOTE]
> While AI was used in sections of code that was hard to get right, like level generation, I made sure everything is working as intended.

## Features

- **Level generation:** Builds new levels in the style of the originals and installs them as a new add-on called RANDOM, with its own button on the play menu.
- **Map viewer:** A map viewer that faithfully recreates the Alien Shooter 1/2 rendering engines and supports both games
- **Widescreen patch:** Adds more resolutions, a borderless window mode and a HUD that fits wide screens.
- **Level analysis:** Works out the route a player takes through a level and draws it over an image of the map.
- **Rule checks:** Tests levels against the rules the generator follows.

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
| `check` | Checks campaign levels 2 to 10 against the level rules |
| `inspect object`, `sprites`, `stacks`, `vid` | Prints the objects, sprites, action stacks and sprite files of a game |

Run a command with `--help` to see its options.

## Random level generation

The generator builds each level around the route the player will take, following how the original levels are designed.

1. **Layout:** It lays out a main route of halls and rooms with side rooms, branches and closets, and gates part of it behind locked doors, a blackout or electric fences, as the original level does.
2. **Structure:** Walls, doors and floors are built around the layout in the look of the lab and military levels, and decoration is copied from the original levels.
3. **Content:** Monsters, bosses, items, ammo and dynamite are placed along the route, using budgets taken from the original levels so that difficulty rises as the level goes on.
4. **Checks:** Each candidate is tested against the rules in [docs/RULES.md](docs/RULES.md). The generator builds up to 40 candidates until one breaks no hard rule and keeps the best one, which gets a roof that hides everything outside the walkable floor.

### The RANDOM add-on

`generate` writes the levels into the game as a new add-on and adds a button for it to the play menu. Both styles produce the same add-on.

- **Campaign style** has ten levels, like the main campaign.
- **Add-on style** (`--addon-style`) has five levels, like the official add-ons.

`--seed` picks the levels, and the same seed gives the same ones. `--first` and `--last` choose which campaign levels the generator replaces in campaign style. `restore` returns the game to its original state.

## Widescreen patch

`widescreen apply` patches the executable and a few game scripts. It requires the stock GOG 1.2 executable and keeps backups, which `widescreen restore` puts back.

## Toolkit GUI

A GUI for the toolkit is included on the repo (the `ASToolkit.Gui` project). It currently has a RES file inspector and a map viewer.

## Project layout

```
src/ASToolkit.Core    File formats, level generation, rules, route analysis and patchers
src/ASToolkit.Cli     The ascli program
src/ASToolkit.Gui     A GUI for the toolkit
tests/                Unit tests
docs/                 Documentation
```

## Documentation

- [docs/GENERATION.md](docs/GENERATION.md) explains how the generator builds a level, stage by stage, and where to look when a level or rule goes wrong.
- [docs/RULES.md](docs/RULES.md) lists the rules the generator follows and checks, with the measurements from the original levels behind them.
- [docs/FORMATS.md](docs/FORMATS.md) describes the game's file formats.
- [docs/NOTES.md](docs/NOTES.md) describes how the game's scripts behave and how the original levels are built.

## Tests

```
dotnet test
```

Tests that need the game files are skipped unless the `ALIEN_SHOOTER_TEST_GAME` environment variable points to an installed game folder.
