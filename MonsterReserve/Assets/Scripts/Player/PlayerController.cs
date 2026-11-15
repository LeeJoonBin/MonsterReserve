using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private PlayerAnimController _playerAnim;
    [SerializeField] private PlayerInputHandler _playerInput;
    [SerializeField] private PlayerMover _playerMover;
    [SerializeField] private PlayerLook _playerLook;

    // 허브 역할 : 다른 기능(UI 팝업 등)은 이 프로퍼티로 PlayerLook을 찾아 잠금을 요청한다
    public PlayerLook PlayerLook => _playerLook;

    private void OnEnable() => SubscribeGameState();
    private void OnDisable() => UnsubscribeGameState();

    private void SubscribeGameState()
    {
        GameManager.Instance.OnStateChanged += HandleStateChanged;
    }

    // 씬을 닫을 때는 어느 오브젝트가 먼저 사라질지 정해져 있지 않다. GameManager가 먼저 사라졌으면 해제할 대상이 없다
    private void UnsubscribeGameState()
    {
        if (GameManager.Instance == null) return;

        GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    // 일시 정지, 맵, 사망 중에는 시점을 잠그고 커서를 보인다.
    // 요청자를 this로 넘기므로, 의뢰 팝업(J)이 따로 잠가 둔 것은 여기서 풀지 않는다
    private void HandleStateChanged(GameState state)
    {
        if (state == GameState.Playing) _playerLook.ReleaseLock(this);
        else _playerLook.RequestLock(this);
    }

    private void ChangeActionMap(string mapName)
    {
        
    }
}
