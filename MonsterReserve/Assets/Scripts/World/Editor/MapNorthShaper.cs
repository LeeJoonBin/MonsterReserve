using UnityEditor;
using UnityEngine;

// 메뉴 5 : 북쪽 줄 전체(A1~C1)에 동서로 이어지는 산맥과 호수 분지를 만든다.
// 정상은 B1 가운데쯤에 두고, 동서로 멀어질수록 봉우리가 낮아져 A1, C1 바깥쪽은 낮은 언덕으로 남긴다 (나중 구역 작업에서 맞춘다).
// 남쪽 경계 줄은 바탕 높이와 정확히 같게 해 중앙, 서, 동 구역과 단차 없이 이어지게 한다.
// 정상 뒤(북쪽)는 맵 끝까지 내려가되 바탕보다 높은 산자락으로 남겨, 맵 바깥 빈 공간이 덜 드러나게 한다.
// 마지막으로 기슭(FoothillMaxHeight 아래)의 경사를 FoothillMaxSlope 이하로 줄여, 플레이어가 기슭은 어디로든 걸어 오를 수 있게 한다.
public static class MapNorthShaper
{
    private const string NorthFromCell = "A1";
    private const string NorthToCell = "C1";
    // 정상이 있는 칸. 정상의 동서 위치는 이 칸의 가운데다
    private const string PeakCell = "B1";

    private const int Seed = 20261110;
    private const float PeakHeight = 380f;

    // 능선(정상이 있는 줄)의 남북 위치. 남쪽 경계에서 북쪽 줄 깊이의 이 비율만큼 북쪽 (지금 z ≈ 800)
    private const float RidgeDepthRatio = 0.55f;
    // 능선이 동서로 가며 남북으로 굽는 정도와 주기. 한 줄로 곧은 벽처럼 보이지 않게 한다 (정상에서는 굽지 않는다)
    private const float RidgeMeanderAmplitude = 70f;
    private const float RidgeMeanderPeriod = 600f;

    // 능선 위 봉우리들 : 정상 기준 동서 거리, 정상 대비 높이 비율, 반폭.
    // 가장 바깥 봉우리의 거리 + 반폭에서 산맥이 끝난다 (지금 서쪽 -1110m, 동쪽 +1150m).
    // 다른 봉우리 비율을 (1 - NoiseDepth)보다 낮게 두어, 노이즈가 어떻게 나와도 최고점이 B1에 남게 한다.
    private static readonly float[] PeakOffsets = { 0f, -420f, 450f, -780f, 820f };
    private static readonly float[] PeakHeightRatios = { 1f, 0.65f, 0.62f, 0.4f, 0.38f };
    private static readonly float[] PeakHalfWidths = { 550f, 380f, 400f, 330f, 330f };
    // 산맥이 끝난 바깥(A1, C1 서쪽/동쪽 끝)에 남기는 낮은 언덕의 높이 비율 (지금 약 17.5m)
    private const float OuterHillRatio = 0.05f;

    // 남쪽 비탈 : 산자락은 평평하게 시작해 능선 쪽으로 갈수록 가팔라진다. 능선에서의 기울기 = 평균 기울기 × 이 값
    private const float SouthCrestSlopeRatio = 1.2f;
    // 정상 뒤 맵 북쪽 끝 높이 (정상이 있는 줄 기준). 동서로 갈수록 능선 높이에 맞춰 함께 낮아진다.
    // 정상 높이에 비례해 정한 값 (바탕 위 높이가 정상의 약 38%). 정상 뒤 비탈이 남쪽 비탈과 같은 비율로 가팔라진다
    private const float NorthEdgeHeight = 165f;
    private const float NorthEdgeRatio = (NorthEdgeHeight - MapTerrainSetup.BaseHeight) / (PeakHeight - MapTerrainSetup.BaseHeight);

    // 능선 노이즈가 산 높이를 깎는 최대 비율 (0이면 매끈한 덩어리, 클수록 골짜기가 깊어지고 경사가 커진다)
    private const float NoiseDepth = 0.3f;
    // 노이즈를 남북으로 늘리는 배율. 산비탈을 따라 남북으로 뻗는 지능선과 골짜기가 생기게 한다
    private const float NoiseStretchZ = 1.6f;

    // 여러 겹의 접힌(ridged) Perlin 노이즈 (큰 능선 + 중간 능선 + 잔굴곡)
    private static readonly float[] OctavePeriods = { 450f, 200f, 90f };
    private static readonly float[] OctaveWeights = { 0.6f, 0.3f, 0.1f };

    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;

