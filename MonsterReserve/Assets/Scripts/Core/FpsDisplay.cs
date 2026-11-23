// 성능 측정용. 개발 빌드와 에디터에서만 컴파일되어 정식 빌드에는 들어가지 않는다.
#if DEVELOPMENT_BUILD || UNITY_EDITOR
using UnityEngine;

public class FpsDisplay : MonoBehaviour
{
    private const float UpdateInterval = 0.5f;
    private const int FontSize = 24;
    private static readonly Rect LabelRect = new Rect(10f, 10f, 400f, 40f);

    private float _elapsedTime;
    private int _frameCount;
    private string _displayText = string.Empty;
    private GUIStyle _labelStyle;

    private void Update() => AccumulateFrame();

    private void OnGUI() => DrawLabel();

    // 씬에 배치하지 않아도 게임 시작 시 스스로 생기게 한다 (씬, 프리팹 수정 없이 붙이기 위해)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateInstance()
    {
        var displayObject = new GameObject(nameof(FpsDisplay));
        displayObject.AddComponent<FpsDisplay>();
        DontDestroyOnLoad(displayObject);
    }

    // 일시 정지 (timeScale 0) 중에도 재기 위해 unscaledDeltaTime을 쓴다
    private void AccumulateFrame()
    {
        _elapsedTime += Time.unscaledDeltaTime;
        _frameCount++;
        if (_elapsedTime < UpdateInterval) return;

        // 매 프레임 바뀌면 읽기 어려워 구간 평균으로 갱신하고, 문자열도 이때만 만든다
        float fps = _frameCount / _elapsedTime;
        float frameMs = _elapsedTime * 1000f / _frameCount;
        _displayText = $"FPS {fps:0.0}  ({frameMs:0.0} ms)";
        _elapsedTime = 0f;
        _frameCount = 0;
    }

    private void DrawLabel()
    {
        // GUI.skin은 OnGUI 안에서만 접근할 수 있어 처음 그릴 때 만든다
        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = FontSize };
            _labelStyle.normal.textColor = Color.yellow;
        }

        GUI.Label(LabelRect, _displayText, _labelStyle);
    }
}
#endif
