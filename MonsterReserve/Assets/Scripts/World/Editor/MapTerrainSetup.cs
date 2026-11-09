using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 메뉴 1 : Terrain과 구역 부모 오브젝트를 만들고, 성능 기준 설정과 안개를 적용한다.
// 여러 번 실행해도 Terrain, 구역 부모, BaseMarker가 중복 생성되지 않는다.
public static class MapTerrainSetup
{
    public const string TerrainDataPath = "Assets/Settings/World/GameTerrain.asset";
    public const float TerrainHeight = 300f;
    public const float BaseHeight = 50f;

    // 중앙 지역 (거점이 있는 구역)
    public const string CenterRegionName = "Center";
    public const string CenterFromCell = "B2";
    public const string CenterToCell = "C3";

    // 지면 레이어 번호. TerrainLayerPaths의 순서와 반드시 같아야 한다.
    public const int GrassLayer = 0;
    public const int ForestGroundLayer = 1;
    public const int DirtLayer = 2;
    public const int StoneLayer = 3;

    private const int HeightmapResolution = 1025;
    private const int AlphamapResolution = 1024;
    private const int BaseMapResolution = 1024;
    private const int DetailResolution = 1024;
    private const int DetailResolutionPerPatch = 32;

    // 성능 기준 (Docs/World/MapLayout.md "## 성능 기준"). 값을 바꿀 때는 이 표만 고친다.
    private const float TreeDistance = 1000f;
    private const float TreeBillboardDistance = 150f;
    private const float DetailObjectDistance = 80f;
    private const float HeightmapPixelError = 5f;
    private const float BasemapDistance = 400f;
    private const bool IsDrawInstanced = true;

    // 먼 곳을 가리는 안개. 끝 거리를 나무 그리는 거리와 맞춰 끊김이 보이지 않게 한다.
    private const float FogStartDistance = 300f;
    private const float FogEndDistance = 1000f;
    private static readonly Color FogColor = new Color(0.70f, 0.78f, 0.85f);

    private const string BaseMarkerName = "BaseMarker";
    private const string TerrainLayerFolder = "Assets/Imports/Polyart/PolyartStudio/DreamscapeMountains/Scenes/";

    private static readonly string[] TerrainLayerNames =
    {
        "Layer_Grass", "Layer_ForestGround", "Layer_Dirt", "Layer_Stone", "Layer_Shore"
    };

    // 구역 부모 오브젝트 이름 (Hierarchy 루트)
    private static readonly string[] RegionNames =
    {
        CenterRegionName, "North", "NorthEast", "East", "SouthEast", "South", "SouthWest", "West", "NorthWest"
    };

    [MenuItem("Tools/Map/1. 지형과 구역 부모 생성")]
    private static void Run()
    {
        TerrainData data = LoadOrCreateTerrainData(out bool isNew);
        bool isResolutionChanged = ApplyResolution(data);
        if (isNew || isResolutionChanged) FlattenHeights(data);
        if (ApplyTerrainLayers(data)) FillWithGrass(data);

        Terrain terrain = FindOrCreateTerrain(data);
        ApplyPerformanceSettings(terrain);
        CreateRegionParents();
        ApplyFog();

        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(terrain);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log($"[MapTerrainSetup] 완료. 새 TerrainData: {isNew}, 해상도 변경: {isResolutionChanged}");
    }

    public static Terrain FindSceneTerrain()
    {
        Terrain terrain = Object.FindAnyObjectByType<Terrain>();
        if (terrain == null) Debug.LogError("씬에 Terrain이 없습니다. 먼저 'Tools/Map/1. 지형과 구역 부모 생성'을 실행하세요.");
        return terrain;
    }

    // 월드 XZ 영역을 Terrain 격자 번호 범위로 바꾼다.
    // divisions : 알파맵/디테일은 해상도 그대로, 높이맵은 (해상도 - 1)을 넣는다 (높이맵은 꼭짓점 기준이라).
    public static RectInt ToIndexRange(Terrain terrain, Rect worldRect, int divisions)
    {
        Vector3 position = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        int xStart = Mathf.RoundToInt((worldRect.xMin - position.x) / size.x * divisions);
        int xEnd = Mathf.RoundToInt((worldRect.xMax - position.x) / size.x * divisions);
        int zStart = Mathf.RoundToInt((worldRect.yMin - position.z) / size.z * divisions);
        int zEnd = Mathf.RoundToInt((worldRect.yMax - position.z) / size.z * divisions);
        return new RectInt(xStart, zStart, xEnd - xStart, zEnd - zStart);
    }

