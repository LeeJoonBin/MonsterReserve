using UnityEditor;
using UnityEngine;

// 메뉴 14 : 남서쪽 구역(A3)에 넓고 완만하게 오르내리는 초원 지형과 높은 언덕 하나를 만든다.
// 서쪽(메뉴 11)은 "바탕 ± 방식은 분지처럼 보였다"는 이유로 바탕 위로만 솟게 했지만, 남서쪽은 의도적으로 바탕 아래로도 내려간다 (바탕 ± 진폭).
// 대신 노이즈 주기를 서쪽보다 길게 잡아, 꺼진 곳이 좁은 구덩이가 아니라 넓은 낮은 땅으로 이어지게 해 분지처럼 보이지 않게 한다.
// 높은 언덕은 구역을 멀리 내려다보는 지점이며, 노이즈 지형 위에 언덕 상승량을 더해 비탈에도 주변 잔굴곡이 남게 한다.
// 노이즈를 덮어쓰면 언덕 위가 매끈한 돔이 되어 주변 땅과 어울리지 않았기 때문이다.
// 언덕 윤곽은 거리를 재는 위치를 노이즈로 비틀어(도메인 워핑) 원이 아니게 하고, 경계 감쇠도 함께 받아 경계 줄 높이를 지킨다.
// 방향마다 반지름을 흔드는 방식은 중심 근처에서 각도가 급히 바뀌어 꼭대기로 모이는 방사형 골(별 모양)이 생겼기 때문이다.
// 네 경계 안쪽 감쇠 폭(기본 100m, 위치에 따라 50 ~ 150m로 흔들림) 안에서 바탕 높이로 돌아가고, 경계 줄은 쓰지 않아 서쪽(A2), 남쪽(B3)의 높이를 건드리지 않는다.
// 폭을 고정하면 경계 안쪽 둑이 경계와 나란한 직선으로 보여서 폭을 흔든다.
// 매번 바탕 높이 기준으로 A3 안쪽을 다시 계산해 덮어쓰므로 여러 번 실행해도 결과가 같다.
// 실행 순서 : ... → 13 → 14 → 15 → 16 → 10. 14를 다시 실행하면 지형 높이가 바뀌므로 15, 16도 순서대로 다시 실행한다.
public static class MapSouthWestShaper
{
    private const string SouthWestFromCell = "A3";
    private const string SouthWestToCell = "A3";

    // 임시값. 다른 구역과 노이즈 무늬가 겹치지 않도록 새 값을 쓴다
    private const int Seed = 20261130;
    // 노이즈를 A3 안 실제 범위로 늘린 뒤 바탕 아래, 위로 내려가고 오르는 최대 높이 (바탕 30m에서 약 2 ~ 65m).
    // 여러 겹 노이즈는 끝값(-1, 1)까지 거의 가지 않아 진폭 하나로는 굴곡이 작게 나왔다.
    // 아래쪽은 Terrain 높이 하한 0m에 닿지 않도록 바탕(30m)보다 조금 작게 둔다
    private const float AmplitudeBelow = 28f;
    private const float AmplitudeAbove = 35f;
    // 경계 감쇠 폭 (기본 폭 ± 흔들림 비율 = 50 ~ 150m)과 폭 노이즈 주기 (m)
    private const float EdgeFadeWidth = 100f;
    private const float EdgeFadeWobble = 0.5f;
    private const float EdgeFadeWobblePeriod = 150f;
    // 폭 노이즈를 지형, 언덕 비틀기 노이즈와 다른 무늬에서 뽑기 위한 위치
    private static readonly Vector2 EdgeFadeNoiseOffset = new Vector2(450f, 950f);

    // 높은 언덕 (임시값). 반지름은 기본 경계 감쇠 구역(경계에서 100m)에 닿지 않게 고른 값이다.
    // 비틀기로 윤곽이 감쇠 구역에 들어가는 쪽은 경계 감쇠를 함께 받는다.
    // 중심, 반지름은 남서쪽 배치(메뉴 15)도 언덕 꼭대기와 언덕 위 판정에 쓰므로 공개한다
    public static readonly Vector2 HillCenter = new Vector2(-480f, -480f);
    private const float HillRise = 40f;
    public const float HillRadius = 120f;
    // 언덕까지 거리를 잴 위치를 비트는 최대 거리와 비틀기 노이즈 주기 (m)
    private const float HillWarpDistance = 40f;
    private const float HillWarpPeriod = 150f;
    // x, z 비틀기가 서로 다른 무늬를 쓰도록 노이즈 위치를 따로 둔다
    private static readonly Vector2 HillWarpOffsetX = new Vector2(300f, 700f);
    private static readonly Vector2 HillWarpOffsetZ = new Vector2(800f, 200f);

