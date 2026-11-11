using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 메뉴 16 : 남서쪽 구역(A3, 초원)의 지면 레이어와 바닥 식생(Terrain 디테일)을 칠한다 (이슈 #9).
// 구조는 서쪽 메뉴 13(MapWestPainter)과 같고, 영역 구분은 MapSouthWestScatter.GetSouthWestZone으로 해서 나무 분포(메뉴 15)와 맞춘다.
// - 지면 : 나무 무리는 숲 바닥, 드문드문·풀밭은 풀. 경사 StoneBlendStartSlope부터 MapTreeScatter.MaxSlope까지 돌로 섞는다 (서쪽과 같은 규칙).
// - 바닥 식생 : 풀 위주. 풀 개수는 서쪽과 같은 규칙(MapDetailPainter의 드문드문 1 ~ 2, 빈터 2 ~ 3)이다.
//   꽃은 풀밭과 드문드문 모두에 피고, 꽃이 피는 칸의 비율을 서쪽 규칙(꽃 노이즈 > 0.65)의 FlowerAreaMultiplier배로 늘린다.
//   고사리, 낙엽은 나무 무리 안에만 둔다 (서쪽의 드문드문 고사리는 두지 않는다). 경사 MaxSlope 이상은 비운다.
// - 랜드마크 바닥(SouthWest/Landmarks 묶음마다 렌더러 범위로 구한 바닥 반지름) 안은 바닥 식생을 비운다. 서식지는 풀밭 그대로 칠한다.
// - Terrain에 디테일 프로토타입이나 지면 레이어를 새로 넣지 않는다. 쓸 프로토타입이 등록되어 있지 않으면 바꾸지 않고 멈춘다.
// A3에는 강, 호수, 등산로가 없어 물가·길 칠하기는 하지 않는다.
// A3 범위만 매번 다시 계산해 덮어쓰므로 여러 번 실행해도 결과가 같고, 다른 구역의 지면과 바닥 식생은 그대로 남는다.
// 실행 순서 : ... → 14 → 15 → 16 → 10.
//   9-1(전체 초기화), 14(지형 높이), 15(구분과 랜드마크 위치)를 다시 실행하면 16도 다시 실행한다.
public static class MapSouthWestPainter
{
    // 순서는 MapDetailPainter의 그룹 순서와 같다 (CreateContext가 받는 그룹별 프로토타입 번호 배열의 순서)
    private enum DetailGroup
    {
        Grass = 0,
        Fern = 1,
        Leaf = 2,
        Flower = 3
    }

    // 랜드마크 묶음 하나의 바닥 (월드 XZ)
    private struct LandmarkFloor
    {
        public string Label;
        public Vector2 Center;
        public float Radius;
    }

    // 꽃이 피는 칸 비율 (서쪽 규칙으로 잰 비율, 늘린 목표 비율, 그 비율이 되는 노이즈 경계)
    private struct FlowerRule
    {
        public float WestRatio;
        public float TargetRatio;
        public float Threshold;
    }

    // 바닥 식생을 칠할 때 함께 쓰는 값 묶음
    private class DetailPlan
    {
        public Terrain Terrain;
        public RectInt Range;
        public int[][] GroupPrototypeIndices;
        public List<LandmarkFloor> Floors;
        public FlowerRule Flower;
    }

    private const string SouthWestCell = "A3";
    private const string SouthWestRegionName = "SouthWest";
    private const string LandmarkParentName = "Landmarks";

    // 앞 메뉴들의 seed(20261109 ~ 20261133)와 겹치지 않게 둔다
    private const int Seed = 20261134;

    // MapDetailPainter의 StoneBlendStartSlope와 같은 값. 그쪽이 private이라 여기 다시 둔다 (바뀌면 함께 바꾼다)
    private const float StoneBlendStartSlope = 25f;

    // ---------- 바닥 식생 규칙 ----------
    // 아래 프로토타입 이름, 비중, 칸당 개수, 꽃 노이즈는 MapDetailPainter와 같은 값이다. 그쪽이 private이라 여기 다시 둔다 (바뀌면 함께 바꾼다)
    private static readonly string[][] GroupPrefabNames =
    {
        new[] { "Prefab_Terrain_Grass_Medium01", "Prefab_Terrain_Grass_Medium02", "Prefab_Terrain_Grass_Medium03", "Prefab_Terrain_Grass_Small01" },
        new[] { "Prefab_Terrain_Fern_02", "Prefab_Terrain_Fern_03" },
        new[] { "Prefab_Terrain_Groundleaf_01" },
        new[] { "Prefab_FlowerGroup_White_Detail", "Prefab_FlowerGroup_Yellow_Detail", "Prefab_FlowerSingle_Purple_Detail", "Prefab_FlowerSingle_Red_Detail" }
    };
    private static readonly float[][] GroupPrefabWeights =
    {
        new[] { 0.85f / 3f, 0.85f / 3f, 0.85f / 3f, 0.15f },
        new[] { 1f, 1f },
        new[] { 1f },
        new[] { 1f, 1f, 1f, 1f }
    };
    private const int ForestFernMax = 1;
    private const int ForestLeafCount = 1;
    private const int SparseGrassMin = 1;
    private const int SparseGrassMax = 2;
    private const int OpenGrassMin = 2;
    private const int OpenGrassMax = 3;
    private const int FlowerCountMin = 1;
    private const int FlowerCountMax = 2;
    private const float FlowerNoisePeriod = 30f;
    private static readonly Vector2 FlowerNoiseOffset = new Vector2(523.9f, 187.1f);
    // 서쪽(와 중앙)에서 꽃이 피는 노이즈 경계
    private const float WestFlowerNoiseThreshold = 0.65f;
    // 꽃이 피는 칸 비율을 서쪽 규칙의 몇 배로 할지 (사용자 승인 임시값)
    private const float FlowerAreaMultiplier = 2f;

    // ---------- 검수 로그 기준 ----------
    // 풀 레이어 비중이 이 값보다 큰 칸을 풀밭으로 본다 (메뉴 9-3 MapGrassFill과 같은 값)
    private const float GrassWeightMin = 0.5f;
    // 풀밭인데 바닥 식생이 0인 칸의 목표 비율 (이하)
    private const float EmptyGrassRatioMax = 0.1f;
    private const float HectareArea = 10000f;
    // 순서는 MapTreeScatter.FieldZone (풀밭, 드문드문, 나무 무리)
    private static readonly string[] ZoneLabels = { "풀밭", "드문드문", "나무 무리" };

    [MenuItem("Tools/Map/16. 남서쪽 지면과 바닥 식생 칠하기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        // 방식을 바꾸면 다른 구역의 기존 값 뜻이 달라지므로 바꾸지 않고 멈춘다 (메뉴 9-2가 개수 방식으로 맞춘다)
        if (data.detailScatterMode != DetailScatterMode.InstanceCountMode)
        {
            Debug.LogError($"[MapSouthWestPainter] 디테일 방식이 {data.detailScatterMode}입니다. 먼저 'Tools/Map/9-2. 북쪽 데모 지면 이식'을 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
            return;
        }

        List<LandmarkFloor> floors = FindLandmarkFloors(terrain);
        if (floors == null) return;

        int[][] groupPrototypeIndices = FindPrototypeIndices(data);
        if (groupPrototypeIndices == null) return;

        Rect southWest = MapGrid.GetRangeBounds(SouthWestCell, SouthWestCell);
        RectInt detailRange = MapTerrainSetup.ToIndexRange(terrain, southWest, data.detailResolution);
        var plan = new DetailPlan
        {
            Terrain = terrain,
            Range = detailRange,
            GroupPrototypeIndices = groupPrototypeIndices,
            Floors = floors,
            Flower = CalculateFlowerRule(terrain, detailRange)
        };

        RectInt alphaRange = PaintAlphamaps(terrain, southWest);
        PaintDetails(plan);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        LogLandmarkFloors(floors);
        LogAlphamaps(terrain, alphaRange);
        LogDetails(plan, alphaRange);
        LogWriteRanges(terrain, southWest, alphaRange, detailRange);
    }

    // ---------- 준비 ----------

    // 메뉴 15는 묶음 GameObject를 바닥 중심에 두므로 그 위치를 중심으로 쓴다.
    // 반지름은 메뉴 15와 같은 방식 : 묶음 안 오브젝트마다 (중심 ~ 렌더러 범위 수평 중심 거리 + 범위의 큰 쪽 반폭) 중 가장 큰 값
    private static List<LandmarkFloor> FindLandmarkFloors(Terrain terrain)
    {
        GameObject southWestRoot = terrain.gameObject.scene.GetRootGameObjects().FirstOrDefault(root => root.name == SouthWestRegionName);
        Transform parent = southWestRoot != null ? southWestRoot.transform.Find(LandmarkParentName) : null;
        if (parent == null)
        {
            Debug.LogError("[MapSouthWestPainter] SouthWest/Landmarks가 없습니다. 먼저 'Tools/Map/15. 남서쪽 나무·바위·랜드마크·서식지 배치'를 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
            return null;
        }

        var floors = new List<LandmarkFloor>();
        foreach (Transform group in parent)
        {
            Vector2 center = ToXZ(group.position);
            float radius = 0f;
            foreach (Transform item in group)
            {
                Renderer[] renderers = item.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;

                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                radius = Mathf.Max(radius, Vector2.Distance(center, ToXZ(bounds.center)) + Mathf.Max(bounds.extents.x, bounds.extents.z));
            }
            floors.Add(new LandmarkFloor { Label = group.name, Center = center, Radius = radius });
        }
        return floors;
    }

    // 이미 등록된 프로토타입 번호만 찾는다. MapDetailPainter.RegisterPrototypes는 없으면 새로 넣으므로 쓰지 않는다.
    // 하나라도 없으면 null (Terrain을 바꾸지 않고 멈춘다)
    private static int[][] FindPrototypeIndices(TerrainData data)
    {
        DetailPrototype[] prototypes = data.detailPrototypes;
        var missing = new List<string>();
        var groupPrototypeIndices = new int[GroupPrefabNames.Length][];
        for (int group = 0; group < GroupPrefabNames.Length; group++)
        {
            groupPrototypeIndices[group] = GroupPrefabNames[group]
                .Select(name => System.Array.FindIndex(prototypes, prototype => prototype.prototype != null && prototype.prototype.name == name))
                .ToArray();
            missing.AddRange(GroupPrefabNames[group].Where((name, i) => groupPrototypeIndices[group][i] < 0));
        }

        if (missing.Count == 0) return groupPrototypeIndices;

        Debug.LogError($"[MapSouthWestPainter] Terrain에 등록되지 않은 디테일 프로토타입이 있습니다 : {string.Join(", ", missing)}. " +
                       "먼저 'Tools/Map/4. 중앙 바닥 식생 칠하기'를 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
        return null;
    }

    // A3 디테일 칸 전체에서 서쪽 규칙(노이즈 > 0.65)으로 꽃이 피는 칸 비율을 재고, 그 FlowerAreaMultiplier배가 되는 노이즈 경계를 분위수로 구한다.
    // 노이즈 무늬는 서쪽과 같으므로 꽃 무리 모양은 같고 넓이만 늘어난다
    private static FlowerRule CalculateFlowerRule(Terrain terrain, RectInt range)
    {
        int resolution = terrain.terrainData.detailResolution;
        var noises = new List<float>(range.width * range.height);
        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
                noises.Add(SampleFlowerNoise(GetCellWorld(terrain, range.x + x, range.y + z, resolution)));
        }

        int total = noises.Count;
        float westRatio = (float)noises.Count(noise => noise > WestFlowerNoiseThreshold) / total;
        float targetRatio = Mathf.Min(1f, westRatio * FlowerAreaMultiplier);
        int targetCount = Mathf.RoundToInt(total * targetRatio);
        noises.Sort();

        // 경계보다 큰 칸이 targetCount개가 되도록 고른다
        float threshold = targetCount >= total ? float.MinValue : noises[total - targetCount - 1];
        return new FlowerRule { WestRatio = westRatio, TargetRatio = targetRatio, Threshold = threshold };
    }

    // 디테일 칸(사각형)이 바닥 원과 조금이라도 겹치면 true. 칸 안 어디에 놓여도 바닥 안으로 들어오지 않게 한다
    private static bool IsOnLandmarkFloor(List<LandmarkFloor> floors, Vector2 cellCenter, Vector2 cellSize)
    {
        Vector2 half = cellSize * 0.5f;
        foreach (LandmarkFloor floor in floors)
        {
            var nearest = new Vector2(
                Mathf.Clamp(floor.Center.x, cellCenter.x - half.x, cellCenter.x + half.x),
                Mathf.Clamp(floor.Center.y, cellCenter.y - half.y, cellCenter.y + half.y));
            if (Vector2.Distance(nearest, floor.Center) < floor.Radius) return true;
        }
        return false;
    }

    // ---------- 칠하기 ----------

    // 지면 레이어 : 나무 무리는 숲 바닥, 그 밖은 풀. 경사면은 돌로 섞는다 (서쪽과 같은 규칙). A3 범위의 모든 레이어를 새로 쓴다
    private static RectInt PaintAlphamaps(Terrain terrain, Rect southWest)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.alphamapResolution;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, southWest, resolution);
        var weights = new float[range.height, range.width, data.alphamapLayers];

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                Vector2 world = GetCellWorld(terrain, range.x + x, range.y + z, resolution);
                int groundLayer = MapSouthWestScatter.GetSouthWestZone(world) == MapTreeScatter.FieldZone.Forest
                    ? MapTerrainSetup.ForestGroundLayer
                    : MapTerrainSetup.GrassLayer;
                float slope = data.GetSteepness(GetNormalized(range.x + x, resolution), GetNormalized(range.y + z, resolution));
                float stoneWeight = Mathf.InverseLerp(StoneBlendStartSlope, MapTreeScatter.MaxSlope, slope);
                weights[z, x, groundLayer] = 1f - stoneWeight;
                weights[z, x, MapTerrainSetup.StoneLayer] = stoneWeight;
            }
        }
        data.SetAlphamaps(range.x, range.y, weights);
        return range;
    }

    // 바닥 식생 : 프로토타입마다 A3 범위의 빈 층을 만들어 새로 계산해 덮어쓴다 (서쪽과 같은 방식)
    private static void PaintDetails(DetailPlan plan)
    {
        TerrainData data = plan.Terrain.terrainData;
        int resolution = data.detailResolution;
        RectInt range = plan.Range;
        Vector2 cellSize = GetCellSize(plan.Terrain, resolution);
        MapDetailPainter.PaintContext context = MapDetailPainter.CreateContext(plan.GroupPrototypeIndices, range, Seed);

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                float slope = data.GetSteepness(GetNormalized(range.x + x, resolution), GetNormalized(range.y + z, resolution));
                if (slope >= MapTreeScatter.MaxSlope) continue;

                Vector2 world = GetCellWorld(plan.Terrain, range.x + x, range.y + z, resolution);
                if (IsOnLandmarkFloor(plan.Floors, world, cellSize)) continue;

                PaintDetailCell(context, x, z, world, plan.Flower.Threshold);
            }
        }

        foreach (KeyValuePair<int, int[,]> layer in context.Layers)
            data.SetDetailLayer(range.x, range.y, layer.Key, layer.Value);
    }

    // 칸 하나 : 나무 무리는 고사리·낙엽, 드문드문은 풀 1 ~ 2, 풀밭은 풀 2 ~ 3. 풀밭과 드문드문은 꽃 노이즈가 경계보다 크면 꽃 1 ~ 2를 더한다
    private static void PaintDetailCell(MapDetailPainter.PaintContext context, int x, int z, Vector2 world, float flowerThreshold)
    {
        System.Random random = context.Random;
        MapTreeScatter.FieldZone zone = MapSouthWestScatter.GetSouthWestZone(world);
        if (zone == MapTreeScatter.FieldZone.Forest)
        {
            AddDetail(context, x, z, DetailGroup.Fern, random.Next(0, ForestFernMax + 1));
            AddDetail(context, x, z, DetailGroup.Leaf, ForestLeafCount);
            return;
        }

        int grassCount = zone == MapTreeScatter.FieldZone.Sparse
            ? random.Next(SparseGrassMin, SparseGrassMax + 1)
            : random.Next(OpenGrassMin, OpenGrassMax + 1);
        AddDetail(context, x, z, DetailGroup.Grass, grassCount);
        if (SampleFlowerNoise(world) > flowerThreshold)
            AddDetail(context, x, z, DetailGroup.Flower, random.Next(FlowerCountMin, FlowerCountMax + 1));
    }

    // 그룹 안에서 prefab 하나를 비중대로 골라 그 칸의 개수를 더한다
    private static void AddDetail(MapDetailPainter.PaintContext context, int x, int z, DetailGroup group, int count)
    {
        int[] indices = context.GroupPrototypeIndices[(int)group];
        int prototypeIndex = indices[PickWeightedIndex(context.Random, GroupPrefabWeights[(int)group])];
        context.Layers[prototypeIndex][z, x] += count;
    }

    private static int PickWeightedIndex(System.Random random, float[] weights)
    {
        double roll = random.NextDouble() * weights.Sum();
        for (int i = 0; i < weights.Length - 1; i++)
        {
            roll -= weights[i];
            if (roll < 0) return i;
        }
        return weights.Length - 1;
    }

    private static float SampleFlowerNoise(Vector2 world)
    {
        return Mathf.PerlinNoise(world.x / FlowerNoisePeriod + FlowerNoiseOffset.x, world.y / FlowerNoisePeriod + FlowerNoiseOffset.y);
    }

    // ---------- 격자, 계산 ----------

    // 칸 번호(Terrain 전체 기준)의 칸 중심을 0 ~ 1로
    private static float GetNormalized(int index, int resolution)
    {
        return (index + 0.5f) / resolution;
    }

    // 알파맵, 디테일 칸 중심의 월드 XZ
    private static Vector2 GetCellWorld(Terrain terrain, int indexX, int indexZ, int resolution)
    {
        Vector3 position = terrain.transform.position;
        Vector2 cellSize = GetCellSize(terrain, resolution);
        return new Vector2(position.x + (indexX + 0.5f) * cellSize.x, position.z + (indexZ + 0.5f) * cellSize.y);
    }

    private static Vector2 GetCellSize(Terrain terrain, int resolution)
    {
        Vector3 size = terrain.terrainData.size;
        return new Vector2(size.x / resolution, size.z / resolution);
    }

    private static Vector2 ToXZ(Vector3 world)
    {
        return new Vector2(world.x, world.z);
    }

    // ---------- 로그 ----------
    // 검수(지면 레이어 비율, 디테일 종류별 수와 꽃 비중, 빈 풀밭 비율, 랜드마크 바닥 안 식생 0개, A3 밖 쓰기 없음)에 쓰는 값이라 남긴다.
    // 모두 칠한 뒤 Terrain에서 다시 읽어 잰다

    private static void LogLandmarkFloors(List<LandmarkFloor> floors)
    {
        IEnumerable<string> lines = floors.Select(floor => $"{floor.Label} : 중심 ({floor.Center.x:F0}, {floor.Center.y:F0}), 바닥 반지름 {floor.Radius:F1}m");
        Debug.Log($"[MapSouthWestPainter] 바닥 식생을 비운 랜드마크 바닥 {floors.Count}곳\n" + string.Join("\n", lines));
    }

    // A3 전체와 구분별 지면 레이어 비율
    private static void LogAlphamaps(Terrain terrain, RectInt range)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.alphamapResolution;
        int layerCount = data.alphamapLayers;
        float[,,] alphas = data.GetAlphamaps(range.x, range.y, range.width, range.height);
        var sums = new double[ZoneLabels.Length, layerCount];
        var counts = new int[ZoneLabels.Length];

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                int zone = (int)MapSouthWestScatter.GetSouthWestZone(GetCellWorld(terrain, range.x + x, range.y + z, resolution));
                counts[zone]++;
                for (int layer = 0; layer < layerCount; layer++)
                    sums[zone, layer] += alphas[z, x, layer];
            }
        }

        string[] layerNames = data.terrainLayers.Select(layer => layer.name.Replace("Layer_", "")).ToArray();
        int total = counts.Sum();
        string totalRatios = string.Join(", ", Enumerable.Range(0, layerCount)
            .Select(layer => $"{layerNames[layer]} {Enumerable.Range(0, ZoneLabels.Length).Sum(zone => sums[zone, layer]) * 100 / total:F1}%"));
        var lines = new List<string> { $"A3 전체 : {totalRatios}" };
        for (int zone = 0; zone < ZoneLabels.Length; zone++)
        {
            if (counts[zone] == 0) continue;
            string ratios = string.Join(", ", Enumerable.Range(0, layerCount).Select(layer => $"{layerNames[layer]} {sums[zone, layer] * 100 / counts[zone]:F1}%"));
            lines.Add($"{ZoneLabels[zone]} (A3의 {counts[zone] * 100f / total:F1}%) : {ratios}");
        }
        Debug.Log("[MapSouthWestPainter] 지면 레이어 비율\n" + string.Join("\n", lines));
    }

    private static void LogDetails(DetailPlan plan, RectInt alphaRange)
    {
        TerrainData data = plan.Terrain.terrainData;
        RectInt range = plan.Range;
        Dictionary<int, int[,]> layers = Enumerable.Range(0, data.detailPrototypes.Length)
            .ToDictionary(index => index, index => data.GetDetailLayer(range.x, range.y, range.width, range.height, index));
        int[] flowerIndices = plan.GroupPrototypeIndices[(int)DetailGroup.Flower];

        long totalInstances = layers.Values.Sum(layer => (long)layer.Cast<int>().Sum());
        long flowerInstances = flowerIndices.Sum(index => (long)layers[index].Cast<int>().Sum());
        IEnumerable<string> kindLines = layers
            .Select(layer => (Index: layer.Key, Count: layer.Value.Cast<int>().Sum()))
            .Where(kind => kind.Count > 0)
            .Select(kind => $"{GetPrototypeName(data.detailPrototypes[kind.Index])} : {kind.Count}개");
        Vector2 cellSize = GetCellSize(plan.Terrain, data.detailResolution);
        float area = range.width * range.height * cellSize.x * cellSize.y / HectareArea;
        Debug.Log($"[MapSouthWestPainter] A3 바닥 식생 인스턴스 {totalInstances}개 ({area:F1}ha, 1ha당 {totalInstances / area:F0}), " +
                  $"꽃 {flowerInstances}개 (전체의 {(totalInstances > 0 ? (float)flowerInstances / totalInstances : 0f):P1})\n" + string.Join("\n", kindLines));

        LogCellChecks(plan, alphaRange, layers, flowerIndices);
    }

    // 텍스처 방식 프로토타입(다른 메뉴가 넣었을 수 있음)은 prefab이 없어 텍스처 이름을 쓴다
    private static string GetPrototypeName(DetailPrototype prototype)
    {
        if (prototype.prototype != null) return prototype.prototype.name;
        return prototype.prototypeTexture != null ? prototype.prototypeTexture.name : "(이름 없음)";
    }

    // 칸 단위 검사 :꽃이 핀 칸 비율 (풀밭, 드문드문), 빈 풀밭 비율 (경사, 랜드마크 바닥 칸 제외), 랜드마크 바닥 안 식생 수
    private static void LogCellChecks(DetailPlan plan, RectInt alphaRange, Dictionary<int, int[,]> layers, int[] flowerIndices)
    {
        TerrainData data = plan.Terrain.terrainData;
        RectInt range = plan.Range;
        int resolution = data.detailResolution;
        int alphaResolution = data.alphamapResolution;
        Vector2 cellSize = GetCellSize(plan.Terrain, resolution);
        float[,,] alphas = data.GetAlphamaps(alphaRange.x, alphaRange.y, alphaRange.width, alphaRange.height);

        int fieldCells = 0;
        int flowerCells = 0;
        int grassCells = 0;
        int emptyGrassCells = 0;
        long onFloorInstances = 0;
        for (int z = 0; z < range.height; z++)
        {
            float v = GetNormalized(range.y + z, resolution);
            for (int x = 0; x < range.width; x++)
            {
                float u = GetNormalized(range.x + x, resolution);
                Vector2 world = GetCellWorld(plan.Terrain, range.x + x, range.y + z, resolution);
                int count = layers.Values.Sum(layer => layer[z, x]);
                if (MapSouthWestScatter.GetSouthWestZone(world) != MapTreeScatter.FieldZone.Forest)
                {
                    fieldCells++;
                    if (flowerIndices.Any(index => layers[index][z, x] > 0)) flowerCells++;
                }

                if (IsOnLandmarkFloor(plan.Floors, world, cellSize))
                {
                    onFloorInstances += count;
                    continue;
                }
                if (data.GetSteepness(u, v) >= MapTreeScatter.MaxSlope) continue;

                // 두 범위의 끝이 반올림으로 칸 반 개쯤 어긋날 수 있어 알파맵 범위 안으로 자른다
                int alphaX = Mathf.Clamp(Mathf.FloorToInt(u * alphaResolution) - alphaRange.x, 0, alphaRange.width - 1);
                int alphaZ = Mathf.Clamp(Mathf.FloorToInt(v * alphaResolution) - alphaRange.y, 0, alphaRange.height - 1);
                if (alphas[alphaZ, alphaX, MapTerrainSetup.GrassLayer] <= GrassWeightMin) continue;

                grassCells++;
                if (count == 0) emptyGrassCells++;
            }
        }

        FlowerRule flower = plan.Flower;
        float flowerCellRatio = fieldCells > 0 ? (float)flowerCells / fieldCells : 0f;
        Debug.Log($"[MapSouthWestPainter] 꽃 : 서쪽 규칙(노이즈 > {WestFlowerNoiseThreshold})이면 A3 칸의 {flower.WestRatio:P1}, " +
                  $"{FlowerAreaMultiplier}배 목표 {flower.TargetRatio:P1} (노이즈 경계 {flower.Threshold:F3}). " +
                  $"실제 풀밭·드문드문 칸 중 꽃이 핀 칸 {flowerCells}/{fieldCells} ({flowerCellRatio:P1})");

        float emptyRatio = grassCells > 0 ? (float)emptyGrassCells / grassCells : 0f;
        Debug.Log($"[MapSouthWestPainter] 풀 비중 > {GrassWeightMin}인데 바닥 식생 0인 칸 {emptyGrassCells}/{grassCells}칸 ({emptyRatio:P1}, 목표 {EmptyGrassRatioMax:P0} 이하, " +
                  $"{(emptyRatio <= EmptyGrassRatioMax ? "안" : "밖")}) / 경사 {MapTreeScatter.MaxSlope}도 이상 칸, 랜드마크 바닥 칸 제외");
        Debug.Log($"[MapSouthWestPainter] 랜드마크 바닥 안 바닥 식생 {onFloorInstances}개 (0이어야 한다)");
    }

    private static void LogWriteRanges(Terrain terrain, Rect southWest, RectInt alphaRange, RectInt detailRange)
    {
        TerrainData data = terrain.terrainData;
        Debug.Log($"[MapSouthWestPainter] 쓴 범위 (A3 = x {southWest.xMin} ~ {southWest.xMax}, z {southWest.yMin} ~ {southWest.yMax})\n" +
                  DescribeRange(terrain, southWest, alphaRange, data.alphamapResolution, "알파맵") + "\n" +
                  DescribeRange(terrain, southWest, detailRange, data.detailResolution, "디테일"));
    }

    // 쓴 칸의 중심이 모두 A3 안인지(A3 밖 쓰기 없음), 바로 바깥 칸의 중심이 모두 A3 밖인지(A3를 빠짐없이 덮음)를 본다.
    // 칸 중심은 번호 순으로 커지므로 네 모서리 칸과 바로 바깥 네 줄의 가운데 칸만 보면 된다
    private static string DescribeRange(Terrain terrain, Rect southWest, RectInt range, int resolution, string label)
    {
        Vector2 first = GetCellWorld(terrain, range.xMin, range.yMin, resolution);
        Vector2 last = GetCellWorld(terrain, range.xMax - 1, range.yMax - 1, resolution);
        bool isInside = southWest.Contains(first) && southWest.Contains(last);

        int middleX = range.xMin + range.width / 2;
        int middleZ = range.yMin + range.height / 2;
        bool isCovered = !southWest.Contains(GetCellWorld(terrain, range.xMin - 1, middleZ, resolution))
                         && !southWest.Contains(GetCellWorld(terrain, range.xMax, middleZ, resolution))
                         && !southWest.Contains(GetCellWorld(terrain, middleX, range.yMin - 1, resolution))
                         && !southWest.Contains(GetCellWorld(terrain, middleX, range.yMax, resolution));

        Vector2 half = GetCellSize(terrain, resolution) * 0.5f;
        return $"{label} : 칸 x {range.xMin} ~ {range.xMax - 1}, z {range.yMin} ~ {range.yMax - 1} / " +
               $"칸 가장자리 x {first.x - half.x:F2} ~ {last.x + half.x:F2}, z {first.y - half.y:F2} ~ {last.y + half.y:F2} / " +
               $"칸 중심 {(isInside ? "모두 A3 안" : "A3 밖 칸 있음")}, A3 {(isCovered ? "빠짐없이 덮음" : "덮지 못한 칸 있음")}";
    }
}
