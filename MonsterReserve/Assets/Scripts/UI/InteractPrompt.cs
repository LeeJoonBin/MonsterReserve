using UnityEngine;

[RequireComponent(typeof(Canvas))]
public class InteractPrompt : MonoBehaviour
{
    private Canvas _canvas;

    private void Awake() => InitializePrompt();
    private void OnEnable() => EventBus<InteractTargetChangedEvent>.Subscribe(HandleInteractTargetChanged);
    private void OnDisable() => EventBus<InteractTargetChangedEvent>.Unsubscribe(HandleInteractTargetChanged);

    private void InitializePrompt()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.enabled = false;
    }

    private void HandleInteractTargetChanged(InteractTargetChangedEvent changedEvent)
    {
        _canvas.enabled = changedEvent.HasTarget;
    }
}
