using UnityEngine;

public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private PlayerInputHandler _playerInputHandler;
    [SerializeField] private Transform _cameraTransform;
    [SerializeField] private float _maxDistance = 3f;
    [SerializeField] private LayerMask _layerMask;

    private IInteractable _currentTarget;

    private void Update()
    {
        UpdateTarget();
        CheckInteractInput();
    }

    private void UpdateTarget()
    {
        IInteractable target = FindTarget();
        if (target == _currentTarget) return;

        _currentTarget = target;
        EventBus<InteractTargetChangedEvent>.Raise(new InteractTargetChangedEvent(_currentTarget != null));
    }

    private IInteractable FindTarget()
    {
        if (!Physics.Raycast(_cameraTransform.position,
                _cameraTransform.forward,
                out RaycastHit hit,
                _maxDistance, _layerMask, QueryTriggerInteraction.Ignore)) return null;

        IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
        if (interactable == null || !interactable.CanInteract) return null;
        return interactable;
    }

    private void CheckInteractInput()
    {
        if (!_playerInputHandler.ConsumeInteractInput()) return;

        _currentTarget?.Interact();
    }
}
