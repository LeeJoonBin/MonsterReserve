using UnityEngine;

public class MagicShop : MonoBehaviour, IInteractable
{
    [SerializeField] private PlayerWallet _playerWallet;
    [SerializeField] private MagicCaster _magicCaster;

    public bool CanInteract => true;

    public void Interact() => EventBus<ShopInteractedEvent>.Raise(new ShopInteractedEvent(this));

    public MagicProjectile GetMagic(int index) => _magicCaster.GetMagic(index);

    public bool IsOwned(int index) => _magicCaster.IsOwned(index);

    public bool TryBuy(int index)
    {
        if (IsOwned(index)) return false;
        if (!_playerWallet.TrySpend(GetMagic(index).Price)) return false;

        _magicCaster.Unlock(index);
        return true;
    }
}
