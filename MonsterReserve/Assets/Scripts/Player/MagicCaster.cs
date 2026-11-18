using System;
using UnityEngine;

// 마우스 왼쪽 클릭으로 마법을 발사한다. 마나 소모를 요청하고, 성공하면 투사체를 풀에서 꺼낸다.
public class MagicCaster : MonoBehaviour
{
    [SerializeField] private Transform _spawnPoint;
    [SerializeField] private MagicProjectile _projectile;
    [SerializeField] private PlayerInputHandler _playerInputHandler;
    [SerializeField] private PlayerMana _playerMana;
    
    private void Update()
    {
        if (_playerInputHandler.ConsumeCastInput())
        {
            if (_playerMana.UseMana(_projectile.ManaCost))
            {
                TakeObject();
            }
        }
    }
    
    
    private void TakeObject()
    {
        GameObject bolt =  PoolManager.Instance.Get(_projectile.gameObject, _spawnPoint.position, _spawnPoint.rotation);
        bolt.GetComponent<MagicProjectile>().SetAttacker(transform);
    }
}
