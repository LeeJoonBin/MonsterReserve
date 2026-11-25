using UnityEngine;
using UnityEngine.PlayerLoop;

public class WindManager : MonoBehaviour
{
    [SerializeField] private float _changeSpeed = 0.03f;
    private WindZone windZone;
    private float seed;
    private float seed2;
    
    public static WindManager Instance { get; private set; }
    public Vector3 Wind { get; private set; }
    
    private void Awake()
    {
        RegisterInstance();
        windZone = GetComponent<WindZone>();
        seed = Random.Range(0, 1000);
        seed2 = Random.Range(0, 1000);
    }
    
    private void SetWind()
    {
        Mathf.PerlinNoise(Time.time * windZone.windMain, seed);
        
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

    
}
