using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 메뉴 9-1 : 맵 전체 지면 레이어를 풀로, 바닥 식생(디테일)을 0으로 되돌린다. 높이맵, 나무, 오브젝트는 건드리지 않는다.
//   맵 크기를 바꾸기 전(2km 맵)에 칠한 얼룩이 늘어나 엉뚱한 곳에 남아 있어, 다시 칠하기 전에 한 번 비운다.
// 메뉴 9 : 북쪽 줄(A1~C1) 산의 지면(높이대별 바닥, 가파른 곳·능선 마루·높은 곳·절벽과 큰 바위 둘레의 돌)과 바닥 식생,
//   호수와 강 둘레의 물가를 칠한다. 남쪽(B3)은 강 둘레만 칠하고, 중앙(B2)은 메뉴 4가 칠하므로 건드리지 않는다.
//   산 영역의 돌 : 초록(풀 + 숲 바닥) 비율이 StoneTargetRatio가 되도록 돌 시작 경사를 칠할 때마다 다시 맞춘다.
// 물가를 정하는 규칙(강, 호수 물 끝까지의 거리)은 메뉴 4도 함께 써서 구역 경계에서 물가가 끊기지 않는다.
//
// 2026-11-10 북쪽 데모 이식(이슈 #5 방향 변경) 이후 메뉴 9는 쓰지 않는다 (메뉴에서 비활성).
//   옛 산(메뉴 5) 기준 규칙이라 실행하면 데모 지면 칠(메뉴 9-2, MapDemoGroundTransplant)을 덮어쓴다. 남쪽(B3) 강 둘레 칠은 메뉴 9-2가 이 파일의 함수로 한다.
// 실행 순서 : 9-1 → 4 → 9-2. 9-1을 실행하면 중앙도 지워지므로 4와 9-2를 다시 실행한다.
// 4와 9-2는 맡은 범위를 다시 계산해 덮어쓰므로 여러 번 실행해도 결과가 같다.
public static class MapGroundPainter
{
    // 강, 호수의 물 위치. 한 번 구해 알파맵, 디테일 칠하기에 함께 쓴다 (강 경로 계산이 무겁다)
    public class WaterSource
    {
        public List<Vector2> RiverPath;
        // 물이 보이는(지형이 수면보다 낮은) 호수 수면 메시 정점의 월드 XZ
        public List<Vector2> LakePoints;
        // 정점 하나가 맡는 물의 반지름
        public float LakePointRadius;
    }

    // 칠할 격자(알파맵 또는 디테일)의 한 범위
    private struct GridArea
    {
        public TerrainData Data;
        public RectInt Range;
        public int Resolution;
        // Terrain 모서리의 월드 XZ와 높이
        public Vector2 Origin;
        public float BaseY;
        public Vector2 CellSize;
    }

    // 격자 칸 하나의 지형 정보 (칸 중심 기준)
    private struct CellSample
    {
        public Vector2 World;
        public float Height;
        public float Slope;
        // 둘레보다 높은 정도 (m). 양수면 능선 마루, 음수면 골짜기
        public float Convexity;
    }

    private struct RockSpot
    {
        public Vector2 Center;
        public float Radius;
    }

    // 산 지면 한 칸의 레이어 비중 (Grass + ForestGround + Dirt + Stone = 1)
    private struct NorthGround
    {
        // A1, C1에서 산이 끝난 바깥 평지면 false (풀만 칠하고 바닥 식생은 비운다)
        public bool IsMountain;
        public MapNorthScatter.HeightBand Band;
        public float Grass;
        public float ForestGround;
        public float Dirt;
        public float Stone;
    }

    // 돌 시작 경사와 무관한 산 지면 한 칸의 값. 돌 시작 경사를 맞출 때 여러 번 다시 쓰므로 한 번만 계산한다
    private struct MountainCell
    {
        public bool IsMountain;
        public MapNorthScatter.HeightBand Band;
        // 산자락 끝 0 ~ 산 1 (A1, C1 바깥 평지의 풀과 섞는 비중)
        public float Mountain;
        // 경사 + 높이·능선·노이즈 가산 (도). 클수록 돌이 되기 쉽다
        public float StoneScore;
        public float RockStone;
        // 돌을 뺀 높이대별 바닥 (Grass + ForestGround + Dirt = 1)
        public NorthGround BandGround;
    }

    private class NorthContext
    {
        public Terrain Terrain;
        public Rect NorthArea;
        public Rect MiddleColumn;
        public WaterSource Water;
        public List<RockSpot> RockSpots;
        // 알파맵을 칠할 때 정하고, 바닥 식생도 같은 값을 쓴다
        public float StoneStartSlope;
    }

    // ---------- 물가 (임시값) ----------
    // 물 끝에서 ShoreWidth(± ShoreJitter) 안을 물가 레이어로 칠하고, 끝의 ShoreFade만큼은 원래 지면과 섞는다.
    // 강둑 꼭대기(물가 밖 2m)를 지나 비탈 앞부분까지 덮는다. 물 속도 물가 레이어로 칠해 투명한 물 아래 바닥이 풀로 보이지 않게 한다
    private const float ShoreWidth = 4f;
    private const float ShoreJitter = 1.5f;
    private const float ShoreFade = 2f;
    private const float ShoreNoisePeriod = 15f;
    private static readonly Vector2 ShoreNoiseOffset = new Vector2(311.7f, 645.3f);
    // 물 끝까지의 거리는 물가가 닿을 수 있는 가장 먼 곳까지만 잰다
    private const float WaterSearchDistance = ShoreWidth + ShoreJitter;

    // ---------- 북쪽 산 (임시값) ----------
    private const int NorthDetailSeed = 20261117;

