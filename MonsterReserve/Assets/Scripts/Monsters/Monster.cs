using UnityEngine;
using System;

public abstract class Monster : MonoBehaviour
{
    public abstract bool IsDead { get; }
    public abstract event Action OnDeath;
}