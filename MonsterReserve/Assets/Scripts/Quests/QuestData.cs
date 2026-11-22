using UnityEngine;

[CreateAssetMenu(fileName = "QuestData", menuName = "Monster Reserve/Quest Data")]
public class QuestData : ScriptableObject
{
    [SerializeField] private int _id;
    [SerializeField] private string _questName;
    [SerializeField] private string _questDescription;
    [SerializeField] private MonsterType _targetType;
    [SerializeField] private int _requiredMoney;
    
    public int Id => _id;
    public string QuestName => _questName;
    public string QuestDescription => _questDescription;
    public MonsterType TargetType => _targetType;
    public int RequiredMoney => _requiredMoney;
}
