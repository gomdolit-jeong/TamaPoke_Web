"""SpriteCollab tracker.json으로 웹판 wwwroot/data/forms.json을 만듭니다.

포켓몬별로 폼 접미사(PC판 스프라이트 파일명과 같은 규칙: Shiny, Female, Alola, Mega_X ...)와
SpriteCollab 폴더 경로를 짝지어 둡니다. 이미지가 없는 폼과 PC판 블랙리스트 폼은 뺍니다.
메가진화는 전투에서 잠깐 쓰는 폼이라 대기 모션만 있어도 넣습니다. (없는 모션은 게임이 대기 모션으로 대체)

사용법:
    python tools/make_form_data.py <tracker.json 경로>
"""

import json
import sys
from pathlib import Path

OUTPUT = Path(__file__).resolve().parent.parent / "TamaPoke.Web" / "wwwroot" / "data" / "forms.json"

# PC판 SpriteConverter.BlackListForms와 같음
BLACKLIST = ("altcolor", "alternate", "cutscene", "turban", "substitute", "beta",
             "mikon", "hakogame", "norowara", "ubausagi", "warabbit", "animon", "gecqua", "browt", "pombon")


def collect(node: dict, path: list[str], names: list[str], out: dict) -> None:
    for key, child in node.get("subgroups", {}).items():
        child_path = path + [key]
        name = (child.get("name") or "").replace(" ", "_")
        child_names = names + ([name] if name else [])
        suffix = "_".join(child_names)
        lower = suffix.lower()
        is_partial_mega = "mega" in lower.split("_") and "Idle" in child.get("sprite_files", {})
        if (child.get("sprite_complete", 0) > 0 or is_partial_mega) and suffix and not any(word in lower for word in BLACKLIST):
            out[suffix] = "/".join(child_path)
        collect(child, child_path, child_names, out)


def main() -> None:
    tracker = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    forms = {}
    for key, node in tracker.items():
        if not key.isdigit() or not 1 <= int(key) <= 1025:
            continue
        species_forms: dict = {}
        collect(node, [key], [], species_forms)
        if species_forms:
            forms[str(int(key))] = species_forms

    OUTPUT.write_text(json.dumps(forms, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"species {len(forms)}, forms {sum(len(v) for v in forms.values())}, bytes {OUTPUT.stat().st_size}")


if __name__ == "__main__":
    main()
