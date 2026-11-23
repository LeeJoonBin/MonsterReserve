using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class MagicAim : MonoBehaviour
{
    [SerializeField] private PlayerInputHandler _inputHandler;
    [SerializeField] private MagicCaster _magicCaster;
    [SerializeField] private Camera _aimCamera;
    //[SerializeField] private Image _zoomImage;
    [SerializeField] private float _zoomSpeed = 5f;
    private float _defaultFieldOfView;
    private Coroutine _zoomRoutine;
    
    private void Awake() => _defaultFieldOfView = _aimCamera.fieldOfView; // 원래 시야각
    
    private void OnEnable() => BindEvents();
    private void OnDisable() => UnbindEvents();

    private void BindEvents()
    {
        _inputHandler.OnAimChanged += ZoomAim;
        EventBus<MagicSelectedEvent>.Subscribe(HandleMagicSelected);
    }

    private void UnbindEvents()
    {
        _inputHandler.OnAimChanged -= ZoomAim;
        EventBus<MagicSelectedEvent>.Unsubscribe(HandleMagicSelected);
    }

    private void HandleMagicSelected(MagicSelectedEvent selectedEvent)
    {
        if (!_inputHandler.IsAiming) return;

        ZoomAim(true);
    }

    private void ZoomAim(bool isZooming)
    {
        float targetFieldOfView = isZooming ? _magicCaster.SelectedMagic.ZoomFieldOfView : _defaultFieldOfView;
        if (_zoomRoutine != null)
        {
            StopCoroutine(_zoomRoutine);
        }
        _zoomRoutine = StartCoroutine(ZoomTo(targetFieldOfView));
        
    }
    private IEnumerator ZoomTo(float targetFieldOfView)
    {
        while (!Mathf.Approximately(_aimCamera.fieldOfView, targetFieldOfView))
        {
            _aimCamera.fieldOfView = Mathf.MoveTowards(_aimCamera.fieldOfView, targetFieldOfView, _zoomSpeed * Time.deltaTime);
            yield return null;
        }
    }
}
