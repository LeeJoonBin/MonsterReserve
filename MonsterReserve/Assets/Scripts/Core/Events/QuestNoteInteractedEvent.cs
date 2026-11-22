public struct QuestNoteInteractedEvent
{
    public QuestBoard Board;
    public int Slot;

    public QuestNoteInteractedEvent(QuestBoard board, int slot)
    {
        Board = board;
        Slot = slot;
    }
}
