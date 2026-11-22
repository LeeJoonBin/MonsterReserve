using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Canvas))]
public class QuestInfoPopup : MonoBehaviour
{
    [SerializeField] private InputActionReference _toggleAction;
    [SerializeField] private PlayerController _playerController;
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text[] _slotTexts;

    private Canvas _canvas;

    private void Awake() => InitializePopup();
    private void OnEnable() => BindEvents();
    private void OnDisable() => UnbindEvents();

    private void InitializePopup()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.enabled = false;
    }

    private void BindEvents()
    {
        _toggleAction.action.performed += HandleToggle;
        _toggleAction.action.Enable();
        GameManager.Instance.OnStateChanged += HandleStateChanged;
        EventBus<QuestAcceptedEvent>.Subscribe(HandleQuestAccepted);
        EventBus<QuestCompletedEvent>.Subscribe(HandleQuestCompleted);
    }

    private void UnbindEvents()
    {
        _toggleAction.action.performed -= HandleToggle;
        _toggleAction.action.Disable();
        EventBus<QuestAcceptedEvent>.Unsubscribe(HandleQuestAccepted);
        EventBus<QuestCompletedEvent>.Unsubscribe(HandleQuestCompleted);

        if (GameManager.Instance == null) return;

        GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void HandleToggle(InputAction.CallbackContext context)
    {
        if (_canvas.enabled)
        {
            Close();
            return;
        }

        if (GameManager.Instance.CurrentState != GameState.Playing) return;

        Open();
    }

    private void HandleStateChanged(GameState state)
    {
        if (state == GameState.Playing || !_canvas.enabled) return;

        Close();
    }

    private void HandleQuestAccepted(QuestAcceptedEvent acceptedEvent)
    {
        if (!_canvas.enabled) return;

        Refresh();
    }

    private void HandleQuestCompleted(QuestCompletedEvent completedEvent)
    {
        if (!_canvas.enabled) return;

        Refresh();
    }

    private void Open()
    {
        Refresh();
        _canvas.enabled = true;
        _playerController.PlayerLook.RequestLock(this);
    }

    private void Close()
    {
        _canvas.enabled = false;
        _playerController.PlayerLook.ReleaseLock(this);
    }

    private void Refresh()
    {
        IReadOnlyList<QuestInstance> quests = QuestManager.Instance.AcceptedQuests;
        _titleText.text = $"의뢰 ({quests.Count}/{QuestManager.MaxAcceptedCount})";

        for (int i = 0; i < _slotTexts.Length; i++)
        {
            _slotTexts[i].text = i < quests.Count ? GetSlotText(quests[i]) : "비어 있음";
        }
    }

    private string GetSlotText(QuestInstance quest)
    {
        return $"{QuestInfoWindow.GetMonsterName(quest.Data.TargetType)} · {quest.Direction} · {quest.Data.RequiredMoney} G";
    }
}
