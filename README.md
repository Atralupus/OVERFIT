# OVERFIT

[한국어](README.ko.md)

OVERFIT is a 2D side-scrolling soulslike with one boss fight. Before each attack the boss makes a whole plan: how long to rest,
whether to run in, which move to open with, and whether to cancel that move into another one. In this version the boss plans at random.
A later version will read the player's dodging habits and plan against them.

The purpose is to check whether a boss that adapts to the player's dodging habits is fair and fun to fight. This version builds the moves,
the cancels, and the running that the adaptive boss will plan with.

## The fight

- One boss with 1,000 HP. The fighter has 220 HP and one life. A fight that lasts 10 minutes is a loss.
- The fighter can dash (a short invincibility), jump, guard (costs stamina), parry, and attack (one or two hits).
- A parry at the right moment stops the boss's move and exhausts the boss for 1.5 seconds. Hits also fill the boss's poise gauge,
  and a full gauge exhausts the boss the same way.

## Moves

Seven moves. Ticks are 1/60 s, counted from the tick the move starts. Each hit stays active for 8 ticks.

| Move | Hits (tick) | Damage | Avoided by | Notes |
|---|---|---|---|---|
| 3-hit combo | 51 · 93 · 159 | 8 · 8 · 14 | dash, guard, parry | A well-timed jump clears the first hit. Cancel points 78 · 144 |
| Off-beat 3-hit combo | 60 · 111 · 186 | 8 · 8 · 14 | dash, guard, parry | Same swings, but each windup is held 0.15 s longer. Catches players who parry by rhythm. Cancel points 87 · 162 |
| Fast 3-hit combo | 24 · 51 · 84 | 8 · 8 · 14 | dash, guard, parry | First hit after 0.4 s. Catches a 2-hit combo started right after the previous move. Cancel points 36 · 69 |
| Rush | 24 after arriving | 14 | dash, guard, parry | Runs at 3,600 px/s to 280 px in front of the player, then hits |
| Grab | 36 | 25, held 1 s | jump only | A white orb flies to the player for 0.6 s first |
| Jump attack | 60 | 24 | jump only | One leap. The landing covers the whole floor; dash invincibility, guard, and parry do not help |
| Uppercut | 51 | 14 | dash, guard, parry | Same timing as the 3-hit combo's first hit, but it reaches high. A player who jumps early to clear the combo is hit |

| <img src="docs/gifs/rush.gif" width="420"> | <img src="docs/gifs/grab.gif" width="420"> |
|---|---|
| **3-hit combo → rush** | **3-hit combo → grab** |
| <img src="docs/gifs/offbeat.gif" width="420"> | <img src="docs/gifs/jump3.gif" width="420"> |
| **Off-beat 3-hit combo** | **Jump attack** |

The GIFs were recorded from fixed scripts before this version. The first two show the old "1 hit → rush" and "1 hit → grab" patterns,
which are now a 3-hit combo cancelled after its first hit. The last one shows the old three-jump version of the jump attack.

## Plans

```
rest (in place, turning to face the player) → [run] → first move → [cancel] → follow-up move → next plan
```

- **Rest.** 0.4, 0.8, or 1.2 seconds. The rest belongs to the move after it: after a short rest the fast 3-hit combo catches a 2-hit combo
  pressed during the previous move's recovery, and after a long rest nothing does.
