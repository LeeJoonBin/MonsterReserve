using UnityEngine;
using UnityEngine.InputSystem;

// 테스트 전용 : PlayerAniController의 상태와 전환을 키 입력으로 확인하기 위한 임시 스크립트 (이슈 #18).
// 실제 플레이어 컨트롤러를 작성하면 이 파일은 삭제한다.
// 지형 높낮이와 충돌은 따지지 않으므로 평평한 곳에서 테스트한다.
public class PlayerAnimationTester : MonoBehaviour
{
    // Animator 파라미터 이름 (PlayerAniController와 같아야 한다)
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
    private static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
    private static readonly int IsMeditatingHash = Animator.StringToHash("IsMeditating");
    private static readonly int CastHash = Animator.StringToHash("Cast");
    private static readonly int HealHash = Animator.StringToHash("Heal");
    private static readonly int DetectHash = Animator.StringToHash("Detect");

    [Header("참조")]
    [SerializeField] private Animator _animator;

    [Header("이동 속도 (임시값, 기획서 미정)")]
    [SerializeField] private float _walkSpeed = 2f;
    [SerializeField] private float _runSpeed = 5f;

    [Header("입력 (PlayerInputActions / Player 맵)")]
    [SerializeField] private InputActionReference _moveAction;
    [SerializeField] private InputActionReference _sprintAction;
    [SerializeField] private InputActionReference _aimAction;
    [SerializeField] private InputActionReference _castMagicAction;
    [SerializeField] private InputActionReference _meditateAction;
    [SerializeField] private InputActionReference _healAction;
    [SerializeField] private InputActionReference _detectAction;

    private Vector2 _moveInput;
    private bool _isRunning;
    private bool _isMeditating;

    private void OnEnable() => BindInputEvents();

    private void Update() => MoveAndAnimate();

    private void OnDisable() => UnbindInputEvents();

    private void BindInputEvents()
    {
        _moveAction.action.performed += HandleMove;
        _moveAction.action.canceled += HandleMove;
        _sprintAction.action.performed += HandleSprint;
        _sprintAction.action.canceled += HandleSprint;
        _aimAction.action.performed += HandleAim;
        _aimAction.action.canceled += HandleAim;
        _meditateAction.action.performed += HandleMeditate;
        _meditateAction.action.canceled += HandleMeditate;
        _castMagicAction.action.performed += HandleCastMagic;
        _healAction.action.performed += HandleHeal;
        _detectAction.action.performed += HandleDetect;
        SetActionsEnabled(true);
    }

    private void UnbindInputEvents()
    {
        _moveAction.action.performed -= HandleMove;
        _moveAction.action.canceled -= HandleMove;
        _sprintAction.action.performed -= HandleSprint;
        _sprintAction.action.canceled -= HandleSprint;
        _aimAction.action.performed -= HandleAim;
        _aimAction.action.canceled -= HandleAim;
        _meditateAction.action.performed -= HandleMeditate;
        _meditateAction.action.canceled -= HandleMeditate;
        _castMagicAction.action.performed -= HandleCastMagic;
        _healAction.action.performed -= HandleHeal;
        _detectAction.action.performed -= HandleDetect;
        SetActionsEnabled(false);
    }

    private void SetActionsEnabled(bool isEnabled)
    {
        InputActionReference[] actions = { _moveAction, _sprintAction, _aimAction, _castMagicAction, _meditateAction, _healAction, _detectAction };
        foreach (InputActionReference reference in actions)
        {
            if (isEnabled) reference.action.Enable();
            else reference.action.Disable();
        }
    }

    // 명상 중에는 멈춘다 (기획서의 "명상 중 이동 가능 여부"가 미정이라 Animator와 같게 맞춤)
    private void MoveAndAnimate()
    {
        Vector2 input = _isMeditating ? Vector2.zero : _moveInput;
        _animator.SetFloat(MoveXHash, input.x);
        _animator.SetFloat(MoveYHash, input.y);

        float speed = _isRunning ? _runSpeed : _walkSpeed;
        // 바라보는 방향 기준으로 움직여야 블렌드 트리의 방향(앞, 옆, 뒤)과 실제 이동이 맞는다
        Vector3 direction = transform.right * input.x + transform.forward * input.y;
        transform.position += direction * speed * Time.deltaTime;
    }

    private void HandleMove(InputAction.CallbackContext context) => _moveInput = context.ReadValue<Vector2>();

    private void HandleSprint(InputAction.CallbackContext context)
    {
        _isRunning = context.performed;
        _animator.SetBool(IsRunningHash, _isRunning);
    }

    private void HandleAim(InputAction.CallbackContext context) => _animator.SetBool(IsAimingHash, context.performed);

    private void HandleMeditate(InputAction.CallbackContext context)
    {
        _isMeditating = context.performed;
        _animator.SetBool(IsMeditatingHash, _isMeditating);
    }

    private void HandleCastMagic(InputAction.CallbackContext context) => _animator.SetTrigger(CastHash);

    private void HandleHeal(InputAction.CallbackContext context) => _animator.SetTrigger(HealHash);

    private void HandleDetect(InputAction.CallbackContext context) => _animator.SetTrigger(DetectHash);
}
