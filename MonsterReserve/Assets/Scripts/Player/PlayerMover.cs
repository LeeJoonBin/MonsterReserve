using UnityEngine;

// WASD 이동, 왼쪽 Shift 달리기. Rigidbody로 움직인다.
public class PlayerMover : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private PlayerInputHandler _inputHandler;
    [SerializeField] private PlayerMana _playerMana;

    // 수치는 모두 임시값 (GameDesign.md에 확정된 값 없음. 걷기는 "초속 3~5m 가정"만 있음)
    [Header("속도")]
    [SerializeField] private float _walkSpeed = 4f;
    [SerializeField] private float _sprintSpeed = 7f;

    [Header("땅, 경사")]
    [SerializeField] private float _groundCheckDistance = 0.2f;
    [SerializeField] private LayerMask _groundLayer;
    [SerializeField] private float _maxSlopeAngle = 45f;

    private Rigidbody _rigidbody;
    private CapsuleCollider _capsule;
    private RaycastHit _groundHit;

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

    // 순서 : 땅 확인 → 이동. Move가 땅 확인 결과(경사 법선)를 쓰므로 땅 확인이 먼저다
    private void UpdateMovement()
    {
        CheckGround();
        Move();
    }

    // 캡슐 가운데에서 아래로 광선을 쏜다. 캡슐 절반 높이 + 여유 거리 안에 바닥이 있으면 땅 위다.
    // 다른 방법 : Physics.SphereCast는 발 넓이만큼 검사해서 돌 모서리 같은 곳에서 덜 놓치지만, 경사 법선이 덜 정확할 수 있다
    private void CheckGround()
    {
        Bounds bounds = _capsule.bounds;
        float distance = bounds.extents.y + _groundCheckDistance;
        IsGrounded = Physics.Raycast(bounds.center, Vector3.down, out _groundHit, distance, _groundLayer, QueryTriggerInteraction.Ignore);
    }

    private void Move()
    {
        // 명상 중에는 입력이 없는 것처럼 다룬다. 서 있을 때와 같은 길을 타므로 중력과 경사 처리는 그대로 남는다
        Vector2 input = _playerMana.IsMeditating ? Vector2.zero : _inputHandler.MoveInput;
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
}
