# UI 구조

> 온디맨드 문서. UI 작업 (`ui-designer`) 때 먼저 읽는다.
> 화면마다 보여 줄 정보와 버튼은 `Docs/Design/GameDesign.md`의 "6. 화면과 상태 흐름"에 있다. 이 문서는 구조와 규칙만 적는다.

---

## 원칙

- UI는 게임 로직의 이벤트를 구독해서 표시한다. 게임 로직은 UI를 모른다 (`Docs/Code/ScriptStructure.md` 연결 구조).
- 커서 표시와 시점 잠금은 UI가 아니라 `PlayerController`가 상태 변화를 받아 처리한다.
- 버튼 클릭 연결과 이벤트 구독 해제는 `Docs/Code/code-convention.md`와 ScriptStructure 결정 기록 (UI 버튼 연결)을 따른다.

## Canvas 구성

역할별로 Canvas를 나눈다. 자주 바뀌는 HUD가 다른 화면을 다시 그리게 하지 않기 위해서다.

| Canvas | 담는 것 | Sort Order | GraphicRaycaster |
| --- | --- | --- | --- |
| HUD | 나침반, 체력, 마나, 조준점, 돈, 회복 남은 횟수 | 0 | 없음 (누를 것이 없음) |
| Popup | 의뢰 정보 (J), 시체 확인 안내, 상호작용 안내 | 10 | 누를 것이 있는 화면에만 |
| Window | 맵, 일시 정지 메뉴, 설정, 상점, 사망 | 20 | 있음 |

- 세 Canvas 모두 Screen Space - Overlay.
- Window와 Popup 안의 화면은 화면마다 하위 Canvas를 하나씩 둔다 (아래 "화면 열고 닫기").

## 화면 열고 닫기

- 게임 상태에 묶인 화면 (맵, 일시 정지 메뉴, 사망)은 각자 `GameManager.OnStateChanged`를 구독해 스스로 켜고 끈다.
- 상태에 묶이지 않은 화면은 여는 쪽이 따로 있다. 정확한 연결은 그 기능을 만들 때 정한다.
    - 설정 : 일시 정지 메뉴의 버튼
    - 상점 : 거점 상점과 E 상호작용
    - 의뢰 정보 : J 입력
    - 시체 확인 안내, 상호작용 안내 : `PlayerInteractor`가 바라보는 대상이 바뀌었다는 알림
- 켜고 끌 때는 GameObject가 아니라 화면의 `Canvas` 컴포넌트 (`enabled`)를 켜고 끈다.
    - 다시 켤 때 레이아웃을 새로 계산하지 않아 싸다.
    - 꺼져 있어도 스크립트가 살아 있어서 이벤트 구독이 끊기지 않는다.

## 최적화 규칙

- HUD 값은 값이 바뀌었다는 이벤트를 받을 때만 고친다. `Update`에서 값을 읽지 않는다. 나침반만 카메라 방향을 매 프레임 따라간다.
- 누를 필요가 없는 이미지와 글자는 `Raycast Target`을 끈다.

## 해상도와 글자

- Canvas Scaler : Scale With Screen Size, 기준 해상도 1920 × 1080, Match 0.5 (임시값).
- 글자 : TMP 기본 폰트, 영어 임시 표기 (GameDesign 결정 기록 2026-11-15). 한글 폰트는 나중에 추가한다.

## 화면 목록

| 화면 | 분류 | Canvas | 여는 방법 | 게임 멈춤 |
| --- | --- | --- | --- | --- |
| 시작 화면 | Window | Title 씬의 Canvas | 씬 시작 | - |
| 게임 화면 | HUD | HUD | 항상 | 아니오 |
| 맵 | Window | Window | M (`Map` 상태) | 예 |
| 일시 정지 메뉴 | Window | Window | ESC (`Paused` 상태) | 예 |
| 설정 | Window | Window | 일시 정지 메뉴의 버튼 | 예 (일시 정지 중에 열림) |
| 사망 | Window | Window | `Dead` 상태 | 예 |
| 상점 | Window | Window | 거점 상점에서 E | 아니오 |
| 의뢰 정보 | Popup | Popup | J | 아니오 |
| 시체 확인 안내, 상호작용 안내 | Popup | Popup | 바라보는 대상이 바뀔 때 | 아니오 |

- 의뢰 게시판과 회복 지점은 월드 안의 오브젝트라 이 문서에서 다루지 않는다.
- 마법 줌인 화면은 표시할 정보가 미정이라 목록에 넣지 않았다 (GameDesign 10번 미정 사항).

## 폴더

- 프리팹 : `Assets/Prefabs/UI/`
- 스크립트 : `Assets/Scripts/UI/`
- 게임 화면 UI는 프리팹 `Assets/Prefabs/UI/GameUI.prefab` 하나에 모으고, 씬에는 이 프리팹을 놓는다.
- EventSystem은 프리팹에 넣지 않고 씬마다 하나씩 둔다 (한 씬에 두 개가 생기지 않게).
