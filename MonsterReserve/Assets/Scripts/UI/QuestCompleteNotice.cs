using System.Collections;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(Canvas))]
public class QuestCompleteNotice : MonoBehaviour
{
    [SerializeField] private TMP_Text _messageText;
    [SerializeField] private float _displayDuration = 2f;

    private Canvas _canvas;
    private WaitForSeconds _displayWait;
    private Coroutine _hideRoutine;

    private void Awake() => InitializeNotice();
    private void OnEnable() => EventBus<QuestCompletedEvent>.Subscribe(HandleQuestCompleted);
    private void OnDisable() => EventBus<QuestCompletedEvent>.Unsubscribe(HandleQuestCompleted);

    private void InitializeNotice()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.enabled = false;
        _displayWait = new WaitForSeconds(_displayDuration);
    }

    private void HandleQuestCompleted(QuestCompletedEvent completedEvent)
    {
        _messageText.text = $"의뢰 완료 +{completedEvent.Reward} G";
        _canvas.enabled = true;

        if (_hideRoutine != null) StopCoroutine(_hideRoutine);
        _hideRoutine = StartCoroutine(CoHide());
    }

    private IEnumerator CoHide()
    {
        yield return _displayWait;
        _canvas.enabled = false;
        _hideRoutine = null;
    }
}
