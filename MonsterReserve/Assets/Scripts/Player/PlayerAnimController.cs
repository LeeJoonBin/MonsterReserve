using UnityEngine;

public class PlayerAnimController : MonoBehaviour
{
    // Animator 파라미터 이름 (PlayerAniController.controller와 같아야 한다)
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");

    // 모델(PicoChan)에 붙은 Animator를 연결한다
    [SerializeField] private Animator _animator;
    [SerializeField] private PlayerInputHandler _inputHandler;

    private void Update() => UpdateLocomotion();

    // 값을 받는 길은 세 가지다 : 입력에게 직접 묻기, PlayerMover가 이벤트로 알리기, PlayerController가 전달하기.
    // 여기서는 "직접 묻기"를 골랐다. 매 프레임 바뀌는 값이라 이벤트로 알려도 매 프레임 불리기 때문이다.
    // 생각해 볼 것 : 입력값 대신 PlayerMover의 실제 속도를 쓰면, 벽에 막혀 서 있을 때 애니메이션이 어떻게 달라질까?
    private void UpdateLocomotion()
    {
        Vector2 input = _inputHandler.MoveInput;
        _animator.SetFloat(MoveXHash, input.x);
        _animator.SetFloat(MoveYHash, input.y);
        // 제자리에서 Shift만 누르면 달리기 모션이 나오지 않게, 움직일 때만 달리기로 본다
        _animator.SetBool(IsRunningHash, _inputHandler.IsSprinting && input != Vector2.zero);
    }

    // TODO: 점프, 공중 애니메이션은 Animator에 점프 상태를 만든 뒤 작성 (PlayerMover.IsGrounded 사용)
}
