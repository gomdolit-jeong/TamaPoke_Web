"""SpriteCollab 최신 상태와 웹판을 비교해서 새로 쓸 수 있는 스프라이트를 알려 줍니다.

웹판은 스프라이트 저장소의 특정 커밋에 고정되어 있어서 새 그림이 자동으로 들어오지 않습니다.
이 스크립트는 아무것도 바꾸지 않고 비교 결과만 보여 줍니다.

사용법:
    python tools/check_sprite_updates.py
    python tools/check_sprite_updates.py --tracker <tracker.json>  (내려받지 않고 이 파일과 비교)
    python tools/check_sprite_updates.py --report <결과.md>        (결과를 파일로 저장)

새 그림이 있으면 GitHub Actions에서 쓰도록 GITHUB_OUTPUT에 has_updates=true를 남깁니다.
"""

import argparse
import json
import os
import re
import urllib.request
from pathlib import Path

from make_form_data import collect

ROOT = Path(__file__).resolve().parent.parent
LOADER = ROOT / "TamaPoke.Web" / "Sprites" / "SpriteLoader.cs"
POKEMON = ROOT / "TamaPoke.Web" / "wwwroot" / "data" / "pokemon.json"
FORMS = ROOT / "TamaPoke.Web" / "wwwroot" / "data" / "forms.json"
REPO = "PMDCollab/SpriteCollab"
LATEST_TRACKER = f"https://raw.githubusercontent.com/{REPO}/master/tracker.json"
LATEST_COMMIT = f"https://api.github.com/repos/{REPO}/commits/master"
MAX_LISTED = 200  # 이슈 본문이 너무 길어지지 않게 합니다.


def fetch(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": "TamaPoke-Web-sprite-check"})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


def pinned_commit() -> str:
    match = re.search(rf"{REPO}/([0-9a-f]{{40}})", LOADER.read_text(encoding="utf-8"))
    if not match:
        raise SystemExit("SpriteLoader.cs에서 고정 커밋을 찾지 못했습니다.")
    return match.group(1)


def latest_commit() -> str | None:
    try:
        return json.loads(fetch(LATEST_COMMIT))["sha"]
    except OSError as error:  # 커밋 번호는 참고용이라 못 받아도 비교는 계속합니다.
        print(f"최신 커밋 확인 실패: {error}")
        return None


def compare(tracker: dict) -> tuple[list[str], list[str]]:
    pokemon = json.loads(POKEMON.read_text(encoding="utf-8"))
    current_forms = json.loads(FORMS.read_text(encoding="utf-8"))

    new_species = []
    for entry in pokemon:
        node = tracker.get(f"{entry['Id']:04d}")
        if not entry.get("HasSprite") and node and "Idle" in node.get("sprite_files", {}):
            new_species.append(f"{entry['Id']} {entry.get('DisplayName') or ''}".strip())

    new_forms = []
    for key, node in tracker.items():
        if not key.isdigit() or not 1 <= int(key) <= 1025:
            continue
        latest: dict = {}
        collect(node, [key], [], latest)
        known = current_forms.get(str(int(key)), {})
        new_forms.extend(f"{int(key)} {suffix}" for suffix in latest if suffix not in known)
    return new_species, new_forms


def listed(items: list[str]) -> str:
    lines = [f"- {item}" for item in items[:MAX_LISTED]]
    if len(items) > MAX_LISTED:
        lines.append(f"- ...외 {len(items) - MAX_LISTED}개")
    return "\n".join(lines)


def build_report(pinned: str, latest: str | None, new_species: list[str], new_forms: list[str]) -> str:
    parts = [
        f"SpriteCollab에 웹판이 아직 쓰지 않는 스프라이트가 있습니다.\n",
        f"- 고정 커밋: `{pinned}`",
        f"- 최신 커밋: `{latest or '확인 못 함'}`",
        f"- 새로 쓸 수 있는 포켓몬: {len(new_species)}마리",
        f"- 새 폼: {len(new_forms)}개\n",
    ]
    if new_species:
        parts += ["## 새로 쓸 수 있는 포켓몬\n", listed(new_species), ""]
    if new_forms:
        parts += ["## 새 폼\n", listed(new_forms), ""]
    parts += [
        "## 반영 방법\n",
        "1. `SpriteLoader.cs`와 `tools/make_pokemon_data.py`의 고정 커밋을 최신 커밋으로 바꿉니다.",
        "2. `tools/make_pokemon_data.py`, `tools/make_form_data.py`를 다시 실행합니다.",
        "3. 로컬에서 확인한 뒤 배포합니다. 동작이 일부만 있는 그림도 있으니 먼저 살펴보세요.",
    ]
    return "\n".join(parts) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tracker", help="내려받지 않고 비교할 tracker.json")
    parser.add_argument("--report", help="결과를 저장할 파일")
    args = parser.parse_args()

    if args.tracker:
        tracker = json.loads(Path(args.tracker).read_text(encoding="utf-8"))
        latest = None
    else:
        tracker = json.loads(fetch(LATEST_TRACKER))
        latest = latest_commit()

    pinned = pinned_commit()
    new_species, new_forms = compare(tracker)
    has_updates = bool(new_species or new_forms)

    print(f"고정 커밋 {pinned}\n최신 커밋 {latest}")
    print(f"새로 쓸 수 있는 포켓몬 {len(new_species)}마리, 새 폼 {len(new_forms)}개")
    for item in (new_species + new_forms)[:30]:
        print(" ", item)

    if args.report and has_updates:
        Path(args.report).write_text(build_report(pinned, latest, new_species, new_forms), encoding="utf-8")

    if output := os.environ.get("GITHUB_OUTPUT"):
        with open(output, "a", encoding="utf-8") as file:
            file.write(f"has_updates={'true' if has_updates else 'false'}\n")


if __name__ == "__main__":
    main()
