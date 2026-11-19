using UnityEngine;
using System;
using System.Collections;

public class MonsterCorpse : MonoBehaviour, IInteractable
{
    [SerializeField] private  Monster _monster;
    [SerializeField] private float _releaseDelay = 3f;

    private bool _isChecked;

    public static event Action<MonsterCorpse> OnAnyCorpseChecked;
    
    private void OnEnable()
    {
        _isChecked = false;
    }

    public void Interact()
    {
        if(!_monster.IsDead || _isChecked) return;
        _isChecked = true;
        Debug.Log("시체확인됨");
        OnAnyCorpseChecked?.Invoke(this);
        StartCoroutine(ReleaseAfterDelay());
    }

    private IEnumerator ReleaseAfterDelay()
    {
        yield return new WaitForSeconds(_releaseDelay);
        PoolManager.Instance.Release(_monster.gameObject);
    }

    
}
