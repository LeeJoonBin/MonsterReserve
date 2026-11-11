using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 메뉴 3 : 중앙 지역(B2)에 나무와 바위, 소품을 prefab 인스턴스(GameObject)로 흩뿌린다.
// Terrain 나무 기능은 LODGroup prefab을 인스턴싱하지 못하고 자식 MeshCollider도 쓰지 못해 GameObject로 둔다.
// 씬 루트 Center 아래 Trees, Rocks의 자식을 모두 지우고 다시 만든다. seed가 고정이라 여러 번 실행해도 결과가 같다.
// 강(메뉴 7)과 깎인 강둑, 등산로(메뉴 7-1) 위에는 놓지 않으므로 메뉴 7-1 다음에 실행한다.
// 나무, 바위 목록과 비율, 크기, 충돌체 규칙은 북쪽 배치(메뉴 8)도 함께 쓴다.
public static class MapTreeScatter
{
    public enum FieldZone
    {
        Open = 0,
        Sparse = 1,
        Forest = 2
    }

    private enum TreeKind
    {
        Main = 0,
        Dead = 1,
        Giant = 2
    }

    // 한 번 실행하는 동안 함께 쓰는 값과 결과 개수 묶음
    private class ScatterContext
    {
        public Terrain Terrain;
        public System.Random Random;
        public int[] TreeKindCounts = new int[3];
        public int RockCount;
        public int ColliderCount;
        public List<Vector2> RiverPath;
        public int RiverSkipCount;
        // 메뉴 7-1이 저장한 등산로 (없으면 null이고 길 피하기를 하지 않는다)
        public List<List<Vector2>> Trails;
        public int TrailSkipCount;
    }

    // 같은 크기 범위로 같은 부모 아래 놓는 prefab 묶음
    private class InstanceGroup
    {
        public GameObject[] Prefabs;
        public Transform Parent;
        public float ScaleMin;
        public float ScaleMax;
    }

    // 원본 prefab에 충돌체가 없는 나무에 붙일 CapsuleCollider 모양 (루트 기준, 세로 방향)
    private struct CapsuleShape
    {
        public Vector3 Center;
        public float Radius;
        public float Height;
    }

    public const float BaseClearRadius = 40f;
    public const float MaxSlope = 35f;
    // 강 중심선에서 (물 반폭 6m + 이 값) 안에는 놓지 않는다 (임시값). 강둑(물가 밖 2m)과 그 밖의 깎은/메운 비탈 앞부분까지 덮는다.
    // 비탈이 더 길게 이어지는 곳이 남으면 사용자가 에디터에서 보고 늘린다
    public const float RiverClearMargin = 12f;
    // 등산로 중심선에서 (길 폭의 반 2m + 이 값) 안에는 나무, 바위를 놓지 않는다 (임시값). 줄기, 바위가 길 가장자리에 걸치지 않게 한다.
    // 북쪽 데모 나무 이식(메뉴 8-1)도 같은 값을 쓴다
    public const float TrailClearMargin = 3f;

    private const int Seed = 20261109;

    // 분포 마스크 : 노이즈 하위 35% = 개활지, 다음 45% = 드문드문, 상위 20% = 빽빽한 숲
    private const float ZoneNoisePeriod = 250f;
    private const float OpenAreaRatio = 0.35f;
    private const float ForestAreaRatio = 0.20f;
    private const int ZoneSampleCount = 200;
    private static readonly Vector2 ZoneNoiseOffset = new Vector2(371.3f, 912.7f);

    // 흩뿌리기 단위 칸 (10m × 10m). 밀도는 1ha(100m × 100m)당 개수
    private const float ScatterCellSize = 10f;
    private const float HectareArea = 10000f;

    // 밀도 표 (1ha당 최소, 최대). 순서는 FieldZone (개활지, 드문드문, 숲)
    private static readonly float[] TreeDensityMin = { 0f, 5f, 100f };
    private static readonly float[] TreeDensityMax = { 0f, 15f, 150f };
    private static readonly float[] RockDensityMin = { 1f, 1f, 10f };
    private static readonly float[] RockDensityMax = { 3f, 3f, 20f };

    // 나무 종류 비율. 고사목은 모든 영역, 거대목은 숲에서만 나온다
    public const float DeadTreeRatio = 0.05f;
    public const float GiantTreeRatio = 0.01f;

