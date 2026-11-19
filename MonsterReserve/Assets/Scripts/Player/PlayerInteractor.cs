using UnityEngine;

public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private PlayerInputHandler _playerInputHandler;
    [SerializeField] private Transform _cameraTransform;
    [SerializeField] private float _maxDistance = 3f;
    [SerializeField] private LayerMask _layerMask;

    private void Update()
    {
        CheckInteractInput();
    }
    
    private void CheckInteractInput() 
    {
        if (_playerInputHandler.ConsumeInteractInput())
        {
            if (Physics.Raycast(_cameraTransform.position,
                    _cameraTransform.forward,
                    out RaycastHit hit,
                    _maxDistance, _layerMask, QueryTriggerInteraction.Ignore))
            {
                IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
                if (interactable == null) return;
                interactable.Interact();
            }
        }
    }
}
