using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class MagicAim : MonoBehaviour
{
    [SerializeField] private PlayerInputHandler _inputHandler;
    [SerializeField] private Camera _aimCamera;
    [SerializeField] private float _zoomFieldOfView = 40f; // 줌인 시야각
    //[SerializeField] private Image _zoomImage;
    [SerializeField] private float _zoomSpeed = 5f;
    private float _defaultFieldOfView;
    private Coroutine _zoomRoutine;
    
    private void Awake() => _defaultFieldOfView = _aimCamera.fieldOfView; // 원래 시야각
    
    private void OnEnable() => _inputHandler.OnAimChanged += ZoomAim;
    private void OnDisable() => _inputHandler.OnAimChanged -= ZoomAim;

    private void ZoomAim(bool isZooming)
    {
        float targetFieldOfView = isZooming ? _zoomFieldOfView : _defaultFieldOfView;
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