    // 호수 분지. 중심 x는 정상과 같고, z는 남쪽 경계에서 북쪽 줄 깊이의 이 비율만큼 북쪽 (지금 z = 500).
    // 이전(z 550)보다 50m 남쪽이다. 남쪽 비탈 곡선(SampleSouthSlope)은 아래로 갈수록 완만해 호수 위아래 높이 차(깎고 메울 양)가 줄고,
    // 능선(z ≈ 800)까지 북쪽 벽을 이어 붙일 거리가 길어진다
    private const float BasinDepthRatio = 0.25f;
    // 물가 타원의 동서, 남북 반지름 (평균 지름 = 두 반지름의 합 = 80m).
    // 비탈 방향(남북)으로 길면 호수 위아래 높이 차가 커져 북쪽을 많이 깎고 남쪽을 많이 메워야 하므로, 등고선 방향(동서)으로 길게 둔다
    private const float LakeRadiusX = 50f;
    private const float LakeRadiusZ = 30f;
    // 가운데 깊이. 노이즈가 더해져 실제 가운데 깊이는 약 3.5 ~ 6.5m가 된다
    private const float LakeDepth = 5f;
    // 물가 밖 물턱이 수면 위로 오르는 높이의 상한. 둘레를 막아 남쪽(내리막)으로 물이 새지 않게 한다
    private const float LipHeight = 4f;
    // 물턱이 끝나고 원래 산 모양으로 이어 붙기 시작하는 곳 (물가 타원의 배율, 동서 70m, 남북 42m).
    // 여기서 물턱은 수면 + 약 2.8m이고, 호수 노이즈는 물가에서 여기까지 0으로 줄어 둘레에 물이 샐 틈이 생기지 않는다
    private const float LipRadiusRatio = 1.4f;
    // 수면 높이 = 남쪽 물턱 끝과 북쪽 물턱 끝의 원래 높이 사이에서 이 비율인 곳 (0이면 북쪽을 깎기만, 1이면 남쪽을 메우기만).
    // 메운 만큼 남쪽에 단이, 깎은 만큼 북쪽에 벽이 생긴다. 이어 붙일 거리가 남북 모두 같아 둘이 비슷해지는 값으로 둔다
    // (물턱 높이만큼 남쪽이 더 메워지므로 0.5보다 조금 낮게)
    private const float WaterLevelRatio = 0.4f;
    // 호수 바닥과 물가를 불규칙하게 하는 노이즈의 높이 폭. 물가 경사(동서 약 0.2, 남북 약 0.33)로 나눈 만큼
    // 물가가 들고 나 후미와 곶이 된다 (동서 약 ±7m, 남북 약 ±4.5m). 물가 가까이에는 작은 섬이 생길 수 있다
    private const float LakeNoiseAmplitude = 1.5f;
    // 큰 굴곡(후미, 곶)과 작은 굴곡(물가의 잔 들쭉날쭉)
    private static readonly float[] LakeNoisePeriods = { 35f, 15f };
    private static readonly float[] LakeNoiseWeights = { 0.7f, 0.3f };
    // 산 노이즈와 따로 두는 호수 노이즈 seed
    private const int LakeNoiseSeed = 20261111;
    // 호수 중심에서 이 반지름까지 원래 산 모양으로 이어 붙인다. 북쪽은 능선(z ≈ 800) 앞에서 끝나 정상 높이를 건드리지 않는다.
    // 남쪽은 남쪽 경계 줄에 닿지 않도록 중심에서 경계까지의 거리를 넘지 않게 줄인다 (지금 둘 다 250m)
    private const float BasinBlendRadius = 250f;

    // 경사 로그에서 정상부 바위 지대로 보고 따로 빼는 높이 (바위 지대는 가팔라도 된다). 정상 380m 기준 약 82%
    private const float SummitZoneHeight = 310f;

    // 기슭 경사 제한 (사용자 결정 : 기슭 최대 35도). 기슭 높이 상한은 MapNorthScatter의 기슭 높이대(160m)와 같은 값이다
    private const float FoothillMaxHeight = 160f;
    private const float FoothillMaxSlope = 35f;
    // 이웃 칸끼리의 높이 차를 제한해도, 높이맵 경사(가운데 차분)는 대각선 방향 때문에 최대 이 배율(√(4 - 2√2) ≈ 1.082)만큼 커질 수 있다.
    // 이웃 사이 제한 기울기를 tan(35도) / 이 값(약 32.9도)으로 두어, 계산된 경사가 35도를 넘지 않게 한다
    private static readonly float StencilSlopeFactor = Mathf.Sqrt(4f - 2f * Mathf.Sqrt(2f));
    // 제한이 풀리는 높이 폭. 이 폭에서 제한 각도가 NoLimitSlope(사실상 제한 없음)까지 서서히 커져 기슭 경계에 단이나 줄이 생기지 않는다.
    // 지금 값이면 폭의 절반(20m)쯤에서 약 61도가 되어, 원래 산의 최대 경사(약 60도)보다 커진다
    private const float LimitTransitionHeight = 40f;
    private const float NoLimitSlope = 89f;
    // 제한이 풀리기 시작하는 높이를 노이즈로 0 ~ 이만큼 올린다. 메운 산자락의 윗끝이 한 등고선을 따라 줄지어 보이지 않게 한다
    private const float LimitJitter = 15f;
    private const float LimitJitterPeriod = 80f;
    private const int LimitNoiseSeed = 20261113;
    // 경사 제한 반복 계산 : 순방향 + 역방향 패스 한 번에 바뀐 높이가 이 값 이하면 끝낸다. 최대 패스 수를 넘으면 경고한다
    private const float RelaxTolerance = 0.0001f;
    private const int MaxRelaxPasses = 200;
    // 기슭 경사 검사의 허용 오차 (높이맵 격자 기준, 반복 계산과 float 오차)
    private const float SlopeCheckTolerance = 0.01f;
    // 경사 제한으로 바뀐 칸으로 세는 최소 높이 차
    private const float ChangeThreshold = 0.01f;

    // 순방향 패스에서 이미 지나온 이웃 (x, z). 역방향 패스는 부호를 뒤집어 반대쪽 이웃을 본다
    private static readonly Vector2Int[] RelaxNeighbors =
    {
        new Vector2Int(-1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1)
    };

    // 산 모양 계산에 함께 쓰는 값
    private struct ShapeContext
    {
        // 실제 높이맵 꼭짓점 범위. 남쪽 첫 줄이 바탕 높이와 정확히 맞도록 격자 범위 대신 이 범위를 기준으로 한다
        public Rect Bounds;
        public float PeakX;
        public float RidgeZ;
        // 마지막 하나는 능선 굽이용, 나머지는 능선 노이즈 겹마다 하나씩
        public Vector2[] NoiseOffsets;
    }

    private struct Basin
    {
        public Vector2 Center;
        // Terrain 기준 높이
        public float WaterHeight;
        public float BlendRadius;
        public Vector2[] NoiseOffsets;
    }

