"""PC판 기술 데이터로 웹판 wwwroot/data/moves.json, learnsets.json을 만듭니다.

습득표는 PC판 SkillDex.GetActiveLearnset과 같은 순서로 세대를 골라 포켓몬마다 하나만 남깁니다.

사용법:
    python tools/make_skill_data.py <PC판 Data 폴더>
"""

import json
import sys
from pathlib import Path

VERSION_PRIORITY = ["scarlet-violet", "sword-shield", "brilliant-diamond-and-shining-pearl", "ultra-sun-ultra-moon", "sun-moon"]
OUTPUT = Path(__file__).resolve().parent.parent / "TamaPoke.Web" / "wwwroot" / "data"


def active_learnset(versions: dict) -> list:
    for version in VERSION_PRIORITY:
        if version in versions:
            return versions[version]
    return next(iter(versions.values()), [])


def main() -> None:
    data = Path(sys.argv[1])
    moves = json.loads((data / "pokemon_movedb.json").read_text(encoding="utf-8-sig"))
    learnsets = json.loads((data / "pokemon_skills.json").read_text(encoding="utf-8-sig"))

    compact = {
        str(entry["SpeciesId"]): [[m["Level"], m["MoveId"]] for m in active_learnset(entry["MovesByVersion"])]
        for entry in learnsets
    }

    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / "moves.json").write_text(json.dumps(moves, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    (OUTPUT / "learnsets.json").write_text(json.dumps(compact, separators=(",", ":")), encoding="utf-8")
    print(f"moves {len(moves)}, learnsets {len(compact)}")


if __name__ == "__main__":
    main()
