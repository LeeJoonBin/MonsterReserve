using UnityEngine;
using UnityEngine.UI;

// HUD의 체력, 마나 막대. 값이 바뀌었다는 이벤트를 받을 때만 다시 그린다 (UI.md "최적화 규칙")
public class PlayerStatusHUD : MonoBehaviour
{
    [SerializeField] private PlayerHealth _playerHealth;
    [SerializeField] private PlayerMana _playerMana;
    [SerializeField] private Image _healthFill;
    [SerializeField] private Image _manaFill;

    private void OnEnable() => BindStatusEvents();
    // 현재 값은 PlayerHealth, PlayerMana의 Awake에서 채워지므로 Start에서 처음 그린다
    private void Start() => DrawCurrentStatus();
    private void OnDisable() => UnbindStatusEvents();

    private void BindStatusEvents()
    {
        _playerHealth.OnHealthChanged += HandleHealthChanged;
        _playerMana.OnManaChanged += HandleManaChanged;
    }

    private void UnbindStatusEvents()
    {
        _playerHealth.OnHealthChanged -= HandleHealthChanged;
        _playerMana.OnManaChanged -= HandleManaChanged;
    }

    private void DrawCurrentStatus()
    {
        HandleHealthChanged(_playerHealth.CurrentHealth, _playerHealth.MaxHealth);
        HandleManaChanged(_playerMana.CurrentMana, _playerMana.MaxMana);
    }

    private void HandleHealthChanged(float current, float max)
    {
        _healthFill.fillAmount = current / max;
    }

    private void HandleManaChanged(float current, float max)
    {
        _manaFill.fillAmount = current / max;
    }
}