    public const float TreeScaleMin = 0.85f;
    public const float TreeScaleMax = 1.2f;
    public const float RockScaleMin = 0.5f;
    public const float RockScaleMax = 1.1f;

    // 씬 루트 Center 아래에 만드는 부모 이름
    private const string TreeParentName = "Trees";
    private const string RockParentName = "Rocks";

    public const string PrefabFolder = "Assets/Imports/Polyart/PolyartStudio/DreamscapeMountains/Prefabs/";

    public static readonly string[] MainTreePaths =
    {
        "Trees/Prefab_Conifer_01", "Trees/Prefab_Conifer_02", "Trees/Prefab_Conifer_03", "Trees/Prefab_Conifer_04",
        "Trees/Prefab_Conifer_05", "Trees/Prefab_Conifer_10", "Trees/Prefab_Conifer_11"
    };
    public static readonly string[] DeadTreePaths = { "Trees/Prefab_Conifer_08", "Trees/Prefab_Conifer_09" };
    public static readonly string[] GiantTreePaths = { "Trees/Prefab_Conifer_06", "Trees/Prefab_Conifer_07" };

    // Prefab_Cliff_*(셰이더 깨짐), Prefab_Mushroom_*(렌더러 없음)은 쓰지 않는다
    public static readonly string[] RockPaths =
    {
        "Stones/Prefab_Rock_01", "Stones/Prefab_Rock_02", "Stones/Prefab_Rock_03", "Stones/Prefab_Rock_04",
        "Stones/Prefab_Rock_05", "Stones/Prefab_Rock_06", "Stones/Prefab_Rock_07", "Stones/Prefab_Rock_08",
        "Stones/Prefab_Rock_09", "Stones/Prefab_Rock_10", "Stones/Prefab_Rock_11", "Stones/Prefab_Rock_12",
        "Stones/Prefab_Rock_13", "Stones/Prefab_SmallRock_01", "Stones/Prefab_SmallRock_02",
        "Objects/Prefab_Stump", "Objects/Prefab_HollowTrunk"
    };

    // 충돌체가 없는 나무의 CapsuleCollider (임시값).
    // Prefab_Conifer_01(높이 16.1m, 반지름 0.55m, 중심은 높이의 약 절반)의 비율을 참고해 각 나무 크기에 맞췄다
    private static readonly Dictionary<string, CapsuleShape> CapsuleColliderShapes = new Dictionary<string, CapsuleShape>
    {
        { "Prefab_Conifer_03", new CapsuleShape { Center = new Vector3(0f, 9.4f, 0f), Radius = 0.55f, Height = 20.4f } },
        { "Prefab_Conifer_06", new CapsuleShape { Center = new Vector3(0f, 20.7f, 0f), Radius = 1.2f, Height = 44.1f } }
    };

    // 충돌체가 없는 바위, 소품. LOD0 메시로 convex MeshCollider를 붙인다
    private static readonly HashSet<string> MeshColliderPrefabNames = new HashSet<string>
    {
        "Prefab_SmallRock_01", "Prefab_SmallRock_02", "Prefab_HollowTrunk"
    };

    // 노이즈 값의 영역 경계 [드문드문 시작, 숲 시작]. 중앙 지역을 표본 조사해 면적 비율이 맞도록 한 번만 계산한다
    private static readonly float[] ZoneThresholds = CalculateZoneThresholds();

    // 같은 위치는 항상 같은 영역을 돌려준다 (바닥 식생 스크립트에서도 같은 분포를 쓰기 위함)
    public static FieldZone GetFieldZone(Vector2 worldXZ)
    {
        float noise = SampleZoneNoise(worldXZ);
        if (noise >= ZoneThresholds[1]) return FieldZone.Forest;
        if (noise >= ZoneThresholds[0]) return FieldZone.Sparse;
        return FieldZone.Open;
    }

    [MenuItem("Tools/Map/3. 중앙 나무·바위 배치")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        GameObject centerRoot = terrain.gameObject.scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == MapTerrainSetup.CenterRegionName);
        if (centerRoot == null)
        {
            Debug.LogError("씬 루트에 Center가 없습니다. 먼저 'Tools/Map/1. 지형과 구역 부모 생성'을 실행하세요.");
            return;
        }

