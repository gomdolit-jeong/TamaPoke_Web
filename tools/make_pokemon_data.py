"""PC판 pokemon_data.json으로 웹판 wwwroot/data/pokemon.json을 만듭니다.

SpriteCollab의 고정 커밋(SpriteLoader.cs의 BaseUrl과 같아야 함)에 AnimData.xml이 있는지
도감 번호마다 확인해서 HasSprite를 붙입니다. 스프라이트가 없는 포켓몬은 부화, 진화 후보에서 빠집니다.

사용법:
    python tools/make_pokemon_data.py <PC판 pokemon_data.json 경로>
"""

import concurrent.futures
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path

SPRITE_BASE = "https://raw.githubusercontent.com/PMDCollab/SpriteCollab/53074f14d6947daf17af7e21db709497169a0e76/sprite"
OUTPUT = Path(__file__).resolve().parent.parent / "TamaPoke.Web" / "wwwroot" / "data" / "pokemon.json"


def has_sprite(species_id: int) -> bool:
    url = f"{SPRITE_BASE}/{species_id:04d}/AnimData.xml"
    request = urllib.request.Request(url, method="HEAD", headers={"User-Agent": "TamaPoke-data-builder"})
    for _ in range(3):
        try:
            with urllib.request.urlopen(request, timeout=30):
                return True
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return False
        except urllib.error.URLError:
            pass
    raise RuntimeError(f"스프라이트 확인 실패: {url}")


def main() -> None:
    source = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))

    with concurrent.futures.ThreadPoolExecutor(8) as pool:
        sprites = dict(zip((p["Id"] for p in source), pool.map(has_sprite, (p["Id"] for p in source))))

    pokemons = [{**p, "HasSprite": sprites[p["Id"]]} for p in source]
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(pokemons, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")

    missing = sorted(i for i, ok in sprites.items() if not ok)
    print(f"{len(pokemons)}마리, 스프라이트 없음 {len(missing)}마리: {missing}")


if __name__ == "__main__":
    main()
