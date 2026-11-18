using UnityEngine;
using System;
public class SkeletonWarrior : MonoBehaviour, IDamageable
{
    [SerializeField] private float _maxHealth =1000f;

    private float _currentHealth;
    private bool _isDead = false;
    
    public bool IsDead => _isDead;
    public float HealthRatio => _currentHealth/_maxHealth;
    
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
        
        _currentHealth = Mathf.Max(_currentHealth - damageInfo.Damage, 0f);
        if (_currentHealth <= 0)
        {
            _isDead = true;
            OnDeath?.Invoke();
            return;
        }
        OnHit?.Invoke(damageInfo);// 맞았다고 알리기
    }
}