    // 여러 겹의 Perlin 노이즈 (큰 오르내림 + 중간 굴곡 + 잔굴곡).
    // 서쪽(400 / 150 / 60m)보다 주기를 길게 잡아, 바탕 아래로 내려가도 넓고 완만한 낮은 땅이 되게 한다
    private static readonly float[] OctavePeriods = { 500f, 200f, 70f };
    private static readonly float[] OctaveWeights = { 0.6f, 0.3f, 0.1f };

    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;

    // 검수 로그에 쓸 높이 통계
    private struct HeightStats
    {
        // 언덕 반지름 밖의 높이 (x = 최저, y = 최고, m)
        public Vector2 OutsideHillRange;
        public float PeakHeight;
        public Vector2 PeakPosition;
        // 정규화에 쓴 노이즈 원값의 최저, 최고
        public float NoiseMin;
        public float NoiseMax;
        // 쓴 점들에서 실제 경계 감쇠 폭의 최저, 최고 (m)
        public float FadeWidthMin;
        public float FadeWidthMax;
    }

    // 노이즈 무늬 위치와, 정규화에 쓰는 A3 안 노이즈 원값 범위
    private class NoiseField
    {
        public Vector2[] Offsets;
        public float Min;
        public float Max;
    }

    [MenuItem("Tools/Map/14. 남서쪽 지형 만들기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        Rect southWest = MapGrid.GetRangeBounds(SouthWestFromCell, SouthWestToCell);
        int divisions = data.heightmapResolution - 1;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, southWest, divisions);

        // 가장자리 줄은 옆 구역과 같이 쓰는 꼭짓점이라 바탕 높이 그대로 두고, 그 안쪽만 덮어쓴다
        var inner = new RectInt(range.x + 1, range.y + 1, range.width - 1, range.height - 1);
        float[,] heights = CreateHeights(terrain, southWest, inner, out HeightStats stats);
        data.SetHeights(inner.x, inner.y, heights);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        // 검수 때 높이 범위, 언덕 꼭대기, 경계가 이어졌는지, A3 밖을 건드리지 않았는지 확인할 값이라 남겨 둔다
        float borderDeviation = GetMaxBorderDeviation(data, range);
        bool isInsideSouthWest = IsInside(terrain, southWest, inner);
        Debug.Log($"[MapSouthWestShaper] 남서쪽(A3) 정규화 노이즈 원값 최저 {stats.NoiseMin:F3}, 최고 {stats.NoiseMax:F3}, " +
                  $"언덕 반지름 밖 높이 최저 {stats.OutsideHillRange.x:F1}m, 최고 {stats.OutsideHillRange.y:F1}m, " +
                  $"언덕 꼭대기 {stats.PeakHeight:F1}m (x {stats.PeakPosition.x:F1}, z {stats.PeakPosition.y:F1}), " +
                  $"경계 감쇠 폭 {stats.FadeWidthMin:F1} ~ {stats.FadeWidthMax:F1}m, " +
                  $"경계 줄 {MapTerrainSetup.BaseHeight}m 대비 최대 차이 {borderDeviation:F3}m, " +
                  $"A3 밖 쓰기 {(isInsideSouthWest ? "없음" : "있음")} (높이맵 번호 x {inner.xMin} ~ {inner.xMax - 1}, z {inner.yMin} ~ {inner.yMax - 1})");
    }

