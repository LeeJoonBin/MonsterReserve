using UnityEngine;
using UnityEngine.InputSystem;

// ESC, M 키를 게임 상태 변경 요청으로 바꾼다. 키 규칙은 GameDesign.md 결정 기록 (2026-11-15)을 따른다.
// 키 입력은 여러 요청자 중 하나일 뿐이다 (나중에 일시 정지 메뉴의 "계속하기" 버튼도 ChangeState를 부른다)
public class GameStateInputHandler : MonoBehaviour
{
    [SerializeField] private InputActionReference _pauseAction;
    [SerializeField] private InputActionReference _mapAction;

    private void OnEnable() => BindInputEvents();
    private void OnDisable() => UnbindInputEvents();

    private void BindInputEvents()
    {
        _pauseAction.action.performed += HandlePause;
        _mapAction.action.performed += HandleMap;

        _pauseAction.action.Enable();
        _mapAction.action.Enable();
    }

    private void UnbindInputEvents()
    {
        _pauseAction.action.performed -= HandlePause;
        _mapAction.action.performed -= HandleMap;

        _pauseAction.action.Disable();
        _mapAction.action.Disable();
    }

    // ESC : 플레이 중이면 멈추고, 일시 정지나 맵이 열려 있으면 닫는다. 사망 중에는 무시한다 (case 없음)
    private void HandlePause(InputAction.CallbackContext context)
    {
        switch (GameManager.Instance.CurrentState)
        {
            case GameState.Playing:
                GameManager.Instance.ChangeState(GameState.Paused);
                break;
            case GameState.Paused:
            case GameState.Map:
                GameManager.Instance.ChangeState(GameState.Playing);
                break;
        }
    }

    // M : 맵을 열고 닫는다. 일시 정지 중에는 두 창이 겹치므로 무시한다
    private void HandleMap(InputAction.CallbackContext context)
    {
        switch (GameManager.Instance.CurrentState)
        {
            case GameState.Playing:
                GameManager.Instance.ChangeState(GameState.Map);
                break;
            case GameState.Map:
                GameManager.Instance.ChangeState(GameState.Playing);
                break;
        }
    }
}
