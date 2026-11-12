using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 메뉴 19 : 남쪽 구역(B3, 강가 유적 도시)의 지면 레이어와 바닥 식생(Terrain 디테일)을 칠한다 (이슈 #11).
// 구조는 남서쪽 메뉴 16(MapSouthWestPainter)과 같고, 영역 구분은 MapSouthScatter.GetSouthZone으로 해서 나무 분포(메뉴 18)와 맞춘다.
// 도시 안은 GetSouthZone이 빈터를 돌려주므로 지면은 풀, 바닥 식생은 빈터 규칙(풀과 꽃)이 된다.
// - 지면 : 나무 무리는 숲 바닥, 드문드문·빈터는 풀. 경사 StoneBlendStartSlope부터 MapTreeScatter.MaxSlope까지 돌로 섞는다 (남서쪽과 같은 규칙).
//   그 위에 물가(강 중심 ShoreFullDistance 안은 물가 레이어, ShoreFullDistance ~ ShoreBlendEndDistance에서 섞임)를 덮는다.
//   유적 둘레는 따로 칠하지 않는다. 원형 흙 경계가 부자연스러워 유적이 풀 위에 그대로 서 있게 했다 (사용자 반려 반영).
// - 바닥 식생 : 강 중심 WaterClearDistance 안(물 아래)은 비우고, 물가 띠(ShoreBlendEndDistance 안)는 고사리 위주에 풀을 더한다.
//   그 밖은 서쪽과 같은 규칙(MapDetailPainter.PaintDetailCell : 나무 무리 고사리·낙엽, 드문드문 풀·가끔 고사리, 빈터 풀·꽃 노이즈 > 0.65에 꽃)이다.
//   경사 MaxSlope 이상은 비운다. 유적 조각(South/Landmarks 아래 모든 조각, South/Rocks 아래 흩어진 유적 조각)은 조각마다 발밑
//   (렌더러 범위 XZ + FootprintMargin)에 걸치는 칸만 비우고, 유적 사이에는 풀을 그대로 둔다.
// - Terrain에 디테일 프로토타입이나 지면 레이어를 새로 넣지 않는다. 쓸 프로토타입이 등록되어 있지 않으면 바꾸지 않고 멈춘다.
// B3 범위만 매번 다시 계산해 덮어쓰므로 여러 번 실행해도 결과가 같고, 다른 구역의 지면과 바닥 식생은 그대로 남는다.
// 실행 순서 : ... → 17 → 18 → 19 → 10.
//   9-1(전체 초기화), 17(지형 높이), 18(구분과 유적 위치)을 다시 실행하면 19도 다시 실행한다.
public static class MapSouthPainter
{
    // 순서는 MapDetailPainter의 그룹 순서와 같다 (MapDetailPainter.CreateContext가 받는 그룹별 프로토타입 번호 배열의 순서)
    private enum DetailGroup
    {
        Grass = 0,
        Fern = 1,
        Leaf = 2,
        Flower = 3
    }

    // 바닥 식생을 칠할 때 함께 쓰는 값 묶음
    private class DetailPlan
    {
        public Terrain Terrain;
        public RectInt Range;
        public int[][] GroupPrototypeIndices;
        // 디테일 칸 중심에서 강 중심선까지의 거리 (RiverDistanceCap에서 자름)
        public float[,] RiverDistances;
        // 유적 조각 발밑에 걸치는 디테일 칸
        public bool[,] IsFootprint;
        public int FootprintCount;
    }

    private const string SouthCell = "B3";
    private const string SouthRegionName = "South";
    private const string LandmarkParentName = "Landmarks";
    private const string RockParentName = "Rocks";
    // South/Rocks 아래 prefab 중 이 폴더의 것을 유적 조각으로 본다 (바위, 그루터기는 발밑을 비우지 않는다)
    private const string RuinPrefabFolder = "/Prefabs/Ruins/";

    // 앞 메뉴들의 seed(20261109 ~ 20261145)와 겹치지 않게 둔다
    private const int Seed = 20261146;

    // MapDetailPainter의 StoneBlendStartSlope와 같은 값. 그쪽이 private이라 여기 다시 둔다 (바뀌면 함께 바꾼다)
    private const float StoneBlendStartSlope = 25f;

    // ---------- 물가 (사용자 승인 임시값) ----------
    // 강 중심에서 이 거리 안은 물가 레이어, 그 밖 ShoreBlendEndDistance까지 아래 레이어로 섞는다.
    // 메뉴 7·9-2의 물가 규칙(MapGroundPainter, 물가에서 잰 폭 + 노이즈)이 아니라 강 중심 거리로 정한 이번 구역 사양이다
    private const float ShoreFullDistance = 14f;
    private const float ShoreBlendEndDistance = 20f;
    // 강 중심에서 이 거리 안(수면 폭 12m의 반 + 1m)은 바닥 식생을 두지 않는다
    private const float WaterClearDistance = 7f;
    // 거리 격자를 이 거리에서 자른다 (위 거리들보다 커야 한다)
    private const float RiverDistanceCap = 25f;
    // 유적 조각 발밑 : 렌더러 범위 XZ를 이만큼 넓힌 사각형에 걸치는 칸을 비운다 (사용자 승인 값)
    private const float FootprintMargin = 0.3f;

    // ---------- 바닥 식생 규칙 ----------
    // 아래 프로토타입 이름, 비중은 MapDetailPainter와 같은 값이다. 그쪽이 private이라 여기 다시 둔다 (바뀌면 함께 바꾼다)
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
    // 물가 띠 칸당 개수 : 고사리 1 + 풀 0 ~ 1 (평균 1.5개, 서쪽 드문드문의 풀 1 ~ 2 + 고사리 10%와 비슷한 밀도)
    private const int ShoreFernCount = 1;
    private const int ShoreGrassMax = 1;

    // ---------- 검수 로그 기준 ----------
    // 풀 레이어 비중이 이 값보다 큰 칸을 풀밭으로 본다 (메뉴 9-3 MapGrassFill과 같은 값)
    private const float GrassWeightMin = 0.5f;
    // 풀밭인데 바닥 식생이 0인 칸의 목표 비율 (이하)
    private const float EmptyGrassRatioMax = 0.1f;
    private const float HectareArea = 10000f;
    // 지면 비율 로그의 묶음. 0 ~ 2는 MapTreeScatter.FieldZone (빈터, 드문드문, 나무 무리)이고, 물가 띠 > 도시 > 구분 순으로 고른다
    private const int ShoreBandCategory = 3;
    private const int CityCategory = 4;
    private static readonly string[] CategoryLabels = { "빈터", "드문드문", "나무 무리", "물가 띠", "도시" };

    [MenuItem("Tools/Map/19. 남쪽 지면과 바닥 식생 칠하기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        // 방식을 바꾸면 다른 구역의 기존 값 뜻이 달라지므로 바꾸지 않고 멈춘다 (메뉴 9-2가 개수 방식으로 맞춘다)
        if (data.detailScatterMode != DetailScatterMode.InstanceCountMode)
        {
            Debug.LogError($"[MapSouthPainter] 디테일 방식이 {data.detailScatterMode}입니다. 먼저 'Tools/Map/9-2. 북쪽 데모 지면 이식'을 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
            return;
        }

        List<Rect> footprints = FindRuinFootprints(terrain);
        if (footprints == null) return;

        int[][] groupPrototypeIndices = FindPrototypeIndices(data);
        if (groupPrototypeIndices == null) return;

        List<Vector2> river = MapRiverBuilder.GetPath(terrain);
        Rect south = MapGrid.GetRangeBounds(SouthCell, SouthCell);
        RectInt alphaRange = MapTerrainSetup.ToIndexRange(terrain, south, data.alphamapResolution);
        RectInt detailRange = MapTerrainSetup.ToIndexRange(terrain, south, data.detailResolution);
        float[,] alphaRiverDistances = CreateRiverDistances(terrain, alphaRange, data.alphamapResolution, river);
        var plan = new DetailPlan
        {
            Terrain = terrain,
            Range = detailRange,
            GroupPrototypeIndices = groupPrototypeIndices,
            RiverDistances = CreateRiverDistances(terrain, detailRange, data.detailResolution, river),
            IsFootprint = MarkFootprintCells(terrain, detailRange, footprints),
            FootprintCount = footprints.Count
        };

        PaintAlphamaps(terrain, alphaRange, alphaRiverDistances);
        PaintDetails(plan);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        LogAlphamaps(terrain, alphaRange, alphaRiverDistances);
        LogDetails(plan, alphaRange);
        LogWriteRanges(terrain, south, alphaRange, detailRange);
    }

    // ---------- 준비 ----------

    // 유적 조각마다 렌더러 범위 XZ를 FootprintMargin만큼 넓힌 사각형 (월드 XZ).
    // South/Landmarks 아래는 모든 prefab 조각(유적, 배, 노), South/Rocks 아래는 Ruins 폴더 prefab(흩어진 유적 조각)만 본다
    private static List<Rect> FindRuinFootprints(Terrain terrain)
    {
        GameObject southRoot = terrain.gameObject.scene.GetRootGameObjects().FirstOrDefault(root => root.name == SouthRegionName);
        Transform landmarks = southRoot != null ? southRoot.transform.Find(LandmarkParentName) : null;
        Transform rocks = southRoot != null ? southRoot.transform.Find(RockParentName) : null;
        if (landmarks == null || rocks == null)
        {
            Debug.LogError("[MapSouthPainter] South/Landmarks 또는 South/Rocks가 없습니다. 먼저 'Tools/Map/18. 남쪽 나무·바위·유적·서식지 배치'를 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
            return null;
        }

        IEnumerable<GameObject> landmarkPieces = landmarks.GetComponentsInChildren<Transform>(true)
            .Select(child => child.gameObject)
            .Where(PrefabUtility.IsOutermostPrefabInstanceRoot);
        IEnumerable<GameObject> debrisPieces = rocks.Cast<Transform>()
            .Select(child => child.gameObject)
            .Where(piece => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(piece).Contains(RuinPrefabFolder));

        var footprints = new List<Rect>();
        foreach (GameObject piece in landmarkPieces.Concat(debrisPieces))
        {
            Renderer[] renderers = piece.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            footprints.Add(Rect.MinMaxRect(
                bounds.min.x - FootprintMargin, bounds.min.z - FootprintMargin,
                bounds.max.x + FootprintMargin, bounds.max.z + FootprintMargin));
        }
        return footprints;
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

        Debug.LogError($"[MapSouthPainter] Terrain에 등록되지 않은 디테일 프로토타입이 있습니다 : {string.Join(", ", missing)}. " +
                       "먼저 'Tools/Map/4. 중앙 바닥 식생 칠하기'를 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
        return null;
    }

    // 칸 중심마다 강 중심선(메뉴 7과 같은 경로)까지의 거리. RiverDistanceCap보다 먼 칸은 그 값으로 둔다.
    // 구간마다 그 둘레 칸만 보아 계산량을 줄인다 (MapSouthShaper와 같은 방식, 같은 점-선분 거리)
    private static float[,] CreateRiverDistances(Terrain terrain, RectInt range, int resolution, List<Vector2> river)
    {
        Vector2 cellSize = GetCellSize(terrain, resolution);
        Vector2 origin = GetCellWorld(terrain, range.x, range.y, resolution);
        var distances = new float[range.height, range.width];
        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
                distances[z, x] = RiverDistanceCap;
        }

        for (int i = 0; i < river.Count - 1; i++)
        {
            Vector2 from = river[i];
            Vector2 to = river[i + 1];
            Vector2 min = Vector2.Min(from, to) - Vector2.one * RiverDistanceCap - origin;
            Vector2 max = Vector2.Max(from, to) + Vector2.one * RiverDistanceCap - origin;
            int xMin = Mathf.Max(Mathf.FloorToInt(min.x / cellSize.x), 0);
            int zMin = Mathf.Max(Mathf.FloorToInt(min.y / cellSize.y), 0);
            int xMax = Mathf.Min(Mathf.CeilToInt(max.x / cellSize.x), range.width - 1);
            int zMax = Mathf.Min(Mathf.CeilToInt(max.y / cellSize.y), range.height - 1);
            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    Vector2 world = origin + new Vector2(x * cellSize.x, z * cellSize.y);
                    float distance = Vector2.Distance(world, Vector2.Lerp(from, to, GetProjection(from, to, world)));
                    if (distance < distances[z, x]) distances[z, x] = distance;
                }
            }
        }
        return distances;
    }

    // 선분 a-b 위에서 position에 가장 가까운 곳의 비율 (0 = a, 1 = b)
    private static float GetProjection(Vector2 a, Vector2 b, Vector2 position)
    {
        Vector2 segment = b - a;
        return Mathf.Clamp01(Vector2.Dot(position - a, segment) / segment.sqrMagnitude);
    }

    // 발밑 사각형과 조금이라도 겹치는 디테일 칸 (칸 안 어디에 놓여도 조각 밑으로 들어가지 않게 한다)
    private static bool[,] MarkFootprintCells(Terrain terrain, RectInt range, List<Rect> footprints)
    {
        int resolution = terrain.terrainData.detailResolution;
        Vector2 cellSize = GetCellSize(terrain, resolution);
        Vector3 position = terrain.transform.position;
        var marks = new bool[range.height, range.width];
        foreach (Rect footprint in footprints)
        {
            // 칸 i는 Terrain 모서리에서 [i, i + 1) × 칸 크기를 덮는다
            int xMin = Mathf.Max(Mathf.FloorToInt((footprint.xMin - position.x) / cellSize.x) - range.x, 0);
            int zMin = Mathf.Max(Mathf.FloorToInt((footprint.yMin - position.z) / cellSize.y) - range.y, 0);
            int xMax = Mathf.Min(Mathf.CeilToInt((footprint.xMax - position.x) / cellSize.x) - 1 - range.x, range.width - 1);
            int zMax = Mathf.Min(Mathf.CeilToInt((footprint.yMax - position.z) / cellSize.y) - 1 - range.y, range.height - 1);
            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                    marks[z, x] = true;
            }
        }
        return marks;
    }

    // 1이면 완전히 물가 레이어, 0이면 물가가 아니다
    private static float GetShoreWeight(float riverDistance)
    {
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ShoreFullDistance, ShoreBlendEndDistance, riverDistance));
    }

    // ---------- 칠하기 ----------

    // 지면 레이어 : 구분 레이어 + 경사 돌 → 물가 순으로 덮는다 (합 1 유지). B3 범위의 모든 레이어를 새로 쓴다
    private static void PaintAlphamaps(Terrain terrain, RectInt range, float[,] riverDistances)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.alphamapResolution;
        var weights = new float[range.height, range.width, data.alphamapLayers];

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                Vector2 world = GetCellWorld(terrain, range.x + x, range.y + z, resolution);
                int groundLayer = MapSouthScatter.GetSouthZone(world) == MapTreeScatter.FieldZone.Forest
                    ? MapTerrainSetup.ForestGroundLayer
                    : MapTerrainSetup.GrassLayer;
                float slope = data.GetSteepness(GetNormalized(range.x + x, resolution), GetNormalized(range.y + z, resolution));
                float stoneWeight = Mathf.InverseLerp(StoneBlendStartSlope, MapTreeScatter.MaxSlope, slope);
                weights[z, x, groundLayer] = 1f - stoneWeight;
                weights[z, x, MapTerrainSetup.StoneLayer] = stoneWeight;

                MapGroundPainter.BlendShore(weights, z, x, GetShoreWeight(riverDistances[z, x]));
            }
        }
        data.SetAlphamaps(range.x, range.y, weights);
    }

    // 바닥 식생 : 프로토타입마다 B3 범위의 빈 층을 만들어 새로 계산해 덮어쓴다 (남서쪽과 같은 방식)
    private static void PaintDetails(DetailPlan plan)
    {
        TerrainData data = plan.Terrain.terrainData;
        int resolution = data.detailResolution;
        RectInt range = plan.Range;
        MapDetailPainter.PaintContext context = MapDetailPainter.CreateContext(plan.GroupPrototypeIndices, range, Seed);

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                float riverDistance = plan.RiverDistances[z, x];
                if (riverDistance < WaterClearDistance) continue;

                float slope = data.GetSteepness(GetNormalized(range.x + x, resolution), GetNormalized(range.y + z, resolution));
                if (slope >= MapTreeScatter.MaxSlope) continue;
                if (plan.IsFootprint[z, x]) continue;

                Vector2 world = GetCellWorld(plan.Terrain, range.x + x, range.y + z, resolution);
                if (riverDistance < ShoreBlendEndDistance) PaintShoreCell(context, x, z);
                else MapDetailPainter.PaintDetailCell(context, x, z, world, MapSouthScatter.GetSouthZone(world));
            }
        }

        foreach (KeyValuePair<int, int[,]> layer in context.Layers)
            data.SetDetailLayer(range.x, range.y, layer.Key, layer.Value);
    }

    // 물가 띠 칸 : 고사리 1 + 풀 0 ~ 1
    private static void PaintShoreCell(MapDetailPainter.PaintContext context, int x, int z)
    {
        AddDetail(context, x, z, DetailGroup.Fern, ShoreFernCount);
        AddDetail(context, x, z, DetailGroup.Grass, context.Random.Next(0, ShoreGrassMax + 1));
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

    // ---------- 로그 ----------
    // 검수(지면 레이어 비율과 흙 0%, 디테일 종류별 수와 꽃 비중, 빈 풀밭 비율, 물 아래·유적 발밑 식생 0개, B3 밖 쓰기 없음)에 쓰는 값이라 남긴다.
    // 모두 칠한 뒤 Terrain에서 다시 읽어 잰다

    // 칸이 속한 로그 묶음. 물가 띠(강 중심 ShoreBlendEndDistance 안) > 도시 > 구분
    private static int GetCategory(Vector2 world, float riverDistance)
    {
        if (riverDistance < ShoreBlendEndDistance) return ShoreBandCategory;
        if (MapSouthScatter.IsInCityArea(world)) return CityCategory;
        return (int)MapSouthScatter.GetSouthZone(world);
    }

    // B3 전체와 묶음별 지면 레이어 비율
    private static void LogAlphamaps(Terrain terrain, RectInt range, float[,] riverDistances)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.alphamapResolution;
        int layerCount = data.alphamapLayers;
        float[,,] alphas = data.GetAlphamaps(range.x, range.y, range.width, range.height);
        var sums = new double[CategoryLabels.Length, layerCount];
        var counts = new int[CategoryLabels.Length];

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                Vector2 world = GetCellWorld(terrain, range.x + x, range.y + z, resolution);
                int category = GetCategory(world, riverDistances[z, x]);
                counts[category]++;
                for (int layer = 0; layer < layerCount; layer++)
                    sums[category, layer] += alphas[z, x, layer];
            }
        }

        string[] layerNames = data.terrainLayers.Select(layer => layer.name.Replace("Layer_", "")).ToArray();
        int total = counts.Sum();
        string totalRatios = string.Join(", ", Enumerable.Range(0, layerCount)
            .Select(layer => $"{layerNames[layer]} {Enumerable.Range(0, CategoryLabels.Length).Sum(category => sums[category, layer]) * 100 / total:F1}%"));
        double dirtRatio = Enumerable.Range(0, CategoryLabels.Length).Sum(category => sums[category, MapTerrainSetup.DirtLayer]) * 100 / total;
        var lines = new List<string> { $"B3 전체 : {totalRatios}", $"흙(Dirt) {dirtRatio:F2}% (0이어야 한다)" };
        for (int category = 0; category < CategoryLabels.Length; category++)
        {
            if (counts[category] == 0) continue;
            string ratios = string.Join(", ", Enumerable.Range(0, layerCount).Select(layer => $"{layerNames[layer]} {sums[category, layer] * 100 / counts[category]:F1}%"));
            lines.Add($"{CategoryLabels[category]} (B3의 {counts[category] * 100f / total:F1}%) : {ratios}");
        }
        Debug.Log($"[MapSouthPainter] 지면 레이어 비율 (물가 띠 = 강 중심 {ShoreBlendEndDistance}m 안, 도시 = MapSouthScatter.IsInCityArea)\n" +
                  string.Join("\n", lines));
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
        Debug.Log($"[MapSouthPainter] B3 바닥 식생 인스턴스 {totalInstances}개 ({area:F1}ha, 1ha당 {totalInstances / area:F0}), " +
                  $"꽃 {flowerInstances}개 (전체의 {(totalInstances > 0 ? (float)flowerInstances / totalInstances : 0f):P1})\n" + string.Join("\n", kindLines));

        LogCellChecks(plan, alphaRange, layers);
    }

    // 텍스처 방식 프로토타입(다른 메뉴가 넣었을 수 있음)은 prefab이 없어 텍스처 이름을 쓴다
    private static string GetPrototypeName(DetailPrototype prototype)
    {
        if (prototype.prototype != null) return prototype.prototype.name;
        return prototype.prototypeTexture != null ? prototype.prototypeTexture.name : "(이름 없음)";
    }

    // 칸 단위 검사 : 빈 풀밭 비율 (경사, 물 아래, 유적 발밑 칸 제외), 물 아래와 유적 발밑 식생 수
    private static void LogCellChecks(DetailPlan plan, RectInt alphaRange, Dictionary<int, int[,]> layers)
    {
        TerrainData data = plan.Terrain.terrainData;
        RectInt range = plan.Range;
        int resolution = data.detailResolution;
        int alphaResolution = data.alphamapResolution;
        float[,,] alphas = data.GetAlphamaps(alphaRange.x, alphaRange.y, alphaRange.width, alphaRange.height);

        int grassCells = 0;
        int emptyGrassCells = 0;
        int footprintCells = 0;
        long onFootprintInstances = 0;
        long inWaterInstances = 0;
        for (int z = 0; z < range.height; z++)
        {
            float v = GetNormalized(range.y + z, resolution);
            for (int x = 0; x < range.width; x++)
            {
                float u = GetNormalized(range.x + x, resolution);
                int count = layers.Values.Sum(layer => layer[z, x]);
                if (plan.RiverDistances[z, x] < WaterClearDistance)
                {
                    inWaterInstances += count;
                    continue;
                }
                if (plan.IsFootprint[z, x])
                {
                    footprintCells++;
                    onFootprintInstances += count;
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

        float emptyRatio = grassCells > 0 ? (float)emptyGrassCells / grassCells : 0f;
        Debug.Log($"[MapSouthPainter] 풀 비중 > {GrassWeightMin}인데 바닥 식생 0인 칸 {emptyGrassCells}/{grassCells}칸 ({emptyRatio:P1}, 목표 {EmptyGrassRatioMax:P0} 이하, " +
                  $"{(emptyRatio <= EmptyGrassRatioMax ? "안" : "밖")}) / 경사 {MapTreeScatter.MaxSlope}도 이상 칸, 강 중심 {WaterClearDistance}m 안 칸, 유적 발밑 칸 제외");
        Debug.Log($"[MapSouthPainter] 유적 조각 {plan.FootprintCount}개의 발밑(렌더러 범위 + {FootprintMargin}m) 칸 {footprintCells}칸. " +
                  $"강 중심 {WaterClearDistance}m 안 바닥 식생 {inWaterInstances}개, 유적 발밑 바닥 식생 {onFootprintInstances}개 (모두 0이어야 한다)");
    }

    private static void LogWriteRanges(Terrain terrain, Rect south, RectInt alphaRange, RectInt detailRange)
    {
        TerrainData data = terrain.terrainData;
        Debug.Log($"[MapSouthPainter] 쓴 범위 (B3 = x {south.xMin} ~ {south.xMax}, z {south.yMin} ~ {south.yMax})\n" +
                  DescribeRange(terrain, south, alphaRange, data.alphamapResolution, "알파맵") + "\n" +
                  DescribeRange(terrain, south, detailRange, data.detailResolution, "디테일"));
    }

    // 쓴 칸의 중심이 모두 B3 안인지(B3 밖 쓰기 없음), 바로 바깥 칸의 중심이 모두 B3 밖인지(B3를 빠짐없이 덮음)를 본다.
    // 칸 중심은 번호 순으로 커지므로 네 모서리 칸과 바로 바깥 네 줄의 가운데 칸만 보면 된다
    private static string DescribeRange(Terrain terrain, Rect south, RectInt range, int resolution, string label)
    {
        Vector2 first = GetCellWorld(terrain, range.xMin, range.yMin, resolution);
        Vector2 last = GetCellWorld(terrain, range.xMax - 1, range.yMax - 1, resolution);
        bool isInside = south.Contains(first) && south.Contains(last);

        int middleX = range.xMin + range.width / 2;
        int middleZ = range.yMin + range.height / 2;
        bool isCovered = !south.Contains(GetCellWorld(terrain, range.xMin - 1, middleZ, resolution))
                         && !south.Contains(GetCellWorld(terrain, range.xMax, middleZ, resolution))
                         && !south.Contains(GetCellWorld(terrain, middleX, range.yMin - 1, resolution))
                         && !south.Contains(GetCellWorld(terrain, middleX, range.yMax, resolution));

        Vector2 half = GetCellSize(terrain, resolution) * 0.5f;
        return $"{label} : 칸 x {range.xMin} ~ {range.xMax - 1}, z {range.yMin} ~ {range.yMax - 1} / " +
               $"칸 가장자리 x {first.x - half.x:F2} ~ {last.x + half.x:F2}, z {first.y - half.y:F2} ~ {last.y + half.y:F2} / " +
               $"칸 중심 {(isInside ? "모두 B3 안" : "B3 밖 칸 있음")}, B3 {(isCovered ? "빠짐없이 덮음" : "덮지 못한 칸 있음")}";
    }
}
