using UnityEditor;
using UnityEngine;

// 메뉴 11 : 서쪽 구역(A2)에 완만한 언덕을 만든다.
// 노이즈 겹침, 진폭, 경계 감쇠는 중앙 언덕(메뉴 2)과 같지만, 중앙과 달리 바탕 높이 위로만 솟는다 (바탕 + 0 ~ 진폭).
// 중앙 방식(바탕 ± 진폭)으로는 A2가 대부분 바탕 아래로 꺼져 분지처럼 보였기 때문이다.
// 네 경계 안쪽 100m에서 바탕 높이로 돌아가고, 경계 줄은 쓰지 않아 북서(A1), 중앙(B2), 남서(A3)의 높이를 건드리지 않는다.
// 매번 바탕 높이 기준으로 A2 안쪽을 다시 계산해 덮어쓰므로 여러 번 실행해도 결과가 같다.
// 실행 순서 : 9-3 → 11 → 12 → 13 → 10. 11을 다시 실행하면 지형 높이가 바뀌므로 12, 13도 순서대로 다시 실행한다.
public static class MapWestShaper
{
    private const string WestFromCell = "A2";
    private const string WestToCell = "A2";

    private const int Seed = 20261126;
    private const float Amplitude = 25f;
    private const float EdgeFadeWidth = 100f;

    // 여러 겹의 Perlin 노이즈 (큰 언덕 + 중간 굴곡 + 잔굴곡). 중앙 언덕과 같은 값
    private static readonly float[] OctavePeriods = { 400f, 150f, 60f };
    private static readonly float[] OctaveWeights = { 0.6f, 0.3f, 0.1f };

    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;

    [MenuItem("Tools/Map/11. 서쪽 언덕 만들기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        Rect west = MapGrid.GetRangeBounds(WestFromCell, WestToCell);
        int divisions = data.heightmapResolution - 1;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, west, divisions);

        // 가장자리 줄은 옆 구역과 같이 쓰는 꼭짓점이라 바탕 높이 그대로 두고, 그 안쪽만 덮어쓴다
        var inner = new RectInt(range.x + 1, range.y + 1, range.width - 1, range.height - 1);
        float[,] heights = CreateHeights(terrain, west, inner, out Vector2 heightRange);
        data.SetHeights(inner.x, inner.y, heights);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        // 검수 때 경계가 이어졌는지, A2 밖을 건드리지 않았는지 확인할 값이라 남겨 둔다
        float borderDeviation = GetMaxBorderDeviation(data, range);
        bool isInsideWest = IsInside(terrain, west, inner);
        Debug.Log($"[MapWestShaper] 서쪽(A2) 높이 최저 {heightRange.x:F1}m, 최고 {heightRange.y:F1}m, " +
                  $"경계 줄 {MapTerrainSetup.BaseHeight}m 대비 최대 차이 {borderDeviation:F3}m, " +
                  $"A2 밖 쓰기 {(isInsideWest ? "없음" : "있음")} (높이맵 번호 x {inner.xMin} ~ {inner.xMax - 1}, z {inner.yMin} ~ {inner.yMax - 1})");
    }

    // heightRange : x = 최저, y = 최고 (m)
    private static float[,] CreateHeights(Terrain terrain, Rect west, RectInt inner, out Vector2 heightRange)
    {
        TerrainData data = terrain.terrainData;
        Vector2[] offsets = CreateOctaveOffsets();
        Vector3 position = terrain.transform.position;
        float sampleSpacing = data.size.x / (data.heightmapResolution - 1);

        var heights = new float[inner.height, inner.width];
        heightRange = new Vector2(float.MaxValue, float.MinValue);
        for (int z = 0; z < inner.height; z++)
        {
            for (int x = 0; x < inner.width; x++)
            {
                var world = new Vector2(position.x + (inner.x + x) * sampleSpacing, position.z + (inner.y + z) * sampleSpacing);
                float height = CalculateHeight(world, west, offsets);
                heights[z, x] = height / data.size.y;
                heightRange.x = Mathf.Min(heightRange.x, height);
                heightRange.y = Mathf.Max(heightRange.y, height);
            }
        }
        return heights;
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

    private static float CalculateHeight(Vector2 world, Rect west, Vector2[] offsets)
    {
        // 서쪽 구역 경계에 가까울수록 언덕을 줄여, 옆 구역과 바탕 높이에서 만나게 한다
        float edgeDistance = Mathf.Min(
            Mathf.Min(world.x - west.xMin, west.xMax - world.x),
            Mathf.Min(world.y - west.yMin, west.yMax - world.y));
        float edgeMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, EdgeFadeWidth, edgeDistance));

        // -1 ~ 1 노이즈를 0 ~ 1로 옮겨 바탕 높이 아래로 내려가는 곳이 없게 한다
        float rise = SampleNoise(world, offsets) * 0.5f + 0.5f;
        return MapTerrainSetup.BaseHeight + Amplitude * rise * edgeMask;
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

    // 쓴 범위의 양 끝 꼭짓점이 A2 경계선보다 안쪽에 있는지 (경계선 위도 밖으로 본다)
    private static bool IsInside(Terrain terrain, Rect west, RectInt inner)
    {
        TerrainData data = terrain.terrainData;
        Vector3 position = terrain.transform.position;
        float sampleSpacing = data.size.x / (data.heightmapResolution - 1);
        var first = new Vector2(position.x + inner.xMin * sampleSpacing, position.z + inner.yMin * sampleSpacing);
        var last = new Vector2(position.x + (inner.xMax - 1) * sampleSpacing, position.z + (inner.yMax - 1) * sampleSpacing);
        return first.x > west.xMin && first.y > west.yMin && last.x < west.xMax && last.y < west.yMax;
    }
}
