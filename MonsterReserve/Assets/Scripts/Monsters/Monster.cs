using UnityEngine;
using System;

public abstract class Monster : MonoBehaviour, IDamageable
{
    [SerializeField] private MonsterType _type;
    [SerializeField] private float _maxHealth = 300f;
    [SerializeField] private int _experience;
    [SerializeField, Range(0f, 1f)] private float _fleeHealthRatio = 0.3f;
    [SerializeField] private float _currentHealth;

    private bool _isDead;

    public MonsterType Type => _type;
    public int Experience => _experience;
    public float FleeHealthRatio => _fleeHealthRatio;
    public bool IsDead => _isDead;
    public float HealthRatio => _currentHealth / _maxHealth;

    public event Action<DamageInfo> OnHit;
    public event Action OnDeath;

    private void OnEnable()
    {
        _isDead = false;
        _currentHealth = _maxHealth;
    }

    public void TakeDamage(DamageInfo damageInfo)
    {
        if (_isDead) return;

        _currentHealth = Mathf.Max(_currentHealth - damageInfo.Damage, 0f);
        if (_currentHealth <= 0f)
        {
            _isDead = true;
            OnDeath?.Invoke();
            EventBus<MonsterKilledEvent>.Raise(new MonsterKilledEvent(_type, _experience));
            return;
        }
        OnHit?.Invoke(damageInfo);
    }
}
