using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 메뉴 3 : 중앙 지역(B2~C3)에 나무와 바위, 소품을 Terrain 나무 기능으로 흩뿌린다.
// 중앙 지역 안의 나무 인스턴스만 지우고 다시 놓으므로 여러 번 실행해도 결과가 같고, 바깥 구역은 그대로 남는다.
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
        public List<TreeInstance> Instances;
        public System.Random Random;
        public int[] TreeKindCounts = new int[3];
        public int RockCount;
    }

    // 같은 크기 범위로 놓는 prefab 묶음 (Terrain 나무 프로토타입 번호 목록)
    private class InstanceGroup
    {
        public int[] PrototypeIndices;
        public float ScaleMin;
        public float ScaleMax;
    }

    public const float BaseClearRadius = 40f;
    public const float MaxSlope = 35f;

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
    private const float DeadTreeRatio = 0.05f;
    private const float GiantTreeRatio = 0.01f;

    private const float TreeScaleMin = 0.85f;
    private const float TreeScaleMax = 1.2f;
    private const float RockScaleMin = 0.5f;
    private const float RockScaleMax = 1.1f;

    private const string PrefabFolder = "Assets/Imports/Polyart/PolyartStudio/DreamscapeMountains/Prefabs/";

    private static readonly string[] MainTreePaths =
    {
        "Trees/Prefab_Conifer_01", "Trees/Prefab_Conifer_02", "Trees/Prefab_Conifer_03", "Trees/Prefab_Conifer_04",
        "Trees/Prefab_Conifer_05", "Trees/Prefab_Conifer_10", "Trees/Prefab_Conifer_11"
    };
    private static readonly string[] DeadTreePaths = { "Trees/Prefab_Conifer_08", "Trees/Prefab_Conifer_09" };
    private static readonly string[] GiantTreePaths = { "Trees/Prefab_Conifer_06", "Trees/Prefab_Conifer_07" };

    // Prefab_Cliff_*(셰이더 깨짐), Prefab_Mushroom_*(렌더러 없음)은 쓰지 않는다
    private static readonly string[] RockPaths =
    {
        "Stones/Prefab_Rock_01", "Stones/Prefab_Rock_02", "Stones/Prefab_Rock_03", "Stones/Prefab_Rock_04",
        "Stones/Prefab_Rock_05", "Stones/Prefab_Rock_06", "Stones/Prefab_Rock_07", "Stones/Prefab_Rock_08",
        "Stones/Prefab_Rock_09", "Stones/Prefab_Rock_10", "Stones/Prefab_Rock_11", "Stones/Prefab_Rock_12",
        "Stones/Prefab_Rock_13", "Stones/Prefab_SmallRock_01", "Stones/Prefab_SmallRock_02",
        "Objects/Prefab_Stump", "Objects/Prefab_HollowTrunk"
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

        TerrainData data = terrain.terrainData;
        var prototypes = new List<TreePrototype>(data.treePrototypes);
        InstanceGroup[] treeGroups =
        {
            CreateGroup(prototypes, MainTreePaths, TreeScaleMin, TreeScaleMax),
            CreateGroup(prototypes, DeadTreePaths, TreeScaleMin, TreeScaleMax),
            CreateGroup(prototypes, GiantTreePaths, TreeScaleMin, TreeScaleMax)
        };
        InstanceGroup rockGroup = CreateGroup(prototypes, RockPaths, RockScaleMin, RockScaleMax);
        data.treePrototypes = prototypes.ToArray();

        Rect center = MapGrid.GetRangeBounds(MapTerrainSetup.CenterFromCell, MapTerrainSetup.CenterToCell);
        var context = new ScatterContext
        {
            Terrain = terrain,
            Instances = KeepInstancesOutside(terrain, center),
            Random = new System.Random(Seed)
        };
        int keptCount = context.Instances.Count;
        var zoneCellCounts = new int[3];

        foreach (Vector2 cellMin in EnumerateCells(center))
        {
            FieldZone zone = GetFieldZone(cellMin + Vector2.one * (ScatterCellSize / 2f));
            zoneCellCounts[(int)zone]++;
            ScatterTrees(context, cellMin, zone, treeGroups);
            ScatterRocks(context, cellMin, zone, rockGroup);
        }

        data.SetTreeInstances(context.Instances.ToArray(), true);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        LogResult(context, keptCount, zoneCellCounts);
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

    // 이미 등록된 prefab은 그 번호를 쓰고, 없으면 뒤에 추가한다 (기존 인스턴스의 번호가 밀리지 않게)
    private static InstanceGroup CreateGroup(List<TreePrototype> prototypes, string[] paths, float scaleMin, float scaleMax)
    {
        var indices = new int[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + paths[i] + ".prefab");
            int index = prototypes.FindIndex(prototype => prototype.prefab == prefab);
            if (index < 0)
            {
                prototypes.Add(new TreePrototype { prefab = prefab });
                index = prototypes.Count - 1;
            }
            indices[i] = index;
        }
        return new InstanceGroup { PrototypeIndices = indices, ScaleMin = scaleMin, ScaleMax = scaleMax };
    }

    private static List<TreeInstance> KeepInstancesOutside(Terrain terrain, Rect worldRect)
    {
        Vector3 position = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        return terrain.terrainData.treeInstances
            .Where(instance => !worldRect.Contains(new Vector2(
                position.x + instance.position.x * size.x,
                position.z + instance.position.z * size.z)))
            .ToList();
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

    // 거점 반경 안이나 급경사면 놓지 않는다. 무작위 값은 조건과 상관없이 같은 순서로 뽑아 결과가 항상 같게 한다
    private static bool TryAddInstance(ScatterContext context, Vector2 cellMin, InstanceGroup group)
    {
        System.Random random = context.Random;
        var world = new Vector2(
            cellMin.x + RandomRange(random, 0f, ScatterCellSize),
            cellMin.y + RandomRange(random, 0f, ScatterCellSize));
        int prototypeIndex = group.PrototypeIndices[random.Next(group.PrototypeIndices.Length)];
        float scale = RandomRange(random, group.ScaleMin, group.ScaleMax);
        float rotation = RandomRange(random, 0f, Mathf.PI * 2f);

        if (world.magnitude < BaseClearRadius) return false;

        Vector3 position = context.Terrain.transform.position;
        Vector3 size = context.Terrain.terrainData.size;
        float normalizedX = (world.x - position.x) / size.x;
        float normalizedZ = (world.y - position.z) / size.z;
        if (context.Terrain.terrainData.GetSteepness(normalizedX, normalizedZ) > MaxSlope) return false;

        // 높이(y)는 SetTreeInstances에서 지면에 붙인다
        context.Instances.Add(new TreeInstance
        {
            position = new Vector3(normalizedX, 0f, normalizedZ),
            widthScale = scale,
            heightScale = scale,
            rotation = rotation,
            color = Color.white,
            lightmapColor = Color.white,
            prototypeIndex = prototypeIndex
        });
        return true;
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    private static void LogResult(ScatterContext context, int keptCount, int[] zoneCellCounts)
    {
        int[] treeKindCounts = context.TreeKindCounts;
        float totalCells = zoneCellCounts.Sum();
        Debug.Log($"[MapTreeScatter] 중앙 나무 {treeKindCounts.Sum()}그루 " +
                  $"(주력 {treeKindCounts[(int)TreeKind.Main]}, 고사목 {treeKindCounts[(int)TreeKind.Dead]}, 거대목 {treeKindCounts[(int)TreeKind.Giant]}), " +
                  $"바위·소품 {context.RockCount}개, 바깥 보존 {keptCount}개 / 영역 비율 " +
                  $"숲 {zoneCellCounts[(int)FieldZone.Forest] / totalCells:P1}, " +
                  $"드문드문 {zoneCellCounts[(int)FieldZone.Sparse] / totalCells:P1}, " +
                  $"개활지 {zoneCellCounts[(int)FieldZone.Open] / totalCells:P1}");
    }
}
