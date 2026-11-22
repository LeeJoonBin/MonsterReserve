using System;
using UnityEngine;

// 플레이어 레벨과 경험치. 몬스터를 처치하면 (시체 확인이 아니라 사망 순간) 그 몬스터의 경험치를 더한다.
// 레벨업 보상인 스킬은 스킬 단계에서 OnLevelUp을 구독해 만든다.
public class PlayerLevel : MonoBehaviour
{
    private const int StartLevel = 1;
    [SerializeField] private int[] _requiredExpTable = { 100, 200, 300, 400 };

    public int Level { get; private set; } = StartLevel;
    public int CurrentExp { get; private set; }
    public int MaxLevel => _requiredExpTable.Length + StartLevel;
    public bool IsMaxLevel => Level >= MaxLevel;
    public int RequiredExp => IsMaxLevel ? 0 : _requiredExpTable[Level - StartLevel];

    // HUD가 구독한다 (현재 경험치, 필요 경험치)
    public event Action<int, int> OnExpChanged;
    public event Action<int> OnLevelUp;

    private void OnEnable() => EventBus<MonsterKilledEvent>.Subscribe(HandleMonsterKilled);
    private void OnDisable() => EventBus<MonsterKilledEvent>.Unsubscribe(HandleMonsterKilled);

    private void HandleMonsterKilled(MonsterKilledEvent killedEvent) => AddExp(killedEvent.Experience);
    private void AddExp(int amount)
    {
        if (amount <= 0 || IsMaxLevel) return;

        CurrentExp += amount;

        while (!IsMaxLevel && CurrentExp >= RequiredExp)
        {
            CurrentExp -= RequiredExp;
            Level++;
            OnLevelUp?.Invoke(Level);
        }
        if (IsMaxLevel) CurrentExp = 0;
        OnExpChanged?.Invoke(CurrentExp, RequiredExp);
    }
}