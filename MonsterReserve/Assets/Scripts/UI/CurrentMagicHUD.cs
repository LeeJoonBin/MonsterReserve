using TMPro;
using UnityEngine;

public class CurrentMagicHUD : MonoBehaviour
{
    [SerializeField] private MagicCaster _magicCaster;
    [SerializeField] private TMP_Text _magicText;

    private void OnEnable() => EventBus<MagicSelectedEvent>.Subscribe(HandleMagicSelected);
    private void Start() => DrawMagic(_magicCaster.SelectedIndex, _magicCaster.SelectedMagic.MagicName);
    private void OnDisable() => EventBus<MagicSelectedEvent>.Unsubscribe(HandleMagicSelected);

    private void HandleMagicSelected(MagicSelectedEvent selectedEvent) => DrawMagic(selectedEvent.Index, selectedEvent.MagicName);

    private void DrawMagic(int index, string magicName)
    {
        _magicText.text = $"{index + 1} {magicName}";
    }
}
