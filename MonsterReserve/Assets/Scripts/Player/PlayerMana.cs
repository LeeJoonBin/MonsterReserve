using System;
using UnityEngine;

public class PlayerMana : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private PlayerInputHandler _inputHandler;

    [Header("마나")]
    [SerializeField] private float _maxMana = 100f;
    [SerializeField] private float _meditationRegenPerSecond = 10f;

    public float CurrentMana { get; private set; }
    public float MaxMana => _maxMana;
    public bool IsMeditating => _inputHandler.IsMeditateHeld;

    // HUD가 구독한다 (현재 마나, 최대 마나)
    public event Action<float, float> OnManaChanged;

    private void Awake() => ResetMana();
    private void Update() => Meditate();

    private void ResetMana()
    {
        CurrentMana = _maxMana;
    }

    public bool UseMana(float amount)
    {
        if (CurrentMana < amount) return false;
        
        CurrentMana -= amount;
        OnManaChanged?.Invoke(CurrentMana, _maxMana);
        return true;
    }
    
    private void Meditate()
    {
        if (!IsMeditating) return;
        if (CurrentMana >= _maxMana) return;

        CurrentMana = Mathf.Min(CurrentMana + _meditationRegenPerSecond * Time.deltaTime, _maxMana);
        OnManaChanged?.Invoke(CurrentMana, _maxMana);
    }
}
