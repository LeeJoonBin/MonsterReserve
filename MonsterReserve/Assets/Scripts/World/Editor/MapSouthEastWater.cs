using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// 메뉴 21 : 남동쪽 지형(메뉴 20)에서 수면보다 낮은 땅에 수면을 놓는다. 수면 높이와 물 판정 기준은 MapSouthEastShaper가 정한다.
// 호수(메뉴 6)와 같은 방식이다. C3 높이맵 격자 전체를 훑어 "지형 높이 < 수면 + 묻힘 깊이"인 꼭짓점을 만나면 거기서 이어진 영역을 찾고(flood fill),
// 그 격자 칸들로 메시를 만들어 물가 모양은 지형 굴곡이 정하고 메시 끝은 지형 속에 묻힌다.
// 모든 물 덩어리를 메시 하나로 합쳐 그리는 비용을 1로 둔다.
// 어떤 덩어리라도 C3 경계에서 MapSouthEastShaper.EdgeDryDistance(80m) 안에 닿으면 물이 옆 구역으로 새는 것이므로,
// 메시와 prefab을 만들지 않고 오류를 남긴다.
// MapSouthEastShaper.MinWaterArea보다 작은 덩어리가 남아 있으면 메뉴 20의 웅덩이 정리가 실패한 것이므로 경고한다 (메시에는 넣는다).
// prefab과 수면 메시를 만들거나 갱신하고, 씬 루트 SouthEast 아래 Ponds의 자식을 지우고 다시 놓은 뒤 씬을 저장한다.
// 실행 순서 : ... → 19 → 20 → 21 → 22 → 23 → 10. 20을 다시 실행하면 21도 다시 실행한다.
public static class MapSouthEastWater
{
    // 수면 메시 끝이 지형 속에 묻히는 최소 깊이 (메뉴 6과 같은 값). 남동쪽 지형(메뉴 20)이 같은 물 판정 기준(수면 + 이 값)을 쓰므로 공개한다
    public const float ShoreBuryDepth = 0.5f;
    // UV 한 칸의 월드 크기. 메뉴 6과 같은 밀도 (물 셰이더가 노멀맵 탄젠트를 UV로 계산한다)
    private const float UvTileSize = 20f;

    private const string RegionName = "SouthEast";
    private const string PondParentName = "Ponds";
    private const string PondObjectName = "SouthEastPonds";

    private const string PrefabRootFolder = "Assets/Prefabs";
    private const string PrefabFolderName = "World";
    private const string PrefabFolder = PrefabRootFolder + "/" + PrefabFolderName;
    private const string PondPrefabPath = PrefabFolder + "/SouthEastPonds.prefab";
    private const string WaterMeshPath = PrefabFolder + "/SouthEastPondsWaterMesh.asset";
    private const string WaterMaterialPath = "Assets/Imports/Polyart/PolyartStudio/DreamscapeMountains/Materials/Terrain/MI_Water_MountainsLake.mat";

    // 물 셰이더는 DOTS 인스턴싱을 지원하지 않아 GPU Resident Drawer에서 빼야 한다.
    // DisallowGPUDrivenRendering은 패키지 안의 internal 클래스라 코드에서 이름으로 찾아 붙인다
    private const string DisallowGpuDrivenTypeName = "UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime";
    // 셰이더를 찾지 못하거나 컴파일에 실패하면 Unity가 대신 쓰는 분홍 셰이더
    private const string ErrorShaderName = "Hidden/InternalErrorShader";

    // C3 전체의 높이맵 꼭짓점들
    private struct WaterGrid
    {
        // [0, 0] 꼭짓점의 월드 XZ
        public Vector2 Origin;
        public float Spacing;
        // 월드 높이 [z, x]
        public float[,] Heights;
        // "지형 < 수면 + 묻힘 깊이"로 이어진 꼭짓점 (모든 물 덩어리를 합친 것)
        public bool[,] IsFlooded;
        // C3 범위 (경계 띠 판정용)
        public Rect SouthEast;
    }

    // 물 덩어리 하나의 flood fill 결과
    private struct WaterRegion
    {
        // 채운 꼭짓점 수 (지형 < 수면 + 묻힘 깊이)
        public int FloodedCount;
        // 그중 지형이 수면보다 낮은 (물로 보이는) 꼭짓점 수
        public int WaterCount;
    }

    [MenuItem("Tools/Map/21. 남동쪽 연못 수면")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        Scene scene = terrain.gameObject.scene;
        if (scene != SceneManager.GetActiveScene())
        {
            Debug.LogError("[MapSouthEastWater] Game 씬을 활성 씬으로 두고 실행하세요.");
            return;
        }

        GameObject southEastRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == RegionName);
        if (southEastRoot == null)
        {
            Debug.LogError("씬 루트에 SouthEast가 없습니다. 먼저 'Tools/Map/1. 지형과 구역 부모 생성'을 실행하세요.");
            return;
        }