    private static float[,] CreateHeights(Terrain terrain, Rect southWest, RectInt inner, out HeightStats stats)
    {
        TerrainData data = terrain.terrainData;
        Vector3 position = terrain.transform.position;
        float sampleSpacing = data.size.x / (data.heightmapResolution - 1);
        NoiseField field = CreateNoiseField(terrain, inner);

        // 언덕 중심이 꼭대기 목표 높이에 닿도록, 중심의 (정규화된) 노이즈 지형 높이와의 차이만큼 올린다
        float hillLift = MapTerrainSetup.BaseHeight + HillRise - CalculateNoiseHeight(HillCenter, southWest, field);

        var heights = new float[inner.height, inner.width];
        stats = new HeightStats
        {
            OutsideHillRange = new Vector2(float.MaxValue, float.MinValue), PeakHeight = float.MinValue, NoiseMin = field.Min, NoiseMax = field.Max,
            FadeWidthMin = float.MaxValue, FadeWidthMax = float.MinValue
        };
        for (int z = 0; z < inner.height; z++)
        {
            for (int x = 0; x < inner.width; x++)
            {
                var world = new Vector2(position.x + (inner.x + x) * sampleSpacing, position.z + (inner.y + z) * sampleSpacing);
                float height = CalculateHeight(world, southWest, field, hillLift);
                heights[z, x] = height / data.size.y;
                RecordStats(ref stats, world, height);
            }
        }
        return heights;
    }

    private static void RecordStats(ref HeightStats stats, Vector2 world, float height)
    {
        float fadeWidth = CalculateEdgeFadeWidth(world);
        stats.FadeWidthMin = Mathf.Min(stats.FadeWidthMin, fadeWidth);
        stats.FadeWidthMax = Mathf.Max(stats.FadeWidthMax, fadeWidth);

        // 비틀린 윤곽 기준. A3 안쪽은 경계 감쇠값이 0보다 커서 언덕 모양 가중치만 보면 된다
        bool isInsideHill = CalculateHillWeight(world) > 0f;
        if (!isInsideHill)
        {
            stats.OutsideHillRange.x = Mathf.Min(stats.OutsideHillRange.x, height);
            stats.OutsideHillRange.y = Mathf.Max(stats.OutsideHillRange.y, height);
            return;
        }

        if (height <= stats.PeakHeight) return;
        stats.PeakHeight = height;
        stats.PeakPosition = world;
    }

    // 쓰는 범위(경계 줄 제외)의 높이맵 점 전체에서 노이즈 원값의 최저, 최고를 잰다. 같은 seed면 항상 같은 값이 나온다
    private static NoiseField CreateNoiseField(Terrain terrain, RectInt inner)
    {
        TerrainData data = terrain.terrainData;
        Vector3 position = terrain.transform.position;
        float sampleSpacing = data.size.x / (data.heightmapResolution - 1);
        var field = new NoiseField { Offsets = CreateOctaveOffsets(), Min = float.MaxValue, Max = float.MinValue };
        for (int z = 0; z < inner.height; z++)
        {
            for (int x = 0; x < inner.width; x++)
            {
                var world = new Vector2(position.x + (inner.x + x) * sampleSpacing, position.z + (inner.y + z) * sampleSpacing);
                float noise = SampleNoise(world, field.Offsets);
                field.Min = Mathf.Min(field.Min, noise);
                field.Max = Mathf.Max(field.Max, noise);
            }
        }
        return field;
    }

    private static Vector2[] CreateOctaveOffsets()
    {
        var random = new System.Random(Seed);
        var offsets = new Vector2[OctavePeriods.Length];
        for (int i = 0; i < offsets.Length; i++)
        {
            float x = NoiseOffsetMin + (float)random.NextDouble() * (NoiseOffsetMax - NoiseOffsetMin);
            float z = NoiseOffsetMin + (float)random.NextDouble() * (NoiseOffsetMax - NoiseOffsetMin);
            offsets[i] = new Vector2(x, z);
        }
        return offsets;
    }

    private static float CalculateHeight(Vector2 world, Rect southWest, NoiseField field, float hillLift)
    {
        // 언덕도 경계 감쇠를 받아, 비틀린 윤곽이 감쇠 구역에 들어가도 경계 줄이 바탕 높이로 남게 한다.
        // 비틀린 윤곽 밖은 가중치 0이라 노이즈 지형 그대로 남는다
        float hillWeight = CalculateHillWeight(world) * CalculateEdgeMask(world, southWest);
        return CalculateNoiseHeight(world, southWest, field) + hillLift * hillWeight;
    }

