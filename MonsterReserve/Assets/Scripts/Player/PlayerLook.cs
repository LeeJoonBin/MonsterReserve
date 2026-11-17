using System.Collections.Generic;
using UnityEngine;

// 마우스 시점 회전. 좌우는 몸, 위아래는 카메라를 돌린다.
// 커서 잠금과 회전 멈춤은 항상 함께 바뀌므로 이 스크립트 한 곳에서 관리한다.
public class PlayerLook : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private PlayerInputHandler _inputHandler;
    [SerializeField] private Transform _cameraTransform;

    // 수치는 모두 변경되는 값 (플레이하며 조정한다)
    [Header("회전")]
    [SerializeField] private float _sensitivity = 0.1f;
    [SerializeField] private float _minPitch = -80f;
    [SerializeField] private float _maxPitch = 80f;

    // 현재 위아래 각도. transform.eulerAngles는 -10°를 350°로 돌려주므로 따로 저장한다
    private float _pitch;

    // 잠금을 요청한 쪽 모음 (J 팝업, 맵, 일시 정지 등). 하나라도 있으면 커서를 풀고 회전을 멈춘다
    private readonly HashSet<object> _lockRequesters = new HashSet<object>();

    private void Start() => ApplyCursorState();
    private void LateUpdate() => UpdateLook();

    // 같은 요청자가 두 번 요청해도 HashSet이라 한 번만 들어간다
    public void RequestLock(object requester)
    {
        _lockRequesters.Add(requester);
        ApplyCursorState();
    }

    // 들어 있지 않은 요청자를 빼도 HashSet은 오류를 내지 않는다
    public void ReleaseLock(object requester)
    {
        _lockRequesters.Remove(requester);
        ApplyCursorState();
    }

    private void UpdateLook()
    {
        if (_lockRequesters.Count > 0) return;

        RotateBody();
        RotateCamera();
    }

    // 마우스 이동량은 이미 "이번 프레임에 움직인 양"이라 Time.deltaTime을 곱하지 않는다 (곱하면 FPS에 따라 감도가 바뀐다)
    // 몸을 transform으로 돌린 이유 : Rigidbody.MoveRotation은 다음 물리 단계에 적용되어, 한 물리 단계 사이에 여러 번 부르면 앞의 회전이 사라진다.
    // 확인해 볼 것 : Rigidbody의 Interpolate와 함께 쓸 때 화면이 떨리는지 Play 모드에서 본다
    private void RotateBody()
    {
        float yaw = _inputHandler.LookInput.x * _sensitivity;
        transform.Rotate(0f, yaw, 0f);
    }

    // 마우스를 위로 올리면 delta.y가 +인데, X축 회전은 +가 아래를 보는 방향이라 빼 준다
    private void RotateCamera()
    {
        _pitch -= _inputHandler.LookInput.y * _sensitivity;
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
