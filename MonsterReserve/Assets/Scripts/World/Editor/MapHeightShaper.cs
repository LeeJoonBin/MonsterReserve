using UnityEditor;
using UnityEngine;

// 메뉴 2 : 중앙 지역(B2~C3)에만 완만한 언덕을 만든다.
// 거점 주변은 평지로 두고, 중앙 지역 경계로 갈수록 기준 높이로 돌아가 바깥 구역과 끊기지 않게 한다.
public static class MapHeightShaper
{
    public const float BaseFlatRadius = 30f;

    private const int Seed = 20261109;
    private const float Amplitude = 25f;
    private const float BaseBlendEndRadius = 180f;
    private const float EdgeFadeWidth = 100f;

    // 여러 겹의 Perlin 노이즈 (큰 언덕 + 중간 굴곡 + 잔굴곡)
    private static readonly float[] OctavePeriods = { 400f, 150f, 60f };
    private static readonly float[] OctaveWeights = { 0.6f, 0.3f, 0.1f };

    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;

    [MenuItem("Tools/Map/2. 중앙 언덕 만들기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        Rect center = MapGrid.GetRangeBounds(MapTerrainSetup.CenterFromCell, MapTerrainSetup.CenterToCell);
        int divisions = data.heightmapResolution - 1;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, center, divisions);

        // 높이맵은 꼭짓점 기준이라 끝 줄까지 포함한다 (경계 줄은 기준 높이로 맞춰져 바깥과 이어진다)
        int width = range.width + 1;
        int depth = range.height + 1;
        Vector2[] offsets = CreateOctaveOffsets();
        Vector3 position = terrain.transform.position;
        float sampleSpacing = data.size.x / divisions;

        var heights = new float[depth, width];
        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                var world = new Vector2(position.x + (range.x + x) * sampleSpacing, position.z + (range.y + z) * sampleSpacing);
                float height = CalculateHeight(world, center, offsets);
                heights[z, x] = height / data.size.y;
                minHeight = Mathf.Min(minHeight, height);
                maxHeight = Mathf.Max(maxHeight, height);
            }
        }
        data.SetHeights(range.x, range.y, heights);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        float originHeight = terrain.SampleHeight(Vector3.zero) + position.y;
        Debug.Log($"[MapHeightShaper] 중앙 높이 최저 {minHeight:F1}m, 최고 {maxHeight:F1}m, 원점 {originHeight:F2}m");
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

    private static float CalculateHeight(Vector2 world, Rect center, Vector2[] offsets)
    {
        // 거점 반경 안은 정확히 평지, 바깥으로 갈수록 언덕이 부드럽게 살아난다
        float baseMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BaseFlatRadius, BaseBlendEndRadius, world.magnitude));

        // 중앙 지역 경계에 가까울수록 언덕을 줄여, 아직 작업하지 않은 바깥 구역(평지)과 이어지게 한다
        float edgeDistance = Mathf.Min(
            Mathf.Min(world.x - center.xMin, center.xMax - world.x),
            Mathf.Min(world.y - center.yMin, center.yMax - world.y));
        float edgeMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, EdgeFadeWidth, edgeDistance));

        return MapTerrainSetup.BaseHeight + Amplitude * SampleNoise(world, offsets) * baseMask * edgeMask;
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
}