    // 남서쪽 구역 경계에 가까울수록 0에 가까워져, 옆 구역과 바탕 높이에서 만나게 한다.
    // 가장 가까운 경계까지 거리를 그 위치의 감쇠 폭으로 나누므로, 거리 0(경계 줄)에서는 폭과 상관없이 0이다
    private static float CalculateEdgeMask(Vector2 world, Rect southWest)
    {
        float edgeDistance = Mathf.Min(
            Mathf.Min(world.x - southWest.xMin, southWest.xMax - world.x),
            Mathf.Min(world.y - southWest.yMin, southWest.yMax - world.y));
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, CalculateEdgeFadeWidth(world), edgeDistance));
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

    // 서쪽과 달리 바탕 위아래로 오르내리게 한다. 음수 쪽은 A3 안 최저값이 -1, 양수 쪽은 최고값이 1이 되도록 따로 늘린 뒤
    // 아래, 위 진폭을 곱한다 (가장 낮은 곳 바탕 - AmplitudeBelow, 가장 높은 곳 바탕 + AmplitudeAbove)
    private static float CalculateNoiseHeight(Vector2 world, Rect southWest, NoiseField field)
    {
        float noise = SampleNoise(world, field.Offsets);
        float offset = noise < 0f
            ? noise / Mathf.Abs(field.Min) * AmplitudeBelow
            : noise / field.Max * AmplitudeAbove;
        return MapTerrainSetup.BaseHeight + offset * CalculateEdgeMask(world, southWest);
    }

    // 비틀린 위치가 언덕 중심에 있으면 1, 반지름 끝에서 0이 되도록 부드럽게 줄어드는 가중치.
    // 비틀기는 위치에 따라 천천히 바뀌므로 꼭대기 쪽에 방사형 골이 생기지 않는다.
    // 남서쪽 배치(메뉴 15)가 언덕 위 판정을 지형과 같은 윤곽으로 하도록 공개한다 (경계 감쇠는 곱하지 않은 값)
    public static float CalculateHillWeight(Vector2 world)
    {
        Vector2 warped = world + SampleHillWarp(world);
        float distance = Vector2.Distance(warped, HillCenter);
        return Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(distance / HillRadius));
    }

    // x, z 각각 -1 ~ 1 노이즈에 최대 비틀기 거리를 곱한 벡터 (m)
    private static Vector2 SampleHillWarp(Vector2 world)
    {
        Vector2 sample = world / HillWarpPeriod;
        float warpX = Mathf.PerlinNoise(sample.x + HillWarpOffsetX.x, sample.y + HillWarpOffsetX.y) * 2f - 1f;
        float warpZ = Mathf.PerlinNoise(sample.x + HillWarpOffsetZ.x, sample.y + HillWarpOffsetZ.y) * 2f - 1f;
        return new Vector2(warpX, warpZ) * HillWarpDistance;
    }

    // -1 ~ 1 범위의 여러 겹 노이즈
    private static float SampleNoise(Vector2 world, Vector2[] offsets)
    {
        float noise = 0f;
        for (int i = 0; i < OctavePeriods.Length; i++)
        {
            float perlin = Mathf.PerlinNoise(world.x / OctavePeriods[i] + offsets[i].x, world.y / OctavePeriods[i] + offsets[i].y);
            noise += OctaveWeights[i] * (perlin * 2f - 1f);
        }
        return Mathf.Clamp(noise, -1f, 1f);
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

    // 쓴 범위의 양 끝 꼭짓점이 A3 경계선보다 안쪽에 있는지 (경계선 위도 밖으로 본다)
    private static bool IsInside(Terrain terrain, Rect southWest, RectInt inner)
    {
        TerrainData data = terrain.terrainData;
        Vector3 position = terrain.transform.position;
        float sampleSpacing = data.size.x / (data.heightmapResolution - 1);
        var first = new Vector2(position.x + inner.xMin * sampleSpacing, position.z + inner.yMin * sampleSpacing);
        var last = new Vector2(position.x + (inner.xMax - 1) * sampleSpacing, position.z + (inner.yMax - 1) * sampleSpacing);
        return first.x > southWest.xMin && first.y > southWest.yMin && last.x < southWest.xMax && last.y < southWest.yMax;
    }
}
