using System;
using UnityEngine;

// 플레이어 마나. R키를 누르고 있는 동안 명상해서, 누른 시간만큼 마나를 채운다.
// 마나를 쓰는 기능은 마법 단계에서 만든다 (쓰는 방식은 ScriptStructure.md "자원 쓰기" 패턴 후보에서 정한다)
public class PlayerMana : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private PlayerInputHandler _inputHandler;

    // 수치는 모두 임시값 (GameDesign.md에 "미정". 2026-11-15 사용자가 임시값으로 정함)
    [Header("마나")]
    [SerializeField] private float _maxMana = 100f;
    [SerializeField] private float _meditationRegenPerSecond = 10f;

    // HUD가 처음 화면을 그릴 때 읽어 간다 (그 뒤로는 OnManaChanged로 받는다)
    public float CurrentMana { get; private set; }
    public float MaxMana => _maxMana;

    // PlayerMover가 읽는다. 명상 중에는 이동하지 않는다 (2026-11-15 사용자 결정)
    public bool IsMeditating => _inputHandler.IsMeditateHeld;

    // HUD가 구독한다 (현재 마나, 최대 마나)
    public event Action<float, float> OnManaChanged;

    // 현재 마나는 저장하지 않으므로 항상 가득 찬 상태로 시작한다 (GameDesign.md "저장" 후순위)
    private void Awake() => ResetMana();
    private void Update() => Meditate();

    private void ResetMana()
    {
        CurrentMana = _maxMana;
    }

    // 초당 회복량 × 지난 시간 : 프레임이 몇 번 돌든 1초에 회복되는 양은 같다.
    // 일시 정지, 맵에서는 시간 배율이 0이라 Time.deltaTime도 0이 되어 저절로 멈춘다.
    // 생각해 볼 것 : 시간 배율이 0일 때도 OnManaChanged는 매 프레임 불린다. 값이 그대로인데 알릴 필요가 있을까?
    private void Meditate()
    {
        if (!IsMeditating) return;
        if (CurrentMana >= _maxMana) return;

        CurrentMana = Mathf.Min(CurrentMana + _meditationRegenPerSecond * Time.deltaTime, _maxMana);
        OnManaChanged?.Invoke(CurrentMana, _maxMana);
    }
}
