using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 메뉴 20 : 남동쪽 구역(C3)에 넓고 불규칙한 물이 퍼진 낮은 습지 지형과 높은 바위 언덕 하나를 만든다.
// 습지 바닥은 주기가 다른 Perlin 노이즈 3겹을 더해 한 직선으로 옮긴 높이(높낮이 약 FloorRange 6m, 대략 27 ~ 33m)다.
// 연못을 따로 파지 않고, 수면(WaterLevel 29m)보다 낮은 땅이 모두 물이 된다 (수면은 메뉴 21이 놓는다).
// 물 넓이는 분위수로 맞춘다. 물이 생길 수 있는 영역(경계, 서식지 올림 구역과 언덕 윤곽 밖)의 노이즈 표본 중
// 하위 WaterAreaRatio 값이 수면 높이가 되도록 옮겨, 그 영역에서 수면보다 낮은 땅이 WaterAreaRatio가 되게 한다.
// C3 경계에서 EdgeDryDistance(80m) 안과 서식지 반경 + HabitatDryMargin 안은 낮은 땅만 DryHeight(29.6m)까지 올려 물이 생기지 않게 하고,
// 그 바깥으로 부드럽게(smoothstep) 원래 바닥과 섞는다.
// 메뉴 21이 물을 찾는 기준(FloodHeight = 수면 + 묻힘 깊이 MapSouthEastWater.ShoreBuryDepth = 29.5m) 아래로 이어진 땅 중
// MinWaterArea(300m²)보다 작은 웅덩이는 낮은 둔덕으로 바꿔 없앤다.
// 높은 언덕은 구역을 멀리 내려다보는 지점이며, 남서쪽(메뉴 14)과 같은 이유로 습지 바닥 위에 언덕 상승량을 더하고
// 거리를 재는 위치를 비틀어 윤곽이 원이 아니게 한다.
// 네 경계(맵 바깥 끝인 동, 남 포함) 안쪽 감쇠 폭(기본 75m, 위치에 따라 50 ~ 100m로 흔들림) 안에서 바탕 높이로 돌아가고,
// 경계 줄은 쓰지 않아 옆 구역(C2, B3)의 높이를 건드리지 않는다.
// 수면 높이, 물 판정 기준, 언덕, 서식지 위치는 이 스크립트가 단일출처이고 메뉴 21(수면), 22, 23이 가져다 쓴다.
// 연못 목록은 없으므로 메뉴 22, 23은 물인지를 "지형 높이 < WaterLevel"로 지형에서 직접 잰다.
// 매번 바탕 높이 기준으로 C3 안쪽을 다시 계산해 덮어쓰므로 여러 번 실행해도 결과가 같다.
// 실행 순서 : ... → 19 → 20 → 21 → 22 → 23 → 10.
// 1-1(평탄화)을 다시 실행하면 20도 다시 실행한다. 20을 다시 실행하면 지형 높이가 바뀌므로 21, 22, 23도 순서대로 다시 실행한다.
// 바닥 노이즈, 물 비율, 경계와 서식지 올림 상수를 바꾸면 물 자리가 바뀌므로 20부터 다시 실행한다.
public static class MapSouthEastShaper
{
    // 남동쪽 칸. 수면(메뉴 21)도 같은 칸 범위를 읽으므로 공개한다
    public const string SouthEastCell = "C3";

    // 임시값. 앞 메뉴들의 seed(20261109 ~ 20261147)와 겹치지 않게 둔다
    private const int Seed = 20261148;

    // 습지 바닥 노이즈 3겹의 주기 (m)와 비중 (임시값). 긴 주기가 큰 물 덩어리를, 짧은 주기가 물가의 불규칙한 굴곡을 만든다
    // 비중 : 45m 주기 비중을 높여 작은 둔덕, 물길, 섬을 늘림. 사용자 승인 임시값
    private static readonly float[] FloorPeriods = { 300f, 120f, 45f };
    private static readonly float[] FloorWeights = { 0.35f, 0.35f, 0.30f };
    // 물이 생길 수 있는 영역의 노이즈 최저 ~ 최고에 대응하는 바닥 높낮이 (m)
    private const float FloorRange = 6f;
    // 물이 생길 수 있는 영역에서 수면보다 낮은 땅의 비율 (임시값)
    private const float WaterAreaRatio = 0.25f;
    // 분위수를 구할 노이즈 표본 간격 (m). 남쪽 구역 나누기(MapSouthScatter)와 같은 방식
    private const float FloorSampleSpacing = 5f;

