using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
public class QuestInfoWindow : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private PlayerController _playerController;

    [Header("Text")]
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _descriptionText;
    [SerializeField] private TMP_Text _targetText;
    [SerializeField] private TMP_Text _directionText;
    [SerializeField] private TMP_Text _moneyText;
    [SerializeField] private TMP_Text _acceptedCountText;

    [Header("Button")]
    [SerializeField] private Button _acceptButton;
    [SerializeField] private Button _closeButton;

    private Canvas _canvas;
    private QuestBoard _board;
    private int _slot;

    private void Awake() => InitializeWindow();
    private void OnEnable() => BindEvents();
    private void OnDisable() => UnbindEvents();

    private void InitializeWindow()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.enabled = false;
    }

    private void BindEvents()
    {
        EventBus<QuestNoteInteractedEvent>.Subscribe(HandleQuestNoteInteracted);
        _acceptButton.onClick.AddListener(HandleAcceptClicked);
        _closeButton.onClick.AddListener(HandleCloseClicked);
    }

    private void UnbindEvents()
    {
        EventBus<QuestNoteInteractedEvent>.Unsubscribe(HandleQuestNoteInteracted);
        _acceptButton.onClick.RemoveListener(HandleAcceptClicked);
        _closeButton.onClick.RemoveListener(HandleCloseClicked);
    }

    private void HandleQuestNoteInteracted(QuestNoteInteractedEvent interactedEvent)
    {
        _board = interactedEvent.Board;
        _slot = interactedEvent.Slot;
        Open();
    }

    private void HandleAcceptClicked()
    {
        if (_board.TryAccept(_slot))
        {
            Close();
            return;
        }

        RefreshAcceptedCount();
    }

    private void HandleCloseClicked() => Close();

    private void Open()
    {
        RefreshQuest();
        RefreshAcceptedCount();
        _canvas.enabled = true;
        _playerController.PlayerLook.RequestLock(this);
    }

    private void Close()
    {
        _canvas.enabled = false;
        _playerController.PlayerLook.ReleaseLock(this);
    }

    private void RefreshQuest()
    {
        QuestInstance quest = _board.GetQuest(_slot);
        _nameText.text = quest.Data.QuestName;
        _descriptionText.text = quest.Data.QuestDescription;
        _targetText.text = $"대상 : {GetMonsterName(quest.Data.TargetType)}";
        _directionText.text = $"서식지 : {quest.Direction}";
        _moneyText.text = $"의뢰비 : {quest.Data.RequiredMoney}";
    }

    private void RefreshAcceptedCount()
    {
        _acceptedCountText.text = $"수락한 의뢰 : {QuestManager.Instance.AcceptedCount} / {QuestManager.MaxAcceptedCount}";
    }

    private static string GetMonsterName(MonsterType type) => type switch
    {
        MonsterType.Goblin => "고블린",
        MonsterType.MushroomMonster => "버섯 몬스터",
        MonsterType.SkeletonWarrior => "스켈레톤 전사",
        MonsterType.Fishman => "피쉬맨",
        MonsterType.RockMonster => "바위 몬스터",
        _ => type.ToString()
    };
}
