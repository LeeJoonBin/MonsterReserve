using UnityEngine;

[RequireComponent(typeof(Canvas))]
public class ScopeOverlay : MonoBehaviour
{
    [SerializeField] private PlayerInputHandler _playerInputHandler;
    [SerializeField] private GameObject _crosshair;

    private Canvas _canvas;

    private void Awake() => InitializeOverlay();
    private void OnEnable() => BindEvents();
    private void OnDisable() => UnbindEvents();

    private void InitializeOverlay()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.enabled = false;
    }

    private void BindEvents()
    {
        _playerInputHandler.OnAimChanged += HandleAimChanged;
        SetScopeVisible(_playerInputHandler.IsAiming);
    }

    private void UnbindEvents()
    {
        if (_playerInputHandler != null) _playerInputHandler.OnAimChanged -= HandleAimChanged;

        SetScopeVisible(false);
    }

    private void HandleAimChanged(bool isAiming)
    {
        SetScopeVisible(isAiming);
    }

    private void SetScopeVisible(bool isVisible)
    {
        _canvas.enabled = isVisible;

        _crosshair.SetActive(!isVisible);
    }
}
