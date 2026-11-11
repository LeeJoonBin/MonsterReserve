using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 메뉴 1 : Terrain과 구역 부모 오브젝트를 만들고, 성능 기준 설정을 적용한다.
// 안개는 메뉴 10 (MapAtmosphereSetup)이 맡는다.
// 여러 번 실행해도 Terrain, 구역 부모, BaseMarker가 중복 생성되지 않는다.
public static class MapTerrainSetup
{
    public const string TerrainDataPath = "Assets/Settings/World/GameTerrain.asset";
    public const float TerrainHeight = 400f;
    public const float BaseHeight = 30f;

    // 중앙 지역 (거점이 있는 구역)
    public const string CenterRegionName = "Center";
    public const string CenterFromCell = "B2";
    public const string CenterToCell = "B2";

    // 지면 레이어 번호. TerrainLayerPaths의 순서와 반드시 같아야 한다.
    public const int GrassLayer = 0;
    public const int ForestGroundLayer = 1;
    public const int DirtLayer = 2;
    public const int StoneLayer = 3;
    public const int ShoreLayer = 4;
    // 데모 NewLayer (T_DirtRock_C). 북쪽 데모 지면 이식에서 데모가 칠한 그대로 쓴다
    public const int DirtRockLayer = 5;

    // 높이맵 간격 약 1.2m (2500m / 2048)
    private const int HeightmapResolution = 2049;
    private const int AlphamapResolution = 1024;
    private const int BaseMapResolution = 1024;
    // 디테일 칸 약 1.95m. 이전 맵 (2000m / 1024)과 1m²당 바닥 식생 밀도를 비슷하게 유지하고, DetailResolutionPerPatch로 나누어떨어지게 한다
    private const int DetailResolution = 1280;
    private const int DetailResolutionPerPatch = 32;

    // 성능 기준 (Docs/World/MapLayout.md "## 성능 기준"). 값을 바꿀 때는 이 표만 고친다.
    private const float TreeDistance = 1000f;
    private const float TreeBillboardDistance = 150f;
    private const float DetailObjectDistance = 80f;
    // 임시값. 5에서는 먼 지형이 거칠게 그려져 얕은 강바닥(약 1m)이 뭉개지고 지형이 강 수면을 가렸다.
    // 낮출수록 먼 지형이 정확해지는 대신 삼각형 수가 늘어나므로 성능 측정 후 다시 정한다 (필요하면 1).
    private const float HeightmapPixelError = 2f;
    private const float BasemapDistance = 400f;
    private const bool IsDrawInstanced = true;

    private const string BaseMarkerName = "BaseMarker";
    private const string TerrainLayerFolder = "Assets/Imports/Polyart/PolyartStudio/DreamscapeMountains/Scenes/";
    // 데모 지면 이식(메뉴 9-2)에 필요한 데모 레이어. 다른 레이어와 폴더가 달라 경로를 따로 둔다
    private const string DirtRockLayerPath = "Assets/Imports/Polyart/PolyartStudio/DreamscapeMountains/Textures/Detail/NewLayer.terrainlayer";

    // 순서가 곧 레이어 번호다. 새 레이어는 맨 뒤에만 추가한다 (앞 번호가 바뀌면 칠해 둔 지면이 어긋난다)
    private static readonly string[] TerrainLayerPaths =
    {
        TerrainLayerFolder + "Layer_Grass.terrainlayer",
        TerrainLayerFolder + "Layer_ForestGround.terrainlayer",
        TerrainLayerFolder + "Layer_Dirt.terrainlayer",
        TerrainLayerFolder + "Layer_Stone.terrainlayer",
        TerrainLayerFolder + "Layer_Shore.terrainlayer",
        DirtRockLayerPath
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
        CreateRegionParents(terrain);

        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(terrain);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log($"[MapTerrainSetup] 완료. 새 TerrainData: {isNew}, 해상도 변경: {isResolutionChanged}");
        LogGrid(terrain);
    }

    // 바탕 높이를 바꾼 뒤 맵 전체 높이맵을 다시 맞출 때 쓴다. 지면 레이어, 디테일, 나무는 건드리지 않는다.
    [MenuItem("Tools/Map/1-1. 맵 전체 바탕 높이로 평탄화")]
    private static void RunFlatten()
    {
        Terrain terrain = FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        FlattenHeights(data);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        Debug.Log($"[MapTerrainSetup] 맵 전체를 바탕 높이 {BaseHeight}m로 평탄화했습니다.");
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
        TerrainLayer[] layers = TerrainLayerPaths
            .Select(path => AssetDatabase.LoadAssetAtPath<TerrainLayer>(path))
            .ToArray();
        if (data.terrainLayers.SequenceEqual(layers)) return false;

        data.terrainLayers = layers;
        return true;
    }

    // 지면 전체 초기화(메뉴 9-1)도 쓴다
    public static void FillWithGrass(TerrainData data)
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

    private static void CreateRegionParents(Terrain terrain)
    {
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (string regionName in RegionNames)
        {
            GameObject region = roots.FirstOrDefault(root => root.name == regionName);
            if (region == null) region = new GameObject(regionName);
            if (regionName == CenterRegionName) PlaceBaseMarker(region.transform, terrain);
        }
    }

    // 거점 위치 표시. 건물과 기능 오브젝트는 사용자가 이 주변에 직접 놓는다.
    // 이미 있으면 새로 만들지 않고, 바탕 높이나 지형이 바뀌어도 땅에 붙어 있도록 위치만 원점의 지면 높이로 맞춘다.
    private static void PlaceBaseMarker(Transform center, Terrain terrain)
    {
        Transform marker = center.Find(BaseMarkerName);
        if (marker == null)
        {
            marker = new GameObject(BaseMarkerName).transform;
            marker.SetParent(center, false);
        }

        // SampleHeight는 Terrain 기준 높이라 Terrain의 y를 더한다
        float groundHeight = terrain.SampleHeight(Vector3.zero) + terrain.transform.position.y;
        marker.position = new Vector3(0f, groundHeight, 0f);
    }

    // 격자 변경 확인용 (MapLayout.md "좌표 규칙"과 대조). 메뉴 1을 실행할 때마다 확인할 값이라 남겨 둔다.
    private static void LogGrid(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        Rect center = MapGrid.GetRangeBounds(CenterFromCell, CenterToCell);
        Debug.Log($"[MapTerrainSetup] 맵 크기 {data.size.x} × {data.size.z}m, 높이 한도 {data.size.y}m, Terrain 위치 {terrain.transform.position}");
        Debug.Log($"[MapTerrainSetup] 해상도 높이맵 {data.heightmapResolution}, 알파맵 {data.alphamapResolution}, " +
                  $"베이스맵 {data.baseMapResolution}, 디테일 {data.detailResolution} (패치 {data.detailResolutionPerPatch})");
        Debug.Log($"[MapTerrainSetup] 중앙 범위 ({CenterFromCell}~{CenterToCell}) x {center.xMin} ~ {center.xMax}, z {center.yMin} ~ {center.yMax}, " +
                  $"BaseMarker 높이 {terrain.SampleHeight(Vector3.zero) + terrain.transform.position.y:F2}m");
    }
}
