using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
public class ShopWindow : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private PlayerController _playerController;
    [SerializeField] private PlayerWallet _playerWallet;

    [Header("Text")]
    [SerializeField] private TMP_Text _moneyText;
    [SerializeField] private TMP_Text _magicNameText;
    [SerializeField] private TMP_Text _priceText;

    [Header("Button")]
    [SerializeField] private Button _buyButton;
    [SerializeField] private TMP_Text _buyButtonLabel;
    [SerializeField] private Button _closeButton;

    [Header("Magic")]
    [SerializeField] private int _magicIndex = 1;

    private Canvas _canvas;
    private MagicShop _shop;

    private void Awake() => InitializeWindow();
    private void OnEnable() => BindEvents();
    private void OnDisable() => UnbindEvents();

    private void InitializeWindow()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.enabled = false;
    }

    private void BindEvents()
    {
        EventBus<ShopInteractedEvent>.Subscribe(HandleShopInteracted);
        EventBus<MoneyChangedEvent>.Subscribe(HandleMoneyChanged);
        _buyButton.onClick.AddListener(HandleBuyClicked);
        _closeButton.onClick.AddListener(HandleCloseClicked);
    }

    private void UnbindEvents()
    {
        EventBus<ShopInteractedEvent>.Unsubscribe(HandleShopInteracted);
        EventBus<MoneyChangedEvent>.Unsubscribe(HandleMoneyChanged);
        _buyButton.onClick.RemoveListener(HandleBuyClicked);
        _closeButton.onClick.RemoveListener(HandleCloseClicked);
    }

    private void HandleShopInteracted(ShopInteractedEvent interactedEvent)
    {
        _shop = interactedEvent.Shop;
        Open();
    }

    private void HandleMoneyChanged(MoneyChangedEvent changedEvent)
    {
        if (!_canvas.enabled) return;
        Refresh();
    }

    private void HandleBuyClicked()
    {
        _shop.TryBuy(_magicIndex);
        Refresh();
    }

    private void HandleCloseClicked() => Close();

    private void Open()
    {
        Refresh();
        _canvas.enabled = true;
        _playerController.PlayerLook.RequestLock(this);
    }

    private void Close()
    {
        _canvas.enabled = false;
        _playerController.PlayerLook.ReleaseLock(this);
    }

    private void Refresh()
    {
        MagicProjectile magic = _shop.GetMagic(_magicIndex);
        int money = _playerWallet.Money;

        _moneyText.text = $"{money} G";
        _magicNameText.text = magic.MagicName;
        _priceText.text = $"{magic.Price} G";

        if (_shop.IsOwned(_magicIndex))
        {
            _buyButtonLabel.text = "보유";
            _buyButton.interactable = false;
            return;
        }

        _buyButtonLabel.text = "구매";
        _buyButton.interactable = money >= magic.Price;
    }
}
