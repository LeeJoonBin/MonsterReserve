using UnityEngine;

// WASD 이동, 왼쪽 Shift 달리기, Space 점프. Rigidbody로 움직인다.
public class PlayerMover : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private PlayerInputHandler _inputHandler;

    // 수치는 모두 임시값 (GameDesign.md에 확정된 값 없음. 걷기는 "초속 3~5m 가정"만 있음)
    [Header("속도")]
    [SerializeField] private float _walkSpeed = 4f;
    [SerializeField] private float _sprintSpeed = 7f;

    [Header("점프")]
    [SerializeField] private float _jumpForce = 5f;

    [Header("땅, 경사")]
    [SerializeField] private float _groundCheckDistance = 0.2f;
    [SerializeField] private LayerMask _groundLayer;
    [SerializeField] private float _maxSlopeAngle = 45f;

    private Rigidbody _rigidbody;
    private CapsuleCollider _capsule;
    private RaycastHit _groundHit;

    // 점프 직후 올라가는 동안 땅 판정을 끄기 위한 표시.
    // 이게 없으면 다음 FixedUpdate에서 발밑 광선이 아직 땅에 닿아 있어 "땅 위"로 판단하고, Move가 위쪽 속도를 지워 버린다
    private bool _isRising;

    // PlayerAnimController가 읽어 가는 값
    public bool IsGrounded { get; private set; }
    public float CurrentSpeed { get; private set; }

    private void Awake() => CacheComponents();
    private void FixedUpdate() => UpdateMovement();

    private void CacheComponents()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _capsule = GetComponent<CapsuleCollider>();
    }

    // 순서 : 땅 확인 → 이동 → 점프.
    // 점프를 이동보다 뒤에 두는 이유 : Move가 속도를 새로 정하므로, 점프 힘을 먼저 주면 그 힘이 덮어써진다
    private void UpdateMovement()
    {
        CheckGround();
        Move();
        Jump();
    }

    // 캡슐 가운데에서 아래로 광선을 쏜다. 캡슐 절반 높이 + 여유 거리 안에 바닥이 있으면 땅 위다.
    // 다른 방법 : Physics.SphereCast는 발 넓이만큼 검사해서 돌 모서리 같은 곳에서 덜 놓치지만, 경사 법선이 덜 정확할 수 있다
    private void CheckGround()
    {
        if (_isRising && _rigidbody.linearVelocity.y > 0f)
        {
            IsGrounded = false;
            return;
        }
        _isRising = false;

        Bounds bounds = _capsule.bounds;
        float distance = bounds.extents.y + _groundCheckDistance;
        IsGrounded = Physics.Raycast(bounds.center, Vector3.down, out _groundHit, distance, _groundLayer, QueryTriggerInteraction.Ignore);
    }

    private void Move()
    {
        Vector2 input = _inputHandler.MoveInput;
        float speed = _inputHandler.IsSprinting ? _sprintSpeed : _walkSpeed;
        // 입력은 평면 (x, y), 이동은 3D (x, z). 몸이 바라보는 방향 기준으로 바꾼다
        Vector3 move = (transform.right * input.x + transform.forward * input.y) * speed;
        CurrentSpeed = move.magnitude;

        if (!IsGrounded)
        {
            ApplyHorizontalVelocity(move);
            return;
        }

        float slopeAngle = Vector3.Angle(_groundHit.normal, Vector3.up);
        // 바닥 법선과 이동 방향이 반대쪽이면 오르막으로 가는 중이다
        bool isMovingUphill = Vector3.Dot(move, _groundHit.normal) < 0f;

        if (slopeAngle > _maxSlopeAngle && isMovingUphill)
        {
            // 가파른 면을 파고드는 수평 성분만 지운다. 옆으로 걷기와 미끄러져 내려오기(중력)는 남긴다
            Vector3 intoSlope = new Vector3(-_groundHit.normal.x, 0f, -_groundHit.normal.z).normalized;
            move -= intoSlope * Vector3.Dot(move, intoSlope);
            ApplyHorizontalVelocity(move);
            return;
        }

        // 이동 방향을 바닥 면 위로 눕혀서 오르막과 내리막을 같은 속력으로 따라간다 (내리막에서 공중에 뜨지 않게)
        _rigidbody.linearVelocity = Vector3.ProjectOnPlane(move, _groundHit.normal).normalized * move.magnitude;
    }

    // 속도를 통째로 바꾸면 떨어지던 y 속도(중력)가 사라지므로, 수평만 바꾸고 y는 물리에 맡긴다
    private void ApplyHorizontalVelocity(Vector3 move)
    {
        move.y = _rigidbody.linearVelocity.y;
        _rigidbody.linearVelocity = move;
    }

    // ForceMode.Impulse : 질량을 고려해 한 번에 속도를 바꾼다 (질량 1이면 위로 _jumpForce m/s)
    private void Jump()
    {
        if (!_inputHandler.IsJumpPressed) return;

        // 공중에서 누른 입력도 지운다. 남겨 두면 착지하는 순간 저절로 뛴다
        _inputHandler.ConsumeJump();
        if (!IsGrounded) return;

        _rigidbody.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
        _isRising = true;
    }
}