    // 경계 감쇠 폭 (기본 폭 ± 흔들림 비율 = 50 ~ 100m)과 폭 노이즈 주기 (m). 남쪽(메뉴 17)과 같은 값
    private const float EdgeFadeWidth = 75f;
    private const float EdgeFadeWobble = 1f / 3f;
    private const float EdgeFadeWobblePeriod = 150f;
    // 폭 노이즈를 바닥, 언덕 비틀기 노이즈와 다른 무늬에서 뽑기 위한 위치
    private static readonly Vector2 EdgeFadeNoiseOffset = new Vector2(710f, 290f);

    // 수면 높이 (m). 수면(메뉴 21)이 쓰므로 공개한다
    public const float WaterLevel = 29f;
    // 물 판정 높이. 메뉴 21이 물을 찾는 기준(수면 + 묻힘 깊이 29.5m)과 같다
    private const float FloodHeight = WaterLevel + MapSouthEastWater.ShoreBuryDepth;
    // 물이 생기지 않게 올릴 때의 최저 높이 (물 판정 높이 + 여유 0.1m = 29.6m)
    private const float DryMargin = 0.1f;
    private const float DryHeight = FloodHeight + DryMargin;
    // 이보다 작은 물웅덩이는 없앤다 (m², 임시값). 메뉴 21이 남은 웅덩이를 경고할 때도 쓰므로 공개한다
    public const float MinWaterArea = 300f;
    // 없앤 웅덩이를 둔덕으로 바꿀 때 원래 깊이(물 판정 높이 아래)에 곱하는 비율 (임시값)
    private const float PuddleMoundRatio = 0.3f;

    // C3 경계에서 이 거리 안은 물이 생기지 않는다 (m). 메뉴 21이 물이 샜는지 판정할 때도 쓰므로 공개한다
    public const float EdgeDryDistance = 80f;
    // 경계 쪽으로 바닥을 올리기 시작하는 거리 (m)
    private const float EdgeDryBlendDistance = 150f;

    // 서식지 (임시값). 남동쪽 배치(메뉴 22)가 서식지 위치 표시와 비움 판정에 쓰므로 공개한다
    public static readonly Vector2 HabitatCenter = new Vector2(750f, -750f);
    public const float HabitatRadius = 80f;
    // 서식지 반경 + 이 여유 안은 물이 생기지 않는다 (m)
    private const float HabitatDryMargin = 20f;
    private const float HabitatDryRadius = HabitatRadius + HabitatDryMargin;
    // 그 바깥으로 바닥을 섞어 올리는 폭 (m, 임시값)
    private const float HabitatDryBlendWidth = 50f;

    // 높은 바위 언덕 (임시값). 중심, 반지름은 남동쪽 배치(메뉴 22)도 언덕 꼭대기와 언덕 위 판정에 쓰므로 공개한다
    public static readonly Vector2 HillCenter = new Vector2(980f, -980f);
    private const float HillRise = 40f;
    public const float HillRadius = 120f;
    // 언덕까지 거리를 잴 위치를 비트는 최대 거리와 비틀기 노이즈 주기 (m). 주기는 남서쪽 언덕과 같다
    private const float HillWarpDistance = 30f;
    private const float HillWarpPeriod = 150f;
    // x, z 비틀기가 서로 다른 무늬를 쓰도록 노이즈 위치를 따로 둔다
    private static readonly Vector2 HillWarpOffsetX = new Vector2(430f, 570f);
    private static readonly Vector2 HillWarpOffsetZ = new Vector2(880f, 330f);

    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;

    // 높이 계산에 함께 쓰는 값
    private class ShapeField
    {
        public Rect SouthEast;
        // 노이즈 겹마다 무늬 위치
        public Vector2[] FloorOffsets;
        // 수면 높이에 대응하는 노이즈 값 (하위 WaterAreaRatio 분위수)과 노이즈 1당 높이 (m)
        public float WaterNoise;
        public float HeightPerNoise;
        // 표본 노이즈 최저에 대응하는 바닥 높이 (검수 로그용, m)
        public float ExpectedBottom;
        public float HillLift;
    }

    // 작은 웅덩이 정리 결과
    private struct PuddleResult
    {
        public int RemovedCount;
        // 남긴 물 덩어리마다 넓이 (물 판정 높이 아래 꼭짓점 기준, m²)
        public List<float> KeptAreas;
    }

