# Tech Specification

> 상시 로드 문서. 이 프로젝트의 기술 스택 관련 결정 사항이다.
> Claude Code는 작업할 때 이 문서를 기준으로 한다.

---

## 프로젝트 개요

- 프로젝트명 : 몬스터 리저브 (Monster Reserve)
- 장르 : 1인칭 슈팅 RPG (레퍼런스: theHunter: Call of the Wild, 동물 대신 몬스터를 사냥)
- 팀 구성 : 1인 (포트폴리오 프로젝트)

## Unity 환경

- 버전 : 6000.3.9f1(Unity 6.3)
- 랜더 파이프라인 : URP
- 빌드 플랫폼 : Windows
- 그래픽 에셋 : Dreamscape Nature Mountains - Stylized Open World Environment (Polyart Studio) 하나만 사용
    - 위치 : `Assets/Imports/Polyart/PolyartStudio/`
    - 에셋의 URP 지원 여부를 프로젝트에 넣기 전에 확인한다.
- 사운드 에셋 : 미정

## 성능

- 목표 : 빌드 기준 60FPS (Windows)
- 측정 : Game 뷰의 Stats (FPS, Batches, Tris)와 Profiler. 에디터 수치는 빌드보다 낮게 나오므로 참고값으로 보고, 확정은 빌드에서 한다.
- 맵 작업의 성능 기준값과 오브젝트 배치 방식은 `Docs/World/MapLayout.md`의 "성능 기준"을 따른다.
- 렌더링 설정 : Forward+, SRP Batcher, GPU Resident Drawer (Instanced Drawing), BatchRendererGroup Variants = Keep All. 이유와 되돌리는 조건은 MapLayout.md에 있다.

## 유저 입력 시스템

- 입력 코드 작성 규칙은 `Docs/Code/code-convention.md`의 "입력"을 따른다.

### 조작 키

| 키 | 행동 |
| --- | --- |
| WASD | 이동 |
| 왼쪽 Shift | 달리기 |
| 마우스 왼쪽 클릭 | 마법 발사 |
| 마우스 오른쪽 클릭 | 조준 (마법 줌인) |
| R (누르고 있기) | 마나 회복 (명상) |
| 숫자 키 1~5, 마우스 휠 | 마법 교체 |
| E | 상호작용 (시체 확인, 회복 지점, 의뢰서 확인) |
| G | 회복 스킬 |
| Q | 탐지와 추적 스킬 |
| M | 맵 |
| J | 의뢰 정보 팝업 |
| ESC | 일시 정지 메뉴 |

## 씬 관리

- Title : 시작 화면 (새로운 게임, 이어가기, 게임 종료)
- Game : 오픈월드 맵 전체와 거점을 포함한 플레이 씬
    - 넓은 맵도 여러 씬으로 나누지 않고 Game 씬 하나에 둔다.

## UI

- UI 구조와 화면 목록은 `Docs/UI/UI.md`를 따른다. (작성 예정)

## 폴더 구조

직접 만든 파일은 종류별 폴더에 두고, 그 안을 기능 폴더로 나눈다. 에셋 스토어에서 가져온 폴더와 섞지 않는다.

```
Assets/
  Scripts/<기능>/     스크립트 (에디터 전용 코드는 <기능>/Editor/)
  Materials/<기능>/   머티리얼
  Prefabs/<기능>/     프리팹
  Scenes/             씬
  Sounds/             사운드 파일, Audio Mixer
  Imports/            에셋 스토어에서 가져온 에셋 (직접 만든 파일을 두지 않는다)
```

기능 폴더

```
Player/    이동, 체력, 스킬
Magic/     마법
Monsters/
Quests/
Base/      상점, 의뢰 게시판, 회복 지점
UI/
Save/
World/     맵 구조물
```

## 수치 관리

- 플레이 중 바뀌지 않는 조정 값 (탄속, 감지 거리, 마법 가격 등)은 코드나 인스펙터에 직접 둔다. 별도 데이터 파일은 만들지 않는다.

## 저장

- 형식 : JSON 파일
- 저장 대상은 플레이어 행동으로 바뀌는 값만 : 돈, 구입한 마법, 스킬, 레벨과 경험치, 수락한 의뢰, 의뢰 게시판 5자리, 회복 남은 횟수, 플레이어 위치
- 몬스터 상태는 저장하지 않고, 게임을 켤 때 모두 새로 배치한다.
- 설정 값 (마우스 감도, 소리 크기)은 게임 저장 파일과 따로 저장한다.

## 연결 원칙

- 다른 기능은 1인칭 컨트롤러 내부를 직접 고치지 않고 요청하는 방식으로 연결한다 (예: 의뢰 정보 팝업이 컨트롤러에 "시점 회전 꺼 줘"라고 요청).
