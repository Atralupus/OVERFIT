# OVERFIT

[한국어](README.ko.md)

OVERFIT is a 2D side-scrolling soulslike with one boss fight. The boss has 1,200 HP and three phases: it changes phase at 900 and
400 HP remaining. A neural network trained by reinforcement learning chooses everything the boss does: when to wait, run in, back off,
leap, keep going with a move, or start a new one. Each phase uses the network at a different point of its training: phase 1 is an
early network that leans on a few moves, phase 2 is partly trained, and phase 3 is the final network.

In a comparison over 512 fights against the same opponents, the phase-3 network wins 80% of fights. The old hand-written boss wins
42%, and a boss trained by reinforcement learning without a network (a lookup table) wins 50%.

## Scenes

The first three scenes are the network boss in each phase, against a fighter who walks in and attacks. The other scenes use a script
of moves, so that each move, cancel, and bomb can be shown on its own.

| <img src="docs/gifs/net1.gif" width="420"> | <img src="docs/gifs/net3.gif" width="420"> |
|---|---|
| **Phase 1 network.** An early network: jump attacks and a grab | **Phase 3 network.** The final network runs in, combos, and follows with an uppercut |
| <img src="docs/gifs/net2.gif" width="420"> | <img src="docs/gifs/form.gif" width="420"> |
| **Phase 2 network.** Partly trained; against this fighter it still picks jump attacks and grabs | **Phase change.** At 900 HP the boss stands still, flashes white three times, and takes no damage for 1.5 seconds |
| <img src="docs/gifs/rush.gif" width="420"> | <img src="docs/gifs/grab.gif" width="420"> |
| **3-hit combo → rush.** Cancelled after the first hit | **3-hit combo → grab.** Cancelled after the second hit; a guard does not stop the grab |
| <img src="docs/gifs/jump.gif" width="420"> | <img src="docs/gifs/uppercut.gif" width="420"> |
| **Jump attack.** Only a jump clears the landing | **Uppercut.** Hits a player who jumped early to clear the combo |
| <img src="docs/gifs/fast.gif" width="420"> | <img src="docs/gifs/offbeat.gif" width="420"> |
| **Fast 3-hit combo.** Catches a player still in a 2-hit combo | **Off-beat 3-hit combo.** Catches a parry timed to the normal rhythm |
| <img src="docs/gifs/bombcut.gif" width="420"> | <img src="docs/gifs/bomb.gif" width="420"> |
| **Bomb, interrupted (rule boss).** The boss cancels at the next cancel point and rushes in before the release | **Bomb, landed.** Thrown as the combo starts, the first cancel point comes too late |
| <img src="docs/gifs/retreat.gif" width="420"> | <img src="docs/gifs/leap.gif" width="420"> |
| **Retreat.** The boss runs backwards, still facing the fighter | **Leaps.** Over the fighter, then back away from the fighter |

## How the boss network was made

No deep-learning background is needed for this section.

