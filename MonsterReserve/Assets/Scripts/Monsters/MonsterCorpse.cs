using UnityEngine;
using System;
using System.Collections;

public class MonsterCorpse : MonoBehaviour, IInteractable
{
    [SerializeField] private  Monster _monster;
    [SerializeField] private float _releaseDelay = 3f;

    private bool _isChecked;
    
    private void OnEnable()
    {
        _isChecked = false;
    }

    public void Interact()
    {
        if(!_monster.IsDead || _isChecked) return;
        _isChecked = true;
        EventBus<CorpseCheckedEvent>.Raise(new CorpseCheckedEvent(_monster.Type));
        StartCoroutine(ReleaseAfterDelay());
    }

    private IEnumerator ReleaseAfterDelay()
    {
        yield return new WaitForSeconds(_releaseDelay);
        PoolManager.Instance.Release(_monster.gameObject);
    }

    
}
