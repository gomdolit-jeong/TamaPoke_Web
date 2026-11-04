# TamaPoke_Web

[TamaPoke_PC](https://github.com/gomdolit-jeong/TamaPoke_PC)의 웹버전입니다. 현재 개발 중입니다.

- **주소:** https://gomdolit-jeong.github.io/TamaPoke_Web/
- **기술:** Blazor WebAssembly (.NET 9), GitHub Pages

## 로컬 실행

```bash
dotnet run --project TamaPoke.Web
```

`main`에 푸시하면 GitHub Actions가 빌드해서 GitHub Pages에 배포합니다.

Debug 빌드에서는 테스트하기 쉽도록 알에서 항상 파이리가 부화합니다. 배포판은 무작위입니다.

포켓몬 데이터(`TamaPoke.Web/wwwroot/data/pokemon.json`)는 PC판 `pokemon_data.json`으로 만듭니다.

```bash
python tools/make_pokemon_data.py <PC판 Data/pokemon_data.json>
```

기술 데이터(`moves.json`, `learnsets.json`)는 PC판 Data 폴더로 만듭니다.

```bash
python tools/make_skill_data.py <PC판 Data 폴더>
```

## 📝 Credits

- **Sprites:** [PMDCollab/SpriteCollab](https://github.com/PMDCollab/SpriteCollab) 기여자들의 작업물이며, [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/)에 따라 비상업적 목적으로만 사용합니다.
- **Game Data:** 포켓몬 이름, 종족값, 진화, 기술, 메가진화 데이터는 [PokéAPI](https://pokeapi.co/) ([PokeAPI/api-data](https://github.com/PokeAPI/api-data), BSD-3-Clause)를 바탕으로 만들었습니다.
- **Badge Images:** 공식 배지 디자인(The Pokémon Company / Nintendo / Game Freak)을 바탕으로 이 프로젝트용으로 만든 32×32 도트 그림입니다.

## ⚖️ 라이선스

- **소스 코드:** [MIT](./LICENSE). MIT 라이선스는 소스 코드에만 적용됩니다.
- **이미지와 데이터:** 스프라이트, 체육관 배지, 포켓몬 이름과 데이터는 각 원저작자에게 권리가 있으며(위 Credits 참고) MIT 라이선스 대상이 **아닙니다**. **비상업적 용도로만** 사용할 수 있습니다.
- Pokémon © Nintendo / Creatures Inc. / GAME FREAK inc. 이 프로젝트는 비공식 팬 프로젝트이며, 위 회사들과 관련이 없고 승인을 받지 않았습니다.
