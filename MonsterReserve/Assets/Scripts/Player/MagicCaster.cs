using System;
using UnityEngine;

// 마우스 왼쪽 클릭으로 마법을 발사한다. 마나 소모를 요청하고, 성공하면 투사체를 풀에서 꺼낸다.
public class MagicCaster : MonoBehaviour
{
    [SerializeField] private Transform _muzzlePoint;
    [SerializeField] private Transform _spawnPoint;
    [SerializeField] private MagicProjectile[] _magics;
    [SerializeField] private PlayerInputHandler _playerInputHandler;
    [SerializeField] private PlayerMana _playerMana;
    [SerializeField] private PlayerLook _playerLook;

    private bool[] _owned;

    public int SelectedIndex { get; private set; }
    public MagicProjectile SelectedMagic => _magics[SelectedIndex];

    private void Awake()
    {
        _owned = new bool[_magics.Length];
        _owned[0] = true;
    }

    private void Update()
    {
        CheckSelectInput();

        if (_playerInputHandler.ConsumeCastInput())
        {
            if (_playerLook.IsLocked) return;
            if (_playerMana.UseMana(SelectedMagic.ManaCost))
            {
                TakeObject();
            }
        }
    }

    public MagicProjectile GetMagic(int index) => _magics[index];

    public bool IsOwned(int index) => _owned[index];

    public void Unlock(int index) => _owned[index] = true;

    private void CheckSelectInput()
    {
        int index = _playerInputHandler.ConsumeSelectMagicInput();
        if (index < 0 || index >= _magics.Length || !_owned[index] || index == SelectedIndex) return;

        SelectedIndex = index;
        EventBus<MagicSelectedEvent>.Raise(new MagicSelectedEvent(index, SelectedMagic.MagicName));
    }

    private void TakeObject()
    {
        GameObject bolt =  PoolManager.Instance.Get(SelectedMagic.gameObject, _spawnPoint.position, _spawnPoint.rotation);
        MagicProjectile projectile = bolt.GetComponent<MagicProjectile>();
        projectile.SetAttacker(transform);
        projectile.SetVisualOffset(_muzzlePoint.position - _spawnPoint.position);
    }
}
