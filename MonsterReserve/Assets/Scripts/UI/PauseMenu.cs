using UnityEngine;
using UnityEngine.UI;

// 일시 정지 메뉴의 버튼(저장, 설정, 종료)과 설정 창의 Back을 맡는다.
// 일시 정지 창 자체를 상태에 맞춰 켜고 끄는 일은 같은 오브젝트의 StateWindow가 한다
public class PauseMenu : MonoBehaviour
{
    [Header("일시 정지 메뉴")]
    [SerializeField] private Button _saveButton;
    [SerializeField] private Button _settingsButton;
    [SerializeField] private Button _quitButton;
    [SerializeField] private Canvas _pauseCanvas;

    [Header("설정 창")]
    [SerializeField] private Button _backButton;
    [SerializeField] private Canvas _settingsCanvas;

    private void Awake() => InitializeWindows();
    private void OnEnable() => BindEvents();
    private void OnDisable() => UnbindEvents();

    // 저장 기능이 아직 없으므로 저장 버튼을 끈다 (TitleMenu의 이어가기와 같은 방식).
    // TODO: 저장 기능이 생기면 저장 버튼을 켜고, 클릭 연결도 BindEvents에 추가한다
    private void InitializeWindows()
    {
        _saveButton.interactable = false;
        _settingsCanvas.enabled = false;
    }

    // 클릭 연결은 코드에서 한다 (ScriptStructure 결정 기록 "UI 버튼 연결")
    private void BindEvents()
    {
        _settingsButton.onClick.AddListener(HandleSettingsClicked);
        _quitButton.onClick.AddListener(HandleQuitClicked);
        _backButton.onClick.AddListener(HandleBackClicked);
        GameManager.Instance.OnStateChanged += HandleStateChanged;
    }

    private void UnbindEvents()
    {
        _settingsButton.onClick.RemoveListener(HandleSettingsClicked);
        _quitButton.onClick.RemoveListener(HandleQuitClicked);
        _backButton.onClick.RemoveListener(HandleBackClicked);
        GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void HandleSettingsClicked()
    {
        _pauseCanvas.enabled = false;
        _settingsCanvas.enabled = true;
    }

    private void HandleBackClicked()
    {
        _settingsCanvas.enabled = false;
        _pauseCanvas.enabled = true;
    }

    // 사망하지 않은 상태이므로 자동 저장 후 종료한다 (GameDesign "일시 정지 메뉴").
    // TODO: 저장 기능이 생기면 종료 전에 자동 저장을 부른다
    private void HandleQuitClicked()
    {
        Application.Quit();
    }

    // 설정 창을 연 채로 ESC로 일시 정지를 풀면 설정 창이 남으므로 함께 끈다
    private void HandleStateChanged(GameState state)
    {
        if (state == GameState.Paused) return;

        _settingsCanvas.enabled = false;
    }
}