    private struct SlopeStats
    {
        public float MaxSlope;
        public float SteepRatio;
        public float SteepRatioBelowSummit;
        public float MaxFoothillSlope;
        public float FoothillSteepRatio;
    }

    // 호수(메뉴 6)가 호수 중심과 수면 높이를 산 만들기와 같은 계산으로 얻게 한다. (x, z = 월드 XZ, y = 월드 수면 높이)
    // 수면 높이가 원래 산 높이에서 정해지므로, 정상 높이를 맞추는 배율까지 산 전체를 다시 계산한다
    public static Vector3 GetLakeCenter(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        int divisions = data.heightmapResolution - 1;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, MapGrid.GetRangeBounds(NorthFromCell, NorthToCell), divisions);
        Vector2[,] positions = CreateSamplePositions(terrain, range, data.size.x / divisions);
        ShapeContext context = CreateContext(positions);
        CalculateShapes(positions, context, out float shapeMax);
        Basin basin = CreateBasin(context, GetHeightScale(shapeMax));
        return new Vector3(basin.Center.x, basin.WaterHeight + terrain.transform.position.y, basin.Center.y);
    }

    [MenuItem("Tools/Map/5. 북쪽 산지 만들기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        int divisions = data.heightmapResolution - 1;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, MapGrid.GetRangeBounds(NorthFromCell, NorthToCell), divisions);
        float sampleSpacing = data.size.x / divisions;

        Vector2[,] positions = CreateSamplePositions(terrain, range, sampleSpacing);
        ShapeContext context = CreateContext(positions);
        float[,] shapes = CalculateShapes(positions, context, out float shapeMax);

        float heightScale = GetHeightScale(shapeMax);
        Basin basin = CreateBasin(context, heightScale);
        // 분지를 넣기 전 높이는 분지 둘레를 얼마나 깎고 메웠는지 로그로 비교할 때 쓴다
        float[,] naturalHeights = CalculateNaturalHeights(shapes, heightScale);
        // 호수 물턱 안은 경사 제한에서 바뀌지 않으므로, 메뉴 6이 GetLakeCenter로 구하는 수면 높이와 그대로 맞는다
        float[,] heights = LimitFoothillSlope(CalculateHeights(positions, naturalHeights, basin), positions, basin, sampleSpacing);

        // 수면 높이는 메뉴 6(호수)이 같은 계산으로 다시 구해 쓴다
        Debug.Log($"[MapNorthShaper] 호수 중심 ({basin.Center.x:F1}, {basin.Center.y:F1}), 수면 높이 {basin.WaterHeight + terrain.transform.position.y:F2}m (월드)");
        LogResult(heights, naturalHeights, positions, basin);
        data.SetHeights(range.x, range.y, ToNormalized(heights, data.size.y));

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
    }

    // 높이맵은 꼭짓점 기준이라 끝 줄까지 포함한다 (남쪽 끝 줄은 바탕 높이로 맞춰져 중앙, 서, 동 구역과 이어진다)
    private static Vector2[,] CreateSamplePositions(Terrain terrain, RectInt range, float sampleSpacing)
    {
        Vector3 position = terrain.transform.position;
        var positions = new Vector2[range.height + 1, range.width + 1];
        for (int z = 0; z <= range.height; z++)
        {
            for (int x = 0; x <= range.width; x++)
                positions[z, x] = new Vector2(position.x + (range.x + x) * sampleSpacing, position.z + (range.y + z) * sampleSpacing);
        }
        return positions;
    }

    private static ShapeContext CreateContext(Vector2[,] positions)
    {
        Vector2 first = positions[0, 0];
        Vector2 last = positions[positions.GetLength(0) - 1, positions.GetLength(1) - 1];
        Rect bounds = Rect.MinMaxRect(first.x, first.y, last.x, last.y);
        return new ShapeContext
        {
            Bounds = bounds,
            PeakX = MapGrid.GetCellCenter(PeakCell).x,
            RidgeZ = bounds.yMin + bounds.height * RidgeDepthRatio,
            NoiseOffsets = CreateNoiseOffsets(Seed, OctavePeriods.Length + 1)
        };
    }

    // 산 노이즈와 호수 노이즈가 같이 쓴다 (seed가 달라 서로 다른 위치가 나온다)
    private static Vector2[] CreateNoiseOffsets(int seed, int count)
    {
        var random = new System.Random(seed);
        var offsets = new Vector2[count];
        for (int i = 0; i < offsets.Length; i++)
        {
            float x = NoiseOffsetMin + (float)random.NextDouble() * (NoiseOffsetMax - NoiseOffsetMin);
            float z = NoiseOffsetMin + (float)random.NextDouble() * (NoiseOffsetMax - NoiseOffsetMin);
            offsets[i] = new Vector2(x, z);
        }
        return offsets;
    }

    private static float[,] CalculateShapes(Vector2[,] positions, ShapeContext context, out float shapeMax)
    {
        int depth = positions.GetLength(0);
        int width = positions.GetLength(1);
        var shapes = new float[depth, width];
        shapeMax = 0f;
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                shapes[z, x] = CalculateShape(positions[z, x], context);
                shapeMax = Mathf.Max(shapeMax, shapes[z, x]);
            }
        }
        return shapes;
    }

    // 0 ~ 1 비율의 산 모양 = 능선 높이(동서) × 남북 단면 × 능선 노이즈. 남쪽 경계에서 정확히 0이 되어 바탕 높이와 이어진다.
    private static float CalculateShape(Vector2 world, ShapeContext context)
    {
        float spine = SampleSpine(world.x - context.PeakX);
        float cross = SampleCrossProfile(world, context);
        float ridge = Mathf.Lerp(1f - NoiseDepth, 1f, SampleRidgeNoise(world, context.NoiseOffsets));
        return spine * cross * ridge;
    }

    // 능선 위 높이 비율. 봉우리마다 가운데가 가장 높은 언덕을 놓고 가장 높은 값을 쓴다 (봉우리 사이는 낮은 고개가 된다)
    private static float SampleSpine(float offsetX)
    {
        float spine = OuterHillRatio;
        for (int i = 0; i < PeakOffsets.Length; i++)
        {
            float distance = Mathf.Abs(offsetX - PeakOffsets[i]);
            float bump = PeakHeightRatios[i] * (1f - Mathf.SmoothStep(0f, 1f, distance / PeakHalfWidths[i]));
            spine = Mathf.Max(spine, bump);
        }
        return spine;
    }

    // 능선을 1로 하는 남북 단면. 남쪽 경계에서 0, 맵 북쪽 끝에서 NorthEdgeRatio.
    private static float SampleCrossProfile(Vector2 world, ShapeContext context)
    {
        float ridgeZ = context.RidgeZ + SampleMeander(world.x - context.PeakX, context.NoiseOffsets);
        if (world.y <= ridgeZ)
            return SampleSouthSlope(Mathf.InverseLerp(context.Bounds.yMin, ridgeZ, world.y));

        // 정상 뒤는 둥글게 넘어가고 맵 끝에서는 평평해지게 해, 맵 끝이 잘린 벽처럼 보이지 않게 한다
        float north = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ridgeZ, context.Bounds.yMax, world.y));
        return Mathf.Lerp(1f, NorthEdgeRatio, north);
    }

    // 시작 기울기 0, 끝 기울기 SouthCrestSlopeRatio인 3차 곡선 (0 → 0, 1 → 1).
    // 산자락이 바탕 높이에서 부드럽게 올라오고, 가장 가파른 곳도 평균의 약 1.35배에 머물게 한다.
    private static float SampleSouthSlope(float t)
    {
        return t * t * ((3f - SouthCrestSlopeRatio) + (SouthCrestSlopeRatio - 2f) * t);
    }

    // 능선이 남북으로 굽는 거리. 정상 위치의 값을 빼서 정상에서는 0이 되게 한다
    private static float SampleMeander(float offsetX, Vector2[] offsets)
    {
        Vector2 offset = offsets[offsets.Length - 1];
        float atPeak = Mathf.PerlinNoise(offset.x, offset.y);
        float here = Mathf.PerlinNoise(offsetX / RidgeMeanderPeriod + offset.x, offset.y);
        return RidgeMeanderAmplitude * 2f * (here - atPeak);
    }

    // 0 ~ 1 범위의 여러 겹 노이즈. 각 겹을 접어 날카로운 능선과 둥근 골짜기가 생기게 한다.
    private static float SampleRidgeNoise(Vector2 world, Vector2[] offsets)
    {
        float noise = 0f;
        for (int i = 0; i < OctavePeriods.Length; i++)
        {
            float perlin = Mathf.PerlinNoise(
                world.x / OctavePeriods[i] + offsets[i].x,
                world.y / (OctavePeriods[i] * NoiseStretchZ) + offsets[i].y);
            noise += OctaveWeights[i] * (1f - Mathf.Abs(perlin * 2f - 1f));
        }
        return Mathf.Clamp01(noise);
    }

    // 산 모양의 최대값이 정상 높이가 되도록 늘리는 배율 (노이즈가 어떻게 나와도 최고점이 PeakHeight로 정해진다)
    private static float GetHeightScale(float shapeMax)
    {
        return (PeakHeight - MapTerrainSetup.BaseHeight) / shapeMax;
    }

    // 분지를 넣기 전 원래 산 높이 (Terrain 기준)
    private static float GetNaturalHeight(Vector2 world, ShapeContext context, float heightScale)
    {
        return MapTerrainSetup.BaseHeight + CalculateShape(world, context) * heightScale;
    }

    private static Basin CreateBasin(ShapeContext context, float heightScale)
    {
        var center = new Vector2(context.PeakX, context.Bounds.yMin + context.Bounds.height * BasinDepthRatio);
        // 수면은 물턱 남북 끝의 원래 높이 사이에 둔다 (비탈 위라 남쪽 끝이 낮고 북쪽 끝이 높다)
        float lipDistance = LakeRadiusZ * LipRadiusRatio;
        float southLipHeight = GetNaturalHeight(center + Vector2.down * lipDistance, context, heightScale);
        float northLipHeight = GetNaturalHeight(center + Vector2.up * lipDistance, context, heightScale);
        return new Basin
        {
            Center = center,
            WaterHeight = Mathf.Lerp(southLipHeight, northLipHeight, WaterLevelRatio),
            // 분지가 남쪽 경계 줄까지 닿지 않게 해 경계 높이가 바탕 높이에서 벗어나지 않게 한다
            BlendRadius = Mathf.Min(BasinBlendRadius, center.y - context.Bounds.yMin),
            NoiseOffsets = CreateNoiseOffsets(LakeNoiseSeed, LakeNoisePeriods.Length)
        };
    }

    private static float[,] CalculateNaturalHeights(float[,] shapes, float heightScale)
    {
        int depth = shapes.GetLength(0);
        int width = shapes.GetLength(1);
        var heights = new float[depth, width];
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
                heights[z, x] = MapTerrainSetup.BaseHeight + shapes[z, x] * heightScale;
        }
        return heights;
    }

    private static float[,] CalculateHeights(Vector2[,] positions, float[,] naturalHeights, Basin basin)
    {
        int depth = naturalHeights.GetLength(0);
        int width = naturalHeights.GetLength(1);
        var heights = new float[depth, width];
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
                heights[z, x] = ApplyBasin(positions[z, x], naturalHeights[z, x], basin);
        }
        return heights;
    }

    // 산비탈 위의 턱(벤치)에 판 웅덩이. 가운데가 깊고 물가로 갈수록 얕아지며, 물가 밖은 수면보다 조금 높은 물턱으로 둘레를 막고,
    // 물턱 끝부터 BlendRadius까지 원래 산 모양으로 돌아간다. 북쪽은 깎인 벽이, 남쪽은 물턱 너머로 계속 내려가는 비탈이 된다
    // (5단계에서 강이 남쪽 물턱을 넘어 흘러내린다).
    private static float ApplyBasin(Vector2 world, float naturalHeight, Basin basin)
    {
        Vector2 offset = world - basin.Center;
        float distance = offset.magnitude;
        if (distance >= basin.BlendRadius) return naturalHeight;

        float shoreRatio = GetShoreRatio(offset);
        // 노이즈는 물가 안쪽에서는 그대로, 물가에서 물턱 끝까지는 0으로 줄인다
        float noiseWeight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, LipRadiusRatio, shoreRatio));
        float noise = SampleLakeNoise(world, basin.NoiseOffsets) * LakeNoiseAmplitude * noiseWeight;
        float lakeHeight = basin.WaterHeight + GetLakeProfile(shoreRatio) + noise;
        if (shoreRatio <= LipRadiusRatio) return lakeHeight;

        // 이어 붙이기 시작하는 거리는 이 방향의 물턱 끝 (타원이라 방향마다 다르다)
        float lipDistance = distance * LipRadiusRatio / shoreRatio;
        float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lipDistance, basin.BlendRadius, distance));
        return Mathf.Lerp(lakeHeight, naturalHeight, blend);
    }

    // 물가 타원 기준 거리 비율 (0 = 호수 중심, 1 = 노이즈 전 물가)
    private static float GetShoreRatio(Vector2 offset)
    {
        float x = offset.x / LakeRadiusX;
        float z = offset.y / LakeRadiusZ;
        return Mathf.Sqrt(x * x + z * z);
    }

    // 수면 기준 높이. 안쪽은 그릇 모양(가운데 -LakeDepth, 물가 0), 바깥은 물가와 같은 기울기로 이어 오르다
    // LipHeight에 가까워지며 평평해진다. 물가에서 기울기가 끊기지 않아 물가에 꺾인 선이 생기지 않는다
    private static float GetLakeProfile(float shoreRatio)
    {
        float rise = shoreRatio * shoreRatio - 1f;
        if (rise <= 0f) return LakeDepth * rise;
        return LipHeight * (1f - Mathf.Exp(-rise * LakeDepth / LipHeight));
    }

    // -1 ~ 1 범위의 두 겹 노이즈. 바닥 깊이와 물가 위치를 함께 흔든다
    private static float SampleLakeNoise(Vector2 world, Vector2[] offsets)
    {
        float noise = 0f;
        for (int i = 0; i < LakeNoisePeriods.Length; i++)
        {
            float perlin = Mathf.PerlinNoise(world.x / LakeNoisePeriods[i] + offsets[i].x, world.y / LakeNoisePeriods[i] + offsets[i].y);
            noise += LakeNoiseWeights[i] * (perlin * 2f - 1f);
        }
        return noise;
    }

    // 기슭의 경사를 FoothillMaxSlope 이하로 만든다. 이웃 칸끼리 높이 차를 "두 칸 중 작은 제한 기울기 × 거리" 이하로 맞춘다.
    // 1) 고정 칸(호수 물턱 안, 남쪽 경계 줄)에서 제한 기울기로 오르는 위 한계를 구해 그보다 높은 곳만 깎는다 (호수 북쪽 깎인 벽이 완만해진다).
    // 2) 나머지는 낮은 쪽을 올려 메운다. 가파른 비탈 아래에 제한 기울기의 산자락이 붙고 골짜기가 얕아진다.
    //    높은 곳은 내려가지 않으므로 정상, 능선, 봉우리 배치는 그대로 남는다.
    // 1)의 위 한계가 고정 칸 높이에서 제한 기울기로 오르므로, 2)에서 메워도 고정 칸은 올라가지 않는다 (호수 물턱, 남쪽 경계 30m 유지).
    private static float[,] LimitFoothillSlope(float[,] heights, Vector2[,] positions, Basin basin, float sampleSpacing)
    {
        float foothillTan = Mathf.Tan(FoothillMaxSlope * Mathf.Deg2Rad) / StencilSlopeFactor;
        bool[,] pins = CreatePins(positions, basin);

        // 제한 각도를 정할 높이. 깎인 뒤의 높이보다 높게 잡히지 않도록, 기슭 기울기로만 깎았을 때의 위 한계로 먼저 낮춰 둔다
        // (이렇게 해야 결과에서 기슭에 든 칸과 그 이웃이 모두 기슭 제한을 받는다)
        float[,] limitBase = CreateCeiling(heights, pins, CreateFilled(heights, foothillTan), sampleSpacing, "기슭 기울기 위 한계");
        MinInPlace(limitBase, heights);
        float[,] limits = CreateSlopeLimits(limitBase, positions, sampleSpacing, foothillTan);

        float[,] limited = CreateCeiling(heights, pins, limits, sampleSpacing, "고정 칸 위 한계");
        MinInPlace(limited, heights);
        Relax(limited, limits, sampleSpacing, true, "메우기");

        LogLimitResult(heights, limited, pins, foothillTan);
        return limited;
    }

    // 경사 제한에서 높이를 바꾸지 않는 칸 : 호수 물턱 안(ApplyBasin이 호수 높이를 쓰는 곳)과 남쪽 경계 줄
    private static bool[,] CreatePins(Vector2[,] positions, Basin basin)
    {
        int depth = positions.GetLength(0);
        int width = positions.GetLength(1);
        var pins = new bool[depth, width];
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
                pins[z, x] = z == 0 || GetShoreRatio(positions[z, x] - basin.Center) <= LipRadiusRatio;
        }
        return pins;
    }

    private static float[,] CreateFilled(float[,] source, float value)
    {
        var filled = new float[source.GetLength(0), source.GetLength(1)];
        for (int z = 0; z < filled.GetLength(0); z++)
        {
            for (int x = 0; x < filled.GetLength(1); x++)
                filled[z, x] = value;
        }
        return filled;
    }

    // 고정 칸 높이에서 제한 기울기로 올라가는 위 한계 (고정 칸이 아닌 곳은 무한대에서 시작해 내려온다)
    private static float[,] CreateCeiling(float[,] heights, bool[,] pins, float[,] limits, float sampleSpacing, string label)
    {
        var ceiling = new float[heights.GetLength(0), heights.GetLength(1)];
        for (int z = 0; z < ceiling.GetLength(0); z++)
        {
            for (int x = 0; x < ceiling.GetLength(1); x++)
                ceiling[z, x] = pins[z, x] ? heights[z, x] : float.PositiveInfinity;
        }
        Relax(ceiling, limits, sampleSpacing, false, label);
        return ceiling;
    }

    private static void MinInPlace(float[,] target, float[,] other)
    {
        for (int z = 0; z < target.GetLength(0); z++)
        {
            for (int x = 0; x < target.GetLength(1); x++)
                target[z, x] = Mathf.Min(target[z, x], other[z, x]);
        }
    }

    // 칸마다 이웃과의 허용 기울기(tan). 기슭과 그 바로 위는 기슭 제한, 그 위 LimitTransitionHeight 폭에서 사실상 제한 없음까지 커진다
    private static float[,] CreateSlopeLimits(float[,] limitBase, Vector2[,] positions, float sampleSpacing, float foothillTan)
    {
        Vector2 offset = CreateNoiseOffsets(LimitNoiseSeed, 1)[0];
        float foothillAngle = Mathf.Atan(foothillTan) * Mathf.Rad2Deg;
        // 기슭 칸의 이웃은 기슭 상한보다 최대 이만큼 높을 수 있다. 제한이 풀리기 시작하는 높이를 이보다 위에 두어 기슭 칸의 경사 계산에 쓰는 이웃도 모두 기슭 제한을 받게 한다
        float startMargin = foothillTan * sampleSpacing * Mathf.Sqrt(2f);
        var limits = new float[limitBase.GetLength(0), limitBase.GetLength(1)];
        for (int z = 0; z < limits.GetLength(0); z++)
        {
            for (int x = 0; x < limits.GetLength(1); x++)
            {
                Vector2 world = positions[z, x];
                float noise = Mathf.PerlinNoise(world.x / LimitJitterPeriod + offset.x, world.y / LimitJitterPeriod + offset.y);
                float start = FoothillMaxHeight + startMargin + Mathf.Clamp01(noise) * LimitJitter;
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, start + LimitTransitionHeight, limitBase[z, x]));
                limits[z, x] = Mathf.Tan(Mathf.Lerp(foothillAngle, NoLimitSlope, blend) * Mathf.Deg2Rad);
            }
        }
        return limits;
    }

    // 이웃 사이 허용 높이 차(두 칸 중 작은 제한 기울기 × 거리)를 넘는 곳을 고친다. isFill이면 낮은 쪽을 올리고, 아니면 높은 쪽을 내린다.
    // 순방향, 역방향 패스를 바뀌는 값이 없을 때까지 되풀이한다 (같은 입력이면 항상 같은 결과)
    private static void Relax(float[,] values, float[,] limits, float sampleSpacing, bool isFill, string label)
    {
        for (int pass = 1; pass <= MaxRelaxPasses; pass++)
        {
            float change = Mathf.Max(RelaxPass(values, limits, sampleSpacing, isFill, true), RelaxPass(values, limits, sampleSpacing, isFill, false));
            if (change <= RelaxTolerance)
            {
                Debug.Log($"[MapNorthShaper] 경사 제한 계산({label}) {pass}회 반복에서 끝남");
                return;
            }
        }
        Debug.LogWarning($"[MapNorthShaper] 경사 제한 계산({label})이 {MaxRelaxPasses}회 안에 끝나지 않았다. 기슭 경사가 제한을 넘을 수 있다");
    }

    // 한 방향 패스에서 가장 크게 바뀐 높이를 돌려준다
    private static float RelaxPass(float[,] values, float[,] limits, float sampleSpacing, bool isFill, bool isForward)
    {
        int depth = values.GetLength(0);
        int width = values.GetLength(1);
        int sign = isForward ? 1 : -1;
        float maxChange = 0f;
        for (int i = 0; i < depth; i++)
        {
            int z = isForward ? i : depth - 1 - i;
            for (int j = 0; j < width; j++)
            {
                int x = isForward ? j : width - 1 - j;
                float value = values[z, x];
                for (int n = 0; n < RelaxNeighbors.Length; n++)
                {
                    int neighborX = x + RelaxNeighbors[n].x * sign;
                    int neighborZ = z + RelaxNeighbors[n].y * sign;
                    if (neighborX < 0 || neighborX >= width || neighborZ < 0 || neighborZ >= depth) continue;

                    float distance = RelaxNeighbors[n].x != 0 && RelaxNeighbors[n].y != 0 ? sampleSpacing * Mathf.Sqrt(2f) : sampleSpacing;
                    float allowed = Mathf.Min(limits[z, x], limits[neighborZ, neighborX]) * distance;
                    value = isFill
                        ? Mathf.Max(value, values[neighborZ, neighborX] - allowed)
                        : Mathf.Min(value, values[neighborZ, neighborX] + allowed);
                }
                if (value == values[z, x]) continue;

                // 무한대에서 처음 내려온 칸은 바뀐 양을 셀 수 없으므로 한 번 더 돌게 한다
                float change = float.IsInfinity(values[z, x]) ? float.MaxValue : Mathf.Abs(value - values[z, x]);
                maxChange = Mathf.Max(maxChange, change);
                values[z, x] = value;
            }
        }
        return maxChange;
    }

    // 경사 제한으로 메운 높이, 깎은 높이, 바뀐 넓이, 고정 칸이 그대로인지 확인한다
    private static void LogLimitResult(float[,] before, float[,] after, bool[,] pins, float foothillTan)
    {
        float maxRaise = 0f;
        float maxCut = 0f;
        float maxPinChange = 0f;
        int changedCount = 0;
        for (int z = 0; z < before.GetLength(0); z++)
        {
            for (int x = 0; x < before.GetLength(1); x++)
            {
                float change = after[z, x] - before[z, x];
                maxRaise = Mathf.Max(maxRaise, change);
                maxCut = Mathf.Max(maxCut, -change);
                if (Mathf.Abs(change) > ChangeThreshold) changedCount++;
                if (pins[z, x]) maxPinChange = Mathf.Max(maxPinChange, Mathf.Abs(change));
            }
        }
        float changedRatio = (float)changedCount / before.Length;
        Debug.Log($"[MapNorthShaper] 기슭 경사 제한 : 이웃 사이 제한 {Mathf.Atan(foothillTan) * Mathf.Rad2Deg:F1}도, 최대 메움 +{maxRaise:F1}m, 최대 깎음 -{maxCut:F1}m, " +
                  $"바뀐 넓이 {changedRatio * 100f:F1}%, 고정 칸(호수 물턱 안, 남쪽 경계 줄) 최대 변화 {maxPinChange:F4}m (0이어야 함)");
    }

    // 검수(최고점, 경계 이어짐, 맵 끝 높이, 경사, 분지 둘레, 강이 흘러내릴 비탈)에 쓰는 값이라 로그를 남긴다.
    private static void LogResult(float[,] heights, float[,] naturalHeights, Vector2[,] positions, Basin basin)
    {
        float sampleSpacing = positions[0, 1].x - positions[0, 0].x;
        Vector2Int highest = FindHighestIndex(heights, out float minHeight);
        Vector2 highestPosition = positions[highest.y, highest.x];
        int northRow = heights.GetLength(0) - 1;
        float northAverage = GetRowAverage(heights, positions, northRow, MapGrid.GetRangeBounds(NorthFromCell, NorthToCell));
        float peakCellAverage = GetRowAverage(heights, positions, northRow, MapGrid.GetRangeBounds(PeakCell, PeakCell));
        SlopeStats slope = CalculateSlopeStats(heights, sampleSpacing);

        Debug.Log($"[MapNorthShaper] 높이 최저 {minHeight:F1}m, 최고 {heights[highest.y, highest.x]:F1}m, 최고점 ({highestPosition.x:F0}, {highestPosition.y:F0})");
        Debug.Log($"[MapNorthShaper] 남쪽 경계 줄(z {positions[0, 0].y:F2})과 바탕 높이의 최대 차이 {GetMaxRowDifference(heights, 0):F4}m");
        Debug.Log($"[MapNorthShaper] 맵 북쪽 끝 평균 높이 전체 {northAverage:F1}m, {PeakCell} 구간 {peakCellAverage:F1}m");
        Debug.Log($"[MapNorthShaper] 최대 경사 {slope.MaxSlope:F1}도, {MapTreeScatter.MaxSlope}도 초과 비율 {slope.SteepRatio * 100f:F1}% " +
                  $"(높이 {SummitZoneHeight}m 미만만 {slope.SteepRatioBelowSummit * 100f:F1}%)");
        Debug.Log($"[MapNorthShaper] 기슭(높이 {FoothillMaxHeight}m 미만) 최대 경사 {slope.MaxFoothillSlope:F2}도, {FoothillMaxSlope}도 초과 비율 " +
                  $"{slope.FoothillSteepRatio * 100f:F3}% (0이어야 함, 높이맵 격자 가운데 차분 기준, 허용 오차 {SlopeCheckTolerance}도)");
        LogBasinRing(heights, naturalHeights, positions, basin);
        LogSouthLine(heights, positions, basin);
    }

    // 분지 둘레(노이즈 전 물가 밖부터 BlendRadius까지)가 원래 산보다 얼마나 가팔라졌는지, 얼마나 메우고 깎였는지 확인한다.
    // 메운 높이가 크면 남쪽 단, 깎은 높이와 경사가 크면 북쪽 벽이 인공적으로 보인다
    private static void LogBasinRing(float[,] heights, float[,] naturalHeights, Vector2[,] positions, Basin basin)
    {
        float sampleSpacing = positions[0, 1].x - positions[0, 0].x;
        float maxSlope = 0f;
        float maxNaturalSlope = 0f;
        Vector2 maxSlopePosition = Vector2.zero;
        float maxFill = 0f;
        float maxCut = 0f;
        for (int z = 1; z < heights.GetLength(0) - 1; z++)
        {
            for (int x = 1; x < heights.GetLength(1) - 1; x++)
            {
                Vector2 offset = positions[z, x] - basin.Center;
                if (offset.magnitude >= basin.BlendRadius || GetShoreRatio(offset) <= 1f) continue;

                float slope = GetSlope(heights, z, x, sampleSpacing);
                if (slope > maxSlope)
                {
                    maxSlope = slope;
                    maxSlopePosition = positions[z, x];
                }
                maxNaturalSlope = Mathf.Max(maxNaturalSlope, GetSlope(naturalHeights, z, x, sampleSpacing));
                float change = heights[z, x] - naturalHeights[z, x];
                maxFill = Mathf.Max(maxFill, change);
                maxCut = Mathf.Max(maxCut, -change);
            }
        }
        Debug.Log($"[MapNorthShaper] 분지 둘레(물가 밖 ~ 중심에서 {basin.BlendRadius:F0}m) 최대 경사 {maxSlope:F1}도 " +
                  $"({maxSlopePosition.x:F0}, {maxSlopePosition.y:F0}), 같은 범위 원래 산 최대 경사 {maxNaturalSlope:F1}도");
        Debug.Log($"[MapNorthShaper] 분지 둘레 원래 산 대비 최대 메움 +{maxFill:F1}m (남쪽 단), 최대 깎음 -{maxCut:F1}m (북쪽 벽)");
    }

    // 호수 중심에서 남쪽 경계까지 중심선을 따라, 남쪽 물턱 꼭대기와 그 너머로 강이 넘어야 할 오르막 높이의 최대값을 구한다.
    // 물턱 꼭대기는 이 중심선에서 호수 중심보다 남쪽의 가장 높은 곳이다 (그 남쪽은 원래 산으로 내려간다)
    private static void LogSouthLine(float[,] heights, Vector2[,] positions, Basin basin)
    {
        float sampleSpacing = positions[0, 1].x - positions[0, 0].x;
        Vector2 origin = positions[0, 0];
        int column = Mathf.RoundToInt((basin.Center.x - origin.x) / sampleSpacing);
        int crestRow = Mathf.RoundToInt((basin.Center.y - origin.y) / sampleSpacing);
        for (int z = crestRow - 1; z >= 0; z--)
        {
            if (heights[z, column] > heights[crestRow, column]) crestRow = z;
        }

        float lowest = heights[crestRow, column];
        float maxRise = 0f;
        for (int z = crestRow - 1; z >= 0; z--)
        {
            lowest = Mathf.Min(lowest, heights[z, column]);
            maxRise = Mathf.Max(maxRise, heights[z, column] - lowest);
        }
        Debug.Log($"[MapNorthShaper] 남쪽 물턱 꼭대기 z {positions[crestRow, column].y:F1}, 수면 + {heights[crestRow, column] - basin.WaterHeight:F2}m. " +
                  $"그 남쪽 중심선 최대 오르막 {maxRise:F2}m (0이면 남쪽 경계까지 계속 내려간다)");
    }

    private static Vector2Int FindHighestIndex(float[,] heights, out float minHeight)
    {
        var highest = Vector2Int.zero;
        minHeight = float.MaxValue;
        for (int z = 0; z < heights.GetLength(0); z++)
        {
            for (int x = 0; x < heights.GetLength(1); x++)
            {
                minHeight = Mathf.Min(minHeight, heights[z, x]);
                if (heights[z, x] > heights[highest.y, highest.x]) highest = new Vector2Int(x, z);
            }
        }
        return highest;
    }

    private static float GetMaxRowDifference(float[,] heights, int row)
    {
        float maxDifference = 0f;
        for (int x = 0; x < heights.GetLength(1); x++)
            maxDifference = Mathf.Max(maxDifference, Mathf.Abs(heights[row, x] - MapTerrainSetup.BaseHeight));
        return maxDifference;
    }

    // 한 줄에서 area의 동서 범위 안에 있는 꼭짓점의 평균 높이
    private static float GetRowAverage(float[,] heights, Vector2[,] positions, int row, Rect area)
    {
        float sum = 0f;
        int count = 0;
        for (int x = 0; x < heights.GetLength(1); x++)
        {
            float worldX = positions[row, x].x;
            if (worldX < area.xMin || worldX > area.xMax) continue;
            sum += heights[row, x];
            count++;
        }
        return sum / count;
    }

    // 나무 배치 기준 경사(MapTreeScatter.MaxSlope)를 넘는 곳이 얼마나 되는지 확인한다. 정상부 바위 지대를 뺀 비율과 기슭 경사 제한 결과도 함께 구한다
    private static SlopeStats CalculateSlopeStats(float[,] heights, float sampleSpacing)
    {
        int depth = heights.GetLength(0);
        int width = heights.GetLength(1);
        var stats = new SlopeStats();
        int steepCount = 0;
        int belowSummitCount = 0;
        int steepBelowSummitCount = 0;
        int foothillCount = 0;
        int foothillSteepCount = 0;
        for (int z = 1; z < depth - 1; z++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                float slope = GetSlope(heights, z, x, sampleSpacing);
                stats.MaxSlope = Mathf.Max(stats.MaxSlope, slope);
                bool isSteep = slope > MapTreeScatter.MaxSlope;
                if (isSteep) steepCount++;
                if (heights[z, x] < FoothillMaxHeight)
                {
                    foothillCount++;
                    stats.MaxFoothillSlope = Mathf.Max(stats.MaxFoothillSlope, slope);
                    if (slope > FoothillMaxSlope + SlopeCheckTolerance) foothillSteepCount++;
                }
                if (heights[z, x] >= SummitZoneHeight) continue;

                belowSummitCount++;
                if (isSteep) steepBelowSummitCount++;
            }
        }
        stats.SteepRatio = (float)steepCount / ((depth - 2) * (width - 2));
        stats.SteepRatioBelowSummit = (float)steepBelowSummitCount / belowSummitCount;
        stats.FoothillSteepRatio = (float)foothillSteepCount / foothillCount;
        return stats;
    }

    private static float GetSlope(float[,] heights, int z, int x, float sampleSpacing)
    {
        float gradientX = (heights[z, x + 1] - heights[z, x - 1]) / (2f * sampleSpacing);
        float gradientZ = (heights[z + 1, x] - heights[z - 1, x]) / (2f * sampleSpacing);
        return Mathf.Atan(Mathf.Sqrt(gradientX * gradientX + gradientZ * gradientZ)) * Mathf.Rad2Deg;
    }

    private static float[,] ToNormalized(float[,] heights, float terrainHeight)
    {
        int depth = heights.GetLength(0);
        int width = heights.GetLength(1);
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
                heights[z, x] /= terrainHeight;
        }
        return heights;
    }
}
