using System;
using UnityEngine;

public class MonsterDetector : MonoBehaviour
{
    [SerializeField] private LayerMask _targetLayer;
    [SerializeField] private LayerMask _obstacleLayer;
    [SerializeField] private float _viewAngle;
    [SerializeField] private float _eyeHeight;
    [SerializeField] private float _targetHeight = 0.7f;
    
    private SphereCollider _sphereCollider;
    private float detectRange;
    public Transform TargetTransform {get; private set;}
    public bool IsTargetEnter { get; private set; }
    public bool IsTargetInSight { get; private set; }
    public Vector3 LastSeenPosition { get; private set; }
    public bool HasLastSeenPosition {get; private set;}
    
    private void Awake() => CacheComponents();
    private void Update() => Detect();
    private void OnDisable() => ResetData();

    private void OnTriggerEnter(Collider other)
    {
        if (IsInTarget(other.gameObject))
        {
            IsTargetEnter = true;
            TargetTransform = other.transform;
        }
    }
    private void OnTriggerExit(Collider other)
    {
        if (IsInTarget(other.gameObject)) ResetData();
    }
    private void CacheComponents() => _sphereCollider = GetComponent<SphereCollider>();

    private void Detect()
    {
        if (!IsTargetEnter) return;
        IsTargetInSight = IsInSight(TargetTransform);
        if (IsTargetInSight)
        {
            LastSeenPosition = TargetTransform.position;
            HasLastSeenPosition = true;
        }
    }
    private bool IsInTarget(GameObject target)
    {
        return (_targetLayer.value & (1 << target.layer)) != 0;
    }

    private bool IsInSight(Transform target)
    {
        Vector3 eye = transform.position + Vector3.up * _eyeHeight;
        Vector3 targetPoint = target.position + Vector3.up * _targetHeight;
        Vector3 toTarget = targetPoint - eye;
        Vector3 flatDirection = toTarget;
        flatDirection.y = 0;

        float threshold = Mathf.Cos(_viewAngle * 0.5f * Mathf.Deg2Rad);
        if (Vector3.Dot(transform.forward, flatDirection.normalized) < threshold) return false;
        bool isHit = Physics.Raycast(eye, toTarget.normalized, out RaycastHit hit, toTarget.magnitude, _targetLayer | _obstacleLayer, QueryTriggerInteraction.Ignore);
       
        return isHit && IsInTarget(hit.collider.gameObject);
    }

    private void ResetData()
    {
        IsTargetEnter = false;
        IsTargetInSight = false;
        TargetTransform = null;
    }
    private void OnDrawGizmos()
    {
        // 에디터에서는 Awake가 불리지 않아 여기서 직접 가져온다
        SphereCollider sphere = GetComponent<SphereCollider>();
        if (sphere == null) return;

        float range = sphere.radius;
        Vector3 origin = transform.position;

        // 감지 범위 (Trigger)
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin, range);

        // 시야각 양쪽 경계
        Vector3 leftDir = Quaternion.AngleAxis(-_viewAngle * 0.5f, Vector3.up) * transform.forward;
        Vector3 rightDir = Quaternion.AngleAxis(_viewAngle * 0.5f, Vector3.up) * transform.forward;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + leftDir * range);
        Gizmos.DrawLine(origin, origin + rightDir * range);

        // 눈에서 플레이어 몸통까지 실제 광선과 같은 선 (보이면 빨강, 안 보이면 회색)
        if (TargetTransform == null) return;
        Vector3 eye = origin + Vector3.up * _eyeHeight;
        Vector3 targetPoint = TargetTransform.position + Vector3.up * _targetHeight;
        Gizmos.color = IsTargetInSight ? Color.red : Color.gray;
        Gizmos.DrawLine(eye, targetPoint);
    }
}
