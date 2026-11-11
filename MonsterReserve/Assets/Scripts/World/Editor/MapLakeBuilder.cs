using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// 메뉴 6 : 북쪽에 이식한 데모 지형(메뉴 5-1)의 데모 호수 자리에 호수 수면을 놓는다. 호수 중심과 수면 높이는 MapDemoTransplant.GetLakeCenter가 정한다.
// 수면을 지형 아래까지 넓게 깔아 지형이 수면보다 낮은 곳만 물로 보이게 한다. 그래서 물가 모양은 지형 굴곡이 정한다.
// 큰 사각 평면을 깔면 남쪽 둑 바깥(내리막)이나 다른 낮은 땅 위에 물이 떠 보이므로, 호수의 물 칸에서 이어진
// "지형 높이 < 수면 + 묻힘 깊이" 영역만 높이맵 격자에서 찾아(flood fill) 그 격자 칸들로 메시를 만든다.
// 호수 prefab과 수면 메시를 만들거나 갱신하고, 씬 루트 North 아래 Lake의 자식을 지우고 다시 놓는다.
// 메뉴 5-1로 지형을 다시 만들었다면 이 메뉴도 다시 실행한다. 강(메뉴 7)이 둑을 판 뒤에 실행하면 물이 강을 따라 새므로 메뉴 5-1부터 다시 실행한다.
public static class MapLakeBuilder
{
    // 수면 메시 끝이 지형 속에 묻히는 최소 깊이. 멀리서 Terrain이 높이맵을 단순하게 그려(heightmapPixelError)
    // 지형이 조금 낮아 보여도 메시 끝이 드러나지 않게 한다
    private const float ShoreBuryDepth = 0.5f;
    // 물을 찾는 범위의 반 변 = 데모 물 평면 반폭(데모에서 물이 보일 수 있는 범위) + 여유 (임시값).
    // 물이 이 범위 끝에 닿으면 둘레가 막지 못하고 새는 것이므로 메시를 만들지 않는다.
    // 범위 안 꼭짓점이 약 570 × 570개라 메시 정점이 65535개를 넘을 수 있어, 넘으면 32비트 인덱스를 쓴다
    private const float SearchMargin = 40f;
    private const float SearchHalfSize = MapDemoTransplant.DemoLakeHalfSize + SearchMargin;
    // UV 한 칸의 월드 크기. SM_WaterPlane(20m에 UV 0 ~ 1)과 같은 밀도.
    // 물 셰이더는 월드 좌표로 물결을 그리지만, 노멀맵에 쓰는 탄젠트를 UV로 계산한다
    private const float UvTileSize = 20f;

    private const string RegionName = "North";
    private const string LakeParentName = "Lake";
    private const string LakeObjectName = "Lake";

    private const string PrefabRootFolder = "Assets/Prefabs";
    private const string PrefabFolderName = "World";
    private const string PrefabFolder = PrefabRootFolder + "/" + PrefabFolderName;
    private const string LakePrefabPath = PrefabFolder + "/Lake.prefab";
    private const string WaterMeshPath = PrefabFolder + "/LakeWaterMesh.asset";
    private const string WaterMaterialPath = "Assets/Imports/Polyart/PolyartStudio/DreamscapeMountains/Materials/Terrain/MI_Water_MountainsLake.mat";

    // 물 셰이더는 DOTS 인스턴싱을 지원하지 않아 GPU Resident Drawer에서 빼야 한다.
    // DisallowGPUDrivenRendering은 패키지 안의 internal 클래스라 코드에서 이름으로 찾아 붙인다
    private const string DisallowGpuDrivenTypeName = "UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime";

    // 호수 중심 둘레의 높이맵 꼭짓점들
    private struct WaterGrid
    {
        // [0, 0] 꼭짓점의 월드 XZ
        public Vector2 Origin;
        public float Spacing;
        // 월드 높이 [z, x]
        public float[,] Heights;
        // 씨앗 꼭짓점에서 이어진 "지형 < 수면 + 묻힘 깊이" 꼭짓점
        public bool[,] IsFlooded;
        // flood fill을 시작한 물 칸 꼭짓점 (x, z 번호)
        public Vector2Int Seed;
    }

