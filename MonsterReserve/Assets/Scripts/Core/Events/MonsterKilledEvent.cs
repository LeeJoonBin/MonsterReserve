public struct MonsterKilledEvent
{
    public MonsterType Type;
    public int Experience;

    public MonsterKilledEvent(MonsterType type, int experience)
    {
        Type = type;
        Experience = experience;
    }
}
