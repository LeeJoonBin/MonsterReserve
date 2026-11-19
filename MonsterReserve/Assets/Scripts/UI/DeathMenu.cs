using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 사망 화면의 버튼(타이틀로, 게임 종료)을 맡는다. 화면을 켜고 끄는 일은 같은 오브젝트의 StateWindow가 한다.
// 사망 중 시간 배율 0은 씬을 나갈 때 GameManager가 1로 되돌린다
public class DeathMenu : MonoBehaviour
{
    // 빌드 씬 목록에 등록된 씬 파일 이름과 글자 하나까지 같아야 한다
    private const string TitleSceneName = "Title";

    [SerializeField] private Button _titleButton;
    [SerializeField] private Button _quitButton;

    private void OnEnable() => BindButtonEvents();
    private void OnDisable() => UnbindButtonEvents();

    private void BindButtonEvents()
    {
        _titleButton.onClick.AddListener(HandleTitleClicked);
        _quitButton.onClick.AddListener(HandleQuitClicked);
    }

    private void UnbindButtonEvents()
    {
        _titleButton.onClick.RemoveListener(HandleTitleClicked);
        _quitButton.onClick.RemoveListener(HandleQuitClicked);
    }

    // 버튼을 누르는 순간 저장 데이터를 지운다 (GameDesign "사망").
    // TODO: 저장 기능이 생기면 씬을 옮기기 전에 저장 데이터를 지운다
    private void HandleTitleClicked()
    {
        SceneManager.LoadScene(TitleSceneName);
    }

    // TODO: 저장 기능이 생기면 종료 전에 저장 데이터를 지운다
    private void HandleQuitClicked()
    {
        Application.Quit();
    }
}
