using System;
using UnityEngine;

[Serializable]
public class QuestInstance
{
    [SerializeField] private QuestData _data;
    [SerializeField] private CompassDirection _direction;
    [SerializeField] private QuestState _state;

    public QuestData Data => _data;
    public CompassDirection Direction => _direction;
    public QuestState State => _state;

    public QuestInstance(QuestData data, CompassDirection direction)
    {
        _data = data;
        _direction = direction;
    }

    public void Accept() => _state = QuestState.Accepted;
    public void Complete() => _state = QuestState.Completed;
}
