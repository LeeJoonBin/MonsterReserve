using UnityEngine;

public class QuestNote : MonoBehaviour, IInteractable
{
    [SerializeField] private QuestBoard _board;
    [SerializeField] private int _slot;
    [SerializeField] private GameObject _visual;

    private void OnEnable() => _board.OnSlotChanged += HandleSlotChanged;
    private void OnDisable() => _board.OnSlotChanged -= HandleSlotChanged;

    public void Interact() => EventBus<QuestNoteInteractedEvent>.Raise(new QuestNoteInteractedEvent(_board, _slot));

    private void HandleSlotChanged(int slot)
    {
        if (slot != _slot) return;

        _visual.SetActive(_board.GetQuest(_slot) != null);
    }
}
