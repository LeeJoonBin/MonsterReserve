using System;
using UnityEngine;

// 게임 전체 상태(플레이, 맵, 일시 정지, 사망)와 시간 멈춤을 맡는다.
// 의뢰 정보 팝업(J)은 게임을 멈추지 않으므로 상태에 넣지 않는다 (시점 잠금은 PlayerLook.RequestLock이 맡는다)
// 실행 순서를 앞당겨, 다른 스크립트의 OnEnable에서 Instance를 쓸 때 이미 준비돼 있게 한다
[DefaultExecutionOrder(-100)]
public class GameManager : MonoBehaviour
{
    private const float PlayingTimeScale = 1f;
    private const float StoppedTimeScale = 0f;

    // 싱글톤 : UI(M, ESC), PlayerHealth(사망) 등 여러 곳이 상태를 바꾸므로 어디서든 찾을 수 있게 둔다
    public static GameManager Instance { get; private set; }

    public GameState CurrentState { get; private set; } = GameState.Playing;
    public event Action<GameState> OnStateChanged;

    private void Awake() => RegisterInstance();
    private void OnDestroy() => UnregisterInstance();

    public void ChangeState(GameState newState)
    {
        if (newState == CurrentState) return;

        CurrentState = newState;
        ApplyTimeScale();
        OnStateChanged?.Invoke(CurrentState);
    }

    // 중복이면 게임 오브젝트가 아니라 이 컴포넌트만 지운다. 같은 오브젝트에 붙은 PoolManager까지 사라지지 않게 하기 위함이다
    private void RegisterInstance()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ApplyTimeScale();
    }

    // 일시 정지 중에 타이틀로 나가면 시간 배율 0이 다음 씬까지 남으므로, 씬을 나갈 때 되돌린다
    private void UnregisterInstance()
    {
        if (Instance != this) return;

        Instance = null;
        Time.timeScale = PlayingTimeScale;
    }

    // 시간 배율 0이면 FixedUpdate와 물리는 멈추고, Update는 계속 돈다 (Time.deltaTime만 0이 된다).
    // 생각해 볼 것 : 일시 정지 메뉴의 UI 애니메이션은 어떤 시간을 써야 멈추지 않을까? (찾아볼 말 : unscaledDeltaTime)
    private void ApplyTimeScale()
    {
        Time.timeScale = CurrentState == GameState.Playing ? PlayingTimeScale : StoppedTimeScale;
    }
}
