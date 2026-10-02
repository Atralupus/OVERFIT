# OVERFIT

[한국어](README.ko.md)

OVERFIT is a 2D side-scrolling action game with one boss fight. The boss has three phases, and each phase is the boss at a different
level of training: a neural network trained by reinforcement learning chooses everything the boss does, and every phase uses a network
that has trained longer than the one before. The boss does not study you during the fight. It was trained before the game shipped.

- The boss has 800 HP. Phase 2 starts at 600 HP and phase 3 at 400 HP.
- At each phase change the boss stands still, flashes white three times, and takes no damage for 1.5 seconds.

## The three bosses

| <img src="docs/gifs/net1.gif" width="420"> | <img src="docs/gifs/net2.gif" width="420"> |
|---|---|
| **Phase 1.** Runs in and opens with fast combos and rushes | **Phase 2.** Keeps its distance, leaps over the fighter, and attacks from behind |
| <img src="docs/gifs/net3.gif" width="420"> | <img src="docs/gifs/form.gif" width="420"> |
| **Phase 3.** Backs off and leaps around the fighter while waiting for an opening | **Phase change.** The boss stands still, flashes white three times, and takes no damage for 1.5 seconds |

## Boss moves

| <img src="docs/gifs/rush.gif" width="420"> | <img src="docs/gifs/grab.gif" width="420"> |
|---|---|
| **3-hit combo → rush.** Cancelled after the first hit | **3-hit combo → grab.** Cancelled after the second hit |
| <img src="docs/gifs/jump.gif" width="420"> | <img src="docs/gifs/uppercut.gif" width="420"> |
| **Jump attack.** Only a jump clears the landing | **Uppercut.** Reaches high; a jump does not clear it |
| <img src="docs/gifs/fast.gif" width="420"> | <img src="docs/gifs/offbeat.gif" width="420"> |
| **Fast 3-hit combo** | **Off-beat 3-hit combo.** Each windup is held a little longer |
| <img src="docs/gifs/retreat.gif" width="420"> | <img src="docs/gifs/leap.gif" width="420"> |
| **Back off.** Runs backwards, still facing the fighter | **Leaps.** Over the fighter, then back away from the fighter |

Ticks are 1/60 s, counted from the tick the move starts.

| Move | Hits (tick) | Damage | Avoided by | Ends (tick) |
|---|---|---|---|---|
| 3-hit combo | 51 · 93 · 159 | 8 · 8 · 14 | dash, guard, distance; a jump clears some hits | 213 |
| Off-beat 3-hit combo | 60 · 111 · 186 | 8 · 8 · 14 | dash, guard, distance; a jump clears some hits | 240 |
| Fast 3-hit combo | 24 · 51 · 84 | 8 · 8 · 14 | dash, guard, distance | 138 |
| Rush | 24 after arriving | 14 | dash, guard | 78 after arriving |
| Grab | 60 | 25, held 1 s | jump only | 158 (a missed grab leaves the boss open for 1.5 s) |
| Jump attack | 60 | 24 | jump only | 108 |
| Uppercut | 51 | 14 | dash, guard | 105 |

The three combos have cancel points: the boss can drop the rest of the combo and start another move at once.

## The fight

- The fighter has 220 HP and one life. A fight that lasts 10 minutes is a loss.
- The fighter can move, jump, dash (a short invincibility), guard (costs stamina), attack (one hit, or two in a row), and throw bombs.
- **Bombs.** 10 per fight. A throw takes 1.5 seconds, and a hit before the release loses the bomb. A bomb that lands does 60 damage.
- Hits fill the boss's poise gauge. A full gauge exhausts the boss for 1.5 seconds.

## How the bosses were trained

**What the boss sees and chooses.** At each decision point (when it becomes free, every 0.2 seconds while waiting or running in, and
at each cancel point) the game turns the fight into 95 numbers: the boss's position, HP, phase, and current move; the fighter's
position, HP, stamina, bombs, and recent actions as they looked 0.3 seconds ago; and bombs in flight. A small network
(95 → 128 → 128 → 13) gives a score to each of 13 choices: wait, run in, back off, leap over, leap back, continue the current move, or
start one of the seven moves. Choices that are not possible at that moment are removed, and the boss picks at random among the rest; a
higher score makes a choice more likely.

**How it learns.** The boss plays many fights and is rewarded after each decision: plus for damage dealt, minus for damage taken, a
small minus for time, a small bonus the first time it uses each move in a fight (so every phase uses all seven), and plus or minus 1
for winning or losing. The training method (PPO) makes choices that led to more reward more likely.

**Who it trains against.** A second network learns to play the fighter. The two train in turns for 20 rounds (self-play), using the
game's own rules. The boss saved after each round is tested against the same set of opponents (bots and saved fighters); its win
rate rose from 22% (untrained) to 89% (round 20).

**Choosing the three phases.** Phase 3 is the strongest saved boss (round 20, 89%). Phases 1 and 2 are the saved bosses closest to
halfway and three quarters of the way from the untrained boss to phase 3 (round 10, 63%; round 16, 71%). Only bosses that use all
seven moves can be picked.

**The final fighter still wins.** Over 256 fights, the last trained fighter beats the phase-3 boss 61% of the time.

## How it runs in the game

- The networks are `overfit/data/boss_net/form1.json`, `form2.json`, and `form3.json`; the game switches to the next one at each
  phase change.
- The random pick uses the game's seeded random numbers and an exponent function computed with additions and multiplications only, so
  a recorded attempt replays exactly on any machine. A test checks the game's results against Python bit for bit.

## Compared with other bosses

512 fights per boss against the same opponents (bots and saved fighters), same seeds (`tools/build.sh gate --name=sp-4`, result in
[`ml/rl/gate/sp-4.json`](ml/rl/gate/sp-4.json)).

| Boss | Win rate |
|---|---|
| Random choices | 21% |
| Hand-written rules (the boss before networks) | 23% |
| Lookup table (reinforcement learning without a network) | 55% |
| Phase 1 · 2 · 3 network, whole fight | 65% · 73% · 89% |
| The game's boss (phase 1 → 2 → 3) | 78% |

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
| Bomb | L |

Building from source needs Godot 4.7 (mono) and .NET 8. See [CONTRIBUTING.md](CONTRIBUTING.md) (Korean).

| Command | What it does |
|---|---|
| `tools/build.sh check` | Format, build, tests. The commit gate |
| `tools/build.sh export` | macOS build |
| `tools/build.sh selfplay --name=NAME` | Trains the boss and fighter networks in turns; needs `ml/.venv` (`ml/requirements.txt`) |
| `tools/build.sh evalboss --name=NAME` · `pick --name=NAME` | Tests every saved boss and picks the three phases |
| `tools/build.sh duel FIGHTER_NET.json` | Records a full fight between a trained fighter and the game's boss |

## License

Code: [MIT](LICENSE). Art: three CC0 packs, not included in the repository. The list is in the in-game credits and
[`overfit/data/credits.json`](overfit/data/credits.json).
