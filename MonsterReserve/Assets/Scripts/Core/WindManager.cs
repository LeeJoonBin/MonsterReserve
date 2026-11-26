using UnityEngine;

[DefaultExecutionOrder(-100)] 
public class WindManager : MonoBehaviour
{
    [SerializeField] private float _changeSpeed = 0.03f;
    [SerializeField] private float _minStrength = 0.5f;
    [SerializeField] private float _maxStrength = 10f;
    [SerializeField] private float _treeSwayScale = 0.1f;
    
    private WindZone _windZone;
    private float _directionSeed;
    private float _strengthSeed;
    
    public static WindManager Instance { get; private set; }
    public Vector3 Wind { get; private set; }
    
    private void Awake()
    {
        RegisterInstance();
        _windZone = GetComponent<WindZone>();
        _directionSeed = Random.Range(0f, 1000f);
        _strengthSeed = Random.Range(0f, 1000f);
    }
    
    private void Update() => SetWind();
    
    private void OnDestroy() => UnregisterInstance();
    
    private void SetWind()
    {
        float time = Time.time * _changeSpeed;
        
        float directionNoise = Mathf.PerlinNoise(time, _directionSeed);
        float strengthNoise = Mathf.PerlinNoise(time, _strengthSeed);
        
        float angle = directionNoise * 360f;
        
        float strength = Mathf.Lerp(_minStrength, _maxStrength, Mathf.Clamp01(strengthNoise));
        
        Quaternion rotation = Quaternion.Euler(0f, angle, 0f);
        Wind = rotation * Vector3.forward *  strength;
        
        _windZone.transform.rotation = rotation;
        _windZone.windMain = strength * _treeSwayScale;
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
        if (Instance != this) return;

        Instance = null;
    }
}
