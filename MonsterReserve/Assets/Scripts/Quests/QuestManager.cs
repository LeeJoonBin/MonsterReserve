using System.Collections.Generic;
using UnityEngine;

public class QuestManager : MonoBehaviour
{
    public const int MaxAcceptedCount = 3;

    private readonly List<QuestInstance> _acceptedQuests = new();

    public static QuestManager Instance { get; private set; }
    public int AcceptedCount => _acceptedQuests.Count;
    public IReadOnlyList<QuestInstance> AcceptedQuests => _acceptedQuests;

    private void Awake() => RegisterInstance();
    private void OnEnable() => EventBus<CorpseCheckedEvent>.Subscribe(HandleCorpseChecked);
    private void OnDisable() => EventBus<CorpseCheckedEvent>.Unsubscribe(HandleCorpseChecked);
    private void OnDestroy() => UnregisterInstance();

    public bool TryAccept(QuestInstance quest)
    {
        if (_acceptedQuests.Count >= MaxAcceptedCount) return false;

        quest.Accept();
        _acceptedQuests.Add(quest);
        EventBus<QuestAcceptedEvent>.Raise(new QuestAcceptedEvent(quest));
        return true;
    }

    private void HandleCorpseChecked(CorpseCheckedEvent checkedEvent)
    {
        QuestInstance quest = _acceptedQuests.Find(accepted => accepted.Data.TargetType == checkedEvent.Type);
        if (quest == null) return;

        quest.Complete();
        _acceptedQuests.Remove(quest);
        EventBus<QuestCompletedEvent>.Raise(new QuestCompletedEvent(quest.Data.RequiredMoney));
    }

    private void RegisterInstance()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void UnregisterInstance()
    {
        if (Instance != this) return;

        Instance = null;
    }
}
