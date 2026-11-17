using System;
using System.Collections;
using UnityEngine;

public class MagicProjectile : MonoBehaviour
{
    // 시험용
    private const float DebugLineDuration = 3f;

    [SerializeField] private LayerMask _layerMask;
    [SerializeField] private float _bulletSpeed = 450f;
    [SerializeField] private float _manaCost = 10f;
    [SerializeField] private float _damage = 30f;
    [SerializeField] private float _minDamage = 10f;
    [SerializeField] private float _startDistance = 300f;
    [SerializeField] private float _endDistance = 2000f;
    
    private float _t;
    private Vector3 _newPosition =  Vector3.zero;
    private Vector3 _newVelocity = Vector3.zero;
    private Vector3 _currentPosition;
    private Vector3 _currentVelocity;
    private Vector3 _startPosition;
    private float _distance;
    private TrailRenderer _trailRenderer;
    
    public float ManaCost => _manaCost;
    
    private void Awake() => _trailRenderer = GetComponent<TrailRenderer>();
    private void OnEnable()
    {
        _trailRenderer.Clear();
        _t = Time.fixedDeltaTime;
        _currentPosition = transform.position;
        _currentVelocity = _bulletSpeed * transform.forward;
        _startPosition = transform.position;
        _distance = 0f;
        StartCoroutine(ReleaseBullet());
    }
     
    private void FixedUpdate()
    {
        MoveBullet();
    }

   //물리엔진
    public static void BackwardEuler(
        float _t,
        Vector3 currentPosition,
        Vector3 currentVelocity,
        out Vector3 newPosition,
        out Vector3 newVelocity)
    {
        Vector3 gravity = Physics.gravity;
        // 속도 계산    
        newVelocity = currentVelocity + _t * gravity;
        // 현재 포지션 계산
        newPosition = currentPosition + _t * newVelocity;
    }

    private void MoveBullet()
    {
        BackwardEuler(_t, _currentPosition, _currentVelocity, out _newPosition, out _newVelocity);
        
        CheckHit();
        
        _currentPosition = _newPosition;
        _currentVelocity = _newVelocity;
        
        transform.position = _currentPosition;
    }
    private void CheckHit()
    {
        Vector3 fireDirection = (_newPosition - _currentPosition).normalized;
        float fireDistance = Vector3.Distance(_newPosition, _currentPosition);
        int layerMask = _layerMask.value;
        RaycastHit hit;
        
        // Debug.DrawLine(_currentPosition, _newPosition, Color.green, DebugLineDuration);
        if (Physics.Raycast(_currentPosition, fireDirection, out hit, fireDistance, layerMask))
        {
            _distance += hit.distance;
            if(hit.collider.TryGetComponent<IDamageable>(out IDamageable damageable))
            {
                damageable.TakeDamage(new DamageInfo(CalculateDamage(_distance), hit.point, _startPosition));
            }
            // Debug.DrawLine(_currentPosition, hit.point, Color.red, DebugLineDuration);
            // 시험용 : 날아간 거리와 계산된 데미지 확인 (_damage는 기본값이라 항상 같은 값이 찍힌다)
            Debug.Log($"[MagicProjectile] 거리 {_distance:F1}m / 데미지 {CalculateDamage(_distance):F1} (맞은 대상 {hit.collider.name})");
            PoolManager.Instance.Release(gameObject);
        }
        else
        {
            // 안 맞은 단계는 이번 단계에 움직인 거리를 통째로 더한다
            _distance += fireDistance;
        }
    }
    // 거리별 데미지 계산식
    private float CalculateDamage(float distance)
    {
        float distanceRatio = Mathf.InverseLerp(_startDistance, _endDistance, distance);
        return Mathf.Lerp(_damage, _minDamage, distanceRatio);
    }
    
    private IEnumerator ReleaseBullet()
    {
        yield return new WaitForSeconds(5f);
        PoolManager.Instance.Release(gameObject);
    }
    // 나중에 비동기로 이팩트 끝날 때까지 기다렸다가 총알 나가게 하기로 수정
}
