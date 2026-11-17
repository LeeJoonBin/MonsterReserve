using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

// 자주 만들고 지우는 오브젝트(투사체, 이펙트, 몬스터)를 프리팹별 풀에서 꺼내 쓰고 돌려받는다.
// 실행 순서를 앞당겨, 다른 스크립트의 OnEnable에서 Instance를 쓸 때 이미 준비돼 있게 한다
[DefaultExecutionOrder(-100)]
public class PoolManager : MonoBehaviour
{
    [SerializeField] private int _defaultCapacity = 10;
    [SerializeField] private int _maxSize = 100;

    // 꺼낼 때는 프리팹으로, 돌려받을 때는 꺼낸 오브젝트로 풀을 찾으므로 표를 두 개 둔다
    private readonly Dictionary<GameObject, ObjectPool<GameObject>> _poolsByPrefab = new Dictionary<GameObject, ObjectPool<GameObject>>();
    private readonly Dictionary<GameObject, ObjectPool<GameObject>> _poolsByInstance = new Dictionary<GameObject, ObjectPool<GameObject>>();

    // 새로 만들 때 쓸 위치. 만들자마자 Awake, OnEnable이 실행되므로, 원점이 아니라 꺼낼 자리에서 만들어지게 한다
    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;

    // 싱글톤 : 투사체, 이펙트, 몬스터 스포너 등 쓰는 곳이 많아 인스펙터로 일일이 연결하지 않는다
    public static PoolManager Instance { get; private set; }

    private void Awake() => RegisterInstance();
    private void OnDestroy() => UnregisterInstance();

    // 위치를 먼저 정하고 켠다. 켠 뒤에 옮기면 OnEnable이 이전 위치에서 실행된다
    public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        _spawnPosition = position;
        _spawnRotation = rotation;

        GameObject instance = GetOrCreatePool(prefab).Get();
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.SetActive(true);
        return instance;
    }

    // 풀에서 꺼내지 않은 오브젝트를 넘기면 KeyNotFoundException이 난다. 잘못 쓴 곳을 빨리 찾으려고 숨기지 않는다
    public void Release(GameObject instance)
    {
        _poolsByInstance[instance].Release(instance);
    }

    private void RegisterInstance()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void UnregisterInstance()
    {
        if (Instance == this) Instance = null;
    }

    private ObjectPool<GameObject> GetOrCreatePool(GameObject prefab)
    {
        if (_poolsByPrefab.TryGetValue(prefab, out ObjectPool<GameObject> pool)) return pool;

        // collectionCheck : 같은 오브젝트를 두 번 돌려보내면 오류를 낸다
        pool = new ObjectPool<GameObject>(
            createFunc: () => CreateInstance(prefab),
            actionOnRelease: instance => instance.SetActive(false),
            actionOnDestroy: DestroyInstance,
            collectionCheck: true,
            defaultCapacity: _defaultCapacity,
            maxSize: _maxSize);

        _poolsByPrefab.Add(prefab, pool);
        return pool;
    }

    // 풀 오브젝트를 PoolManager 아래에 모아 Hierarchy가 어지러워지지 않게 한다
    private GameObject CreateInstance(GameObject prefab)
    {
        GameObject instance = Instantiate(prefab, _spawnPosition, _spawnRotation, transform);
        _poolsByInstance.Add(instance, _poolsByPrefab[prefab]);
        return instance;
    }

    // 돌려받은 수가 최대 개수를 넘으면 풀이 이 메서드로 지운다
    private void DestroyInstance(GameObject instance)
    {
        _poolsByInstance.Remove(instance);
        Destroy(instance);
    }
}
