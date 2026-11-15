using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 시작 화면의 버튼 3개(새로운 게임, 이어가기, 게임 종료)를 맡는다. 규칙은 GameDesign.md "시작 화면"과 결정 기록 (2026-11-06)을 따른다.
public class TitleMenu : MonoBehaviour
{
    // 빌드 씬 목록에 등록된 씬 파일 이름과 글자 하나까지 같아야 한다
    private const string GameSceneName = "Game";

    [SerializeField] private Button _newGameButton;
    [SerializeField] private Button _continueButton;
    [SerializeField] private Button _quitButton;

    private void Awake() => DisableContinueButton();
    private void OnEnable() => BindButtonEvents();
    private void OnDisable() => UnbindButtonEvents();

    // 클릭 연결을 코드에서 하는 이유 : 인스펙터 OnClick은 코드에서 보이지 않아, 무엇이 연결됐는지 스크립트만 읽어서는 알 수 없다
    private void BindButtonEvents()
    {
        _newGameButton.onClick.AddListener(HandleNewGameClicked);
        _quitButton.onClick.AddListener(HandleQuitClicked);
    }

    private void UnbindButtonEvents()
    {
        _newGameButton.onClick.RemoveListener(HandleNewGameClicked);
        _quitButton.onClick.RemoveListener(HandleQuitClicked);
    }

    // 저장 기능이 아직 없으므로 항상 끈다. interactable을 끄면 버튼이 회색으로 바뀌고 눌리지 않는다.
    // TODO: 저장 기능이 생기면 "저장 데이터가 있을 때만 켜기"로 바꾸고, 클릭 연결도 BindButtonEvents에 추가한다
    private void DisableContinueButton()
    {
        _continueButton.interactable = false;
    }

    // 새로운 게임은 확인 팝업 없이 바로 시작한다 (결정 기록 2026-11-06).
    // 생각해 볼 것 : 저장 기능이 생기면, 기존 저장 데이터를 덮어쓰는 일은 이 메서드에서 할까, Game 씬에서 할까?
    private void HandleNewGameClicked()
    {
        SceneManager.LoadScene(GameSceneName);
    }

    // 생각해 볼 것 : 에디터에서 Play 중에 눌러도 아무 일이 없다. 왜 그럴까? (찾아볼 말 : Application.Quit, EditorApplication.isPlaying)
    private void HandleQuitClicked()
    {
        Application.Quit();
    }
}
