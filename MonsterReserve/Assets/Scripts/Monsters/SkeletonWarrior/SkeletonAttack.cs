using UnityEngine;

public class SkeletonAttack : MonoBehaviour
{
    [SerializeField] private MonsterDetector _monsterDetector;
    [SerializeField] private float _damage =10f;
    [SerializeField] private float _attackRange = 1.5f;
 
    public void AttackEvent()
    {
        Transform target = _monsterDetector.TargetTransform;
        if (target == null) return;
        float distance = Vector3.Distance(transform.position, target.position);
        if (distance > _attackRange) return;
        if (target.TryGetComponent(out IDamageable damageable))
        {
            damageable.TakeDamage(new DamageInfo(_damage, target.position, transform.position,transform.parent));
        }
    }
}