        ClearTerrainTrees(terrain.terrainData);
        Transform treeParent = ResetParent(centerRoot.transform, TreeParentName);
        Transform rockParent = ResetParent(centerRoot.transform, RockParentName);

        InstanceGroup[] treeGroups =
        {
            CreateGroup(MainTreePaths, treeParent, TreeScaleMin, TreeScaleMax),
            CreateGroup(DeadTreePaths, treeParent, TreeScaleMin, TreeScaleMax),
            CreateGroup(GiantTreePaths, treeParent, TreeScaleMin, TreeScaleMax)
        };
        InstanceGroup rockGroup = CreateGroup(RockPaths, rockParent, RockScaleMin, RockScaleMax);

        Rect center = MapGrid.GetRangeBounds(MapTerrainSetup.CenterFromCell, MapTerrainSetup.CenterToCell);
        var context = new ScatterContext
        {
            Terrain = terrain,
            Random = new System.Random(Seed),
            RiverPath = MapRiverBuilder.GetPath(terrain),
            Trails = MapTrailBuilder.GetTrails()
        };
        if (context.Trails == null) Debug.LogWarning("[MapTreeScatter] 등산로 저장 파일이 없어 길 피하기 없이 배치합니다. 'Tools/Map/7-1. 등산로 만들기' 다음에 다시 실행하세요.");
        var zoneCellCounts = new int[3];
        int riverCellCount = 0;