**What the network sees and does.** The boss decides at decision points (listed under [Decisions](#decisions)). At each point the
game turns the fight into 104 numbers (the observation): the decision point, the boss's position, HP, phase, and current move, the
fighter's position, HP, stamina, bombs, and action 0.3 seconds ago, the fighter's actions over the 0.8 seconds before that, and up to
four bombs in flight. The network is a small stack of multiplications (104 → 128 → 128 → 13). It turns the 104 numbers into a score
for each of 13 choices: wait, run in, back off, leap over, leap back, continue the current move, or start one of the seven moves. Choices
that are not possible at that point (for example, "continue" while resting) are removed, and the boss picks one of the rest at random,
weighted by the scores.

**How it learns.** The boss starts with equal scores and plays thousands of fights. After each decision it is given a reward: plus for
damage dealt to the fighter, minus for damage taken, a small minus for time, and plus or minus 1 for winning or losing the fight. The
training method (PPO) slightly raises the scores of choices that led to more reward than expected, and lowers the others. This repeats
over many rounds. The training code is plain numpy in `ml/rl`; the fights are run by the game's own rules in C# (`tools/factory`), so the
boss learns against exactly the game that ships.

**Who it fights while learning.** Against scripted bots alone, the boss found one weakness (the bots could not escape a grab) and used
only the grab. So a second network learns to play the fighter, and the two train in turns (self-play). In each of 20 rounds the boss
trains for 10 steps against bots and saved fighters, then the fighter trains for 10 steps against the rule boss and saved bosses. Each saved
boss is then tested against the same set of opponents: the bot fleet and four saved fighters. The win rate rose from 21% (round 2)
to 80% (round 20).

**Choosing the phases.** Phase 1 is the saved boss with the lowest win rate after round 0 (round 2, 21%); phase 3 is the last one
(round 20, 80%); phase 2 is the one closest to halfway between them (round 8, 46%). The three networks are
`overfit/data/boss_net/form1.json`, `form2.json`, and `form3.json`, and `picks.json` records which saved boss each one came from.

## How the network is wired into the game

- **One list of choices.** The game asks a controller at each decision point. The rule controller (the boss of version 0.11) turns a
  random plan into choices; the network controller (`FormNetController`) runs the network of the current phase. `stages.json` chooses
  the controller (`"controller": "net"`); scripted scenes always use the rule controller.
- **Same seed, same fight.** The random pick uses the game's seeded random numbers (`overfit/core/Det.cs`), and the exponent function
  in the pick is computed with additions and multiplications only (`DetMath.Exp`), because the system's version may differ in the last
  bit between machines. A recorded attempt therefore replays the network boss exactly.
- **Checked against Python.** A test computes the networks' outputs for fixed inputs in pure Python with the same order of additions
  and compares them with the game's C# results bit for bit.
- **Data fingerprint.** The three networks are part of the hash of the game data saved with every attempt, so an attempt recorded
  before a network changes is reported as "data changed" instead of a replay failure.
- **No reaction to bombs.** The rule boss has a built-in reaction to bombs. The network boss does not; whether to cut a move short is
  its own choice, and it shows no "!".

## Did it need a network?

`tools/build.sh gate --name=sp-1` puts seven bosses against the same opponents (the bot fleet and four saved fighters) for 512 fights
each, with the same seeds. The result is in [`ml/rl/gate/sp-1.json`](ml/rl/gate/sp-1.json).

| Boss | Win rate | Damage to the fighter (of 220) | Damage taken (of 1,200) |
|---|---|---|---|
| Random choices | 42% | 139 | 649 |
| Rule boss (0.11) | 42% | 147 | 605 |
| Lookup table (reinforcement learning without a network) | 50% | 177 | 532 |
| Phase 1 network, whole fight | 22% | 96 | 745 |
| Phase 2 network, whole fight | 49% | 142 | 691 |
| **Phase 3 network, whole fight** | **80%** | **204** | **221** |
| The game's boss (phase 1 → 2 → 3) | 77% | 190 | 578 |

- **The lookup table** learns the same thing as the network, with the same rewards and the same number of fights (51,200), but it cuts
  each situation into a few boxes: the decision point, the distance to the fighter (four ranges), whether the fighter is in the air,
  the fighter's action, the boss's current move, and the phase. It even trained against these exact opponents. It still reaches only
  50%: boxes this coarse cannot tell apart situations that need different answers, and finer boxes would be visited too rarely to
  learn. A network reads all 104 numbers at once.
- **The gate passes** when phase 3 beats the stronger of the rule boss and the lookup table by at least 10 percentage points. It wins by
  31.

## The fight

- One boss with 1,200 HP in three phases. The fighter has 220 HP and one life. A fight that lasts 10 minutes is a loss.
- **Phase change.** When the boss's HP reaches 900 or 400, the boss drops its move, stands still, flashes white three times, and takes
  no damage for 1.5 seconds; then the next phase starts. Damage that would go past 900 or 400 stops there, so the phases take exactly
  300, 500, and 400 HP. The boss's HP bar marks both points.
- The fighter can dash (a short invincibility), jump, guard (costs stamina), parry, attack (one or two hits), and throw bombs
  (10 per fight).
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

## Decisions

The boss decides at decision points: when it becomes free (a move ends, an exhaustion starts, a phase change ends), every 0.2 seconds
while resting or running in, when a run arrives, and at each cancel point. At each point it picks one entry from a single list: wait,
run in, back off (runs backwards, facing the fighter), leap over the fighter, leap back 600 px from the fighter, continue the current
move, or start one of the moves. A leap crouches for 0.25 seconds and stays in the air for 0.6 seconds; it does not attack. What the boss sees of the fighter is 0.3 seconds old. The network boss
picks from this list directly. The rule boss (version 0.11, still used by the scripted scenes and as a baseline) makes a whole plan and
turns it into these entries:

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

## Bombs

- 10 per fight, thrown with L. Bombs cost no stamina; the count is the cost.
- Throwing works only on the ground and commits the fighter: nothing else can be done during the 1.5-second windup, and **a hit before
  the release interrupts the throw and the bomb is lost.** After the release the fighter is stiff for 0.25 seconds.
- A released bomb flies for 0.5 seconds, follows the boss, and lands on it for 60 damage (1.5 times a 2-hit combo). The only defence
  is to interrupt the throw before the release. Bombs do not fill the poise gauge.

## The rule boss tries to interrupt

This section is about the rule boss; the network boss has no built-in reaction. The rule boss sees a throw start and knows about it 0.3 seconds later; a "!" appears above its head. From then on it **always tries to
interrupt, whatever it is doing,** at the nearest chance:

| What the boss is doing | Chance to interrupt |
|---|---|
| Resting, running | The tick it knows |
| A move | The move's next cancel point, even one the plan did not pick |
| A move without cancel points (rush, grab, jump attack, uppercut) | After the move ends |
| Exhausted | After the exhaustion ends |

- When it interrupts, the plan ends, the boss turns toward the thrower, pauses for 0.25 seconds, and rushes. If the rush hits before the
  release, the throw is interrupted. The boss tries even when it will be too late; whether it gets there depends on the distance and the
  cancel points.
- A throw in front of a resting boss is interrupted at any distance. Thrown at random moments from a middle distance (760 px), three bombs
  in four are lost: the boss interrupts 52% and the move it is doing hits the fighter in another 23%.
- **Timing makes bombs land:** right after a cancel point passes (as a 3-hit combo starts, or after its first or second hit), in the
  last 0.3 seconds of a rest (the boss only knows once its next move has started), and right after exhausting the boss with a parry or
  a single hit (a 2-hit combo leaves the fighter stiff for too long). No bomb lands during the moves that close in on the fighter (rush,
  grab, jump attack).
- This boss cannot interrupt "throw as soon as a 3-hit combo starts"; the first cancel point comes too late. Reading such a habit and
  standing ready to interrupt it is the job of the prediction in slice 4. The reaction delay, pause, and move are `bomb_reaction` in
  `overfit/data/bosses.json`. The measurements are in §2.5 of the
  [slice 2 design](docs/superpowers/specs/2026-09-30-조각2-폭탄과-반응-design.md) (Korean).

## Replays

Each finished attempt adds one JSON line to `user://attempts/<session seed>.jsonl`: the plans, the inputs (run-length encoded;
the longest fight is about 27 KB), the ticks the phase changes started, the dodge events, the bombs (the throw tick, the boss's move at that moment, the outcome — landed,
interrupted by the boss, lost to another hit, or cut short by the end of the fight — and the tick the boss interrupted), and a hash of the
game data. Nothing is uploaded.

- `EXTRA="--history=<file> --attempt=N" tools/build.sh demo` replays an attempt with its saved inputs and compares the plans,
  the length, the result, the phase changes, every dodge event, and every bomb. It logs `replay_match`, `[E] replay_mismatch` (determinism broke),
  or `[W] replay_data_changed` (the game data changed since the attempt).
- Lines written by version 0.9 and earlier have no inputs and cannot be replayed (`[E] replay_no_inputs`). Lines written by 0.10 have
  no bombs, so bombs are not compared for them. Lines written by 0.11 and earlier have no phase changes, so phase changes are not
  compared for them.
- The result screen shows how long each phase took and how the boss planned: the number of plans and cancels, the dodges used in the fight, what happened to the
  bombs, how many times each move appeared and hit, and each cancel pair.

## Where things are

| Part | Location |
|---|---|
| Moves, cancel points | `overfit/data/patterns.json` |
| Boss numbers (HP, phases, rest, run, bomb reaction) | `overfit/data/bosses.json` |
| Phases | `overfit/battle/rules/BossForms.cs`, `FormReport.cs`, `BattleSim.cs` |
| Bomb numbers | `overfit/data/fighters.json` (`bomb`) |
| Picker settings | `overfit/data/balance.json` (`picker`) |
| Decision points, controllers | `overfit/battle/rules/BossDecision.cs`, `RuleController.cs`, `RandomController.cs`, `SightBuffer.cs`, `BattleSim.cs` |
| Back off, leaps | `overfit/data/bosses.json` (`movement`), `overfit/battle/rules/BossTravel.cs`, `RetreatMotion.cs`, `LeapMotion.cs` |
| Plans and cancels | `overfit/battle/rules/BossPlan.cs`, `PatternPickers.cs`, `RuleController.cs` |
| Running | `overfit/battle/rules/BattleSim.cs`, `RushMotion.cs` |
| Bombs, the boss's reaction | `overfit/battle/rules/Fighter.cs`, `Bombs.cs`, `BombWatch.cs`, `BombRecord.cs`, `BattleSim.cs` |
| Attempt log, replay | `overfit/battle/rules/AttemptLog.cs`, `InputTape.cs`, `Replay.cs`, `overfit/battle/debug/BattleDemo.cs` |
| Bot fleet, data factory | `overfit/battle/rules/FleetBot.cs`, `BotTraits.cs`, `tools/factory/` |
| Network boss in the game | `overfit/data/boss_net/`, `overfit/battle/rules/FormNetController.cs`, `PolicyNet.cs`, `MaskedSampler.cs`, `DetMath.cs`, `BossObservation.cs` |
| Training (PPO, self-play, phase picks) | `ml/rl/` (`train.json` holds the settings), `tools/factory/RolloutRun.cs` |
| Lookup-table boss, comparison | `overfit/battle/rules/QTable.cs`, `tools/factory/QTrain.cs`, `ml/rl/gate.py` |

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
| Bomb | L |

Building from source needs Godot 4.7 (mono) and .NET 8. See [CONTRIBUTING.md](CONTRIBUTING.md) (Korean).

| Command | What it does |
|---|---|
| `tools/build.sh check` | Format, build, rule tests, `.uid` pairs, hitbox shapes. The commit gate |
| `tools/build.sh smoke` | Boots the game headless and tours the scenes |
| `tools/build.sh demo [seed]` | One headless fight with a bot |
| `tools/build.sh factory` | The bot fleet fights the boss and writes samples |
| `tools/build.sh shots` | Screenshots |
| `tools/build.sh export` | macOS build |
| `tools/build.sh selfplay` | Trains the boss and fighter networks in turns (about 40 minutes); needs `ml/.venv` (`ml/requirements.txt`) |
| `tools/build.sh evalboss --name=NAME` | Win rate of every saved boss against the test opponents |
| `tools/build.sh pick --name=NAME` | Picks the three phase networks and writes the Python check values |
| `tools/build.sh qtrain --name=NAME` | Trains the lookup-table boss |
| `tools/build.sh gate --name=NAME` | The comparison above |

## License

Code: [MIT](LICENSE). Art: three CC0 packs, not included in the repository. The list is in the in-game credits and
[`overfit/data/credits.json`](overfit/data/credits.json).
