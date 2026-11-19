using UnityEngine;
using UnityEngine.AI;

public class MonsterSpawner : MonoBehaviour
{
    private const int MaxSampleAttempts = 10;
    private const float FullTurnDegrees = 360f;

    [SerializeField] private SkeletonWarrior _monsterPrefab;
    [SerializeField] private int _count = 3;
    [SerializeField] private float _radius = 80f;
    [SerializeField] private float _respawnDelay = 600f;
    [SerializeField] private float _sampleDistance = 20f;

    private SkeletonWarrior[] _monsters;
    private float[] _remainingTimes;
    private SkeletonWarrior[] _corpses;

    private void Awake() => CreateSlots();
    private void Update() => TickSlots();

    private void CreateSlots()
    {
        _monsters = new SkeletonWarrior[_count];
        _remainingTimes = new float[_count];
        _corpses =  new SkeletonWarrior[_count];
    }

    private void TickSlots()
    {
        for (int i = 0; i < _count; i++)
        {
            if (_monsters[i] != null)
            {
                if(!_monsters[i].IsDead) continue;
                _corpses[i] = _monsters[i];
                _monsters[i] = null;
            }
            _remainingTimes[i] -= Time.deltaTime;
            if (_remainingTimes[i] > 0f) continue;

            Spawn(i);
        }
    } 

    private void Spawn(int index)
    {
        if (!TryGetSpawnPosition(out Vector3 position)) return;

        if (_corpses[index] && _corpses[index].gameObject.activeSelf && _corpses[index].IsDead)
            PoolManager.Instance.Release(_corpses[index].gameObject);
        
        _corpses[index] = null;
        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, FullTurnDegrees), 0f);
        GameObject monster = PoolManager.Instance.Get(_monsterPrefab.gameObject, position, rotation);
        _monsters[index] = monster.GetComponent<SkeletonWarrior>();
        _remainingTimes[index] = _respawnDelay;
    }

    private bool TryGetSpawnPosition(out Vector3 position)
    {
        for (int i = 0; i < MaxSampleAttempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * _radius;
            Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, _sampleDistance, NavMesh.AllAreas))
            {
                position = hit.position;
                return true;
            }
        }
        position = default;
        return false;
    }
}
