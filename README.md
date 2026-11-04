# TamaPoke Web

브라우저에서 포켓몬을 키우는 다마고치 팬 게임입니다. 설치 없이 PC와 휴대폰에서 바로 할 수 있어요.
Windows 데스크톱 펫 [TamaPoke_PC](https://github.com/gomdolit-jeong/TamaPoke_PC)의 웹 버전입니다.

**▶ 플레이: https://gomdolit-jeong.github.io/TamaPoke_Web/**

> 비공식 비상업 팬 프로젝트입니다. Nintendo, Creatures Inc., GAME FREAK inc., The Pokémon Company와 관련이 없고 승인을 받지 않았습니다.

## 주요 기능

- **돌보기:** 알에서 부화시켜 밥 주기, 놀아 주기, 재우기, 씻기기로 키웁니다. 돌본 만큼 진화하고, 시간이 지나면 작별의 순간도 찾아옵니다.
- **미니게임:** 공놀이, 받기, 기억력, 청소 게임이 있습니다.
- **배틀과 포획:** 야생 포켓몬과 턴제 배틀을 하고, 잡아서 최대 6마리 파티를 꾸립니다.
- **체육관 64곳:** 지방별 관장에게 도전해 배지를 모읍니다.
- **메가진화:** 배지 8개로 메가링을 얻고, 메가스톤 조각을 모아 전투에서 메가진화합니다.
- **2.5D 산책:** 파티 포켓몬이 들판을 돌아다니고 풀숲에서 아이템을 찾아옵니다. 화면을 돌리고 확대하고 옮길 수 있습니다.
- **이로치와 특수 폼:** 색이 다른 포켓몬과 지방 폼이 나옵니다.
- **세이브:** 게스트는 브라우저에 저장하고, 구글로 로그인하면 계정 세이브를 다른 기기에서 이어서 합니다. 두 세이브는 따로 관리됩니다.
- **그 밖에:** 도감, 가방, 프로필, 한국어와 영어, 브라우저 알림, 세이브 내보내기와 가져오기를 지원합니다.

## PC판과 다른 점

브라우저의 한계로 바탕화면 산책, 항상 위 고정, 트레이 메뉴는 없습니다. 산책은 브라우저 창 안에서 하고, 시간은 PC판처럼 게임을 켜 둔 동안에만 흐릅니다.

## 기술

- [Blazor WebAssembly](https://learn.microsoft.com/aspnet/core/blazor/) (.NET 9), 서버 없는 정적 사이트
- [GitHub Pages](https://pages.github.com/)와 GitHub Actions 자동 배포
- [Firebase](https://firebase.google.com/) Authentication, Cloud Firestore (구글 로그인 세이브)

`cloud.js`의 Firebase `apiKey`는 웹 앱에 원래 공개되는 식별값입니다. 세이브는 Firestore 보안 규칙으로 보호하여 본인 계정의 문서만 읽고 쓸 수 있습니다.

## 개발

[.NET 9 SDK](https://dotnet.microsoft.com/download)가 필요합니다.

```bash
dotnet run --project TamaPoke.Web
```

`main`에 푸시하면 GitHub Actions가 빌드해서 GitHub Pages에 배포합니다.

### 로컬 테스트 (Debug 빌드 전용)

배포되는 Release 빌드에는 들어가지 않습니다.

- 세이브가 비어 있으면 Lv.100 포켓몬 6마리, 메가링과 모든 메가스톤을 가진 테스트 세이브로 시작합니다.
- 다른 세이브가 있어도 게스트 상태의 **설정 → 테스트 세이브 불러오기**로 바꿀 수 있습니다.
- 알에서는 항상 파이리가 부화합니다. 배포판은 무작위입니다.

### 데이터 만들기

`wwwroot`의 게임 데이터와 풍경 그림은 `tools`의 스크립트로 만듭니다.

```bash
python tools/make_pokemon_data.py <PC판 Data/pokemon_data.json>   # 포켓몬 데이터
python tools/make_skill_data.py <PC판 Data 폴더>                   # 기술 데이터
python tools/make_form_data.py <SpriteCollab tracker.json>         # 폼 목록
python tools/make_walk_scene.py                                    # 산책 풍경 그림
python tools/make_walk_bg.py                                       # 산책 풀밭 그림
```

## 참고한 오픈소스 프로젝트

이 프로젝트는 [TamaPoke_PC](https://github.com/gomdolit-jeong/TamaPoke_PC)를 웹으로 옮긴 것입니다. PC판은 아래의 임베디드/웹 기반 `TamaPoke` 프로젝트들의 게임 루프, 기획, 아이디어를 참고해 만들었습니다.

- [socquique/TamaPoke](https://github.com/socquique/TamaPoke): 원본 펌웨어와 기본 다마고치 구조
- [ShadowEnemyx/TamaPoke (Expanded Update)](https://github.com/ShadowEnemyx/TamaPoke/tree/tamapoke-expanded-update): 확장 기능과 콘텐츠
- [DylanPDao/TamaPoke](https://github.com/DylanPDao/TamaPoke): 배틀과 추가 시스템 아이디어

## Credits

- **Sprites:** [PMDCollab/SpriteCollab](https://github.com/PMDCollab/SpriteCollab) 기여자들의 작업물이며, [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/)에 따라 비상업적 목적으로만 사용합니다.
- **Game Data:** 포켓몬 이름, 종족값, 진화, 기술, 메가진화 데이터는 [PokéAPI](https://pokeapi.co/) ([PokeAPI/api-data](https://github.com/PokeAPI/api-data), BSD-3-Clause)를 바탕으로 만들었습니다.
- **Badge Images:** 공식 배지 디자인(The Pokémon Company / Nintendo / Game Freak)을 바탕으로 이 프로젝트용으로 만든 32×32 도트 그림입니다.

## 라이선스

- **소스 코드:** [MIT](./LICENSE). MIT 라이선스는 소스 코드에만 적용됩니다.
- **이미지와 데이터:** 스프라이트, 체육관 배지, 포켓몬 이름과 데이터는 각 원저작자에게 권리가 있으며(위 Credits 참고) MIT 라이선스 대상이 **아닙니다**. **비상업적 용도로만** 사용할 수 있습니다.
- Pokémon © Nintendo / Creatures Inc. / GAME FREAK inc. 이 프로젝트는 비공식 팬 프로젝트이며, 위 회사들과 관련이 없고 승인을 받지 않았습니다.

---

## English

A Tamagotchi-style virtual pet fan game you can play in your browser, on desktop or mobile, with nothing to install. It is the web version of the Windows desktop pet [TamaPoke_PC](https://github.com/gomdolit-jeong/TamaPoke_PC).

**▶ Play: https://gomdolit-jeong.github.io/TamaPoke_Web/**

Hatch and raise your Pokémon, play mini-games, battle and catch wild Pokémon, challenge 64 gyms, Mega Evolve, and take your party for a walk in a rotatable 2.5D field. Play as a guest with a save in your browser, or sign in with Google to continue on other devices. Available in Korean and English.

TamaPoke PC, which this project ports to the web, drew its game loops, planning and ideas from [socquique/TamaPoke](https://github.com/socquique/TamaPoke), [ShadowEnemyx/TamaPoke (Expanded Update)](https://github.com/ShadowEnemyx/TamaPoke/tree/tamapoke-expanded-update) and [DylanPDao/TamaPoke](https://github.com/DylanPDao/TamaPoke).

This is an unofficial, non-commercial fan project, not affiliated with or endorsed by Nintendo, Creatures Inc., GAME FREAK inc. or The Pokémon Company. Pokémon © Nintendo / Creatures Inc. / GAME FREAK inc.

The MIT license covers the source code only. Sprites ([PMDCollab/SpriteCollab](https://github.com/PMDCollab/SpriteCollab), [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/)), gym badges and Pokémon data belong to their respective owners and may be used for non-commercial purposes only.