    // 검수 로그에 쓸 높이 통계
    private struct HeightStats
    {
        // 쓴 점 전체의 높이 (x = 최저, y = 최고, m)
        public Vector2 WrittenRange;
        // 습지 바닥(언덕 윤곽, 경계 올림 구역 밖)의 높이 (x = 최저, y = 최고, m)
        public Vector2 OpenFloorRange;
        public float PeakHeight;
        public Vector2 PeakPosition;
        // 쓴 점들에서 실제 경계 감쇠 폭의 최저, 최고 (m)
        public float FadeWidthMin;
        public float FadeWidthMax;
        // 쓴 점 수와 그중 수면보다 낮은 점 수
        public int TotalCount;
        public int WetCount;
        // 물이 생길 수 있는 영역의 점 수와 그중 수면보다 낮은 점 수
        public int WaterAreaCount;
        public int WaterAreaWetCount;
        // 경계 EdgeDryDistance 안, 서식지 HabitatDryRadius 안에서 물 판정 높이보다 낮은 점 수 (0이어야 함)
        public int EdgeLeakCount;
        public int HabitatLeakCount;
    }

    [MenuItem("Tools/Map/20. 남동쪽 지형 만들기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        Rect southEast = MapGrid.GetRangeBounds(SouthEastCell, SouthEastCell);
        int divisions = data.heightmapResolution - 1;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, southEast, divisions);

        // 가장자리 줄은 옆 구역과 같이 쓰는 꼭짓점(동, 남은 맵 끝)이라 바탕 높이 그대로 두고, 그 안쪽만 덮어쓴다
        var inner = new RectInt(range.x + 1, range.y + 1, range.width - 1, range.height - 1);
        float spacing = data.size.x / divisions;
        Vector3 position = terrain.transform.position;
        var origin = new Vector2(position.x + inner.x * spacing, position.z + inner.y * spacing);

        ShapeField field = CreateField(southEast);
        float[,] heights = CalculateHeights(origin, spacing, inner, field);
        PuddleResult puddles = RemoveSmallPuddles(heights, spacing);
        HeightStats stats = MeasureHeights(origin, spacing, heights, field);
        data.SetHeights(inner.x, inner.y, ToNormalized(heights, data.size.y));

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        // 검수 때 높이 범위, 물 넓이, 작은 웅덩이, 물이 경계와 서식지로 새지 않는지, 경계가 이어졌는지, C3 밖을 건드리지 않았는지 확인할 값이라 남겨 둔다
        float borderDeviation = GetMaxBorderDeviation(data, range);
        bool isInsideSouthEast = IsInside(terrain, southEast, inner);
        LogResult(stats, field, puddles);
        Debug.Log($"[MapSouthEastShaper] 경계 줄 {MapTerrainSetup.BaseHeight}m 대비 최대 차이 {borderDeviation:F3}m, " +
                  $"경계 감쇠 폭 {stats.FadeWidthMin:F1} ~ {stats.FadeWidthMax:F1}m");
        Debug.Log($"[MapSouthEastShaper] C3 밖 쓰기 {(isInsideSouthEast ? "없음" : "있음")} " +
                  $"(높이맵 번호 x {inner.xMin} ~ {inner.xMax - 1}, z {inner.yMin} ~ {inner.yMax - 1})");
    }

