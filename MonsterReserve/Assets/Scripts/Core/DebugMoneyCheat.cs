// 시험용 돈 치트. 개발 빌드와 에디터에서만 컴파일되어 정식 빌드에는 들어가지 않는다.
#if DEVELOPMENT_BUILD || UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;

public class DebugMoneyCheat : MonoBehaviour
{
    private const int CheatMoney = 1000;
    private const string CheatBinding = "<Keyboard>/5";

    private InputAction _cheatAction;

    private void Awake() => CreateAction();
    private void OnEnable() => BindInputEvents();
    private void OnDisable() => UnbindInputEvents();
    private void OnDestroy() => _cheatAction.Dispose();

    // 씬, 프리팹 수정 없이 붙이기 위해 게임 시작 시 스스로 생긴다
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateInstance()
    {
        var cheatObject = new GameObject(nameof(DebugMoneyCheat));
        cheatObject.AddComponent<DebugMoneyCheat>();
        DontDestroyOnLoad(cheatObject);
    }

    // 프리팹에 컴포넌트를 붙이면 정식 빌드에서 Missing Script가 남으므로, InputActionReference 대신 코드에서 만든다
    private void CreateAction()
    {
        _cheatAction = new InputAction(nameof(DebugMoneyCheat), InputActionType.Button, CheatBinding);
    }

    private void BindInputEvents()
    {
        _cheatAction.performed += HandleCheatPerformed;
        _cheatAction.Enable();
    }

    private void UnbindInputEvents()
    {
        _cheatAction.performed -= HandleCheatPerformed;
        _cheatAction.Disable();
    }

    private void HandleCheatPerformed(InputAction.CallbackContext context) => AddCheatMoney();

    // 씬이 바뀌면 지갑도 바뀌므로 누를 때마다 찾는다. Title 씬처럼 지갑이 없으면 무시한다
    private void AddCheatMoney()
    {
        PlayerWallet wallet = FindFirstObjectByType<PlayerWallet>();
        if (wallet == null) return;

        wallet.AddMoney(CheatMoney);
    }
}
#endif
