# 포켓몬 특성 데이터를 만듭니다. (서바이벌 특성 효과에 씀)
#
# PokeAPI 데이터(https://github.com/PokeAPI/pokeapi, BSD-3-Clause)의 CSV를 받아
# wwwroot/data/abilities.json에 저장합니다.
#   {
#     "names":   {"overgrow": ["심록", "Overgrow"], ...},        # 특성 이름표: [한국어, 영어]
#     "species": {"1": [["overgrow", 0], ["chlorophyll", 1]], ...} # 포켓몬 번호: [[특성, 숨겨진 특성이면 1], ...] (칸 순서)
#   }
# 우리 도감(pokemon.json)에 있는 포켓몬(기본 모습)만 넣습니다.
#
# 실행: python tools/make_ability_data.py   (인터넷이 필요합니다)
import csv
import io
import json
import os
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "TamaPoke.Web", "wwwroot", "data")
CSV = "https://raw.githubusercontent.com/PokeAPI/pokeapi/master/data/v2/csv/{}.csv"
KOREAN, ENGLISH = "3", "9"  # PokeAPI languages.csv의 언어 번호


def rows(name):
    with urllib.request.urlopen(CSV.format(name), timeout=60) as response:
        return list(csv.DictReader(io.StringIO(response.read().decode("utf-8"))))


species_ids = {p["Id"] for p in json.load(open(os.path.join(DATA, "pokemon.json"), encoding="utf-8"))}

identifiers = {r["id"]: r["identifier"].replace("-", "") for r in rows("abilities")}

names = {}
for r in rows("ability_names"):
    key = identifiers.get(r["ability_id"])
    if key is None or r["local_language_id"] not in (KOREAN, ENGLISH):
        continue
    entry = names.setdefault(key, ["", ""])
    entry[0 if r["local_language_id"] == KOREAN else 1] = r["name"]

species = {}
for r in sorted(rows("pokemon_abilities"), key=lambda r: (int(r["pokemon_id"]), int(r["slot"]))):
    pid = int(r["pokemon_id"])
    if pid not in species_ids:  # 10001번 이후는 폼(메가 등)이라 뺍니다.
        continue
    key = identifiers[r["ability_id"]]
    species.setdefault(str(pid), []).append([key, int(r["is_hidden"])])

used = {key for abilities in species.values() for key, _ in abilities}
names = {key: value for key, value in sorted(names.items()) if key in used}
# 한국어 이름이 없으면 영어 이름을 씁니다.
for value in names.values():
    if not value[0]:
        value[0] = value[1]

out = os.path.join(DATA, "abilities.json")
with open(out, "w", encoding="utf-8") as f:
    json.dump({"names": names, "species": species}, f, ensure_ascii=False, separators=(",", ":"))
missing = sorted(species_ids - {int(k) for k in species})
print(f"특성 {len(names)}개, 포켓몬 {len(species)}마리, 특성 없는 포켓몬 {len(missing)}마리 {missing[:10]}")
