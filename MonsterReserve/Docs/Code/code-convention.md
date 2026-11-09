# C# 코드 컨벤션

## 네이밍
| 대상 | 규칙 | 예시 |
|---|---|---|
| 클래스, 구조체, 메서드, 프로퍼티 | PascalCase | `PlayerMover`, `TakeDamage()` |
| private 필드 | `_camelCase` | `_moveSpeed` |
| 지역 변수, 매개변수 | camelCase | `moveInput` |
| 인터페이스 | `I` + PascalCase | `IDamageable` |
| 상수 (`const`, `static readonly`) | PascalCase | `MaxHealth` |
| 열거형 타입, 멤버 | PascalCase | `PlayerState.Idle` |
| 이벤트 | `On` + 동사 과거형/명사 | `OnDied`, `OnHealthChanged` |
| 이벤트 핸들러 메서드 | `Handle` + 이벤트 이름 (`On` 제외) | `HandleDied()`, `HandleHealthChanged()` |
| 코루틴 | `Co` + 동작 | `CoRespawn()` |

- bool은 질문처럼 읽히게 쓴다: `isGrounded`, `hasKey`, `canJump`
- 메서드는 동사로 시작한다: `Move()`, `ApplyDamage()`, `GetMoveDirection()`
- 줄임말은 피한다: `pos` 대신 `position`, `cnt` 대신 `count`. 단, `UI`, `ID`, `HP`처럼 널리 쓰는 말은 허용한다.

## 클래스 내부 순서
1. 상수
2. `[SerializeField]` 필드
3. private 필드
4. 프로퍼티, 이벤트
5. Unity 생명주기 메서드 (`Awake` → `OnEnable` → `Start` → `Update` → `FixedUpdate` → `LateUpdate` → `OnDisable` → `OnDestroy`)
6. public 메서드
7. private 메서드

## 필드
- Inspector에 노출할 값은 `public` 대신 `[SerializeField] private`로 쓴다.
- 다른 스크립트에서 읽어야 하면 프로퍼티로 공개한다: `public float MoveAmount { get; private set; }`
- 접근 제한자를 생략하지 않는다: `void Move()`가 아니라 `private void Move()`
- 숫자를 코드에 바로 쓰지 않는다. 조절할 값은 `[SerializeField]`로, 고정 값은 상수로 뺀다.
  ```csharp
  // 나쁨
  transform.position += dir * 4f * Time.deltaTime;
  // 좋음
  [SerializeField] private float _moveSpeed = 4f;
  ```
- Inspector 필드가 많으면 `[Header("...")]`로 묶는다.(6개 이상)

## 열거형
- 타입 이름은 단수형으로 쓴다: `PlayerState` (`PlayerStates` 아님)
- 멤버에 타입 이름을 반복하지 않는다: `PlayerState.Idle` (`PlayerState.PlayerIdle` 아님)
- 비트 플래그로 쓸 때만 `[Flags]`를 붙이고 이름을 복수형으로 쓴다. 값은 2의 거듭제곱으로 정하고 `None = 0`을 둔다.
- 저장하거나 직렬화하는 열거형은 값을 명시한다. 중간에 멤버를 추가해도 기존 데이터가 깨지지 않게 하기 위함이다.
  ```csharp
  public enum ItemType
  {
      None = 0,
      Weapon = 1,
      Armor = 2
  }
  ```

## 메서드
- Unity 생명주기 메서드는 한 줄로 쓰고, 실제 처리는 이름 있는 메서드로 분리한다.
  ```csharp
  private void Awake() => CacheComponents();
  private void OnEnable() => BindInputEvents();
  private void OnDisable() => UnbindInputEvents();
  ```
- 메서드 하나는 한 가지 일만 한다. 30줄을 넘으면 나눌 수 있는지 본다.
- 매개변수가 4개를 넘으면 구조체나 클래스로 묶는다.
- 조건이 맞지 않으면 일찍 반환해서 중첩을 줄인다.
  ```csharp
  if (!_controller.isGrounded) return;
  ```

## 이벤트
- 이벤트를 OnEnable에서 구독했다면 OnDisable에서 해제한다.
- 클래스 간 알림은 `event Action<T>`를 쓴다. 외부에서 호출하지 못하게 `event` 키워드를 붙인다.
- 호출할 때 구독자가 없을 수 있으므로 `?.Invoke()`로 호출한다.

## 성능
- `GetComponent`, `Camera.main`, `Find` 계열은 `Awake`에서 한 번만 찾아 필드에 저장한다. `Update`에서 호출하지 않는다.
- `Update`에서 `new`로 객체나 배열을 만들지 않는다. 필요하면 재사용한다.
- 태그 비교는 `CompareTag("Player")`를 쓴다 (`tag == "Player"`는 매번 문자열을 만든다).
- 물리 처리는 `FixedUpdate`, 카메라·애니메이션 후처리는 `LateUpdate`에서 한다.
- 자주 생성·삭제하는 오브젝트(총알, 이펙트)는 오브젝트 풀을 쓴다.

## null 처리
- Unity 오브젝트(`GameObject`, `Component` 등)에는 `?.`, `??`를 쓰지 않는다. 파괴된 오브젝트를 null로 판단하지 못한다.
  ```csharp
  // 나쁨
  _target?.TakeDamage(1);
  // 좋음
  if (_target != null) _target.TakeDamage(1);
  ```
- 반드시 연결돼야 하는 Inspector 참조는 null 체크로 숨기지 말고, 비어 있으면 오류가 나게 둔다. 문제를 빨리 찾기 위함이다.

## 입력
- new Input System을 쓴다. 구 `InputManager`와 구 `Input` 클래스(`Input.GetKey` 등)는 쓰지 않는다.
- `InputActionReference`를 Inspector로 받아 InputAction을 참조하고, `performed` / `canceled` 이벤트에 바인딩한다.
- OnEnable에서 InputAction의 `Enable()`, OnDisable에서 `Disable()`을 호출한다.

## 코루틴 · 비동기
- 코루틴을 멈출 수 있어야 하면 `Coroutine` 변수에 저장하고 `StopCoroutine`으로 멈춘다.
- `WaitForSeconds`처럼 반복해서 쓰는 대기 객체는 필드에 캐싱한다.
- 오브젝트가 비활성화되면 코루틴도 멈춘다는 점을 고려한다.

## 서식
- 중괄호는 새 줄에 쓴다 (Allman 스타일).
- 한 줄짜리 `if`는 중괄호 없이 한 줄로 쓸 수 있다. 두 줄 이상이면 중괄호를 쓴다.
- 들여쓰기는 공백 4칸.
- `var`는 오른쪽에서 타입이 분명할 때만 쓴다: `var list = new List<int>();`
- 사용하지 않는 `using`은 지운다.

## 파일과 폴더
- 파일 하나에 클래스 하나. 파일 이름과 클래스 이름을 같게 한다.
- 스크립트, 머티리얼 등 파일을 둘 폴더는 `Docs/TechSpec.md`의 "폴더 구조"를 따른다.
- 에디터 전용 코드는 `Editor` 폴더에 둔다. 빌드에 포함되지 않게 하기 위함이다.

## 주석
- 코드가 무엇을 하는지가 아니라, 왜 그렇게 했는지를 적는다.
- 사용하지 않는 코드는 주석 처리하지 말고 지운다.
- `Debug.Log`는 작업이 끝나면 지운다. 남겨야 하면 이유를 주석으로 적는다.
- 할 일은 `// TODO: 내용` 형식으로 쓴다.