    // 비틀린 위치가 언덕 중심에 있으면 1, 반지름 끝에서 0이 되도록 부드럽게 줄어드는 가중치.
    // 남동쪽 배치(메뉴 22)가 언덕 위 판정을 지형과 같은 윤곽으로 하도록 공개한다 (경계 감쇠는 곱하지 않은 값)
    public static float CalculateHillWeight(Vector2 world)
    {
        Vector2 warped = world + SampleWarp(world / HillWarpPeriod, HillWarpOffsetX, HillWarpOffsetZ) * HillWarpDistance;
        float distance = Vector2.Distance(warped, HillCenter);
        return Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(distance / HillRadius));
    }

    // 가장 가까운 C3 경계까지 거리 (m). 메뉴 21이 물이 경계 띠에 닿았는지 판정할 때도 쓰므로 공개한다
    public static float GetEdgeDistance(Vector2 world, Rect southEast)
    {
        return Mathf.Min(
            Mathf.Min(world.x - southEast.xMin, southEast.xMax - world.x),
            Mathf.Min(world.y - southEast.yMin, southEast.yMax - world.y));
    }

    private static ShapeField CreateField(Rect southEast)
    {
        var random = new System.Random(Seed);
        var field = new ShapeField
        {
            SouthEast = southEast,
            FloorOffsets = FloorPeriods.Select(_ => CreateNoiseOffset(random)).ToArray()
        };
        SetFloorMapping(field);
        field.HillLift = CalculateHillLift(field);
        return field;
    }

    private static Vector2 CreateNoiseOffset(System.Random random)
    {
        float x = NoiseOffsetMin + (float)random.NextDouble() * (NoiseOffsetMax - NoiseOffsetMin);
        float z = NoiseOffsetMin + (float)random.NextDouble() * (NoiseOffsetMax - NoiseOffsetMin);
        return new Vector2(x, z);
    }

    // Perlin 노이즈는 값이 가운데에 몰려 고정 임계값으로는 물 비율을 맞추기 어려워, 남쪽 구역 나누기처럼 분위수로 정한다.
    // 물이 생길 수 있는 영역의 표본만 모아 하위 WaterAreaRatio 값을 수면 높이에, 표본 최저 ~ 최고 폭을 FloorRange에 대응시킨다
    private static void SetFloorMapping(ShapeField field)
    {
        Rect southEast = field.SouthEast;
        int columns = Mathf.RoundToInt(southEast.width / FloorSampleSpacing);
        int rows = Mathf.RoundToInt(southEast.height / FloorSampleSpacing);
        var samples = new List<float>(columns * rows);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var world = new Vector2(southEast.xMin + (x + 0.5f) * FloorSampleSpacing, southEast.yMin + (z + 0.5f) * FloorSampleSpacing);
                if (IsWaterArea(world, southEast)) samples.Add(SampleFloorNoise(world, field.FloorOffsets));
            }
        }
        samples.Sort();

        float noiseMin = samples[0];
        float noiseMax = samples[samples.Count - 1];
        field.WaterNoise = samples[Mathf.FloorToInt(samples.Count * WaterAreaRatio)];
        field.HeightPerNoise = FloorRange / (noiseMax - noiseMin);
        field.ExpectedBottom = WaterLevel + (noiseMin - field.WaterNoise) * field.HeightPerNoise;
    }

    // 물이 생길 수 있는 영역 : 경계 올림 구역, 서식지 올림 구역, 언덕 윤곽 밖. 이 영역의 바닥은 노이즈를 옮긴 높이 그대로다
    private static bool IsWaterArea(Vector2 world, Rect southEast)
    {
        return IsOpenFloor(world, southEast) && Vector2.Distance(world, HabitatCenter) >= HabitatDryRadius + HabitatDryBlendWidth;
    }

    // 언덕 윤곽과 경계 올림 구역 밖의 습지 바닥
    private static bool IsOpenFloor(Vector2 world, Rect southEast)
    {
        return GetEdgeDistance(world, southEast) >= EdgeDryBlendDistance && CalculateHillWeight(world) <= 0f;
    }

    // 노이즈 3겹의 가중합 (대략 0 ~ 1)
    private static float SampleFloorNoise(Vector2 world, Vector2[] offsets)
    {
        float noise = 0f;
        for (int i = 0; i < FloorPeriods.Length; i++)
            noise += FloorWeights[i] * Mathf.PerlinNoise(world.x / FloorPeriods[i] + offsets[i].x, world.y / FloorPeriods[i] + offsets[i].y);
        return noise;
    }

    // 노이즈를 한 직선으로 옮긴 습지 바닥. 경계와 서식지 가까이에서는 DryHeight보다 낮은 땅만 그 높이 쪽으로 올리고,
    // 높은 땅은 그대로 두어 원래 굴곡을 살린다. 올림 가중치는 경계(80 ~ 150m)와 서식지(100 ~ 150m) 중 큰 값이다
    private static float CalculateFloorHeight(Vector2 world, ShapeField field)
    {
        float floor = WaterLevel + (SampleFloorNoise(world, field.FloorOffsets) - field.WaterNoise) * field.HeightPerNoise;
        float edgeDryWeight = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(EdgeDryBlendDistance, EdgeDryDistance, GetEdgeDistance(world, field.SouthEast)));
        float habitatDryWeight = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(HabitatDryRadius + HabitatDryBlendWidth, HabitatDryRadius, Vector2.Distance(world, HabitatCenter)));
        return floor + Mathf.Max(0f, DryHeight - floor) * Mathf.Max(edgeDryWeight, habitatDryWeight);
    }

    // 언덕 중심이 꼭대기 목표 높이(바탕 + HillRise)에 닿도록, 중심의 (언덕을 더하기 전) 습지 바닥 높이와의 차이만큼 올린다
    private static float CalculateHillLift(ShapeField field)
    {
        float centerFloor = (CalculateFloorHeight(HillCenter, field) - MapTerrainSetup.BaseHeight) * CalculateEdgeMask(HillCenter, field.SouthEast);
        return HillRise - centerFloor;
    }

    // C3 안쪽 점마다 월드 높이 (m) [z, x]
    private static float[,] CalculateHeights(Vector2 origin, float spacing, RectInt inner, ShapeField field)
    {
        var heights = new float[inner.height, inner.width];
        for (int z = 0; z < inner.height; z++)
        {
            for (int x = 0; x < inner.width; x++)
                heights[z, x] = CalculateGroundHeight(origin + new Vector2(x, z) * spacing, field);
        }
        return heights;
    }

    // 습지 바닥에 언덕을 더한 높이. 바닥과 언덕 모두 경계 감쇠를 받아 경계 줄에서 바탕 높이다.
    // 경계 80m 안은 바닥이 DryHeight 이상이고 언덕 상승은 0 이상이라, 바탕(30m)과 섞여도 DryHeight 아래로 내려가지 않는다
    private static float CalculateGroundHeight(Vector2 world, ShapeField field)
    {
        float lift = CalculateFloorHeight(world, field) - MapTerrainSetup.BaseHeight + field.HillLift * CalculateHillWeight(world);
        return MapTerrainSetup.BaseHeight + lift * CalculateEdgeMask(world, field.SouthEast);
    }

    // 물 판정 높이 아래로 이어진(8방향, 메뉴 21과 같음) 점 덩어리를 찾아, 넓이가 MinWaterArea 미만이면 없앤다.
    // 평평하게 메우면 판판한 면이 남고, 깊이를 그대로 뒤집으면 둔덕이 너무 높아지므로,
    // 덩어리 가장자리(물 판정 높이 근처)에서 DryHeight로 시작해 원래 깊었던 곳일수록 조금 더 높은(깊이 × PuddleMoundRatio) 낮은 둔덕으로 바꾼다
    private static PuddleResult RemoveSmallPuddles(float[,] heights, float spacing)
    {
        var isVisited = new bool[heights.GetLength(0), heights.GetLength(1)];
        var result = new PuddleResult { KeptAreas = new List<float>() };
        float cellArea = spacing * spacing;
        for (int z = 0; z < heights.GetLength(0); z++)
        {
            for (int x = 0; x < heights.GetLength(1); x++)
            {
                if (isVisited[z, x] || heights[z, x] >= FloodHeight) continue;

                List<Vector2Int> puddle = FloodLowGround(heights, isVisited, new Vector2Int(x, z));
                float area = puddle.Count * cellArea;
                if (area >= MinWaterArea)
                {
                    result.KeptAreas.Add(area);
                    continue;
                }

                foreach (Vector2Int point in puddle)
                    heights[point.y, point.x] = DryHeight + (FloodHeight - heights[point.y, point.x]) * PuddleMoundRatio;
                result.RemovedCount++;
            }
        }
        return result;
    }

    // 시작 점에서 8방향 이웃으로 물 판정 높이보다 낮은 점을 모두 모은다
    private static List<Vector2Int> FloodLowGround(float[,] heights, bool[,] isVisited, Vector2Int seed)
    {
        int depth = heights.GetLength(0);
        int width = heights.GetLength(1);
        var points = new List<Vector2Int> { seed };
        var queue = new Queue<Vector2Int>();
        isVisited[seed.y, seed.x] = true;
        queue.Enqueue(seed);
        while (queue.Count > 0)
        {
            Vector2Int point = queue.Dequeue();
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = point.x + dx;
                    int z = point.y + dz;
                    if (x < 0 || z < 0 || x >= width || z >= depth) continue;
                    if (isVisited[z, x] || heights[z, x] >= FloodHeight) continue;

                    isVisited[z, x] = true;
                    points.Add(new Vector2Int(x, z));
                    queue.Enqueue(new Vector2Int(x, z));
                }
            }
        }
        return points;
    }

    // 작은 웅덩이를 정리한 뒤의 최종 높이로 통계를 낸다
    private static HeightStats MeasureHeights(Vector2 origin, float spacing, float[,] heights, ShapeField field)
    {
        var stats = new HeightStats
        {
            WrittenRange = new Vector2(float.MaxValue, float.MinValue), OpenFloorRange = new Vector2(float.MaxValue, float.MinValue),
            PeakHeight = float.MinValue, FadeWidthMin = float.MaxValue, FadeWidthMax = float.MinValue
        };
        for (int z = 0; z < heights.GetLength(0); z++)
        {
            for (int x = 0; x < heights.GetLength(1); x++)
            {
                Vector2 world = origin + new Vector2(x, z) * spacing;
                float height = heights[z, x];
                bool isWet = height < WaterLevel;
                bool isFlooded = height < FloodHeight;

                stats.WrittenRange.x = Mathf.Min(stats.WrittenRange.x, height);
                stats.WrittenRange.y = Mathf.Max(stats.WrittenRange.y, height);
                float fadeWidth = CalculateEdgeFadeWidth(world);
                stats.FadeWidthMin = Mathf.Min(stats.FadeWidthMin, fadeWidth);
                stats.FadeWidthMax = Mathf.Max(stats.FadeWidthMax, fadeWidth);

                stats.TotalCount++;
                if (isWet) stats.WetCount++;
                if (IsWaterArea(world, field.SouthEast))
                {
                    stats.WaterAreaCount++;
                    if (isWet) stats.WaterAreaWetCount++;
                }
                if (IsOpenFloor(world, field.SouthEast))
                {
                    stats.OpenFloorRange.x = Mathf.Min(stats.OpenFloorRange.x, height);
                    stats.OpenFloorRange.y = Mathf.Max(stats.OpenFloorRange.y, height);
                }
                if (isFlooded && GetEdgeDistance(world, field.SouthEast) < EdgeDryDistance) stats.EdgeLeakCount++;
                if (isFlooded && Vector2.Distance(world, HabitatCenter) < HabitatDryRadius) stats.HabitatLeakCount++;

                // 비틀린 윤곽 기준. C3 안쪽은 경계 감쇠값이 0보다 커서 언덕 모양 가중치만 보면 된다
                if (CalculateHillWeight(world) <= 0f || height <= stats.PeakHeight) continue;
                stats.PeakHeight = height;
                stats.PeakPosition = world;
            }
        }
        return stats;
    }

    private static float[,] ToNormalized(float[,] heights, float terrainHeight)
    {
        var normalized = new float[heights.GetLength(0), heights.GetLength(1)];
        for (int z = 0; z < heights.GetLength(0); z++)
        {
            for (int x = 0; x < heights.GetLength(1); x++)
                normalized[z, x] = heights[z, x] / terrainHeight;
        }
        return normalized;
    }

    // 남동쪽 구역 경계에 가까울수록 0에 가까워져, 옆 구역과 맵 끝에서 바탕 높이로 만나게 한다.
    // 가장 가까운 경계까지 거리를 그 위치의 감쇠 폭으로 나누므로, 거리 0(경계 줄)에서는 폭과 상관없이 0이다
    private static float CalculateEdgeMask(Vector2 world, Rect southEast)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, CalculateEdgeFadeWidth(world), GetEdgeDistance(world, southEast)));
    }

    // 위치에 따라 흔들리는 감쇠 폭 (m). 월드 위치로 노이즈를 뽑아 경계를 따라, 그리고 모서리를 돌아서도 끊김 없이 이어진다
    private static float CalculateEdgeFadeWidth(Vector2 world)
    {
        float perlin = Mathf.PerlinNoise(
            world.x / EdgeFadeWobblePeriod + EdgeFadeNoiseOffset.x,
            world.y / EdgeFadeWobblePeriod + EdgeFadeNoiseOffset.y);
        float wobble = Mathf.Clamp(perlin * 2f - 1f, -1f, 1f);
        return EdgeFadeWidth * (1f + EdgeFadeWobble * wobble);
    }

    // x, z 각각 -1 ~ 1 노이즈 벡터. -1 ~ 1로 잘라 비틀기가 최대 거리를 넘지 않게 한다
    private static Vector2 SampleWarp(Vector2 sample, Vector2 offsetX, Vector2 offsetZ)
    {
        float warpX = Mathf.PerlinNoise(sample.x + offsetX.x, sample.y + offsetX.y) * 2f - 1f;
        float warpZ = Mathf.PerlinNoise(sample.x + offsetZ.x, sample.y + offsetZ.y) * 2f - 1f;
        return new Vector2(Mathf.Clamp(warpX, -1f, 1f), Mathf.Clamp(warpZ, -1f, 1f));
    }

    private static void LogResult(HeightStats stats, ShapeField field, PuddleResult puddles)
    {
        Debug.Log($"[MapSouthEastShaper] 남동쪽(C3) 높이 최저 {stats.WrittenRange.x:F2}m, 최고 {stats.WrittenRange.y:F1}m, " +
                  $"언덕 꼭대기 {stats.PeakHeight:F1}m (x {stats.PeakPosition.x:F1}, z {stats.PeakPosition.y:F1}), 언덕 상승량 {field.HillLift:F1}m");
        Debug.Log($"[MapSouthEastShaper] 습지 바닥(언덕, 경계 {EdgeDryBlendDistance}m 띠 제외) 최저 {stats.OpenFloorRange.x:F2}m, 최고 {stats.OpenFloorRange.y:F2}m " +
                  $"(분위수 매핑 : 수면 노이즈 {field.WaterNoise:F3}, 노이즈 1당 {field.HeightPerNoise:F2}m, 표본 최저 바닥 {field.ExpectedBottom:F2}m)");
        Debug.Log($"[MapSouthEastShaper] 수면 {WaterLevel}m보다 낮은 점 비율 : 물이 생길 수 있는 영역 기준 " +
                  $"{(float)stats.WaterAreaWetCount / stats.WaterAreaCount:P1} (목표 {WaterAreaRatio:P0}, 작은 웅덩이 정리로 조금 줄 수 있음), " +
                  $"C3 전체 기준 {(float)stats.WetCount / stats.TotalCount:P1}");

        string keptDetail = puddles.KeptAreas.Count == 0
            ? "없음"
            : $"가장 작은 넓이 {puddles.KeptAreas.Min():F0}m², 가장 큰 넓이 {puddles.KeptAreas.Max():F0}m²";
        Debug.Log($"[MapSouthEastShaper] {MinWaterArea}m² 미만 웅덩이 {puddles.RemovedCount}개 정리, " +
                  $"남은 물 덩어리 {puddles.KeptAreas.Count}개 ({FloodHeight}m 아래 점 기준) : {keptDetail}");
        Debug.Log($"[MapSouthEastShaper] {FloodHeight}m 미만 점 : 경계 {EdgeDryDistance}m 안 {stats.EdgeLeakCount}개, " +
                  $"서식지 {HabitatDryRadius}m 안 {stats.HabitatLeakCount}개 (모두 0이어야 함)");
    }

    // 네 가장자리 줄의 높이가 바탕 높이에서 가장 많이 벗어난 값 (m)
    private static float GetMaxBorderDeviation(TerrainData data, RectInt range)
    {
        int width = range.width + 1;
        int depth = range.height + 1;
        float[,] heights = data.GetHeights(range.x, range.y, width, depth);

        float maxDeviation = 0f;
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isBorder = x == 0 || x == width - 1 || z == 0 || z == depth - 1;
                if (!isBorder) continue;
                maxDeviation = Mathf.Max(maxDeviation, Mathf.Abs(heights[z, x] * data.size.y - MapTerrainSetup.BaseHeight));
            }
        }
        return maxDeviation;
    }

    // 쓴 범위의 양 끝 꼭짓점이 C3 경계선보다 안쪽에 있는지 (경계선 위도 밖으로 본다)
    private static bool IsInside(Terrain terrain, Rect southEast, RectInt inner)
    {
        TerrainData data = terrain.terrainData;
        Vector3 position = terrain.transform.position;
        float sampleSpacing = data.size.x / (data.heightmapResolution - 1);
        var first = new Vector2(position.x + inner.xMin * sampleSpacing, position.z + inner.yMin * sampleSpacing);
        var last = new Vector2(position.x + (inner.xMax - 1) * sampleSpacing, position.z + (inner.yMax - 1) * sampleSpacing);
        return first.x > southEast.xMin && first.y > southEast.yMin && last.x < southEast.xMax && last.y < southEast.yMax;
    }
}
