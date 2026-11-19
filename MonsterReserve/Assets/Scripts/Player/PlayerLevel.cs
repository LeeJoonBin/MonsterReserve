using System;
using UnityEngine;

public class PlayerLevel : MonoBehaviour
{
    [SerializeField] private int[] _requiredExperience;

    private void OnEnable()
    {
       // Monster.OnMonsterDied += 
    }
    
    private Action OnExperienceChanged;
}