    // 돌 지면 : 칸마다 돌 점수(경사 + 가산, 도)를 매겨, 돌 시작 경사부터 섞기 시작해 StoneBlendRange만큼 더 높으면 완전히 돌이 된다.
    // 돌 시작 경사는 고정값이 아니라, 산 영역(물가 제외)의 돌 : (돌 + 초록)이 StoneTargetRatio가 되도록 이분 탐색으로 찾는다.
    //   흙은 어느 쪽에도 넣지 않는다. 로그는 목표 ± StoneTargetTolerance 안인지 알려 준다
    private const float StoneTargetRatio = 0.3f;
    private const float StoneTargetTolerance = 0.05f;
    // 돌 시작 경사를 찾는 범위와 반복 횟수 (범위 50도를 30번 나누면 오차가 0.001도 아래다)
    private const float StoneStartSearchMin = 20f;
    private const float StoneStartSearchMax = 70f;
    private const int StoneStartSearchIterations = 30;
    private const float StoneBlendRange = 10f;
    // 돌 경계가 등고선이나 경사 줄을 따라 곧게 나뉘지 않도록 점수를 ± StoneSlopeJitter 흔든다
    private const float StoneSlopeJitter = 6f;
    private const float StoneNoisePeriod = 25f;
    private static readonly Vector2 StoneNoiseOffset = new Vector2(733.9f, 251.3f);
    // 높을수록 돌 : StoneHeightMin부터 StoneHeightMax까지 올라가며 점수에 최대 StoneHeightBonus를 더한다 (정상 부근은 돌 위주)
    private const float StoneHeightMin = 160f;
    private const float StoneHeightMax = 340f;
    private const float StoneHeightBonus = 8f;
    // 능선 마루는 돌, 골짜기는 초록 : 둘레 RidgeProbeDistance 네 곳의 평균보다 RidgeConvexityFull만큼 높으면 + RidgeStoneBonus,
    //   그만큼 낮으면 - RidgeStoneBonus. 골짜기를 따라 초록이 중턱 위쪽까지 올라간다
    private const float RidgeProbeDistance = 20f;
    private const float RidgeConvexityFull = 6f;
    private const float RidgeStoneBonus = 10f;

    // 절벽과 큰 바위 둘레 : 바닥 반지름 안은 돌, 그 밖 RockStoneSkirt까지 서서히 줄인다
    private const float RockStoneSkirt = 6f;

    // 바닥 무늬 : 기슭은 노이즈가 낮은 곳에 풀 조각을 남긴 숲 바닥 위주, 중턱과 정상 부근은 노이즈가 높은 곳에 흙 조각이 섞인 풀
    private const float GroundNoisePeriod = 40f;
    private static readonly Vector2 GroundNoiseOffset = new Vector2(419.3f, 877.1f);
    private const float FoothillForestNoiseMin = 0.3f;
    private const float FoothillForestNoiseMax = 0.45f;
    private const float MidDirtNoiseMin = 0.45f;
    private const float MidDirtNoiseMax = 0.7f;
    private const float MidDirtMax = 0.7f;
    // A1, C1 산자락 끝(바탕 + MapNorthScatter.OuterFlatMaxRise)부터 이 높이만큼 올라가며 풀에서 산 지면으로 바뀐다
    private const float OuterFadeRise = 10f;

    // 돌이 이 비율 이상인 칸에는 바닥 식생을 두지 않는다
    private const float DetailStoneLimit = 0.5f;

    // ---------- 이름, 로그 ----------
    private const string SouthCell = "B3";
    private const string LakeRegionName = "North";
    private const string LakeParentName = "Lake";
    private const string CliffParentName = "Cliffs";
    private const string RockParentName = "Rocks";
    // 구역별 로그를 내는 칸
    private static readonly string[] LogCellNames = { "A1", "B1", "C1", "A2", "B2", "C2", "A3", "B3", "C3" };
    // 순서는 MapNorthScatter.HeightBand와 같다
    private static readonly string[] BandLogNames = { "기슭", "중턱", "정상 부근" };
    // 알파맵 비중이 이 값 이상이면 그 레이어만 칠한 칸으로 본다 (float 오차)
    private const float FullWeight = 0.999f;
    private const float HectareArea = 10000f;