        System.Type disallowType = System.Type.GetType(DisallowGpuDrivenTypeName);
        if (disallowType == null)
        {
            Debug.LogError($"{DisallowGpuDrivenTypeName} 타입을 찾지 못했습니다. URP 패키지 버전이 바뀌었는지 확인하세요.");
            return;
        }

        WaterGrid grid = ReadGrid(terrain);
        List<WaterRegion> regions = FloodAllRegions(grid);
        if (regions == null) return;

        // 메시 원점은 C3 칸 중심의 수면이고 회전 없이 놓는다
        Vector2 cellCenter = MapGrid.GetCellCenter(MapSouthEastShaper.SouthEastCell);
        var pivot = new Vector3(cellCenter.x, MapSouthEastShaper.WaterLevel, cellCenter.y);
        CreatePrefabFolder();
        Mesh waterMesh = SaveWaterMesh(grid, pivot);
        GameObject pondPrefab = SavePondPrefab(waterMesh, disallowType);
        AssetDatabase.SaveAssets();

        // prefab 연결을 유지한 채 놓는다. Static 플래그는 켜지 않는다
        Transform pondParent = ResetParent(southEastRoot.transform, PondParentName);
        var pondObject = (GameObject)PrefabUtility.InstantiatePrefab(pondPrefab, pondParent);
        pondObject.transform.position = pivot;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        LogResult(grid, regions, waterMesh, pondObject.GetComponent<MeshRenderer>());
    }

    private static WaterGrid ReadGrid(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        int divisions = data.heightmapResolution - 1;
        float spacing = data.size.x / divisions;
        Rect southEast = MapGrid.GetRangeBounds(MapSouthEastShaper.SouthEastCell, MapSouthEastShaper.SouthEastCell);
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, southEast, divisions);

        // 높이맵은 꼭짓점 기준이라 끝 줄까지 포함한다
        float[,] normalized = data.GetHeights(range.x, range.y, range.width + 1, range.height + 1);
        Vector3 position = terrain.transform.position;
        var heights = new float[range.height + 1, range.width + 1];
        for (int z = 0; z <= range.height; z++)
        {
            for (int x = 0; x <= range.width; x++)
                heights[z, x] = normalized[z, x] * data.size.y + position.y;
        }

        return new WaterGrid
        {
            Origin = new Vector2(position.x + range.x * spacing, position.z + range.y * spacing),
            Spacing = spacing,
            Heights = heights,
            IsFlooded = new bool[range.height + 1, range.width + 1],
            SouthEast = southEast
        };
    }

    // 격자 전체를 훑어, 아직 채우지 않은 "지형 < 수면 + 묻힘 깊이" 꼭짓점마다 그 덩어리를 채운다.
    // 한 덩어리라도 경계 띠에 닿으면(샘) null을 돌려 메시를 만들지 않게 한다
    private static List<WaterRegion> FloodAllRegions(WaterGrid grid)
    {
        float threshold = MapSouthEastShaper.WaterLevel + ShoreBuryDepth;
        var regions = new List<WaterRegion>();
        for (int z = 0; z < grid.Heights.GetLength(0); z++)
        {
            for (int x = 0; x < grid.Heights.GetLength(1); x++)
            {
                if (grid.IsFlooded[z, x] || grid.Heights[z, x] >= threshold) continue;
                if (!TryFloodRegion(grid, new Vector2Int(x, z), out WaterRegion region)) return null;
                regions.Add(region);
            }
        }
        return regions;
    }

    // 시작 꼭짓점에서 8방향 이웃으로 "지형 < 수면 + 묻힘 깊이"인 꼭짓점을 채운다 (메뉴 6과 같은 이유로 8방향).
    // 경계 띠(C3 경계에서 EdgeDryDistance 안) 꼭짓점을 꺼내면 바로 멈춘다.
    // 격자 끝 줄은 경계 띠 안이라, 띠 밖 꼭짓점의 이웃은 항상 격자 안에 있다
    private static bool TryFloodRegion(WaterGrid grid, Vector2Int seed, out WaterRegion region)
    {
        float threshold = MapSouthEastShaper.WaterLevel + ShoreBuryDepth;
        var visited = new List<Vector2Int> { seed };
        var queue = new Queue<Vector2Int>();
        grid.IsFlooded[seed.y, seed.x] = true;
        queue.Enqueue(seed);
        while (queue.Count > 0)
        {
            Vector2Int vertex = queue.Dequeue();
            Vector2 world = GetVertexWorld(grid, vertex.x, vertex.y);
            if (MapSouthEastShaper.GetEdgeDistance(world, grid.SouthEast) < MapSouthEastShaper.EdgeDryDistance)
            {
                Debug.LogError($"[MapSouthEastWater] 물이 C3 경계 {MapSouthEastShaper.EdgeDryDistance}m 안 ({world.x:F0}, {world.y:F0})까지 이어져(옆 구역으로 샘) " +
                               "수면 메시와 prefab을 만들지 않았습니다. 'Tools/Map/20. 남동쪽 지형 만들기'를 다시 실행한 뒤 이 메뉴를 다시 실행하세요.");
                region = default;
                return false;
            }

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = vertex.x + dx;
                    int z = vertex.y + dz;
                    if (grid.IsFlooded[z, x] || grid.Heights[z, x] >= threshold) continue;

                    grid.IsFlooded[z, x] = true;
                    visited.Add(new Vector2Int(x, z));
                    queue.Enqueue(new Vector2Int(x, z));
                }
            }
        }

        int waterCount = visited.Count(vertex => grid.Heights[vertex.y, vertex.x] < MapSouthEastShaper.WaterLevel);
        region = new WaterRegion { FloodedCount = visited.Count, WaterCount = waterCount };
        return true;
    }

    private static Vector2 GetVertexWorld(WaterGrid grid, int x, int z)
    {
        return grid.Origin + new Vector2(x, z) * grid.Spacing;
    }

    private static void CreatePrefabFolder()
    {
        if (!AssetDatabase.IsValidFolder(PrefabRootFolder)) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder(PrefabRootFolder, PrefabFolderName);
    }

    // 이미 있으면 같은 에셋을 다시 채워, prefab이 가리키는 메시 참조가 끊기지 않게 한다
    private static Mesh SaveWaterMesh(WaterGrid grid, Vector3 pivot)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(WaterMeshPath);
        if (mesh == null)
        {
            mesh = new Mesh();
            AssetDatabase.CreateAsset(mesh, WaterMeshPath);
        }
        BuildWaterMesh(mesh, grid, pivot);
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // 채워진 꼭짓점을 하나라도 가진 격자 칸마다 사각형(삼각형 2개)을 만든다 (메뉴 6과 같은 방식).
    // 메시 끝 꼭짓점은 모두 채워지지 않은 꼭짓점(지형이 수면 + 묻힘 깊이 이상)이라 지형 속에 묻힌다
    private static void BuildWaterMesh(Mesh mesh, WaterGrid grid, Vector3 pivot)
    {
        int depth = grid.Heights.GetLength(0);
        int width = grid.Heights.GetLength(1);
        // 꼭짓점마다 만든 정점 번호 + 1 (0이면 아직 만들지 않음). 이웃 칸이 정점을 함께 쓴다
        var vertexNumbers = new int[depth, width];
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        int GetVertex(int z, int x)
        {
            if (vertexNumbers[z, x] > 0) return vertexNumbers[z, x] - 1;

            vertices.Add(new Vector3(grid.Origin.x + x * grid.Spacing - pivot.x, 0f, grid.Origin.y + z * grid.Spacing - pivot.z));
            vertexNumbers[z, x] = vertices.Count;
            return vertices.Count - 1;
        }

        for (int z = 0; z < depth - 1; z++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                if (!IsWetCell(grid, z, x)) continue;

                int southWest = GetVertex(z, x);
                int southEast = GetVertex(z, x + 1);
                int northWest = GetVertex(z + 1, x);
                int northEast = GetVertex(z + 1, x + 1);
                // 앞면이 위를 향하도록, 위에서 내려다볼 때 시계 방향 순서로 넣는다
                triangles.AddRange(new[] { southWest, northWest, northEast, southWest, northEast, southEast });
            }
        }

        var uvs = vertices.Select(vertex => new Vector2(vertex.x, vertex.z) / UvTileSize).ToList();
        var normals = Enumerable.Repeat(Vector3.up, vertices.Count).ToList();
        mesh.Clear();
        // 물 덩어리를 모두 합치면 정점이 16비트 인덱스 한도를 넘을 수 있다. 정점을 넣기 전에 인덱스 형식을 정해야 한다
        mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
    }

    // [z, x] 꼭짓점을 남서 모서리로 하는 격자 칸에 채워진 꼭짓점이 하나라도 있는지
    private static bool IsWetCell(WaterGrid grid, int z, int x)
    {
        return grid.IsFlooded[z, x] || grid.IsFlooded[z, x + 1] || grid.IsFlooded[z + 1, x] || grid.IsFlooded[z + 1, x + 1];
    }

    // 임시 GameObject로 구성해 prefab으로 저장한다. 같은 경로에 저장하면 GUID가 유지되어 다른 참조가 끊기지 않는다.
    // 물에 들어가는 동작은 기획에 없으므로 충돌체는 붙이지 않는다 (메뉴 6과 같음)
    private static GameObject SavePondPrefab(Mesh waterMesh, System.Type disallowType)
    {
        var ponds = new GameObject(PondObjectName);
        ponds.AddComponent<MeshFilter>().sharedMesh = waterMesh;
        var renderer = ponds.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        // 투명한 수면이 바닥에 그림자를 드리우지 않게 한다
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        ponds.AddComponent(disallowType);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(ponds, PondPrefabPath);
        Object.DestroyImmediate(ponds);
        return prefab;
    }

    // 부모가 없으면 만들고, 있으면 자식을 모두 지운다
    private static Transform ResetParent(Transform root, string name)
    {
        Transform parent = root.Find(name);
        if (parent == null)
        {
            parent = new GameObject(name).transform;
            parent.SetParent(root, false);
        }
        for (int i = parent.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(parent.GetChild(i).gameObject);
        return parent;
    }

    // 검수(물 넓이, 작은 웅덩이가 남지 않았는지, 메시 크기, 물이 떠 보이는 곳이 없는지, 분홍 머티리얼이 없는지)에 쓰는 값이라 로그를 남긴다.
    // 꼭짓점 하나를 격자 칸 하나 넓이로 센다. 물 넓이는 수면보다 낮은 꼭짓점, 작은 웅덩이 판정은 메뉴 20과 같이 채운 꼭짓점 기준이다
    private static void LogResult(WaterGrid grid, List<WaterRegion> regions, Mesh mesh, MeshRenderer renderer)
    {
        float cellArea = grid.Spacing * grid.Spacing;
        float waterArea = regions.Sum(region => region.WaterCount) * cellArea;
        float southEastArea = grid.SouthEast.width * grid.SouthEast.height;
        float largestArea = regions.Select(region => region.WaterCount * cellArea).DefaultIfEmpty(0f).Max();
        int smallCount = regions.Count(region => region.FloodedCount * cellArea < MapSouthEastShaper.MinWaterArea);
        int brokenCount = renderer.sharedMaterials.Count(IsBrokenMaterial);
        Debug.Log($"[MapSouthEastWater] 물 덩어리 {regions.Count}개, 수면 {MapSouthEastShaper.WaterLevel}m, " +
                  $"물 넓이 {waterArea / 10000f:F2}ha (C3의 {waterArea / southEastArea:P1}), 가장 큰 덩어리 {largestArea:F0}m²");
        if (smallCount > 0)
            Debug.LogWarning($"[MapSouthEastWater] {MapSouthEastShaper.MinWaterArea}m² 미만 물 덩어리 {smallCount}개가 남아 있습니다 " +
                             "(메뉴 20의 작은 웅덩이 정리 실패). 메뉴 20을 확인하세요.");
        Debug.Log($"[MapSouthEastWater] 수면 메시 정점 {mesh.vertexCount}, 삼각형 {mesh.GetIndexCount(0) / 3}, 인덱스 형식 {mesh.indexFormat}");
        Debug.Log($"[MapSouthEastWater] 수면 메시 가장자리의 (지형 높이 - 수면 높이) 최소 {GetMinEdgeGap(grid, MapSouthEastShaper.WaterLevel):F2}m " +
                  $"(묻힘 깊이 {ShoreBuryDepth}m 이상이어야 한다. 음수면 물이 떠 보이는 곳이 있다)");
        Debug.Log($"[MapSouthEastWater] 분홍(깨진) 머티리얼 {brokenCount}개 (0이어야 함)");
    }

    // 메시를 이루는 칸의 채워지지 않은 꼭짓점 = 메시 가장자리 꼭짓점
    private static float GetMinEdgeGap(WaterGrid grid, float waterHeight)
    {
        float minGap = float.MaxValue;
        for (int z = 0; z < grid.Heights.GetLength(0) - 1; z++)
        {
            for (int x = 0; x < grid.Heights.GetLength(1) - 1; x++)
            {
                if (!IsWetCell(grid, z, x)) continue;

                for (int dz = 0; dz <= 1; dz++)
                {
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        if (grid.IsFlooded[z + dz, x + dx]) continue;
                        minGap = Mathf.Min(minGap, grid.Heights[z + dz, x + dx] - waterHeight);
                    }
                }
            }
        }
        return minGap;
    }

    // 분홍으로 보이는 머티리얼 (머티리얼 없음, 셰이더 없음, 오류 셰이더, 이 환경에서 지원하지 않는 셰이더)
    private static bool IsBrokenMaterial(Material material)
    {
        if (material == null || material.shader == null) return true;
        return material.shader.name == ErrorShaderName || !material.shader.isSupported;
    }
}
