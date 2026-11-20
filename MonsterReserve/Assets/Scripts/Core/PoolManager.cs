using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

[DefaultExecutionOrder(-100)]
public class PoolManager : MonoBehaviour
{
    [Serializable] private struct PrewarmEntry { public GameObject Prefab; public int Count;}
    [SerializeField] private PrewarmEntry[] _prewarmList;
    [SerializeField] private int _defaultCapacity = 10;
    [SerializeField] private int _maxSize = 100;

    private readonly Dictionary<GameObject, ObjectPool<GameObject>> _poolsByPrefab = new Dictionary<GameObject, ObjectPool<GameObject>>();
    private readonly Dictionary<GameObject, ObjectPool<GameObject>> _poolsByInstance = new Dictionary<GameObject, ObjectPool<GameObject>>();

    private Transform _inactiveParent;

    public static PoolManager Instance { get; private set; }

    private void Awake() => Initialize();
    private void OnDestroy() => UnregisterInstance();

    public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        GameObject instance = GetOrCreatePool(prefab).Get();
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.SetActive(true);
        return instance;
    }

    public void Release(GameObject instance)
    {
        _poolsByInstance[instance].Release(instance);
    }

    private void Initialize()
    {
        if (!RegisterInstance()) return;
        Prewarm();
    }

    private bool RegisterInstance()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return false;
        }

        Instance = this;
        return true;
    }

    private void Prewarm()
    {
        _inactiveParent = new GameObject("PoolInactiveParent").transform;
        _inactiveParent.SetParent(transform, false);
        _inactiveParent.gameObject.SetActive(false);

        foreach (PrewarmEntry entry in _prewarmList)
        {
            ObjectPool<GameObject> pool = GetOrCreatePool(entry.Prefab);
            var created = new List<GameObject>(entry.Count);
            for (int i = 0; i < entry.Count; i++) created.Add(pool.Get());
            foreach (GameObject instance in created) pool.Release(instance);
        }
    }

    private void UnregisterInstance()
    {
        if (Instance == this) Instance = null;
    }

    private ObjectPool<GameObject> GetOrCreatePool(GameObject prefab)
    {
        if (_poolsByPrefab.TryGetValue(prefab, out ObjectPool<GameObject> pool)) return pool;
        
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

    private GameObject CreateInstance(GameObject prefab)
    {
        GameObject instance = Instantiate(prefab, _inactiveParent);
        instance.SetActive(false);
        instance.transform.SetParent(transform, false);
        _poolsByInstance.Add(instance, _poolsByPrefab[prefab]);
        return instance;
    }

    private void DestroyInstance(GameObject instance)
    {
        _poolsByInstance.Remove(instance);
        Destroy(instance);
    }
}