    [MenuItem("Tools/Map/9-1. 지면과 바닥 식생 전체 초기화")]
    private static void RunReset()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        MapTerrainSetup.FillWithGrass(data);
        int resolution = data.detailResolution;
        int prototypeCount = data.detailPrototypes.Length;
        for (int i = 0; i < prototypeCount; i++)
            data.SetDetailLayer(0, 0, i, new int[resolution, resolution]);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        LogReset(data);
    }

    [MenuItem("Tools/Map/9. 북쪽 산 지면과 물가 칠하기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;
        if (FindLakeFilter(terrain) == null)
        {
            Debug.LogError("North/Lake 아래에 호수 수면이 없습니다. 먼저 'Tools/Map/6. 호수 만들기'를 실행하세요.");
            return;
        }

        TerrainData data = terrain.terrainData;
        if (data.detailScatterMode != DetailScatterMode.InstanceCountMode)
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

        var context = new NorthContext
        {
            Terrain = terrain,
            NorthArea = MapGrid.GetRangeBounds(MapNorthScatter.NorthFromCell, MapNorthScatter.NorthToCell),
            MiddleColumn = MapGrid.GetRangeBounds(MapNorthScatter.MiddleCell, MapNorthScatter.MiddleCell),
            Water = CreateWaterSource(terrain),
            RockSpots = FindRockSpots(terrain)
        };
        int[][] groupPrototypeIndices = MapDetailPainter.RegisterPrototypes(data);
        Rect south = MapGrid.GetRangeBounds(SouthCell, SouthCell);

        PaintNorthAlphamaps(context);
        PaintNorthDetails(context, groupPrototypeIndices);
        PaintSouthShoreAlphamaps(terrain, context.Water, south);
        ClearSouthShoreDetails(terrain, context.Water, south);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        LogResult(context);
    }

    // 데모 이식 이후 메뉴 9를 실행하면 북쪽 데모 지면 칠이 옛 규칙으로 덮이므로 메뉴를 비활성으로 둔다 (위 머리말 참고)
    [MenuItem("Tools/Map/9. 북쪽 산 지면과 물가 칠하기", true)]
    private static bool CanRun()
    {
        return false;
    }

    // ---------- 물가 (메뉴 4와 함께 씀) ----------

    public static WaterSource CreateWaterSource(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        var water = new WaterSource
        {
            RiverPath = MapRiverBuilder.GetPath(terrain),
            LakePoints = new List<Vector2>(),
            // 수면 메시 정점 간격(= 높이맵 간격)의 반
            LakePointRadius = data.size.x / (data.heightmapResolution - 1) * 0.5f
        };

        MeshFilter lakeFilter = FindLakeFilter(terrain);
        if (lakeFilter == null)
        {
            Debug.LogWarning("North/Lake 아래에 호수 수면이 없어 강만 물로 봅니다.");
            return water;
        }

        // 수면 메시는 물가 밖 지형 속까지 깔려 있어, 지형이 수면보다 낮은(물이 보이는) 정점만 쓴다
        foreach (Vector3 vertex in lakeFilter.sharedMesh.vertices)
        {
            Vector3 point = lakeFilter.transform.TransformPoint(vertex);
            float ground = terrain.SampleHeight(point) + terrain.transform.position.y;
            if (ground < point.y) water.LakePoints.Add(new Vector2(point.x, point.z));
        }
        return water;
    }

    // range 칸마다 가장 가까운 물 끝까지의 거리 [z, x]. 물 속은 0 이하이고, WaterSearchDistance보다 먼 곳은 그 값으로 둔다
    public static float[,] CreateWaterDistances(WaterSource water, Terrain terrain, RectInt range, int resolution)
    {
        return CreateWaterDistances(water, CreateArea(terrain, range, resolution));
    }

    // 1이면 완전히 물가 레이어, 0이면 물가가 아니다. 경계는 노이즈로 흔들어 강둑을 따라 곧은 줄로 보이지 않게 한다
    public static float GetShoreWeight(float waterDistance, Vector2 world)
    {
        if (waterDistance <= 0f) return 1f;

        float width = ShoreWidth + (SampleNoise(world, ShoreNoisePeriod, ShoreNoiseOffset) * 2f - 1f) * ShoreJitter;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(width - ShoreFade, width, waterDistance));
    }

    // 칸의 기존 비중을 (1 - shoreWeight)로 줄이고 남는 만큼 물가 레이어를 더한다 (합 1 유지)
    public static void BlendShore(float[,,] weights, int z, int x, float shoreWeight)
    {
        if (shoreWeight <= 0f) return;

        for (int layer = 0; layer < weights.GetLength(2); layer++)
            weights[z, x, layer] *= 1f - shoreWeight;
        weights[z, x, MapTerrainSetup.ShoreLayer] += shoreWeight;
    }

    private static float[,] CreateWaterDistances(WaterSource water, GridArea area)
    {
        var distances = new float[area.Range.height, area.Range.width];
        for (int z = 0; z < area.Range.height; z++)
        {
            for (int x = 0; x < area.Range.width; x++)
                distances[z, x] = WaterSearchDistance;
        }
        StampRiver(water.RiverPath, area, distances);
        StampLake(water, area, distances);
        return distances;
    }

    // 강 구간마다 그 둘레 칸만 보아 계산량을 줄인다
    private static void StampRiver(List<Vector2> path, GridArea area, float[,] distances)
    {
        float reach = MapRiverBuilder.HalfWidth + WaterSearchDistance;
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 from = path[i];
            Vector2 to = path[i + 1];
            if (!TryGetCellRange(area, Vector2.Min(from, to) - Vector2.one * reach, Vector2.Max(from, to) + Vector2.one * reach, out RectInt cells)) continue;

            for (int z = cells.yMin; z <= cells.yMax; z++)
            {
                for (int x = cells.xMin; x <= cells.xMax; x++)
                {
                    float distance = GetDistanceToSegment(from, to, GetCellWorld(area, x, z)) - MapRiverBuilder.HalfWidth;
                    distances[z, x] = Mathf.Min(distances[z, x], distance);
                }
            }
        }
    }

    private static void StampLake(WaterSource water, GridArea area, float[,] distances)
    {
        float reach = water.LakePointRadius + WaterSearchDistance;
        foreach (Vector2 point in water.LakePoints)
        {
            if (!TryGetCellRange(area, point - Vector2.one * reach, point + Vector2.one * reach, out RectInt cells)) continue;

            for (int z = cells.yMin; z <= cells.yMax; z++)
            {
                for (int x = cells.xMin; x <= cells.xMax; x++)
                {
                    float distance = Vector2.Distance(GetCellWorld(area, x, z), point) - water.LakePointRadius;
                    distances[z, x] = Mathf.Min(distances[z, x], distance);
                }
            }
        }
    }

    private static MeshFilter FindLakeFilter(Terrain terrain)
    {
        GameObject region = terrain.gameObject.scene.GetRootGameObjects().FirstOrDefault(root => root.name == LakeRegionName);
        Transform lake = region != null ? region.transform.Find(LakeParentName) : null;
        return lake != null ? lake.GetComponentInChildren<MeshFilter>() : null;
    }

    // ---------- 북쪽 산 ----------

    private static void PaintNorthAlphamaps(NorthContext context)
    {
        TerrainData data = context.Terrain.terrainData;
        GridArea area = CreateArea(context.Terrain, MapTerrainSetup.ToIndexRange(context.Terrain, context.NorthArea, data.alphamapResolution), data.alphamapResolution);
        float[,] waterDistances = CreateWaterDistances(context.Water, area);
        float[,] rockStones = CreateRockStones(context.RockSpots, area);
        var cells = new MountainCell[area.Range.height, area.Range.width];
        var shores = new float[area.Range.height, area.Range.width];
        // 돌 시작 경사를 맞출 칸 : 산 영역 중 물가가 아닌 칸
        var targetCells = new List<MountainCell>();

        for (int z = 0; z < area.Range.height; z++)
        {
            for (int x = 0; x < area.Range.width; x++)
            {
                CellSample sample = SampleCell(area, x, z);
                cells[z, x] = GetMountainCell(context, sample, rockStones[z, x]);
                shores[z, x] = GetShoreWeight(waterDistances[z, x], sample.World);
                if (cells[z, x].IsMountain && shores[z, x] <= 0f) targetCells.Add(cells[z, x]);
            }
        }
        context.StoneStartSlope = FindStoneStartSlope(targetCells);

        var weights = new float[area.Range.height, area.Range.width, data.alphamapLayers];
        for (int z = 0; z < area.Range.height; z++)
        {
            for (int x = 0; x < area.Range.width; x++)
            {
                NorthGround ground = ToNorthGround(cells[z, x], context.StoneStartSlope);
                weights[z, x, MapTerrainSetup.GrassLayer] = ground.Grass;
                weights[z, x, MapTerrainSetup.ForestGroundLayer] = ground.ForestGround;
                weights[z, x, MapTerrainSetup.DirtLayer] = ground.Dirt;
                weights[z, x, MapTerrainSetup.StoneLayer] = ground.Stone;
                BlendShore(weights, z, x, shores[z, x]);
            }
        }
        data.SetAlphamaps(area.Range.x, area.Range.y, weights);
    }

    // 산 영역의 돌 : (돌 + 초록)이 StoneTargetRatio가 되는 돌 시작 경사를 이분 탐색으로 찾는다.
    // 시작 경사가 높을수록 돌이 줄고 초록이 느는 관계라 반으로 나눠 가며 좁힐 수 있다. 같은 지형이면 결과도 같다
    private static float FindStoneStartSlope(List<MountainCell> cells)
    {
        if (GetStoneRatio(cells, StoneStartSearchMin) < StoneTargetRatio)
        {
            Debug.LogWarning($"[MapGroundPainter] 돌 시작 경사를 최소 {StoneStartSearchMin}도로 낮춰도 돌이 목표보다 적습니다. 최소값으로 칠합니다.");
            return StoneStartSearchMin;
        }
        if (GetStoneRatio(cells, StoneStartSearchMax) > StoneTargetRatio)
        {
            Debug.LogWarning($"[MapGroundPainter] 돌 시작 경사를 최대 {StoneStartSearchMax}도로 높여도 돌이 목표보다 많습니다. 최대값으로 칠합니다.");
            return StoneStartSearchMax;
        }

        float low = StoneStartSearchMin;
        float high = StoneStartSearchMax;
        for (int i = 0; i < StoneStartSearchIterations; i++)
        {
            float middle = (low + high) * 0.5f;
            if (GetStoneRatio(cells, middle) > StoneTargetRatio) low = middle;
            else high = middle;
        }
        return (low + high) * 0.5f;
    }

    // 돌 : (돌 + 초록). 칸이 많아 double로 더한다
    private static float GetStoneRatio(List<MountainCell> cells, float stoneStartSlope)
    {
        double stone = 0;
        double green = 0;
        foreach (MountainCell cell in cells)
        {
            NorthGround ground = ToNorthGround(cell, stoneStartSlope);
            stone += ground.Stone;
            green += ground.Grass + ground.ForestGround;
        }
        return (float)(stone / (stone + green));
    }

    // 바닥 식생은 알파맵과 같은 규칙으로 칸의 지면을 다시 계산해, 가장 많이 칠한 레이어에 맞춰 놓는다
    private static void PaintNorthDetails(NorthContext context, int[][] groupPrototypeIndices)
    {
        TerrainData data = context.Terrain.terrainData;
        GridArea area = CreateArea(context.Terrain, MapTerrainSetup.ToIndexRange(context.Terrain, context.NorthArea, data.detailResolution), data.detailResolution);
        float[,] waterDistances = CreateWaterDistances(context.Water, area);
        float[,] rockStones = CreateRockStones(context.RockSpots, area);
        MapDetailPainter.PaintContext paint = MapDetailPainter.CreateContext(groupPrototypeIndices, area.Range, NorthDetailSeed);

        for (int z = 0; z < area.Range.height; z++)
        {
            for (int x = 0; x < area.Range.width; x++)
            {
                CellSample sample = SampleCell(area, x, z);
                if (sample.Slope >= MapTreeScatter.MaxSlope) continue;
                if (GetShoreWeight(waterDistances[z, x], sample.World) > 0f) continue;

                NorthGround ground = ToNorthGround(GetMountainCell(context, sample, rockStones[z, x]), context.StoneStartSlope);
                if (TryGetDetailZone(ground, out MapTreeScatter.FieldZone zone))
                    MapDetailPainter.PaintDetailCell(paint, x, z, sample.World, zone);
            }
        }

        foreach (KeyValuePair<int, int[,]> layer in paint.Layers)
            data.SetDetailLayer(area.Range.x, area.Range.y, layer.Key, layer.Value);
    }

    private static MountainCell GetMountainCell(NorthContext context, CellSample sample, float rockStone)
    {
        var cell = new MountainCell();
        bool isMiddle = sample.World.x >= context.MiddleColumn.xMin && sample.World.x < context.MiddleColumn.xMax;
        // A1, C1에서 산이 끝난 바깥 평지는 메뉴 8과 같은 기준으로 비워 둔다 (구역 작업 때 채움)
        float rise = sample.Height - (MapTerrainSetup.BaseHeight + MapNorthScatter.OuterFlatMaxRise);
        if (!isMiddle && rise < 0f) return cell;

        cell.IsMountain = true;
        cell.Band = MapNorthScatter.GetBand(sample.World, sample.Height);
        // 산자락 끝에서 바깥 평지의 풀과 이어지도록 산 지면을 서서히 늘린다
        cell.Mountain = isMiddle ? 1f : Mathf.InverseLerp(0f, OuterFadeRise, rise);
        cell.StoneScore = GetStoneScore(sample);
        cell.RockStone = rockStone;
        cell.BandGround = GetBandGround(cell.Band, sample.World);
        return cell;
    }

    // 돌은 점수에 따른 돌과 절벽·큰 바위 둘레 돌 중 큰 쪽이다
    private static NorthGround ToNorthGround(MountainCell cell, float stoneStartSlope)
    {
        if (!cell.IsMountain) return new NorthGround { Grass = 1f };

        float scoreStone = Mathf.InverseLerp(stoneStartSlope, stoneStartSlope + StoneBlendRange, cell.StoneScore);
        float stone = Mathf.Max(scoreStone, cell.RockStone) * cell.Mountain;
        float rest = 1f - stone;
        return new NorthGround
        {
            IsMountain = true,
            Band = cell.Band,
            Grass = rest * (1f - cell.Mountain + cell.Mountain * cell.BandGround.Grass),
            ForestGround = rest * cell.Mountain * cell.BandGround.ForestGround,
            Dirt = rest * cell.Mountain * cell.BandGround.Dirt,
            Stone = stone
        };
    }

    // 경사에 노이즈, 높이, 능선(골짜기는 뺌) 가산을 더한 돌 점수 (도)
    private static float GetStoneScore(CellSample sample)
    {
        float noise = SampleNoise(sample.World, StoneNoisePeriod, StoneNoiseOffset) * 2f - 1f;
        float height = Mathf.InverseLerp(StoneHeightMin, StoneHeightMax, sample.Height) * StoneHeightBonus;
        float ridge = Mathf.Clamp(sample.Convexity / RidgeConvexityFull, -1f, 1f) * RidgeStoneBonus;
        return sample.Slope + noise * StoneSlopeJitter + height + ridge;
    }

    // 돌을 빼고 본 높이대별 바닥 (Grass + ForestGround + Dirt = 1)
    private static NorthGround GetBandGround(MapNorthScatter.HeightBand band, Vector2 world)
    {
        float patch = SampleNoise(world, GroundNoisePeriod, GroundNoiseOffset);
        var ground = new NorthGround();
        switch (band)
        {
            case MapNorthScatter.HeightBand.Foothill:
                ground.ForestGround = Mathf.InverseLerp(FoothillForestNoiseMin, FoothillForestNoiseMax, patch);
                ground.Grass = 1f - ground.ForestGround;
                break;
            // 정상 부근도 돌이 아닌 곳은 풀이다 (흙만 칠하면 흙이 커져 돌 : 초록 비율을 맞추기 어렵다)
            case MapNorthScatter.HeightBand.Mid:
            case MapNorthScatter.HeightBand.Summit:
                ground.Dirt = Mathf.InverseLerp(MidDirtNoiseMin, MidDirtNoiseMax, patch) * MidDirtMax;
                ground.Grass = 1f - ground.Dirt;
                break;
        }
        return ground;
    }

    // 기슭·중턱만 칠한다. 숲 바닥은 중앙의 숲, 기슭의 풀은 드문드문, 중턱의 풀은 개활지(꽃 무리 포함)와 같은 규칙이고,
    // 흙과 돌이 많은 칸, 정상 부근, 바깥 평지는 비운다
    private static bool TryGetDetailZone(NorthGround ground, out MapTreeScatter.FieldZone zone)
    {
        zone = MapTreeScatter.FieldZone.Open;
        if (!ground.IsMountain || ground.Band == MapNorthScatter.HeightBand.Summit || ground.Stone >= DetailStoneLimit) return false;

        if (ground.ForestGround >= ground.Grass && ground.ForestGround >= ground.Dirt)
        {
            zone = MapTreeScatter.FieldZone.Forest;
            return true;
        }
        if (ground.Grass < ground.Dirt) return false;

        zone = ground.Band == MapNorthScatter.HeightBand.Foothill ? MapTreeScatter.FieldZone.Sparse : MapTreeScatter.FieldZone.Open;
        return true;
    }

    // 메뉴 8이 놓은 절벽 전부와 큰 바위. 작은 바위는 둘레까지 돌로 칠하면 얼룩처럼 보여 뺀다
    private static List<RockSpot> FindRockSpots(Terrain terrain)
    {
        var spots = new List<RockSpot>();
        GameObject[] roots = terrain.gameObject.scene.GetRootGameObjects();
        foreach (string regionName in MapNorthScatter.RegionNames)
        {
            GameObject root = roots.FirstOrDefault(candidate => candidate.name == regionName);
            if (root == null) continue;

            AddRockSpots(spots, root.transform.Find(CliffParentName), 0f);
            AddRockSpots(spots, root.transform.Find(RockParentName), MapNorthScatter.BigRockScaleMin);
        }
        return spots;
    }

    // 큰 바위는 메뉴 8에서 배율 BigRockScaleMin 이상(작은 바위는 최대 MapTreeScatter.RockScaleMax)이라 배율로 가린다.
    // 바닥 반지름은 렌더러 범위의 동서, 남북 반폭 평균이다 (돌린 절벽은 범위가 실제보다 조금 크다)
    private static void AddRockSpots(List<RockSpot> spots, Transform parent, float minScale)
    {
        if (parent == null) return;

        foreach (Transform child in parent)
        {
            if (child.localScale.x < minScale) continue;

            Renderer[] renderers = child.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            spots.Add(new RockSpot
            {
                Center = new Vector2(bounds.center.x, bounds.center.z),
                Radius = (bounds.extents.x + bounds.extents.z) * 0.5f
            });
        }
    }

    private static float[,] CreateRockStones(List<RockSpot> spots, GridArea area)
    {
        var stones = new float[area.Range.height, area.Range.width];
        foreach (RockSpot spot in spots)
        {
            float reach = spot.Radius + RockStoneSkirt;
            if (!TryGetCellRange(area, spot.Center - Vector2.one * reach, spot.Center + Vector2.one * reach, out RectInt cells)) continue;

            for (int z = cells.yMin; z <= cells.yMax; z++)
            {
                for (int x = cells.xMin; x <= cells.xMax; x++)
                {
                    float distance = Vector2.Distance(GetCellWorld(area, x, z), spot.Center);
                    float stone = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(spot.Radius, reach, distance));
                    stones[z, x] = Mathf.Max(stones[z, x], stone);
                }
            }
        }
        return stones;
    }

    // ---------- 남쪽 강 둘레 ----------

    // B3은 구역 작업 전이라 강 둘레만 칠한다. 물가가 닿는 칸만 풀 바탕 + 물가로 새로 칠하고 나머지 칸은 그대로 두어,
    // 다시 실행해도 결과가 같고 B3의 다른 칸을 건드리지 않는다
    // 메뉴 9-2(MapDemoGroundTransplant)도 쓴다
    public static void PaintSouthShoreAlphamaps(Terrain terrain, WaterSource water, Rect south)
    {
        TerrainData data = terrain.terrainData;
        GridArea area = CreateArea(terrain, MapTerrainSetup.ToIndexRange(terrain, south, data.alphamapResolution), data.alphamapResolution);
        float[,] waterDistances = CreateWaterDistances(water, area);
        float[,,] weights = data.GetAlphamaps(area.Range.x, area.Range.y, area.Range.width, area.Range.height);

        for (int z = 0; z < area.Range.height; z++)
        {
            for (int x = 0; x < area.Range.width; x++)
            {
                float shore = GetShoreWeight(waterDistances[z, x], GetCellWorld(area, x, z));
                if (shore <= 0f) continue;

                for (int layer = 0; layer < data.alphamapLayers; layer++)
                    weights[z, x, layer] = 0f;
                weights[z, x, MapTerrainSetup.GrassLayer] = 1f;
                BlendShore(weights, z, x, shore);
            }
        }
        data.SetAlphamaps(area.Range.x, area.Range.y, weights);
    }

    // B3의 강 둘레 칸만 바닥 식생을 비운다. 메뉴 9-2(MapDemoGroundTransplant)도 쓴다
    public static void ClearSouthShoreDetails(Terrain terrain, WaterSource water, Rect south)
    {
        TerrainData data = terrain.terrainData;
        GridArea area = CreateArea(terrain, MapTerrainSetup.ToIndexRange(terrain, south, data.detailResolution), data.detailResolution);
        float[,] waterDistances = CreateWaterDistances(water, area);
        int prototypeCount = data.detailPrototypes.Length;

        for (int i = 0; i < prototypeCount; i++)
        {
            int[,] layer = data.GetDetailLayer(area.Range.x, area.Range.y, area.Range.width, area.Range.height, i);
            for (int z = 0; z < area.Range.height; z++)
            {
                for (int x = 0; x < area.Range.width; x++)
                {
                    if (GetShoreWeight(waterDistances[z, x], GetCellWorld(area, x, z)) > 0f) layer[z, x] = 0;
                }
            }
            data.SetDetailLayer(area.Range.x, area.Range.y, i, layer);
        }
    }

    // ---------- 격자, 계산 ----------

    private static GridArea CreateArea(Terrain terrain, RectInt range, int resolution)
    {
        TerrainData data = terrain.terrainData;
        Vector3 position = terrain.transform.position;
        return new GridArea
        {
            Data = data,
            Range = range,
            Resolution = resolution,
            Origin = new Vector2(position.x, position.z),
            BaseY = position.y,
            CellSize = new Vector2(data.size.x / resolution, data.size.z / resolution)
        };
    }

    private static GridArea CreateFullArea(Terrain terrain, int resolution)
    {
        return CreateArea(terrain, new RectInt(0, 0, resolution, resolution), resolution);
    }

    // area 안 [z, x] 칸 중심의 월드 XZ
    private static Vector2 GetCellWorld(GridArea area, int x, int z)
    {
        return area.Origin + Vector2.Scale(new Vector2(area.Range.x + x + 0.5f, area.Range.y + z + 0.5f), area.CellSize);
    }

    private static CellSample SampleCell(GridArea area, int x, int z)
    {
        float u = (area.Range.x + x + 0.5f) / area.Resolution;
        float v = (area.Range.y + z + 0.5f) / area.Resolution;
        float height = area.Data.GetInterpolatedHeight(u, v) + area.BaseY;
        return new CellSample
        {
            World = GetCellWorld(area, x, z),
            Height = height,
            Slope = area.Data.GetSteepness(u, v),
            Convexity = height - GetProbeAverageHeight(area, u, v)
        };
    }

    // 동서남북 RidgeProbeDistance 떨어진 네 곳의 평균 높이 (맵 끝 밖은 끝 높이로 본다)
    private static float GetProbeAverageHeight(GridArea area, float u, float v)
    {
        float du = RidgeProbeDistance / area.Data.size.x;
        float dv = RidgeProbeDistance / area.Data.size.z;
        float sum = area.Data.GetInterpolatedHeight(Mathf.Clamp01(u + du), v)
                    + area.Data.GetInterpolatedHeight(Mathf.Clamp01(u - du), v)
                    + area.Data.GetInterpolatedHeight(u, Mathf.Clamp01(v + dv))
                    + area.Data.GetInterpolatedHeight(u, Mathf.Clamp01(v - dv));
        return sum * 0.25f + area.BaseY;
    }

    // 월드 XZ 사각형(min ~ max)에 걸친 칸의 범위 (area 기준 번호, xMax·yMax 포함). 겹치는 칸이 없으면 false
    private static bool TryGetCellRange(GridArea area, Vector2 min, Vector2 max, out RectInt cells)
    {
        int xMin = Mathf.Max(Mathf.FloorToInt((min.x - area.Origin.x) / area.CellSize.x) - area.Range.x, 0);
        int zMin = Mathf.Max(Mathf.FloorToInt((min.y - area.Origin.y) / area.CellSize.y) - area.Range.y, 0);
        int xMax = Mathf.Min(Mathf.CeilToInt((max.x - area.Origin.x) / area.CellSize.x) - area.Range.x, area.Range.width - 1);
        int zMax = Mathf.Min(Mathf.CeilToInt((max.y - area.Origin.y) / area.CellSize.y) - area.Range.y, area.Range.height - 1);
        cells = new RectInt(xMin, zMin, xMax - xMin, zMax - zMin);
        return xMin <= xMax && zMin <= zMax;
    }

    private static float GetDistanceToSegment(Vector2 from, Vector2 to, Vector2 position)
    {
        Vector2 segment = to - from;
        float t = Mathf.Clamp01(Vector2.Dot(position - from, segment) / segment.sqrMagnitude);
        return Vector2.Distance(position, from + segment * t);
    }

    private static float SampleNoise(Vector2 world, float period, Vector2 offset)
    {
        return Mathf.PerlinNoise(world.x / period + offset.x, world.y / period + offset.y);
    }

    // ---------- 로그 ----------
    // 검수(옛 얼룩이 남지 않았는지, 구역별 칠한 비율과 돌 지면 면적, 물 위 바닥 식생 0개, 성능 측정용 인스턴스 수)에 쓰는 값이라 로그를 남긴다

    private static void LogReset(TerrainData data)
    {
        int resolution = data.alphamapResolution;
        float[,,] alphas = data.GetAlphamaps(0, 0, resolution, resolution);
        int grassCount = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                if (alphas[z, x, MapTerrainSetup.GrassLayer] >= FullWeight) grassCount++;
            }
        }

        int detailResolution = data.detailResolution;
        int prototypeCount = data.detailPrototypes.Length;
        long detailTotal = 0;
        for (int i = 0; i < prototypeCount; i++)
            detailTotal += data.GetDetailLayer(0, 0, detailResolution, detailResolution, i).Cast<int>().Sum();

        Debug.Log($"[MapGroundPainter] 초기화 완료. 풀만 칠한 칸 {grassCount * 100f / (resolution * resolution):F2}% (100%여야 한다), " +
                  $"바닥 식생 인스턴스 {detailTotal}개 (0이어야 한다, 프로토타입 {prototypeCount}종)");
    }

    private static void LogResult(NorthContext context)
    {
        Debug.Log($"[MapGroundPainter] 둘레를 돌로 칠한 절벽·큰 바위 {context.RockSpots.Count}개, 물이 보이는 호수 수면 정점 {context.Water.LakePoints.Count}개");
        LogAlphamaps(context);
        LogMountainGround(context);
        LogDetails(context);
    }

    // 산 영역(물가 제외)의 돌 : 초록 비율과 높이대별 돌 비율. 실제로 칠한 알파맵에서 잰다
    private static void LogMountainGround(NorthContext context)
    {
        TerrainData data = context.Terrain.terrainData;
        GridArea area = CreateArea(context.Terrain, MapTerrainSetup.ToIndexRange(context.Terrain, context.NorthArea, data.alphamapResolution), data.alphamapResolution);
        float[,,] alphas = data.GetAlphamaps(area.Range.x, area.Range.y, area.Range.width, area.Range.height);
        float[,] waterDistances = CreateWaterDistances(context.Water, area);
        int bandCount = BandLogNames.Length;
        var stones = new double[bandCount];
        var greens = new double[bandCount];
        var dirts = new double[bandCount];
        var counts = new int[bandCount];

        for (int z = 0; z < area.Range.height; z++)
        {
            for (int x = 0; x < area.Range.width; x++)
            {
                CellSample sample = SampleCell(area, x, z);
                if (GetShoreWeight(waterDistances[z, x], sample.World) > 0f) continue;

                MountainCell cell = GetMountainCell(context, sample, 0f);
                if (!cell.IsMountain) continue;

                int band = (int)cell.Band;
                stones[band] += alphas[z, x, MapTerrainSetup.StoneLayer];
                greens[band] += alphas[z, x, MapTerrainSetup.GrassLayer] + alphas[z, x, MapTerrainSetup.ForestGroundLayer];
                dirts[band] += alphas[z, x, MapTerrainSetup.DirtLayer];
                counts[band]++;
            }
        }

        double stone = stones.Sum();
        double green = greens.Sum();
        double dirt = dirts.Sum();
        int count = counts.Sum();
        double ratio = stone / (stone + green);
        bool isInRange = System.Math.Abs(ratio - StoneTargetRatio) <= StoneTargetTolerance;
        Debug.Log($"[MapGroundPainter] 산 영역 (물가 제외 {count}칸) 지면 : Stone {stone * 100 / count:F1}%, Grass+ForestGround {green * 100 / count:F1}%, Dirt {dirt * 100 / count:F1}% / " +
                  $"돌 : 초록 = {ratio * 100:F1} : {(1 - ratio) * 100:F1} (목표 돌 {(StoneTargetRatio - StoneTargetTolerance) * 100:F0} ~ {(StoneTargetRatio + StoneTargetTolerance) * 100:F0}%, {(isInRange ? "범위 안" : "범위 밖")}) / " +
                  $"돌 시작 경사 {context.StoneStartSlope:F1}도");

        var lines = new List<string>();
        for (int band = 0; band < bandCount; band++)
        {
            if (counts[band] == 0) continue;
            lines.Add($"{BandLogNames[band]} : 산 영역의 {counts[band] * 100f / count:F1}%, Stone {stones[band] * 100 / counts[band]:F1}%, " +
                      $"Grass+ForestGround {greens[band] * 100 / counts[band]:F1}%, Dirt {dirts[band] * 100 / counts[band]:F1}%");
        }
        Debug.Log("[MapGroundPainter] 높이대별 산 지면\n" + string.Join("\n", lines));
    }

    // 구역별 레이어 비율과 돌 지면 면적, 옛 얼룩 검사 (북쪽 줄, 중앙, 물가가 아닌 칸은 풀만 칠해져 있어야 한다)
    private static void LogAlphamaps(NorthContext context)
    {
        TerrainData data = context.Terrain.terrainData;
        GridArea area = CreateFullArea(context.Terrain, data.alphamapResolution);
        float[,,] alphas = data.GetAlphamaps(0, 0, area.Resolution, area.Resolution);
        float[,] waterDistances = CreateWaterDistances(context.Water, area);
        Rect[] cellRects = LogCellNames.Select(name => MapGrid.GetRangeBounds(name, name)).ToArray();
        Rect center = MapGrid.GetRangeBounds(MapTerrainSetup.CenterFromCell, MapTerrainSetup.CenterToCell);
        int layerCount = data.alphamapLayers;
        var layerSums = new float[cellRects.Length, layerCount];
        var cellCounts = new int[cellRects.Length];
        int checkedCount = 0;
        int grassCount = 0;

        for (int z = 0; z < area.Resolution; z++)
        {
            for (int x = 0; x < area.Resolution; x++)
            {
                Vector2 world = GetCellWorld(area, x, z);
                int cell = GetLogCellIndex(cellRects, world);
                cellCounts[cell]++;
                for (int layer = 0; layer < layerCount; layer++)
                    layerSums[cell, layer] += alphas[z, x, layer];

                if (context.NorthArea.Contains(world) || center.Contains(world) || GetShoreWeight(waterDistances[z, x], world) > 0f) continue;
                checkedCount++;
                if (alphas[z, x, MapTerrainSetup.GrassLayer] >= FullWeight) grassCount++;
            }
        }

        float cellArea = area.CellSize.x * area.CellSize.y;
        string[] layerNames = data.terrainLayers.Select(layer => layer.name.Replace("Layer_", "")).ToArray();
        for (int cell = 0; cell < cellRects.Length; cell++)
        {
            string ratios = string.Join(", ", Enumerable.Range(0, layerCount).Select(layer => $"{layerNames[layer]} {layerSums[cell, layer] * 100f / cellCounts[cell]:F1}%"));
            Debug.Log($"[MapGroundPainter] {LogCellNames[cell]} 지면 : {ratios} / 돌 지면 {layerSums[cell, MapTerrainSetup.StoneLayer] * cellArea / HectareArea:F1}ha");
        }
        Debug.Log($"[MapGroundPainter] 옛 얼룩 검사 (북쪽 줄, 중앙, 물가 밖 {checkedCount}칸) : 풀만 칠한 칸 {grassCount * 100f / checkedCount:F2}% (100%여야 한다)");
    }

    // 칸 중심은 칸 경계에 놓이지 않으므로 반드시 한 칸에 든다
    private static int GetLogCellIndex(Rect[] cellRects, Vector2 world)
    {
        for (int i = 0; i < cellRects.Length; i++)
        {
            if (cellRects[i].Contains(world)) return i;
        }
        return cellRects.Length - 1;
    }

    // 프로토타입별 인스턴스 수와 물 위(물 끝까지 거리 0 이하)에 놓인 바닥 식생 수 (0이어야 한다)
    private static void LogDetails(NorthContext context)
    {
        TerrainData data = context.Terrain.terrainData;
        GridArea area = CreateFullArea(context.Terrain, data.detailResolution);
        float[,] waterDistances = CreateWaterDistances(context.Water, area);
        DetailPrototype[] prototypes = data.detailPrototypes;
        var lines = new List<string>();
        long total = 0;
        long overWater = 0;

        for (int i = 0; i < prototypes.Length; i++)
        {
            int[,] layer = data.GetDetailLayer(0, 0, area.Resolution, area.Resolution, i);
            long prototypeTotal = 0;
            long northTotal = 0;
            for (int z = 0; z < area.Resolution; z++)
            {
                for (int x = 0; x < area.Resolution; x++)
                {
                    int count = layer[z, x];
                    if (count == 0) continue;

                    prototypeTotal += count;
                    if (context.NorthArea.Contains(GetCellWorld(area, x, z))) northTotal += count;
                    if (waterDistances[z, x] <= 0f) overWater += count;
                }
            }
            total += prototypeTotal;
            lines.Add($"{prototypes[i].prototype.name}: 전체 {prototypeTotal}, 북쪽 줄 {northTotal}");
        }
        Debug.Log($"[MapGroundPainter] 바닥 식생 인스턴스 총 {total}개\n" + string.Join("\n", lines));
        Debug.Log($"[MapGroundPainter] 물 위에 놓인 바닥 식생 {overWater}개 (0이어야 한다)");
    }
}