        foreach (Vector2 cellMin in EnumerateCells(center))
        {
            Vector2 cellCenter = cellMin + Vector2.one * (ScatterCellSize / 2f);
            FieldZone zone = GetFieldZone(cellCenter);
            zoneCellCounts[(int)zone]++;
            // 로그용 : 칸 중심이 강 둘레에 든 칸. 실제 건너뛰기는 인스턴스마다 TryAddInstance에서 한다
            // (칸을 통째로 건너뛰면 무작위 값 순서가 강 경로에 따라 바뀌어 다른 칸의 결과까지 달라진다)
            if (MapRiverBuilder.IsNearRiver(context.RiverPath, new Vector3(cellCenter.x, 0f, cellCenter.y), RiverClearMargin)) riverCellCount++;
            ScatterTrees(context, cellMin, zone, treeGroups);
            ScatterRocks(context, cellMin, zone, rockGroup);
        }

        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        LogResult(context, zoneCellCounts);
        LogRiver(context, riverCellCount, new[] { treeParent, rockParent });
        LogTrail(context, new[] { treeParent, rockParent });
    }

    // 이전 방식(Terrain 나무 기능)으로 심은 인스턴스와 프로토타입을 모두 지운다
    private static void ClearTerrainTrees(TerrainData data)
    {
        data.SetTreeInstances(new TreeInstance[0], false);
        data.treePrototypes = new TreePrototype[0];
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
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

    private static float SampleZoneNoise(Vector2 worldXZ)
    {
        return Mathf.PerlinNoise(worldXZ.x / ZoneNoisePeriod + ZoneNoiseOffset.x, worldXZ.y / ZoneNoisePeriod + ZoneNoiseOffset.y);
    }

    // Perlin 노이즈는 값이 가운데에 몰려 있어 고정 임계값으로는 비율을 맞추기 어렵다.
    // 그래서 중앙 지역의 노이즈 값을 정렬해 분위수로 경계를 정한다.
    private static float[] CalculateZoneThresholds()
    {
        Rect center = MapGrid.GetRangeBounds(MapTerrainSetup.CenterFromCell, MapTerrainSetup.CenterToCell);
        var samples = new float[ZoneSampleCount * ZoneSampleCount];
        for (int z = 0; z < ZoneSampleCount; z++)
        {
            for (int x = 0; x < ZoneSampleCount; x++)
            {
                float worldX = Mathf.Lerp(center.xMin, center.xMax, (x + 0.5f) / ZoneSampleCount);
                float worldZ = Mathf.Lerp(center.yMin, center.yMax, (z + 0.5f) / ZoneSampleCount);
                samples[z * ZoneSampleCount + x] = SampleZoneNoise(new Vector2(worldX, worldZ));
            }
        }
        System.Array.Sort(samples);
        float sparseStart = samples[(int)(samples.Length * OpenAreaRatio)];
        float forestStart = samples[(int)(samples.Length * (1f - ForestAreaRatio))];
        return new[] { sparseStart, forestStart };
    }

    private static InstanceGroup CreateGroup(string[] paths, Transform parent, float scaleMin, float scaleMax)
    {
        GameObject[] prefabs = paths
            .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + path + ".prefab"))
            .ToArray();
        return new InstanceGroup { Prefabs = prefabs, Parent = parent, ScaleMin = scaleMin, ScaleMax = scaleMax };
    }

    private static IEnumerable<Vector2> EnumerateCells(Rect worldRect)
    {
        int columns = Mathf.RoundToInt(worldRect.width / ScatterCellSize);
        int rows = Mathf.RoundToInt(worldRect.height / ScatterCellSize);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
                yield return new Vector2(worldRect.xMin + x * ScatterCellSize, worldRect.yMin + z * ScatterCellSize);
        }
    }

    private static void ScatterTrees(ScatterContext context, Vector2 cellMin, FieldZone zone, InstanceGroup[] treeGroups)
    {
        int count = RollCount(context.Random, TreeDensityMin[(int)zone], TreeDensityMax[(int)zone]);
        for (int i = 0; i < count; i++)
        {
            TreeKind kind = PickTreeKind(context.Random, zone);
            if (TryAddInstance(context, cellMin, treeGroups[(int)kind])) context.TreeKindCounts[(int)kind]++;
        }
    }

    private static void ScatterRocks(ScatterContext context, Vector2 cellMin, FieldZone zone, InstanceGroup rockGroup)
    {
        int count = RollCount(context.Random, RockDensityMin[(int)zone], RockDensityMax[(int)zone]);
        for (int i = 0; i < count; i++)
        {
            if (TryAddInstance(context, cellMin, rockGroup)) context.RockCount++;
        }
    }

    // 1ha당 밀도를 칸 하나의 개수로 바꾼다. 소수 부분은 확률로 처리해 평균 밀도가 맞게 한다
    private static int RollCount(System.Random random, float densityMin, float densityMax)
    {
        float density = RandomRange(random, densityMin, densityMax);
        float expected = density * ScatterCellSize * ScatterCellSize / HectareArea;
        int count = Mathf.FloorToInt(expected);
        if (random.NextDouble() < expected - count) count++;
        return count;
    }

    private static TreeKind PickTreeKind(System.Random random, FieldZone zone)
    {
        double roll = random.NextDouble();
        if (zone == FieldZone.Forest)
        {
            if (roll < GiantTreeRatio) return TreeKind.Giant;
            roll -= GiantTreeRatio;
        }
        return roll < DeadTreeRatio ? TreeKind.Dead : TreeKind.Main;
    }

    // 거점 반경 안, 강 둘레, 급경사면 놓지 않는다. 무작위 값은 조건과 상관없이 같은 순서로 뽑아 결과가 항상 같게 한다
    private static bool TryAddInstance(ScatterContext context, Vector2 cellMin, InstanceGroup group)
    {
        System.Random random = context.Random;
        var world = new Vector2(
            cellMin.x + RandomRange(random, 0f, ScatterCellSize),
            cellMin.y + RandomRange(random, 0f, ScatterCellSize));
        GameObject prefab = group.Prefabs[random.Next(group.Prefabs.Length)];
        float scale = RandomRange(random, group.ScaleMin, group.ScaleMax);
        float rotation = RandomRange(random, 0f, Mathf.PI * 2f);

        if (world.magnitude < BaseClearRadius) return false;
        if (MapRiverBuilder.IsNearRiver(context.RiverPath, new Vector3(world.x, 0f, world.y), RiverClearMargin))
        {
            context.RiverSkipCount++;
            return false;
        }
        if (context.Trails != null && MapTrailBuilder.IsNearTrail(context.Trails, new Vector3(world.x, 0f, world.y), TrailClearMargin))
        {
            context.TrailSkipCount++;
            return false;
        }

        Terrain terrain = context.Terrain;
        Vector3 terrainPosition = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        float normalizedX = (world.x - terrainPosition.x) / size.x;
        float normalizedZ = (world.y - terrainPosition.z) / size.z;
        if (terrain.terrainData.GetSteepness(normalizedX, normalizedZ) > MaxSlope) return false;

        // SampleHeight는 Terrain 기준 높이라 Terrain의 y를 더해 지면에 붙인다
        var position = new Vector3(world.x, 0f, world.y);
        position.y = terrain.SampleHeight(position) + terrainPosition.y;

        // prefab 연결을 유지한 채 놓는다. Static 플래그는 켜지 않는다 (GPU Resident Drawer로 그림)
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.Parent);
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotation * Mathf.Rad2Deg, 0f));
        instance.transform.localScale = Vector3.one * scale;

        if (AddMissingCollider(instance, prefab.name)) context.ColliderCount++;
        return true;
    }

    // 원본 prefab에 충돌체가 없는 종류만 씬 인스턴스의 루트에 충돌체를 붙인다 (원본 prefab은 고치지 않는다)
    public static bool AddMissingCollider(GameObject instance, string prefabName)
    {
        if (CapsuleColliderShapes.TryGetValue(prefabName, out CapsuleShape shape))
        {
            var capsule = instance.AddComponent<CapsuleCollider>();
            capsule.center = shape.Center;
            capsule.radius = shape.Radius;
            capsule.height = shape.Height;
            capsule.direction = 1;
            return true;
        }

        if (MeshColliderPrefabNames.Contains(prefabName))
        {
            Renderer lod0Renderer = instance.GetComponent<LODGroup>().GetLODs()[0].renderers[0];
            var meshCollider = instance.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = lod0Renderer.GetComponent<MeshFilter>().sharedMesh;
            meshCollider.convex = true;
            return true;
        }

        return false;
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    private static void LogResult(ScatterContext context, int[] zoneCellCounts)
    {
        int[] treeKindCounts = context.TreeKindCounts;
        float totalCells = zoneCellCounts.Sum();
        Debug.Log($"[MapTreeScatter] 중앙 나무 {treeKindCounts.Sum()}그루 " +
                  $"(주력 {treeKindCounts[(int)TreeKind.Main]}, 고사목 {treeKindCounts[(int)TreeKind.Dead]}, 거대목 {treeKindCounts[(int)TreeKind.Giant]}), " +
                  $"바위·소품 {context.RockCount}개, 충돌체 추가 {context.ColliderCount}개 / 영역 비율 " +
                  $"숲 {zoneCellCounts[(int)FieldZone.Forest] / totalCells:P1}, " +
                  $"드문드문 {zoneCellCounts[(int)FieldZone.Sparse] / totalCells:P1}, " +
                  $"개활지 {zoneCellCounts[(int)FieldZone.Open] / totalCells:P1}");
    }

    // 검수(강을 피했는지, 성능 측정용 배치 수)에 쓰는 값이라 로그를 남긴다.
    // 물 위 검사는 놓인 오브젝트의 위치를 다시 읽어 물 폭(여유 없이) 안에 든 것을 센다. 0이어야 한다
    private static void LogRiver(ScatterContext context, int riverCellCount, Transform[] parents)
    {
        int totalCount = 0;
        int inWaterCount = 0;
        foreach (Transform parent in parents)
        {
            foreach (Transform child in parent)
            {
                totalCount++;
                if (MapRiverBuilder.IsNearRiver(context.RiverPath, child.position, 0f)) inWaterCount++;
            }
        }
        Debug.Log($"[MapTreeScatter] 강 둘레(물가 밖 {RiverClearMargin}m까지)에 중심이 든 칸 {riverCellCount}개, 강 때문에 건너뛴 나무·바위 {context.RiverSkipCount}개");
        Debug.Log($"[MapTreeScatter] 물 위에 놓인 오브젝트 {inWaterCount}개 (0이어야 한다). Center 배치 GameObject 총 {totalCount}개");
    }

    // 검수(길을 피했는지)에 쓰는 값이라 로그를 남긴다. 놓인 오브젝트의 위치를 다시 읽어 길 폭(여유 없이) 안에 든 것을 센다. 0이어야 한다
    private static void LogTrail(ScatterContext context, Transform[] parents)
    {
        if (context.Trails == null) return;

        int onTrailCount = parents.Sum(parent => parent.Cast<Transform>().Count(child => MapTrailBuilder.IsNearTrail(context.Trails, child.position, 0f)));
        Debug.Log($"[MapTreeScatter] 등산로 때문에 건너뛴 나무·바위 {context.TrailSkipCount}개 (길 가장자리 밖 {TrailClearMargin}m까지), 길 위에 놓인 오브젝트 {onTrailCount}개 (0이어야 한다)");
    }
}
