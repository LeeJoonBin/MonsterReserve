using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 현재 열린 씬에 바탕 지형 (Terrain)과 섹션 부모 오브젝트 9개를 만든다. 다시 실행해도 이미 있는 것은 건너뛴다.
public static class MapBaseSetup
{
    private const string MenuPath = "Tools/Map/바탕 지형과 섹션 생성";
    private const string TerrainFolderParent = "Assets/Settings";
    private const string TerrainFolderName = "World";
    private const string TerrainFolderPath = TerrainFolderParent + "/" + TerrainFolderName;
    private const string TerrainDataPath = TerrainFolderPath + "/GameTerrain.asset";
    private const string TerrainObjectName = "Terrain";
    private const int HeightmapResolution = 2049;
    private const float MaxHeight = 300f;
    private const float BaseHeight = 50f;

    // 섹션 이름과 범위는 Docs/World/MapLayout.md의 "섹션" 표를 따른다.
    private static readonly string[] SectionNames = { "중앙", "북", "남", "동", "서", "북서", "북동", "남서", "남동" };

    [MenuItem(MenuPath)]
    private static void CreateBaseAndSections()
    {
        Scene scene = SceneManager.GetActiveScene();
        string[] rootNames = scene.GetRootGameObjects().Select(root => root.name).ToArray();

        bool isTerrainCreated = TryCreateTerrain(rootNames);
        int createdSectionCount = CreateSections(rootNames);

        if (isTerrainCreated || createdSectionCount > 0)
            EditorSceneManager.MarkSceneDirty(scene);

        // 메뉴 실행 결과를 확인하는 용도라 남겨 둔다.
        Debug.Log($"[MapBaseSetup] 씬 '{scene.name}' : Terrain {(isTerrainCreated ? "생성" : "건너뜀 (이미 있음)")}, 섹션 {createdSectionCount}개 생성 / {SectionNames.Length - createdSectionCount}개 건너뜀");
    }

    private static bool TryCreateTerrain(string[] rootNames)
    {
        if (rootNames.Contains(TerrainObjectName)) return false;

        TerrainData terrainData = LoadOrCreateTerrainData();
        GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
        terrainObject.name = TerrainObjectName;

        // 지형 정중앙이 월드 원점 (거점 중심)에 오도록 맵 크기의 절반만큼 옮긴다.
        float halfMapSize = MapGrid.MapSize / 2f;
        terrainObject.transform.position = new Vector3(-halfMapSize, 0f, -halfMapSize);

        Undo.RegisterCreatedObjectUndo(terrainObject, "바탕 지형 생성");
        return true;
    }

    private static TerrainData LoadOrCreateTerrainData()
    {
        // 이미 있는 에셋은 사용자가 다듬었을 수 있으므로 덮어쓰지 않는다.
        TerrainData terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (terrainData != null) return terrainData;

        if (!AssetDatabase.IsValidFolder(TerrainFolderPath))
            AssetDatabase.CreateFolder(TerrainFolderParent, TerrainFolderName);

        // 해상도를 바꾸면 크기가 다시 계산되므로 해상도를 먼저 정한다.
        terrainData = new TerrainData();
        terrainData.heightmapResolution = HeightmapResolution;
        terrainData.size = new Vector3(MapGrid.MapSize, MaxHeight, MapGrid.MapSize);
        FlattenHeights(terrainData);

        AssetDatabase.CreateAsset(terrainData, TerrainDataPath);
        return terrainData;
    }

    // 높이 값은 0~1로 정규화되므로 바닥 높이를 최대 높이로 나눈 값으로 채운다.
    private static void FlattenHeights(TerrainData terrainData)
    {
        int resolution = terrainData.heightmapResolution;
        float normalizedHeight = BaseHeight / MaxHeight;
        float[,] heights = new float[resolution, resolution];

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
                heights[y, x] = normalizedHeight;
        }

        terrainData.SetHeights(0, 0, heights);
    }

    private static int CreateSections(string[] rootNames)
    {
        int createdCount = 0;

        foreach (string sectionName in SectionNames)
        {
            if (rootNames.Contains(sectionName)) continue;

            GameObject section = new GameObject(sectionName);
            Undo.RegisterCreatedObjectUndo(section, "섹션 생성");
            createdCount++;
        }

        return createdCount;
    }
}
