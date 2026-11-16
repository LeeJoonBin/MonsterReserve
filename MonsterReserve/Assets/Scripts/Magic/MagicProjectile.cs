using System;
using System.Collections;
using UnityEngine;

public class MagicProjectile : MonoBehaviour
{
    // [시험용] 레이 검사 선을 Scene 뷰에 남겨 둘 시간 (초). 확인이 끝나면 지운다
    private const float DebugLineDuration = 3f;

    [SerializeField] private LayerMask LayerMask;
    [SerializeField] private float _bulletSpeed = 450f;
    // 좌클릭할 때 풀에서 당겨올거니까 여기에서 설정할 필요없고 정보 전달하는 것만 할까? 그럼 public 으로 선언할 필요없잖아.
    
    private float _t;
    private Vector3 _newPosition =  Vector3.zero;
    private Vector3 _newVelocity = Vector3.zero;
    private Vector3 _currentPosition;
    private Vector3 _currentVelocity;
    
    private void OnEnable()
    {
        _t = Time.fixedDeltaTime;
        _currentPosition = transform.position;
        // _position = transform.position; // 시작할 때 현재위치로 설정
        _currentVelocity = _bulletSpeed * transform.forward;
        
    }
     
    private void FixedUpdate()
    {
        MoveBullet();
    }

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
        DestroyBullet();
    }

    private void CheckHit()
    {
        Vector3 fireDirection = (_newPosition - _currentPosition).normalized;
        float fireDistance = Vector3.Distance(_newPosition, _currentPosition);
        
        int layerMask = LayerMask.value;
        RaycastHit hit;

        // [시험용] 이번 프레임에 검사한 구간을 Scene 뷰에 초록 선으로 그린다. 확인이 끝나면 지운다
        Debug.DrawLine(_currentPosition, _newPosition, Color.green, DebugLineDuration);

        if (Physics.Raycast(_currentPosition, fireDirection, out hit, fireDistance, layerMask))
        {
            // [시험용] 맞은 구간은 출발점부터 맞은 점까지 빨간 선으로 덮어 그린다
            Debug.DrawLine(_currentPosition, hit.point, Color.red, DebugLineDuration);

            //IDamageable.TakeDamage(_bulletSpeed);
            Destroy(this);
            //PoolManager.Instance.GetOrAddComponent<>();
        }
        
    }
    private IEnumerator DestroyBullet()
    {
        Destroy(gameObject);

        yield return new WaitForSeconds(10f);
    }
}
