using UnityEngine;

public class PlayerMover : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private PlayerInputHandler _inputHandler;
    [SerializeField] private PlayerMana _playerMana;
    
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

    public bool IsGrounded { get; private set; }
    public float CurrentSpeed { get; private set; }

    private void Awake() => CacheComponents();
    private void FixedUpdate() => UpdateMovement();

    private void CacheComponents()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _capsule = GetComponent<CapsuleCollider>();
    }
    private void UpdateMovement()
    {
        CheckGround();
        Move();
    }

      private void CheckGround()
    {
        Bounds bounds = _capsule.bounds;
        float distance = bounds.extents.y + _groundCheckDistance;
        IsGrounded = Physics.Raycast(bounds.center, Vector3.down, out _groundHit, distance, _groundLayer, QueryTriggerInteraction.Ignore);
    }

    private void Move()
    {
        Vector2 input = _playerMana.IsMeditating ? Vector2.zero : _inputHandler.MoveInput;
        float speed = _inputHandler.IsSprinting ? _sprintSpeed : _walkSpeed;
        Vector3 move = (transform.right * input.x + transform.forward * input.y) * speed;
        CurrentSpeed = move.magnitude;

        if (!IsGrounded)
        {
            ApplyHorizontalVelocity(move);
            return;
        }

        float slopeAngle = Vector3.Angle(_groundHit.normal, Vector3.up);
        bool isMovingUphill = Vector3.Dot(move, _groundHit.normal) < 0f;

        if (slopeAngle > _maxSlopeAngle && isMovingUphill)
        {
            Vector3 intoSlope = new Vector3(-_groundHit.normal.x, 0f, -_groundHit.normal.z).normalized;
            move -= intoSlope * Vector3.Dot(move, intoSlope);
            ApplyHorizontalVelocity(move);
            return;
        }
        
        _rigidbody.linearVelocity = Vector3.ProjectOnPlane(move, _groundHit.normal).normalized * move.magnitude;
    }
    
    private void ApplyHorizontalVelocity(Vector3 move)
    {
        move.y = _rigidbody.linearVelocity.y;
        _rigidbody.linearVelocity = move;
    }
}
