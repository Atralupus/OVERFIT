"""일꾼을 빌드 없이 부른다 — 학습기가 바퀴마다 부르므로 빌드는 처음 한 번(tools/build.sh train 이 한다)."""

from __future__ import annotations

import subprocess
from pathlib import Path

from ml.rl import rollout

ROOT = rollout.ROOT
DLL = ROOT / "tools" / "factory" / "bin" / "Release" / "net8.0" / "Overfit.Factory.dll"


def build() -> None:
    subprocess.run(["dotnet", "build", str(ROOT / "tools" / "factory" / "Overfit.Factory.csproj"), "-c", "Release", "-v", "quiet", "-nologo"],
                   check=True, cwd=ROOT, stdout=subprocess.DEVNULL)


def run(seed: int, episodes: int, out_dir: str | Path, weights: str | Path | None = None, controller: str = "net",
        threads: int | None = None, learner: str = "boss", opponents: list[str] | None = None) -> rollout.Rollout:
    """일꾼 한 번 — [E] 가 있거나 표지가 없으면 멈춘다(공장과 같은 판정). learner · opponents 는 셀프 플레이(조각 6)."""
    args = ["dotnet", str(DLL), "--rollout", f"--seed={seed}", f"--episodes={episodes}", f"--out={out_dir}",
            f"--weights={weights if weights else 'none'}", f"--controller={controller}", f"--learner={learner}"]
    if opponents:
        args.append("--opponents=" + ",".join(str(o) for o in opponents))
    if threads:
        args.append(f"--threads={threads}")
    p = subprocess.run(args, cwd=ROOT, capture_output=True, text=True)
    out = p.stdout + p.stderr
    if p.returncode != 0 or "rollout=done" not in out or "][E]" in out:
        raise RuntimeError(f"일꾼이 멈췄다 (코드 {p.returncode}):\n{out[-2000:]}")
    return rollout.read(out_dir)
