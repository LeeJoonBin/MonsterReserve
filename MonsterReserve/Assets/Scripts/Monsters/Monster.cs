using UnityEngine;
using System;

public abstract class Monster : MonoBehaviour
{
    public abstract bool IsDead { get; }
    public abstract event Action OnDeath;
    public static event Action<Monster> OnMonsterDied;
    protected void NotifyDied() => OnMonsterDied?.Invoke(this); 
    
}