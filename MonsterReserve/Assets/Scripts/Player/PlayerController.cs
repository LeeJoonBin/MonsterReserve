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

    private void ChangeActionMap(string mapName)
    {
        
    }
}
