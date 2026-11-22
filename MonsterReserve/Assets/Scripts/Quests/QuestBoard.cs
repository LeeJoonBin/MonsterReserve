using System;
using System.Collections.Generic;
using UnityEngine;

public class QuestBoard : MonoBehaviour
{
    private const int SlotCount = 5;
    private const float DegreesPerDirection = 45f;
    private const int DirectionCount = 8;

    [SerializeField] private QuestData[] _candidates;
    [SerializeField] private MonsterSpawner[] _habitats;
    [SerializeField] private float _refillDelay = 600f;

    private readonly QuestInstance[] _slots = new QuestInstance[SlotCount];
    private readonly float[] _remainingTimes = new float[SlotCount];
    private readonly List<MonsterSpawner> _matchingHabitats = new();

    public event Action<int> OnSlotChanged;

    private void Update() => TickSlots();

    public QuestInstance GetQuest(int slot) => _slots[slot];

    public bool TryAccept(int slot)
    {
        if (_slots[slot] == null) return false;
        if (!QuestManager.Instance.TryAccept(_slots[slot])) return false;

        _slots[slot] = null;
        _remainingTimes[slot] = _refillDelay;
        OnSlotChanged?.Invoke(slot);
        return true;
    }

    private void TickSlots()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_slots[i] != null) continue;

            _remainingTimes[i] -= Time.deltaTime;
            if (_remainingTimes[i] > 0f) continue;

            Fill(i);
        }
    }

    private void Fill(int slot)
    {
        QuestData data = _candidates[UnityEngine.Random.Range(0, _candidates.Length)];
        CompassDirection direction = GetDirection(PickHabitat(data.TargetType).transform.position);

        _slots[slot] = new QuestInstance(data, direction);
        Debug.Log($"게시판 {slot}번 : {data.QuestName} ({direction})");
        OnSlotChanged?.Invoke(slot);
    }

    private MonsterSpawner PickHabitat(MonsterType type)
    {
        _matchingHabitats.Clear();
        foreach (MonsterSpawner habitat in _habitats)
        {
            if (habitat.Type == type) _matchingHabitats.Add(habitat);
        }
        return _matchingHabitats[UnityEngine.Random.Range(0, _matchingHabitats.Count)];
    }

    private CompassDirection GetDirection(Vector3 habitatPosition)
    {
        float angle = Mathf.Atan2(habitatPosition.x, habitatPosition.z) * Mathf.Rad2Deg;
        int index = Mathf.RoundToInt(angle / DegreesPerDirection);
        return (CompassDirection)((index + DirectionCount) % DirectionCount);
    }
}