- **Run.** If the plan runs, the boss runs at 840 px/s (twice the fighter's walking speed) to 280 px in front of the player and starts
  the first move when it arrives. It turns to face the player on every tick. If the player is already within 280 px, it does not run.
  A run that lasts more than 3 seconds stops where it is. While resting, the boss stays in place.
- **Cancel.** The three combos have cancel points at the moment the next hit's windup would start. At a cancel point the boss drops
  the rest of the move, turns to face the player, and starts the follow-up move on the same tick, without resting.
  One cancel per plan; the follow-up plays to the end. If the boss is exhausted, the plan ends and the cancel is dropped.
- **Random picking.** Each decision (first move, rest, run, cancel, cancel point, follow-up) uses its own seeded random stream
  (`overfit/core/Det.cs`), so the same seed gives the same boss. Half of the combos are cancelled and half of the plans run
  (`picker` in `overfit/data/balance.json`).
- **Data only.** A move added to `overfit/data/patterns.json` and to the roster in `stages.json` becomes both a first move and a follow-up
  for every cancel point, without code changes. A cancel point added to a move adds a cancel to that move's plans.

## Replays

Each finished attempt adds one JSON line to `user://attempts/<session seed>.jsonl`: the plans, the inputs (run-length encoded;
the longest fight is about 27 KB), the dodge events, and a hash of the game data. Nothing is uploaded.

- `EXTRA="--history=<file> --attempt=N" tools/build.sh demo` replays an attempt with its saved inputs and compares the plans,
  the length, the result, and every dodge event. It logs `replay_match`, `[E] replay_mismatch` (determinism broke),
  or `[W] replay_data_changed` (the game data changed since the attempt).
- Lines written by earlier versions have no inputs and cannot be replayed (`[E] replay_no_inputs`).
- The result screen shows how the boss planned: the number of plans and cancels, the dodges used in the fight,
  how many times each move appeared and hit, and each cancel pair.

## The adaptive boss

Version 0.9 (tag [`v0.9.2`](https://github.com/Atralupus/OVERFIT/tree/v0.9.2)) had a neural network that predicted,
from the player's stage-1 dodges, how likely the player was to be hit by each stage-2 pattern. It was removed with this version's
move rework, because its outputs were tied to the old patterns. A new network that plans from recent and accumulated habits comes in
slice 4 of the [design](docs/superpowers/specs/2026-09-29-똑똑한-보스-design.md) (Korean).

## Where things are

| Part | Location |
|---|---|
| Moves, cancel points | `overfit/data/patterns.json` |
| Boss numbers (rest, run) | `overfit/data/bosses.json` |
| Picker settings | `overfit/data/balance.json` (`picker`) |
| Plans and cancels | `overfit/battle/rules/BossPlan.cs`, `PatternPickers.cs`, `PlanFlow.cs`, `BattleSim.cs` |
| Running | `overfit/battle/rules/BattleSim.cs`, `RushMotion.cs` |
| Attempt log, replay | `overfit/battle/rules/AttemptLog.cs`, `InputTape.cs`, `Replay.cs`, `overfit/battle/debug/BattleDemo.cs` |
| Bot fleet, data factory | `overfit/battle/rules/FleetBot.cs`, `BotTraits.cs`, `tools/factory/` |

## Play

macOS builds are on the [Releases](https://github.com/Atralupus/OVERFIT/releases/latest) page. The build is not signed.
If macOS blocks it, open System Settings → Privacy & Security and choose Open Anyway.

| Action | Key |
|---|---|
| Move | ← → (A D) |
| Jump | Space |
| Dash | Shift |
| Attack | J (press again for a 2-hit combo) |
| Guard | ↓ (S), hold |
| Parry | K |

Building from source needs Godot 4.7 (mono) and .NET 8. See [CONTRIBUTING.md](CONTRIBUTING.md) (Korean).

| Command | What it does |
|---|---|
| `tools/build.sh check` | Format, build, rule tests, `.uid` pairs, hitbox shapes. The commit gate |
| `tools/build.sh smoke` | Boots the game headless and tours the scenes |
| `tools/build.sh demo [seed]` | One headless fight with a bot |
| `tools/build.sh factory` | The bot fleet fights the boss and writes samples |
| `tools/build.sh shots` | Screenshots |
| `tools/build.sh export` | macOS build |

## License

Code: [MIT](LICENSE). Art: three CC0 packs, not included in the repository. The list is in the in-game credits and
[`overfit/data/credits.json`](overfit/data/credits.json).