    [MenuItem("Tools/Map/6. 호수 만들기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        GameObject northRoot = terrain.gameObject.scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == RegionName);
        if (northRoot == null)
        {
            Debug.LogError("씬 루트에 North가 없습니다. 먼저 'Tools/Map/1. 지형과 구역 부모 생성'을 실행하세요.");
            return;
        }

        System.Type disallowType = System.Type.GetType(DisallowGpuDrivenTypeName);
        if (disallowType == null)
        {
            Debug.LogError($"{DisallowGpuDrivenTypeName} 타입을 찾지 못했습니다. URP 패키지 버전이 바뀌었는지 확인하세요.");
            return;
        }

        // y는 수면 높이
        Vector3 lakeCenter = MapDemoTransplant.GetLakeCenter(terrain);
        WaterGrid grid = ReadGrid(terrain, lakeCenter);
        if (!TryFindSeed(ref grid, lakeCenter)) return;
        if (!FloodWater(grid, lakeCenter)) return;

        CreatePrefabFolder();
        Mesh waterMesh = SaveWaterMesh(grid, lakeCenter);
        GameObject lakePrefab = SaveLakePrefab(waterMesh, disallowType);
        AssetDatabase.SaveAssets();

        // prefab 연결을 유지한 채 놓는다. Static 플래그는 켜지 않는다
        Transform lakeParent = ResetParent(northRoot.transform, LakeParentName);
        var lake = (GameObject)PrefabUtility.InstantiatePrefab(lakePrefab, lakeParent);
        lake.transform.position = lakeCenter;

        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        LogResult(grid, lakeCenter, waterMesh);
    }

    private static WaterGrid ReadGrid(Terrain terrain, Vector3 lakeCenter)
    {
        TerrainData data = terrain.terrainData;
        int divisions = data.heightmapResolution - 1;
        float spacing = data.size.x / divisions;
        var searchRect = new Rect(lakeCenter.x - SearchHalfSize, lakeCenter.z - SearchHalfSize, SearchHalfSize * 2f, SearchHalfSize * 2f);
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, searchRect, divisions);

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
            IsFlooded = new bool[range.height + 1, range.width + 1]
        };
    }

    // 데모 호수 중심 아래가 물 밖일 수 있어, 데모 물 평면 안에서 지형이 수면보다 낮은 꼭짓점 중 중심에 가장 가까운 것을 씨앗으로 고른다.
    // 중심이 물 안이면 중심 꼭짓점이 그대로 씨앗이 된다
    private static bool TryFindSeed(ref WaterGrid grid, Vector3 lakeCenter)
    {
        var center = new Vector2(lakeCenter.x, lakeCenter.z);
        float bestDistance = float.MaxValue;
        bool isFound = false;
        for (int z = 0; z < grid.Heights.GetLength(0); z++)
        {
            for (int x = 0; x < grid.Heights.GetLength(1); x++)
            {
                Vector2 world = GetVertexWorld(grid, x, z);
                if (!IsInsideDemoPlane(world, center) || grid.Heights[z, x] >= lakeCenter.y) continue;

                float distance = Vector2.Distance(world, center);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                grid.Seed = new Vector2Int(x, z);
                isFound = true;
            }
        }

        if (!isFound)
            Debug.LogError("데모 물 평면 안에 수면보다 낮은 지형이 없습니다. 먼저 'Tools/Map/5-1. 북쪽 지형 (데모 이식)'을 실행하세요.");
        return isFound;
    }

    private static Vector2 GetVertexWorld(WaterGrid grid, int x, int z)
    {
        return grid.Origin + new Vector2(x, z) * grid.Spacing;
    }

    // 데모에서 물이 보이는 사각형(물 평면) 안인지. 회전은 180도라 사각형이 축에 맞춰 그대로 남는다
    private static bool IsInsideDemoPlane(Vector2 world, Vector2 center)
    {
        Vector2 offset = world - center;
        return Mathf.Abs(offset.x) <= MapDemoTransplant.DemoLakeHalfSize && Mathf.Abs(offset.y) <= MapDemoTransplant.DemoLakeHalfSize;
    }

    // 씨앗 꼭짓점에서 시작해 8방향 이웃으로 "지형 < 수면 + 묻힘 깊이"인 꼭짓점을 채운다.
    // 8방향으로 이어야 메시 격자 칸의 꼭짓점이 모두 "채워짐" 또는 "수면 + 묻힘 깊이 이상" 중 하나가 된다
    // (대각선으로만 닿은 낮은 꼭짓점이 메시 끝에 남으면 그곳 물가가 떠 보인다)
    private static bool FloodWater(WaterGrid grid, Vector3 lakeCenter)
    {
        int depth = grid.Heights.GetLength(0);
        int width = grid.Heights.GetLength(1);
        float threshold = lakeCenter.y + ShoreBuryDepth;
        var queue = new Queue<Vector2Int>();
        grid.IsFlooded[grid.Seed.y, grid.Seed.x] = true;
        queue.Enqueue(grid.Seed);
        while (queue.Count > 0)
        {
            Vector2Int vertex = queue.Dequeue();
            if (vertex.x == 0 || vertex.y == 0 || vertex.x == width - 1 || vertex.y == depth - 1)
            {
                Vector2 leak = GetVertexWorld(grid, vertex.x, vertex.y);
                Debug.LogError($"물이 호수 중심에서 {SearchHalfSize:F0}m 범위 끝 ({leak.x:F0}, {leak.y:F0})까지 이어집니다 (둘레가 막지 못함). " +
                               "강(메뉴 7)이 둑을 판 뒤 실행했다면 메뉴 1-1 → 2 → 5-1 → 6 순서로 다시 실행하세요. 메시를 만들지 않았습니다.");
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
                    queue.Enqueue(new Vector2Int(x, z));
                }
            }
        }
        return true;
    }

    private static void CreatePrefabFolder()
    {
        if (!AssetDatabase.IsValidFolder(PrefabRootFolder)) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder(PrefabRootFolder, PrefabFolderName);
    }

    // 이미 있으면 같은 에셋을 다시 채워, prefab이 가리키는 메시 참조가 끊기지 않게 한다
    private static Mesh SaveWaterMesh(WaterGrid grid, Vector3 lakeCenter)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(WaterMeshPath);
        if (mesh == null)
        {
            mesh = new Mesh();
            AssetDatabase.CreateAsset(mesh, WaterMeshPath);
        }
        BuildWaterMesh(mesh, grid, lakeCenter);
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // 채워진 꼭짓점을 하나라도 가진 격자 칸마다 사각형(삼각형 2개)을 만든다.
    // 메시 끝 꼭짓점은 모두 채워지지 않은 꼭짓점(지형이 수면 + 묻힘 깊이 이상)이고, 칸 경계가 Terrain 격자선과 같아 그 사이도 지형 속에 묻힌다.
    // 메시 원점은 호수 중심의 수면이고 회전 없이 놓으므로 메시 x, z가 월드 동, 북과 같다
    private static void BuildWaterMesh(Mesh mesh, WaterGrid grid, Vector3 lakeCenter)
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

            vertices.Add(new Vector3(grid.Origin.x + x * grid.Spacing - lakeCenter.x, 0f, grid.Origin.y + z * grid.Spacing - lakeCenter.z));
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
        // 데모 호수는 커서 정점이 16비트 인덱스 한도를 넘을 수 있다. 정점을 넣기 전에 인덱스 형식을 정해야 한다
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
    // 물에 들어가는 동작은 기획에 없으므로 충돌체는 붙이지 않는다
    private static GameObject SaveLakePrefab(Mesh waterMesh, System.Type disallowType)
    {
        var lake = new GameObject(LakeObjectName);
        lake.AddComponent<MeshFilter>().sharedMesh = waterMesh;
        var renderer = lake.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        // 투명한 수면이 바닥에 그림자를 드리우지 않게 한다
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        lake.AddComponent(disallowType);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(lake, LakePrefabPath);
        Object.DestroyImmediate(lake);
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

    // 검수(호수 크기와 깊이, 메시 크기, 물이 떠 보이는 곳이 없는지)에 쓰는 값이라 로그를 남긴다.
    // 보이는 물 = 채워진 꼭짓점 중 지형이 수면보다 낮은 곳. 꼭짓점 하나를 격자 칸 하나 넓이로 센다
    private static void LogResult(WaterGrid grid, Vector3 lakeCenter, Mesh mesh)
    {
        int depth = grid.Heights.GetLength(0);
        int width = grid.Heights.GetLength(1);
        var center = new Vector2(lakeCenter.x, lakeCenter.z);
        int waterCount = 0;
        // 데모 물 평면 안에서 수면보다 낮지만 씨앗과 이어지지 않아 메시에서 빠진 꼭짓점 (데모에서는 물로 보이던 웅덩이)
        int detachedCount = 0;
        float lowest = float.MaxValue;
        var waterMin = new Vector2Int(width, depth);
        var waterMax = new Vector2Int(-1, -1);
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!grid.IsFlooded[z, x])
                {
                    if (grid.Heights[z, x] < lakeCenter.y && IsInsideDemoPlane(GetVertexWorld(grid, x, z), center)) detachedCount++;
                    continue;
                }

                lowest = Mathf.Min(lowest, grid.Heights[z, x]);
                if (grid.Heights[z, x] >= lakeCenter.y) continue;

                waterCount++;
                waterMin = Vector2Int.Min(waterMin, new Vector2Int(x, z));
                waterMax = Vector2Int.Max(waterMax, new Vector2Int(x, z));
            }
        }

        float area = waterCount * grid.Spacing * grid.Spacing;
        float diameter = 2f * Mathf.Sqrt(area / Mathf.PI);
        Vector2 extent = (Vector2)(waterMax - waterMin + Vector2Int.one) * grid.Spacing;
        Vector2 seed = GetVertexWorld(grid, grid.Seed.x, grid.Seed.y);
        Vector2 worldMin = GetVertexWorld(grid, waterMin.x, waterMin.y);
        Vector2 worldMax = GetVertexWorld(grid, waterMax.x, waterMax.y);
        Debug.Log($"[MapLakeBuilder] 호수 중심 ({lakeCenter.x:F1}, {lakeCenter.z:F1}), 수면 높이 {lakeCenter.y:F2}m, 최대 깊이 {lakeCenter.y - lowest:F2}m, " +
                  $"씨앗 꼭짓점 ({seed.x:F1}, {seed.y:F1}) 지형 {grid.Heights[grid.Seed.y, grid.Seed.x]:F2}m");
        Debug.Log($"[MapLakeBuilder] 수면 면적 {area:F0}m² (등가 지름 {diameter:F1}m), 동서 {extent.x:F0}m × 남북 {extent.y:F0}m " +
                  $"(x {worldMin.x:F0} ~ {worldMax.x:F0}, z {worldMin.y:F0} ~ {worldMax.y:F0}), 물을 찾은 범위 반 변 {SearchHalfSize:F0}m");
        Debug.Log($"[MapLakeBuilder] 데모 물 평면 안에서 호수와 이어지지 않아 빠진 물 꼭짓점 {detachedCount}개 (0이 아니면 떨어진 웅덩이가 있다)");
        Debug.Log($"[MapLakeBuilder] 수면 메시 정점 {mesh.vertexCount}, 삼각형 {mesh.GetIndexCount(0) / 3}, 인덱스 형식 {mesh.indexFormat}");
        Debug.Log($"[MapLakeBuilder] 수면 메시 가장자리의 (지형 높이 - 수면 높이) 최소 {GetMinEdgeGap(grid, lakeCenter.y):F2}m " +
                  $"(묻힘 깊이 {ShoreBuryDepth}m 이상이어야 한다. 음수면 물이 떠 보이는 곳이 있다)");
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
}
