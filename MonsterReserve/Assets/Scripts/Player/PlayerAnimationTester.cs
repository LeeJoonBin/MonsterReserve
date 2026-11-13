using UnityEngine;
using UnityEngine.InputSystem;

// 테스트 전용 : PlayerAniController의 상태와 전환을 키 입력으로 확인하기 위한 임시 스크립트 (이슈 #18).
// 실제 플레이어 컨트롤러를 작성하면 이 파일은 삭제한다.
// 이동 방식 두 가지를 비교하는 실험용 (_useRigidbody로 고른다).
//   Transform : 발 높이를 지형(Terrain) 높이에 맞춘다. 바위, 구조물, 가파른 경사를 막지 않는다.
//   Rigidbody : 물리로 움직여 Collider끼리 막힌다. 이동을 바닥 면에 맞추고, _maxSlopeAngle보다 가파른 면은 오르지 못한다.
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

    // 발밑 판정 여유 (m). 캡슐 아래 끝에서 이만큼 더 내려다봐야 울퉁불퉁한 지형에서도 바닥을 놓치지 않는다
    private const float GroundCheckMargin = 0.2f;

    [Header("참조")]
    [SerializeField] private Animator _animator;

    [Header("이동 방식 (실험용)")]
    [SerializeField] private bool _useRigidbody;
    [SerializeField] private float _maxSlopeAngle = 45f; // 사용자 지정 실험값 (기획서 미정)

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
    private Terrain _terrain;
    private Rigidbody _rigidbody;
    private CapsuleCollider _capsule;

    private void Awake() => CacheComponents();

    private void OnEnable() => BindInputEvents();

    private void Update() => AnimateAndMoveByTransform();

    private void FixedUpdate() => MoveByRigidbody();

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

    private void CacheComponents()
    {
        _terrain = Terrain.activeTerrain;
        _rigidbody = GetComponent<Rigidbody>();
        _capsule = GetComponent<CapsuleCollider>();
        // Transform 방식일 때 Rigidbody가 중력으로 끌어내리지 않게, 물리 계산에서 뺀다
        if (_rigidbody != null) _rigidbody.isKinematic = !_useRigidbody;
    }

    // 명상 중에는 멈춘다 (기획서의 "명상 중 이동 가능 여부"가 미정이라 Animator와 같게 맞춤)
    private Vector2 GetMoveInput() => _isMeditating ? Vector2.zero : _moveInput;

    // 바라보는 방향 기준으로 움직여야 블렌드 트리의 방향(앞, 옆, 뒤)과 실제 이동이 맞는다
    private Vector3 GetMoveVelocity()
    {
        Vector2 input = GetMoveInput();
        float speed = _isRunning ? _runSpeed : _walkSpeed;
        return (transform.right * input.x + transform.forward * input.y) * speed;
    }

    private void AnimateAndMoveByTransform()
    {
        Vector2 input = GetMoveInput();
        _animator.SetFloat(MoveXHash, input.x);
        _animator.SetFloat(MoveYHash, input.y);

        if (_useRigidbody) return;

        Vector3 position = transform.position + GetMoveVelocity() * Time.deltaTime;
        // 물리 없이 위치를 직접 옮기므로 Collider가 막아 주지 않는다. 그래서 매 프레임 지형 높이로 발을 붙인다
        position.y = _terrain.SampleHeight(position) + _terrain.transform.position.y;
        transform.position = position;
    }

    private void MoveByRigidbody()
    {
        if (!_useRigidbody) return;

        Vector3 move = GetMoveVelocity();

        // 공중이면 수평 속도만 정하고, 위아래 속도는 물리(중력)에 맡긴다
        if (!TryGetGround(out RaycastHit ground))
        {
            move.y = _rigidbody.linearVelocity.y;
            _rigidbody.linearVelocity = move;
            return;
        }

        float slopeAngle = Vector3.Angle(ground.normal, Vector3.up);
        // 바닥 법선과 이동 방향이 반대쪽이면 경사를 향해(오르막으로) 가는 중이다
        bool isMovingUphill = Vector3.Dot(move, ground.normal) < 0f;

        if (slopeAngle > _maxSlopeAngle && isMovingUphill)
        {
            // A : 가파른 면을 파고드는 수평 성분만 지운다. 옆으로 걷기와 미끄러져 내려오기(중력)는 남긴다
            Vector3 intoSlope = new Vector3(-ground.normal.x, 0f, -ground.normal.z).normalized;
            move -= intoSlope * Vector3.Dot(move, intoSlope);
            move.y = _rigidbody.linearVelocity.y;
            _rigidbody.linearVelocity = move;
            return;
        }

        // B : 이동 방향을 바닥 면 위로 눕혀서, 오르막과 내리막을 같은 속력으로 따라간다
        Vector3 alongGround = Vector3.ProjectOnPlane(move, ground.normal).normalized * move.magnitude;
        _rigidbody.linearVelocity = alongGround;
    }

    // 캡슐 가운데에서 아래로 광선을 쏴, 발밑 바로 아래에 바닥이 있는지 본다
    private bool TryGetGround(out RaycastHit hit)
    {
        Bounds bounds = _capsule.bounds;
        float distance = bounds.extents.y + GroundCheckMargin;
        return Physics.Raycast(bounds.center, Vector3.down, out hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
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
