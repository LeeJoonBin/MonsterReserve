using System.Collections.Generic;
using UnityEngine;

public class PlayerLook : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private PlayerInputHandler _inputHandler;
    [SerializeField] private Transform _cameraTransform;
    
    [Header("회전")]
    [SerializeField] private float _sensitivity = 0.1f;
    [SerializeField] private float _minPitch = -80f;
    [SerializeField] private float _maxPitch = 80f;
    [SerializeField] private float _aimSensitivityRatio = 0.2f;
    
    // 현재각도
    private float _pitch;

    private readonly HashSet<object> _lockRequesters = new HashSet<object>();

    private Rigidbody _rigidbody;

    public bool IsLocked => _lockRequesters.Count > 0;

    private void Awake() => _rigidbody = GetComponent<Rigidbody>();
    private void Start() => ApplyCursorState();
    private void LateUpdate() => UpdateLook();
    
    public void RequestLock(object requester)
    {
        _lockRequesters.Add(requester);
        ApplyCursorState();
    }

    public void ReleaseLock(object requester)
    {
        _lockRequesters.Remove(requester);
        ApplyCursorState();
    }

    private void UpdateLook()
    {
        float lookSensitivity = _sensitivity;
        if (_lockRequesters.Count > 0) return;
        if (_inputHandler.IsAiming)
        {
            lookSensitivity= _sensitivity * _aimSensitivityRatio;
        }
        RotateBody(lookSensitivity);
        RotateCamera(lookSensitivity);
    }

    private void RotateBody(float a)
    {
        float yaw = _inputHandler.LookInput.x * a;
        transform.Rotate(0f, yaw, 0f);
        _rigidbody.rotation = transform.rotation;
    }

    private void RotateCamera(float a)
    {
        _pitch -= _inputHandler.LookInput.y * a;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
        _cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    private void ApplyCursorState()
    {
        bool isLocked = _lockRequesters.Count > 0;
        Cursor.lockState = isLocked ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = isLocked;
    }
}
