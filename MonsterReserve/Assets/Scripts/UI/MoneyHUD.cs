using TMPro;
using UnityEngine;

public class MoneyHUD : MonoBehaviour
{
    [SerializeField] private PlayerWallet _playerWallet;
    [SerializeField] private TMP_Text _moneyText;

    private void OnEnable() => EventBus<MoneyChangedEvent>.Subscribe(HandleMoneyChanged);
    private void Start() => DrawMoney(_playerWallet.Money);
    private void OnDisable() => EventBus<MoneyChangedEvent>.Unsubscribe(HandleMoneyChanged);

    private void HandleMoneyChanged(MoneyChangedEvent changedEvent) => DrawMoney(changedEvent.Money);

    private void DrawMoney(int money)
    {
        _moneyText.text = $"{money} G";
    }
}
