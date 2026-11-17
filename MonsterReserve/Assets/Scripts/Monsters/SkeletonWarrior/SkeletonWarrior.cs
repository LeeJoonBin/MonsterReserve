using UnityEngine;
using System;
public class SkeletonWarrior : MonoBehaviour, IDamageable
{
    [SerializeField] private float _maxHealth =1000f;
    [SerializeField] private float _damage;

    private float _currentHealth;
    private bool _isDead = false;
    
    public bool IsDead => _isDead;
    
    public event Action OnDeath;
    public event Action<DamageInfo> OnHit;
    
    private void OnEnable()
    {
        _isDead = false;
        _currentHealth = _maxHealth;    
    }
    
    public void TakeDamage(DamageInfo  damageInfo)
    {
        if(_isDead) return;
        _currentHealth -= damageInfo.Damage;
        OnHit?.Invoke(damageInfo);// 맞았다고 알리기
        _currentHealth = Mathf.Max(_currentHealth - damageInfo.Damage, 0f);
        if (_currentHealth <= 0)
        {
            _isDead = true;
            OnDeath?.Invoke();
        }
    }
}
