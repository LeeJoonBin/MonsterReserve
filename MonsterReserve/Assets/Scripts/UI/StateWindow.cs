using UnityEngine;

// 게임 상태에 묶인 화면(맵, 일시 정지, 사망)을 상태에 맞춰 켜고 끈다 (UI.md "화면 열고 닫기").
// GameObject가 아니라 Canvas를 끄므로, 꺼져 있어도 구독이 유지된다
[RequireComponent(typeof(Canvas))]
public class StateWindow : MonoBehaviour
{
    // 이 화면이 보이는 상태
    [SerializeField] private GameState _visibleState;

    private Canvas _canvas;

    private void Awake() => CacheComponents();
    private void OnEnable() => BindStateEvents();
    private void OnDisable() => UnbindStateEvents();

    private void CacheComponents()
    {
        _canvas = GetComponent<Canvas>();
    }

    // 구독 전에 바뀐 상태를 놓치지 않도록 현재 상태로 한 번 맞춘다
    private void BindStateEvents()
    {
        GameManager.Instance.OnStateChanged += HandleStateChanged;
        HandleStateChanged(GameManager.Instance.CurrentState);
    }

    private void UnbindStateEvents()
    {
        GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void HandleStateChanged(GameState state)
    {
        _canvas.enabled = state == _visibleState;
    }
}
