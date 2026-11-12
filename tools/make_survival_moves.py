# 서바이벌 대표 기술 데이터를 만듭니다.
#
# Pokémon Showdown 랜덤 배틀에서 각 포켓몬이 실제로 쓰는 기술 묶음(pkmn/randbats, MIT)을 받아
# 포켓몬마다 "자주 나오는 기술"과 그 무게를 wwwroot/data/survival_moves.json에 저장합니다.
#   {"6": [[394, 9], [53, 7], ...]}   # 포켓몬 번호: [[기술 번호, 무게], ...] (무게가 큰 순서)
# 최신 세대일수록 무게가 큽니다. 우리 기술 목록(moves.json)에 없는 기술은 뺍니다.
#
# 실행: python tools/make_survival_moves.py   (인터넷이 필요합니다)
import json
import os
import re
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "TamaPoke.Web", "wwwroot", "data")
URL = "https://pkmn.github.io/randbats/data/gen{}randombattle.json"
GEN_WEIGHT = {9: 4, 8: 3, 7: 2, 6: 2, 5: 1, 4: 1, 3: 1, 2: 1, 1: 1}


def key(name):
    return re.sub(r"[^a-z0-9]", "", name.lower())


def load(name):
    return json.load(open(os.path.join(DATA, name), encoding="utf-8"))


species = {key(p["NameEn"]): p["Id"] for p in load("pokemon.json")}
moves = {key(m["NameEn"]): m["Id"] for m in load("moves.json")}

weights = {}  # 포켓몬 번호 -> {기술 번호: 무게}
missing_moves = set()
for gen, gen_weight in GEN_WEIGHT.items():
    try:
        request = urllib.request.Request(URL.format(gen), headers={"User-Agent": "Mozilla/5.0 (TamaPoke data tool)"})
        with urllib.request.urlopen(request, timeout=30) as response:
            sets = json.load(response)
    except Exception as error:  # 없는 세대는 건너뜁니다.
        print(f"gen{gen}: {error}")
        continue

    # 기본 모습이 있으면 기본 모습만, 없으면 폼(예: 지역 폼)의 기술을 기본 모습으로 셉니다.
    names = {}
    for name, entry in sets.items():
        base = key(name)
        if base not in species:
            base = key(name.split("-")[0])
            if base not in species or name.split("-")[0] in sets:
                continue
        names.setdefault(base, []).append(entry)

    for base, entries in names.items():
        bucket = weights.setdefault(species[base], {})
        for entry in entries:
            groups = [role["moves"] for role in entry["roles"].values()] if "roles" in entry else [entry.get("moves", [])]
            for group in groups:
                for move in group:
                    move_id = moves.get(key(move))
                    if move_id is None:
                        missing_moves.add(move)
                        continue
                    bucket[move_id] = bucket.get(move_id, 0) + gen_weight
    print(f"gen{gen}: {len(names)} species")

out = {str(sid): sorted(bucket.items(), key=lambda kv: (-kv[1], kv[0]))
       for sid, bucket in sorted(weights.items()) if bucket}
with open(os.path.join(DATA, "survival_moves.json"), "w", encoding="utf-8", newline="\n") as f:
    json.dump(out, f, separators=(",", ":"))
print(f"{len(out)} species written; moves not in moves.json: {len(missing_moves)}")
print(", ".join(sorted(missing_moves)[:40]))
