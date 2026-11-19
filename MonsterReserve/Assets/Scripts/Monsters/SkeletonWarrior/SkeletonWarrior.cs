using UnityEngine;
using System;
public class SkeletonWarrior : Monster, IDamageable
{
    [SerializeField] private int _experience;
    [SerializeField] private float _maxHealth = 300f;
    [SerializeField] private float _currentHealth;
    private bool _isDead = false;
    
    public int experience => _experience;
    public override bool IsDead => _isDead;
    public float HealthRatio => _currentHealth/_maxHealth;
    
    public override event Action OnDeath;
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
            NotifyDied();
            return;
        }
        OnHit?.Invoke(damageInfo);// 맞았다고 알리기
    }
}