    private static TerrainData LoadOrCreateTerrainData(out bool isNew)
    {
        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        isNew = data == null;
        if (!isNew) return data;

        data = new TerrainData();
        AssetDatabase.CreateAsset(data, TerrainDataPath);
        return data;
    }

    // 해상도는 바뀐 경우에만 다시 넣는다. 같은 값을 다시 넣어도 기존 데이터가 지워질 수 있어서다.
    // 높이맵 해상도를 바꾸면 크기가 다시 계산되므로 크기는 해상도 다음에 넣는다.
    private static bool ApplyResolution(TerrainData data)
    {
        bool isHeightmapChanged = data.heightmapResolution != HeightmapResolution;
        if (isHeightmapChanged) data.heightmapResolution = HeightmapResolution;
        data.size = new Vector3(MapGrid.MapSize, TerrainHeight, MapGrid.MapSize);

        if (data.alphamapResolution != AlphamapResolution) data.alphamapResolution = AlphamapResolution;
        if (data.baseMapResolution != BaseMapResolution) data.baseMapResolution = BaseMapResolution;
        if (data.detailResolution != DetailResolution || data.detailResolutionPerPatch != DetailResolutionPerPatch)
            data.SetDetailResolution(DetailResolution, DetailResolutionPerPatch);

        return isHeightmapChanged;
    }

    private static void FlattenHeights(TerrainData data)
    {
        int resolution = data.heightmapResolution;
        float normalizedHeight = BaseHeight / TerrainHeight;
        var heights = new float[resolution, resolution];
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
                heights[z, x] = normalizedHeight;
        }
        data.SetHeights(0, 0, heights);
    }

    // 레이어 구성이 이미 같으면 칠해 둔 지면을 지키기 위해 그대로 둔다. 새로 넣었으면 true.
    private static bool ApplyTerrainLayers(TerrainData data)
    {
        TerrainLayer[] layers = TerrainLayerNames
            .Select(name => AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainLayerFolder + name + ".terrainlayer"))
            .ToArray();
        if (data.terrainLayers.SequenceEqual(layers)) return false;

        data.terrainLayers = layers;
        return true;
    }

    private static void FillWithGrass(TerrainData data)
    {
        int resolution = data.alphamapResolution;
        var weights = new float[resolution, resolution, data.alphamapLayers];
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
                weights[z, x, GrassLayer] = 1f;
        }
        data.SetAlphamaps(0, 0, weights);
    }

    private static Terrain FindOrCreateTerrain(TerrainData data)
    {
        Terrain terrain = Object.FindAnyObjectByType<Terrain>();
        if (terrain == null)
        {
            terrain = Terrain.CreateTerrainGameObject(data).GetComponent<Terrain>();
        }
        else
        {
            terrain.terrainData = data;
            terrain.GetComponent<TerrainCollider>().terrainData = data;
        }

        // 맵 중심이 월드 원점(거점)이 되도록 Terrain 모서리를 옮긴다
        float half = MapGrid.MapSize / 2f;
        terrain.transform.position = new Vector3(-half, 0f, -half);
        return terrain;
    }

    private static void ApplyPerformanceSettings(Terrain terrain)
    {
        terrain.treeDistance = TreeDistance;
        terrain.treeBillboardDistance = TreeBillboardDistance;
        terrain.detailObjectDistance = DetailObjectDistance;
        terrain.heightmapPixelError = HeightmapPixelError;
        terrain.basemapDistance = BasemapDistance;
        terrain.drawInstanced = IsDrawInstanced;
    }

    private static void CreateRegionParents()
    {
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (string regionName in RegionNames)
        {
            GameObject region = roots.FirstOrDefault(root => root.name == regionName);
            if (region == null) region = new GameObject(regionName);
            if (regionName == CenterRegionName) CreateBaseMarker(region.transform);
        }
    }

    // 거점 위치 표시. 건물과 기능 오브젝트는 사용자가 이 주변에 직접 놓는다.
    private static void CreateBaseMarker(Transform center)
    {
        if (center.Find(BaseMarkerName) != null) return;

        var marker = new GameObject(BaseMarkerName);
        marker.transform.SetParent(center, false);
        marker.transform.position = new Vector3(0f, BaseHeight, 0f);
    }

    private static void ApplyFog()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = FogStartDistance;
        RenderSettings.fogEndDistance = FogEndDistance;
        RenderSettings.fogColor = FogColor;
    }
}
