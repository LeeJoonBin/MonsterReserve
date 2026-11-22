public struct QuestAcceptedEvent
{
    public QuestInstance Quest;

    public QuestAcceptedEvent(QuestInstance quest)
    {
        Quest = quest;
    }
}
