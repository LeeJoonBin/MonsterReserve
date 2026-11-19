using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD 나침반. HUD 중 유일하게 매 프레임 카메라 방향을 따라간다 (UI.md "최적화 규칙")
public class CompassHUD : MonoBehaviour
{
    private const float FullCircle = 360f;
    private const float DirectionStep = 45f;
    // 텍스처의 북쪽 눈금 위치. 눈금 32개가 각 칸의 가운데(i/32 + 1/64)에 있고 0번이 북쪽이다
    private const float NorthOffsetU = 1f / 64f;

    private static readonly string[] DirectionNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    [SerializeField] private Transform _cameraTransform;
    [SerializeField] private RawImage _ticksImage;
    [SerializeField] private TMP_Text _directionLabel;
    // 나침반에 한 번에 보이는 각도. 변경되는 값
    [SerializeField] private float _visibleAngle = 180f;

    // 처음 한 번은 반드시 그리도록 없는 인덱스로 시작한다
    private int _lastDirectionIndex = -1;

    private void LateUpdate() => FollowCameraYaw();

    // yaw 0 = 북쪽(+Z). 텍스처 Wrap이 Repeat이라 x가 0~1을 벗어나도 이어서 보인다
    private void FollowCameraYaw()
    {
        float yaw = _cameraTransform.eulerAngles.y;
        float width = _visibleAngle / FullCircle;
        float x = yaw / FullCircle + NorthOffsetU - width * 0.5f;

        _ticksImage.uvRect = new Rect(x, 0f, width, 1f);
        UpdateDirectionLabel(yaw);
    }

    // 매 프레임 문자열을 넣으면 TMP가 매번 다시 그리므로, 방향이 바뀔 때만 넣는다
    private void UpdateDirectionLabel(float yaw)
    {
        int index = Mathf.RoundToInt(yaw / DirectionStep) % DirectionNames.Length;
        if (index == _lastDirectionIndex) return;

        _lastDirectionIndex = index;
        _directionLabel.text = DirectionNames[index];
    }
}
