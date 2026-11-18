using System;
using UnityEngine;


public class PlayerHealth : MonoBehaviour, IDamageable
{
    // 변경되는 값 (플레이하며 조정한다)
    [SerializeField] private float _maxHealth = 100f;

    private bool _isDead;

    // UI가 처음 화면을 그릴 때 읽어 간다 (그 뒤로는 OnHealthChanged로 받는다)
    public float CurrentHealth { get; private set; }
    public float MaxHealth => _maxHealth;

    // UI가 구독한다 (현재 체력, 최대 체력)
    public event Action<float, float> OnHealthChanged;
    public event Action OnDied;

    // 현재 체력은 저장하지 않으므로 항상 가득 찬 상태로 시작한다 (TechSpec "저장")
    private void Awake() => ResetHealth();

    public void TakeDamage(DamageInfo damageInfo)
    {
        // 사망 뒤에 들어온 피해를 받으면 OnDied가 또 불릴 수 있으므로 막는다
        if (_isDead) return;

        CurrentHealth = Mathf.Max(CurrentHealth - damageInfo.Damage, 0f);
        OnHealthChanged?.Invoke(CurrentHealth, _maxHealth);

        if (CurrentHealth <= 0f) Die();
    }
    
    private void ResetHealth()
    {
        CurrentHealth = _maxHealth;
        _isDead = false;
    }

    // 생각해 볼 것 : OnDied를 먼저 알리는 것과 상태를 먼저 바꾸는 것은 구독하는 쪽에서 볼 때 무엇이 다를까?
    private void Die()
    {
        _isDead = true;
        OnDied?.Invoke();
        GameManager.Instance.ChangeState(GameState.Dead);
    }
}
