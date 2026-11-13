# 스크립트 구조 초안 (Script Structure)

> 작성 : 2026-11-12. 게임 로직 스크립트를 짜기 전에 참고하는 **초안**이다. 정답이 아니라 출발점이다.
> 스크립트는 사용자가 직접 작성하므로, 이 문서의 이름, 나눈 방식, 연결 방식은 사용자가 따져 보고 고친다.
> 정해진 규칙은 `Docs/TechSpec.md`(폴더 구조, 연결 원칙, 수치 관리), `Docs/Code/code-convention.md`(이름, 작성 규칙), `Docs/Design/GameDesign.md`(게임 규칙)에 있다. 이 문서는 그 규칙을 다시 적지 않는다.

---

## 1. 전체 그림

```
[입력 에셋 .inputactions]
        │
   ┌────┴─────────────────────────────┐
 Player                             Magic
 (이동·시점·체력·마나·스킬·상호작용)     (발사·교체·조준·투사체)
   │  ▲ 이벤트(OnDied, OnHealthChanged…)      │ 투사체가 맞힘 → IDamageable
   │  │                                     ▼
   │  └──── UI가 구독해서 표시          Monsters (감지·추격·공격·시체)
   │                                     │ OnDied → 경험치, 의뢰 확인
   ▼ IInteractable (E키)                  ▼
 Base (상점·회복 지점)  ←→  Quests (게시판·수락한 의뢰)
                    ↘        ↙
                      Save (JSON)
```

### 공통 약속 (여러 폴더가 함께 쓰는 인터페이스)

| 이름 | 뜻 | 쓰는 곳 (후보) |
|---|---|---|
| `IDamageable` | 맞을 수 있는 것 | 플레이어, 몬스터 |
| `IInteractable` | E키로 상호작용할 수 있는 것 | 시체, 회복 지점, 의뢰서, 상점 |

- 두 인터페이스를 어느 폴더에 둘지는 미정이다 (아래 `## 3. 생각해 볼 질문`).

---

## 2. 폴더별 후보 (`Assets/Scripts/<기능>/`)

### Player

| 후보 스크립트 | 맡는 일 |
|---|---|
| `PlayerMover` | WASD 이동, 왼쪽 Shift 달리기 |
| `PlayerLook` | 마우스 시점 회전. 다른 기능이 "시점 회전 꺼 줘"라고 **요청**하는 메서드를 둔다 (TechSpec 연결 원칙) |
| `PlayerHealth` | 체력, `OnHealthChanged`, `OnDied` |
| `PlayerMana` | 마나, R키 명상 |
| `PlayerLevel` | 경험치, 레벨업, `OnLevelUp` |
| `PlayerWallet` | 돈 |
| `HealSkill` | G키 회복, 남은 횟수 |
| `TrackingSkill` | Q키 탐지와 추적 |
| `PlayerInteractor` | E키. 바라보는 `IInteractable`을 찾아 실행 |

### Magic

| 후보 스크립트 | 맡는 일 |
|---|---|
| `MagicCaster` | 마우스 왼쪽 클릭 발사, 마나 소모 요청 |
| `MagicSelector` | 숫자 키 1~5, 마우스 휠로 교체 |
| `MagicAim` | 마우스 오른쪽 클릭 줌인 |
| `MagicProjectile` | 탄도 (중력 낙차와 비행 시간) |

### Monsters

| 후보 스크립트 | 맡는 일 |
|---|---|
| `Monster` | 체력, `IDamageable`, `OnDied` |
| `MonsterSenses` | 감지 거리, 피격 시 감지 거리, 은신에 따른 감지 확률 |
| `MonsterBrain` | 상태 전환 (대기 → 추격 → 공격 → 사망) |
| `MonsterCorpse` | 시체 확인 (`IInteractable`) |
| `MonsterSpawner` | 서식지 배치, 처치 10분 뒤 다시 나옴 |

### Quests

| 후보 스크립트 | 맡는 일 |
|---|---|
| `QuestBoard` | 게시판 5자리, 빈자리 채우는 시간 |
| `QuestNote` | 의뢰서 한 장 (`IInteractable`) |
| `QuestLog` | 수락한 의뢰 (최대 3), 시체 확인 시 완료 |

### Base

| 후보 스크립트 | 맡는 일 |
|---|---|
| `MagicShop` | 마법 구매 |
| `HealingPoint` | 회복 남은 횟수 초기화 (`IInteractable`) |

### Save

| 후보 스크립트 | 맡는 일 |
|---|---|
| `SaveData` | 저장할 값 묶음 |
| `SaveSystem` | JSON 읽고 쓰기, 사망 후 나갈 때 삭제 |
| `SettingsStorage` | 설정 값 (마우스 감도, 소리 크기) 따로 저장 |

---

## 3. 생각해 볼 질문

구조를 확정하기 전에 직접 판단할 것들이다. 답을 정하면 이 문서에 결정과 이유를 적는다.

1. **마법 데이터를 어디에 둘까?**
    - TechSpec에 "별도 데이터 파일은 만들지 않는다"는 규칙이 있다. ScriptableObject는 이 규칙에 걸릴까?
    - 걸린다면, 마법마다 다른 값 (탄속, 마나 소모, 줌 배율)을 어떻게 구분할까? (힌트 : 마법마다 프리팹)
2. **빠른 투사체가 얇은 몬스터를 뚫고 지나가면?**
    - 한 프레임에 이동하는 거리가 몬스터 두께보다 크면 충돌을 놓칠 수 있다. 어떻게 막을까?
3. **게임 멈춤 (M, ESC)과 사망은 누가 관리할까?**
    - 폴더 목록에 "게임 전체 상태"를 맡을 자리가 없다. 새 폴더를 만들까, 기존 폴더에 넣을까?
4. **`PlayerHealth`가 UI를 직접 알아야 할까, UI가 이벤트를 구독해야 할까?**
    - 각 방식에서 어느 쪽이 어느 쪽을 몰라도 되는지 생각해 본다.
5. **스킬 수준 (마법 다루기, 은신)은 어디에 둘까?**
    - 쓰는 곳 (`MagicAim`, `MonsterSenses`)과 저장하는 곳 (`SaveData`)이 다르다.
6. **공통 인터페이스 (`IDamageable`, `IInteractable`)는 어느 폴더에 둘까?**

---

## 4. 구현 순서

- `Docs/Design/GameDesign.md` 8번 "구현 단계" 표에서 사용자가 정한다.
- 참고 : 같은 문서에 "저격 탄도는 넓은 월드가 없어도 시험장 하나에서 먼저 만들어 볼 수 있다"는 힌트가 있다. **이동과 시점 → 발사와 탄도 → 맞는 표적** 중 무엇이 가장 큰 위험을 먼저 확인시켜 줄지 생각해 본다.
