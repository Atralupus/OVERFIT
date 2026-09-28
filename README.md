# OVERFIT

[한국어](README.ko.md)

OVERFIT is a 2D side-scrolling soulslike with one boss fought in two stages. Stage 1 records how the player avoids attacks.
In stage 2, a neural network reads the record and the boss picks its attack patterns from the network's predictions.

The purpose is to check whether a boss that adapts to the player's dodging habits is fair and fun to fight.

## Stage 2 patterns

Each new stage-2 pattern punishes one habit from stage 1.

| <img src="docs/gifs/rush.gif" width="420"> | <img src="docs/gifs/grab.gif" width="420"> |
|---|---|
| **1 hit → rush → 3 hits**<br>Targets players who stay back and attack only during the boss's recovery. | **1 hit → grab**<br>Targets players who only dash. |
| <img src="docs/gifs/offbeat.gif" width="420"> | <img src="docs/gifs/jump3.gif" width="420"> |
| **Off-beat 3-hit combo**<br>Targets players who parry a lot. | **Jump attack ×3**<br>Targets players who guard. |

The fifth stage-2 pattern is the stage-1 3-hit combo. The GIFs come from fixed scripts with a fighter that follows
one habit (`tools/build.sh gifs`). These pairs are the design intent. The pairs the network learned are listed under [Results](#results).

## Neural network

```
bot fleet ─▶ data factory ─▶ training (NumPy) ─▶ network.json ─▶ stage-2 pattern picker (C#)
```

For each of the five stage-2 patterns, the network predicts the chance that the player gets hit by the pattern.
The network does not choose patterns. A fixed rule turns the five predictions into the boss's pattern list,
so odd network outputs cannot break the game.

### Input

19 numbers computed from the player's dodge events in the current run (stage 1 and any stage-2 retries):

| Feature | Meaning |
|---|---|
| `dash_timing_bias`, `dash_timing_var` | Mean and variance of dash timing error (early or late) |
| `dash_direction_bias` | Dashes toward the boss or away from it |
| `jump_timing_bias` | Mean jump timing error |
| `jump_reliance`, `parry_reliance` | Share of attacks where jump or parry was chosen although another option would also have worked |
| `airborne_at_impact` | Share of boss attacks that met the player in the air |
| `parry_rate` | Share of parries that succeeded |
| `greed` | Share of boss attacks that met the player in the middle of an attack |
| `distance_bias` | Average distance to the boss (px) |
| `guard_rate` | Share of boss attacks answered with guard |
| 8 counts | Number of events behind the values above: total, dash, jump, parry, guard, guard broken, jump possible, parry possible |

The counts let the network tell a value measured from 3 events apart from the same value measured from 300.
`PlayerFeatures.From` computes the input. The data factory and the game call the same function.

The pattern is not an input. Each pattern has its own output. Pattern tags (parryable, dash window, range, …) are not used,
because the 3-hit combo and the off-beat 3-hit combo have identical tags and differ only in timing.

### Training data

One attempt produces about 20 dodge events, and no existing model uses this game's measurements. Training data comes from bots.

- **Bot fleet.** 100,000 bots. Half rely on one option (dash, jump, parry, guard, or keeping distance). The other half mix
  the four defensive options. Each bot also has a reaction time, timing noise, greed, preferred distance, and combo habits.
  Bots send the same inputs as a player and fight under the real battle rules, without rendering.
- **Data factory** (`tools/build.sh factory`, a .NET console). Each bot plays stage 1 until it wins (up to 5 tries), then stage 2
  (up to 5 tries) with patterns drawn at random. Each stage-2 pattern instance becomes one sample: the 19 inputs at the start of
  the attempt, the pattern, and a label (hit or not hit). 1,383,287 samples in about 105 seconds on 4 cores.
- Random numbers are looked up by seed, stream, and key instead of drawn in sequence (`overfit/core/Det.cs`).
  The same seed and commit give byte-identical data, regardless of thread count.

### Model and training

- MLP 19 → 32 → 32 → 5 with ReLU, about 1,900 parameters. NumPy with hand-written backpropagation, checked against finite differences.
- Binary cross-entropy on the drawn pattern's output only. Output biases start at each pattern's base-rate logit.
- Split by bot: 80% train, 10% validation, 10% test. Inputs standardized with training statistics. Adam, batch 1024, early stopping.
- `tools/build.sh train` writes `overfit/data/network.json` (weights, standardization, base rates, training source) and `ml/report.md`.

Test set (10,000 bots not used in training):

| Pattern | Log loss | Base-rate log loss | ECE | AUC |
|---|---|---|---|---|
| 3-hit combo | 0.6131 | 0.6466 | 0.0085 | 0.653 |
| Jump attack ×3 | 0.4595 | 0.4944 | 0.0052 | 0.687 |
| 1 hit → rush | 0.6060 | 0.6473 | 0.0123 | 0.669 |
| 1 hit → grab | 0.3639 | 0.3939 | 0.0090 | 0.695 |
| Off-beat 3-hit combo | 0.6033 | 0.6296 | 0.0066 | 0.638 |

### In the game

- **Loading.** At boot the game loads `network.json`. `PlayerNet` runs the forward pass in C# with only + − × ÷ and comparisons,
  in a fixed order. On 20 fixed inputs, Python and C# give bit-identical logits. `tools/build.sh check` compares them on every commit.
  If the game data changed after training, boot logs `[net][W] stale`.
- **Selection rule** (`NetworkPicker`), once at the start of each stage-2 attempt:
  1. Compute the 19 inputs from the run's records. Fewer than 20 events: use all five patterns.
  2. Get a logit for each pattern. Lift = logit − base-rate logit: how much more likely this player is to be hit than an average bot.
  3. Targets: up to 2 patterns with lift ≥ 0.405 (1.5× the odds), largest first. No target: use all five patterns.
  4. Breathing room: the non-target pattern with the lowest logit.
  5. For the whole attempt, the boss draws only from the targets and the breathing-room pattern.

  Lift is used instead of the raw hit chance so the boss does not simply pick what is hard for everyone.
  Settings are in `overfit/data/balance.json` (`picker`). With all five patterns, the draw is identical to the random picker.
- **Control arm.** Each stage-2 attempt flips a coin from the attempt seed: 50% network, 50% random. Logs show `arm=network` or `arm=uniform`.
  Stage 1 is always random because it is where habits are measured.
- **Attempt log.** Each finished attempt adds one JSON line to `user://attempts/<session seed>.jsonl`: arm, drawn patterns, dodge events,
  the 19 inputs, hit or not for each pattern instance, and the network's decision. Nothing is uploaded.
  - Replay an attempt with a bot: `EXTRA="--history=<file> --attempt=N" tools/build.sh demo`.
  - Compare human logs with the bot fleet: copy the files to `ml/human/` and run `ml/.venv/bin/python ml/sim2real.py`.

### Results

50,000 bots not used in training (31,580 reached stage 2) played stage 2 twice with the same seeds: once against the network boss,
once against the random boss (`tools/build.sh evaluate`, then `tools/build.sh validate`). Brackets are 95% bootstrap intervals over bots.

| Check | Target | Result | |
|---|---|---|---|
| Guard bots get grab | Pattern drawn ≥ 1.5× as often as with the random boss, and hit rate up | 2.36×, +3.7 pts [+3.4, +4.1] | pass |
| Parry bots get jump attack ×3 | Same | 1.93×, +2.9 pts [+2.5, +3.2] | pass |
| Jump bots get rush | Same | 1.59×, +1.2 pts [+0.8, +1.6] | pass |
| Dash bots get grab | Same | 1.38×, +1.5 pts [+1.2, +1.8] | fail |
| Breathing room | In every narrowed attempt, and hit less than targets | Always present, 59% vs 89% hit | pass |
| Not picking on weak players | ≥ 80% of network attempts by bots with no habit and slow, noisy reactions use all five patterns | 38% [36, 41] | fail |
| Win rate does not collapse | Stage-2 win rate against the network boss ≥ half of the rate against the random boss | 52% vs 56% | pass |
| Calibration | ECE ≤ 0.05 for each pattern | 0.004 to 0.022 | pass |

- The pairs the network learned: dash → grab, guard → grab, parry → jump attack ×3, jump → rush.
  For parry bots the network picks the off-beat combo 7.9% of the time (random: 20%). For guard bots it picks jump attack ×3 1.8% of the time.
- Dash bots: the network often targets grab and jump attack ×3 together, so grab is one of three patterns.
- Weak bots: even bots without a habit get uneven lifts across patterns (spread about 0.65), so one pattern often passes 0.405.
- The network learned from bots, not people. `ml/sim2real.py` measures the gap once human logs exist.

Full report: [`ml/validation.md`](ml/validation.md). Rule variants are compared in the
[design document §7.1](docs/superpowers/specs/2026-09-28-패턴-고르는-망-design.md#71-같은-봇에게-망-보스와-무작위-보스) (Korean).

### Files

| Part | Location |
|---|---|
| Bot fleet | `overfit/battle/rules/FleetBot.cs`, `BotTraits.cs`, `tools/factory/fleet.json` |
| Input | `overfit/battle/rules/PlayerAxes.cs`, `PlayerFeatures.cs` |
| Data factory, evaluation | `tools/factory/` |
| Training, validation, sim-to-real | `ml/` |
| Weights | `overfit/data/network.json` |
| Forward pass | `overfit/battle/rules/PlayerNet.cs` |
| Selection rule, coin | `overfit/battle/rules/NetworkPicker.cs`, `StageRoster.cs` |
| Attempt log | `overfit/battle/rules/AttemptLog.cs`, `InstanceTracker.cs`, `overfit/battle/AttemptFile.cs` |

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

Building from source needs Godot 4.7 (mono) and .NET 8. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Code: [MIT](LICENSE). Art: three CC0 packs, not included in the repository. The list is in the in-game credits and
[`overfit/data/credits.json`](overfit/data/credits.json).
