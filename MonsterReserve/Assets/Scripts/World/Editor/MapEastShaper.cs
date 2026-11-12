using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 메뉴 24 : 동쪽 구역(C2)에 Dreamscape 데모 지형의 산맥 조각을 옮겨 심고, 남쪽으로 습지 높이(30m)까지 내려가는 기슭을 만든다.
// 1) 조각 고르기 : 데모 지형(MapDemoTransplant.DemoTerrainDataPath, 읽기만 함)에서 PatchWidth × PatchDepth 조각을
//    PatchSearchStep 간격의 자리, 회전 0/90/270°(180°는 북쪽 줄과 같아 뺌), 좌우 뒤집기 유무로 돌며, 실제 호수 물(호수 중심과 이어진 수면 아래 땅)이
//    들어오지 않고 점수가 가장 높은 것을 고른다. 호수와 이어지지 않은 낮은 골짜기는 들어와도 된다.
//    점수 = 핵심 구역(우리 z -100 ~ 250) 높이 범위 + 잔굴곡(라플라시안) × TextureWeight + 북쪽이 높은 기울기 × TiltWeight
//          + 모양 맞춤 × ShapeWeight. 모양 맞춤은 조각 북쪽 끝(우리 z 250 ~ 370) 열별 최고 높이와 원래 C1 z 400 줄 높이의 x 방향 상관계수다.
//    ShapeWeight가 커서 C1과 모양이 맞는 조각이 먼저 골라진다.
// 2) 높이 맞추기 : 조각 핵심 구역의 최저 → 30m로 한 직선으로 옮긴다. 배율은 핵심 구역 최고 → PeakHeight(130m)를 넘지 않고,
//    조각 북쪽 끝이 C1 z 400 높이 + NorthAllowance를 넘지 않는 가장 큰 값이며, 최고점이 PeakMinHeight(100m) 아래로 내려가게는 줄이지 않는다.
// 3) 남쪽 기슭 : z FootStartZ(50) → -250에서 30m 위 높이에서 깎는 양 D를 0 → PeakHeight - 30으로 늘려(t^FootCutPower) 빼고,
//    0 아래는 부드럽게 0으로 모은다. 처음엔 조금씩 깎아 골짜기만 먼저 30m에 닿고, 산줄기는 남쪽 경계 가까이까지 흘러내린다.
//    D를 노이즈로 흔들어(±FootCutWobble) 산 남쪽 끝이 한 줄로 끊기지 않게 한다. 조각은 z -250까지 덮어 데모 지형이 그대로 쓰인다.
// 4) C1 섞기 : 조각은 z 370까지 덮는다. C1 z 250 ~ 400 띠 중 z 250 ~ 370에서 새 산맥과 원래 C1 참조를 w(z)(smoothstep)로 섞고,
//    z 370 ~ 400은 원래 C1 참조만 쓴다.
//    원래 C1 참조 = z 400 줄 높이와, z 400 ~ 450 줄(띠 밖, 읽기만)로 잰 남북 기울기를 x 방향으로 고른 값으로 남쪽으로 이어 간 것.
//    기울기는 z 400에서 그대로 시작해 남쪽으로 갈수록 줄어(지수 감쇠, NorthExtendLength) 떨어지는 양에 한도가 있다.
//    z 400 바로 앞 ResidualFadeDepth에서만 고르기 전 값으로 돌아가 z 400 줄과 높이가 맞는다.
// 5) 경계 : 서(B2, 띠는 B1)와 동(맵 끝)은 감쇠 폭 150 ~ 250m, 남(C3)은 50 ~ 100m (위치에 따라 흔들림) 안에서 그 경계 줄 높이로 돌아간다.
//    C2의 경계 줄은 30m이고, 띠의 서·동 끝 줄은 원래 높이 그대로다.
// z 400 줄, 서·동 끝 줄, 남쪽 끝 줄은 쓰지 않는다. 이 메뉴는 그 줄들과 데모 지형만 읽어 다시 실행해도 결과가 같다.
// 높이를 쓴 뒤 띠 안의 NorthEast 배치물(나무, 바위 등)을 지면이 바뀐 만큼 위아래로 옮긴다 (메뉴 8-1의 놓는 방식과 같은 기준점).
// 서식지 자리는 지형이 나온 뒤 고른다 : 거점 기준 방위 67.5 ~ 112.5°, 반경 안 경사 평균이 낮은 후보 3곳을 로그로 낸다.
// 산맥 위인지는 따로 판정 함수를 두지 않는다. 메뉴 25, 26은 지형 높이와 경사로 판단한다.
// 실행 순서 : ... → 23 → 24 → 25 → 26 → 10.
// 1-1(평탄화), 5-1(북쪽 지형), 8-1(북쪽 오브젝트) 등 C1 지형이나 배치물을 다시 만드는 메뉴를 실행하면 24도 다시 실행한다.
// 24를 다시 실행하면 지형 높이가 바뀌므로 25, 26도 순서대로 다시 실행한다.
public static class MapEastShaper
{
    // 동쪽 칸. 메뉴 25, 26도 같은 칸 범위를 읽으므로 공개한다
    public const string EastCell = "C2";

    // 서식지 (사용자 승인 임시값). 산 기슭 남쪽 평지(지면 약 30m), 거점 기준 방위 약 105.6°(동).
    // 지형 계산에는 쓰지 않고 로그에만 쓴다. 동쪽 배치(메뉴 25)가 서식지 위치 표시와 비움 판정에 쓰므로 공개한다
    public static readonly Vector2 HabitatCenter = new Vector2(610f, -170f);
    public const float HabitatRadius = 80f;

    // 조각 크기 (우리 x, z 방향, m). 남쪽 경계 z -250부터 z 370까지 덮는다.
    // 깊이를 650(z 400까지)으로 하면 데모 호수를 피하는 자리가 없어 620으로 줄였다 (데모 호수 z 623 ~ 1057, 호수 동쪽 남는 폭 620m)
    private const float PatchWidth = 1000f;
    private const float PatchDepth = 620f;
    // 조각 남쪽 끝에서 잰 핵심 구역 (우리 z -100 ~ 250). 점수와 높이 맞추기는 이 구역으로 한다
    private const float CoreStart = 150f;
    private const float CoreEnd = 500f;
    // 조각 자리 후보 간격과 점수 표본 간격 (m, 임시값)
    private const float PatchSearchStep = 20f;
    private const float ScoreSampleStep = 10f;
    // 조각 회전 후보 (위에서 볼 때 도). 180°는 북쪽 줄(메뉴 5-1)과 같은 방향이라 뺀다
    private static readonly int[] PatchRotations = { 0, 90, 270 };
    // 좌우 뒤집기 후보 (뒤집지 않음, 뒤집음)
    private static readonly bool[] MirrorOptions = { false, true };
    // 점수 가중치 (임시값). 잔굴곡은 표본 하나가 이웃 4개 평균에서 벗어난 평균 (m), 기울기는 핵심 구역 북쪽 1/3 평균 - 남쪽 1/3 평균 (m)
    private const float TextureWeight = 10f;
    private const float TiltWeight = 0.5f;
    // 모양 맞춤 가중치 (임시값). 상관계수 0.1 차이가 점수 100이라, 높이 범위 등 다른 항목보다 먼저 본다
    private const float ShapeWeight = 1000f;

    // 맞춘 높이 : 핵심 구역 최고점의 최대, 최소 (m)
    private const float PeakHeight = 130f;
    private const float PeakMinHeight = 100f;
    // 조각 북쪽 끝 높이가 C1 z 400 높이를 넘어도 되는 양 (m, 임시값)
    private const float NorthAllowance = 10f;
    // 남쪽 기슭이 시작하는 z (m)
    private const float FootStartZ = 50f;
    // 깎는 양 곡선 t^FootCutPower와 노이즈 흔들림 비율, 주기 (임시값). 3이면 z -200(t 0.83)에서도 최대의 약 58%만 깎는다
    private const float FootCutPower = 3f;
    private const float FootCutWobble = 0.25f;
    private const float FootCutWobblePeriod = 150f;
    private static readonly Vector2 FootCutNoiseOffset = new Vector2(260f, 470f);
    // 30m 아래로 내려간 값을 0 쪽으로 모으는 부드러움 (m, 임시값). 골짜기 바닥이 30m에서 꺾이지 않게 한다
    private const float SoftFloorWidth = 3f;

    // C2 북쪽 경계에서 C1 안쪽으로 섞는 띠의 깊이 (m)
    private const float BlendDepth = 150f;
    // 원래 C1 높이(z 400 줄)와 기울기를 x 방향으로 고르게 하는 반경, 고르기 전 값으로 돌아가는 z 400 앞 거리 (m, 임시값)
    private const float NorthSmoothRadius = 20f;
    private const float ResidualFadeDepth = 20f;
    // C1 기울기를 잴 z 400 북쪽 거리 (띠 밖, 읽기만)와, 남쪽으로 이어 갈 때 기울기가 줄어드는 길이 (m, 임시값)
    private const float NorthSlopeSampleDepth = 50f;
    private const float NorthExtendLength = 30f;

    // 서, 동 경계 감쇠 폭 (기본 폭 ± 흔들림 비율 = 150 ~ 250m)
    private const float SideFadeWidth = 200f;
    private const float SideFadeWobble = 0.25f;
    // 남 경계 감쇠 폭 (50 ~ 100m). 남쪽(메뉴 17), 남동쪽(메뉴 20)과 같은 값
    private const float EdgeFadeWidth = 75f;
    private const float EdgeFadeWobble = 1f / 3f;
    private const float EdgeFadeWobblePeriod = 150f;
    // 폭 노이즈 무늬 위치
    private static readonly Vector2 EdgeFadeNoiseOffset = new Vector2(540f, 910f);

    // 서식지 후보 : 거점 기준 방위 범위 (도), 후보 간격, 경사 평균 표본 간격, 후보끼리 최소 거리, 낼 개수 (m, 임시값)
    private const float HabitatBearingMin = 67.5f;
    private const float HabitatBearingMax = 112.5f;
    private const float HabitatSearchStep = 20f;
    private const float HabitatSlopeSampleStep = 10f;
    private const float HabitatCandidateSpacing = 150f;
    private const int HabitatCandidateCount = 3;

    // 띠 안 배치물을 찾을 구역 부모와, 바닥 둘레 최저점에 놓인 바위 묶음 이름
    private const string NorthEastRegionName = "NorthEast";
    private const string RockGroupName = "Rocks";
    // 바위 바닥 반지름 (크기 1 기준, m). 메뉴 8-1(MapDemoObjectTransplant)과 같은 값
    private const float RockFootprintRadius = 1f;

    // 로그 : 남북 단면 x와 z 범위·간격, 거칠기를 볼 z, 서쪽 경사를 볼 x 폭, 봉우리 수·칸·간격 (m)
    private static readonly float[] SectionLogXs = { 400f, 600f, 800f, 1100f };
    private const float SectionLogZMin = -250f;
    private const float SectionLogZMax = 450f;
    private const float SectionLogStep = 50f;
    private static readonly float[] RoughnessLogZs = { 150f, 300f, 350f, 450f };
    private const float WestSlopeLogWidth = 200f;
    private const int PeakLogCount = 5;
    private const float PeakCellSize = 10f;
    private const float PeakSpacing = 100f;
    // 쓴 범위 밖이 바뀌지 않았는지 볼 때 둘레로 더 읽는 높이맵 칸 수
    private const int OutsideCheckMargin = 16;
    // 높이맵 점, 배치물이 바뀌었다고 볼 최소 높이 차 (m). 높이맵 저장 단위(약 0.006m)보다 작다
    private const float ChangeTolerance = 0.001f;

    private static readonly Rect EastBounds = MapGrid.GetRangeBounds(EastCell, EastCell);

    // 데모 높이맵 (m, 데모 높이맵 기준 좌표. 데모 Terrain 위치가 원점이라 데모 월드 좌표와 같다)
    private class DemoField
    {
        public float[,] Heights;
        public int Resolution;
        public float SpacingX;
        public float SpacingZ;
        public float Width;
        public float Depth;
    }

    // 조각 하나. Origin은 데모에서 조각이 차지하는 사각형의 왼쪽 아래 (데모 x, z).
    // IsMirrored면 조각 안에서 동서를 뒤집은 뒤 회전한다 (차지하는 데모 사각형은 같다)
    private struct Patch
    {
        public Vector2 Origin;
        public int Rotation;
        public bool IsMirrored;
        public float Score;
        // 조각 북쪽 끝과 C1 z 400 줄의 x 방향 높이 상관계수 (-1 ~ 1)
        public float ShapeMatch;
        public float Relief;
        public float Texture;
        public float Tilt;
        public float RawMin;
        public float RawMax;
    }

    // 데모 호수 실제 물 마스크. 데모 높이맵 번호 (OriginX, OriginZ)부터의 사각형에 대한 물 점 누적 합
    private class LakeMask
    {
        public int OriginX;
        public int OriginZ;
        public int[,] Prefix;
        public int CellCount;
    }

    // 높이 계산에 함께 쓰는 값. 높이맵 번호 [z, x]는 Range 왼쪽 아래 꼭짓점이 [0, 0]이다
    private class ShapeField
    {
        // C2 + 섞는 띠 (가장자리 줄 포함)
        public Rect Area;
        public RectInt Range;
        public Vector2 Origin;
        public float Spacing;
        // 쓰기 전 Range 전체 높이 (m). 가장자리 줄은 쓰지 않으므로 경계 높이로 읽는다
        public float[,] Frame;
        // 열마다 z 400 원래 C1 높이, x 방향으로 고른 값, 고른 남북 기울기 (북쪽으로 오를 때 +, m/m)
        public float[] NorthTops;
        public float[] NorthSmooth;
        public float[] NorthSlopes;
        public DemoField Demo;
        public Patch Patch;
        // 데모 높이 1m당 맞춘 높이 (m)와, 배율을 정한 근거 (북쪽 끝 한도 배율, 한도를 넘는 열 수)
        public float Scale;
        public float NorthLimitScale;
        public int NorthOverCount;
    }

    // 지면이 바뀐 만큼 옮길 띠 안 배치물
    private class BandObject
    {
        public Transform Target;
        // 0이면 피벗 아래 지면, 0보다 크면 이 반경 둘레 최저 지면을 기준으로 놓인 것
        public float FootprintRadius;
        public float GroundBefore;
    }

    // 서식지 후보
    private struct HabitatCandidate
    {
        public Vector2 Center;
        public float MeanSlope;
        public float Height;
    }

    [MenuItem("Tools/Map/24. 동쪽 지형 만들기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        ShapeField field = CreateField(terrain, out int candidateCount, out int lakeSkipCount);
        if (field == null) return;

        TerrainData data = terrain.terrainData;
        RectInt checkRange = GetCheckRange(field.Range, data.heightmapResolution);
        float[,] checkBefore = data.GetHeights(checkRange.x, checkRange.y, checkRange.width, checkRange.height);
        List<BandObject> bandObjects = CollectBandObjects(terrain);

        float[,] heights = CalculateHeights(field);
        // 가장자리 줄은 쓰지 않고 그 안쪽만 덮어쓴다
        data.SetHeights(field.Range.x + 1, field.Range.y + 1, ToNormalized(heights, data.size.y));
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        Vector2 moved = MoveBandObjects(terrain, bandObjects);
        // 검수 때 조각, 산맥 높이, 단면, 띠 줄무늬, 경사, 서식지 후보, 경계, 쓴 범위 밖, 띠 배치물을 확인할 값이라 남겨 둔다
        float[,] slopes = CalculateSlopes(heights, field.Spacing);
        LogPatch(field, candidateCount, lakeSkipCount);
        LogHeights(field, heights, slopes);
        LogSections(terrain);
        LogBand(terrain, field);
        LogHabitat(field, heights, slopes);
        LogChecks(data, field, CountOutsideChanged(data, checkRange, checkBefore, field.Range));
        Debug.Log($"[MapEastShaper] 띠 안 {NorthEastRegionName} 배치물 {bandObjects.Count}개 중 {moved.x:F0}개 옮김, 옮긴 높이 최대 {moved.y:F2}m");
    }

    // ---------- 데모 조각 고르기 ----------

    private static ShapeField CreateField(Terrain terrain, out int candidateCount, out int lakeSkipCount)
    {
        TerrainData data = terrain.terrainData;
        int divisions = data.heightmapResolution - 1;
        var area = Rect.MinMaxRect(EastBounds.xMin, EastBounds.yMin, EastBounds.xMax, EastBounds.yMax + BlendDepth);
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, area, divisions);
        float spacing = data.size.x / divisions;
        Vector3 position = terrain.transform.position;
        var field = new ShapeField
        {
            Area = area, Range = range, Spacing = spacing, Demo = LoadDemo(),
            Origin = new Vector2(position.x + range.x * spacing, position.z + range.y * spacing),
            Frame = ReadMeters(data, range.x, range.y, range.width + 1, range.height + 1)
        };
        ReadNorthEdge(data, field);

        float[] c1Profile = CreateC1Profile(field);
        field.Patch = SelectPatch(field.Demo, c1Profile, out candidateCount, out lakeSkipCount);
        if (candidateCount == lakeSkipCount)
        {
            Debug.LogError("[MapEastShaper] 호수가 없는 데모 조각 자리가 없습니다. 조각 크기나 회전 후보를 확인하세요.");
            return null;
        }
        SetScale(field, c1Profile);
        return field;
    }

    // z 400 줄(쓰지 않음)의 높이와, z 400 ~ 400 + NorthSlopeSampleDepth 줄(띠 밖)로 잰 남북 기울기(최소제곱)를 열마다 읽고 x 방향으로 고른다
    private static void ReadNorthEdge(TerrainData data, ShapeField field)
    {
        int count = field.Range.width + 1;
        int sampleRows = Mathf.RoundToInt(NorthSlopeSampleDepth / field.Spacing);
        float[,] rows = ReadMeters(data, field.Range.x, field.Range.yMax, count, sampleRows + 1);
        field.NorthTops = new float[count];
        var slopes = new float[count];
        float meanOffset = sampleRows * 0.5f;
        for (int c = 0; c < count; c++)
        {
            field.NorthTops[c] = rows[0, c];
            float meanHeight = 0f;
            for (int r = 0; r <= sampleRows; r++)
                meanHeight += rows[r, c] / (sampleRows + 1);
            float numerator = 0f, denominator = 0f;
            for (int r = 0; r <= sampleRows; r++)
            {
                numerator += (r - meanOffset) * (rows[r, c] - meanHeight);
                denominator += (r - meanOffset) * (r - meanOffset);
            }
            slopes[c] = numerator / denominator / field.Spacing;
        }
        int radius = Mathf.RoundToInt(NorthSmoothRadius / field.Spacing);
        field.NorthSmooth = SmoothColumns(field.NorthTops, radius);
        field.NorthSlopes = SmoothColumns(slopes, radius);
    }

    // C1 z 400 줄(고른 값)을 조각 점수 표본과 같은 x 간격(ScoreSampleStep)으로 뽑는다
    private static float[] CreateC1Profile(ShapeField field)
    {
        var profile = new float[Mathf.RoundToInt(PatchWidth / ScoreSampleStep) + 1];
        for (int i = 0; i < profile.Length; i++)
        {
            int column = Mathf.Clamp(Mathf.RoundToInt((EastBounds.xMin + i * ScoreSampleStep - field.Origin.x) / field.Spacing), 0, field.Range.width);
            profile[i] = field.NorthSmooth[column];
        }
        return profile;
    }

    // 핵심 구역 최저 → 30m. 배율은 최고점 PeakHeight 이하이면서 조각 북쪽 끝이 C1 + NorthAllowance 이하인 가장 큰 값이고,
    // 최고점이 PeakMinHeight보다 낮아지게는 줄이지 않는다 (그때 한도를 넘는 열 수를 남긴다)
    private static void SetScale(ShapeField field, float[] c1Profile)
    {
        Patch patch = field.Patch;
        float relief = patch.RawMax - patch.RawMin;
        float[] north = CalculateNorthProfile(field.Demo, patch);
        field.NorthLimitScale = float.MaxValue;
        for (int i = 0; i < north.Length; i++)
        {
            float rawRise = north[i] - patch.RawMin;
            if (rawRise <= 0f) continue;
            field.NorthLimitScale = Mathf.Min(field.NorthLimitScale, (c1Profile[i] + NorthAllowance - MapTerrainSetup.BaseHeight) / rawRise);
        }

        float minScale = (PeakMinHeight - MapTerrainSetup.BaseHeight) / relief;
        float maxScale = (PeakHeight - MapTerrainSetup.BaseHeight) / relief;
        field.Scale = Mathf.Clamp(field.NorthLimitScale, minScale, maxScale);
        for (int i = 0; i < north.Length; i++)
        {
            float height = MapTerrainSetup.BaseHeight + (north[i] - patch.RawMin) * field.Scale;
            if (height > c1Profile[i] + NorthAllowance) field.NorthOverCount++;
        }
    }

    // 데모 높이맵을 미터 단위로 한 번에 읽는다 (MapDemoTransplant와 같은 방식)
    private static DemoField LoadDemo()
    {
        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(MapDemoTransplant.DemoTerrainDataPath);
        int resolution = data.heightmapResolution;
        float[,] heights = data.GetHeights(0, 0, resolution, resolution);
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
                heights[z, x] *= data.size.y;
        }
        return new DemoField
        {
            Heights = heights, Resolution = resolution, Width = data.size.x, Depth = data.size.z,
            SpacingX = data.size.x / (resolution - 1), SpacingZ = data.size.z / (resolution - 1)
        };
    }

    // 데모 간격과 우리 간격이 달라 쌍선형 보간으로 읽는다
    private static float SampleDemo(DemoField demo, Vector2 position)
    {
        int last = demo.Resolution - 1;
        float gridX = Mathf.Clamp(position.x / demo.SpacingX, 0f, last);
        float gridZ = Mathf.Clamp(position.y / demo.SpacingZ, 0f, last);
        int x0 = Mathf.Min((int)gridX, last - 1);
        int z0 = Mathf.Min((int)gridZ, last - 1);
        float south = Mathf.Lerp(demo.Heights[z0, x0], demo.Heights[z0, x0 + 1], gridX - x0);
        float north = Mathf.Lerp(demo.Heights[z0 + 1, x0], demo.Heights[z0 + 1, x0 + 1], gridX - x0);
        return Mathf.Lerp(south, north, gridZ - z0);
    }

    // 조각 회전에 따라 데모에서 차지하는 크기 (x, z)
    private static Vector2 GetDemoSpan(int rotation)
    {
        return rotation == 0 ? new Vector2(PatchWidth, PatchDepth) : new Vector2(PatchDepth, PatchWidth);
    }

    // 조각 기준 위치 (u = 서쪽 끝에서 동쪽으로, v = 남쪽 끝에서 북쪽으로, m) → 데모 좌표. 뒤집기가 있으면 동서를 먼저 뒤집고 회전한다
    private static Vector2 PatchToDemo(Patch patch, float u, float v)
    {
        if (patch.IsMirrored) u = PatchWidth - u;
        switch (patch.Rotation)
        {
            case 90: return patch.Origin + new Vector2(v, PatchWidth - u);
            case 270: return patch.Origin + new Vector2(PatchDepth - v, u);
            default: return patch.Origin + new Vector2(u, v);
        }
    }

    // 자리, 회전마다 호수를 한 번 보고, 뒤집기 유무 두 가지로 점수를 낸다 (뒤집어도 차지하는 데모 사각형은 같다)
    private static Patch SelectPatch(DemoField demo, float[] c1Profile, out int candidateCount, out int lakeSkipCount)
    {
        var best = new Patch { Score = float.MinValue };
        candidateCount = 0;
        lakeSkipCount = 0;
        LakeMask lake = CreateLakeMask(demo);
        // 검수 때 실제 호수만 뺐는지 확인할 값이라 남겨 둔다
        Debug.Log($"[MapEastShaper] 데모 호수 마스크 : 수면 {MapDemoTransplant.DemoLakePosition.y:F3}m보다 낮고 호수 중심과 이어진 땅 " +
                  $"{lake.CellCount}칸, 넓이 {lake.CellCount * demo.SpacingX * demo.SpacingZ:F0}m²");
        foreach (int rotation in PatchRotations)
        {
            Vector2 span = GetDemoSpan(rotation);
            for (float z = 0f; z <= demo.Depth - span.y; z += PatchSearchStep)
            {
                for (float x = 0f; x <= demo.Width - span.x; x += PatchSearchStep)
                {
                    var patch = new Patch { Origin = new Vector2(x, z), Rotation = rotation };
                    candidateCount += 2;
                    if (HasLake(demo, lake, patch, span))
                    {
                        lakeSkipCount += 2;
                        continue;
                    }
                    foreach (bool isMirrored in MirrorOptions)
                    {
                        patch.IsMirrored = isMirrored;
                        ScorePatch(demo, c1Profile, ref patch);
                        if (patch.Score > best.Score) best = patch;
                    }
                }
            }
        }
        return best;
    }

    // 실제 호수 물 : 데모 호수 물 평면 사각형 안에서 수면보다 낮고 호수 중심과 이어진(4방향) 데모 높이맵 점.
    // 호수와 이어지지 않은 낮은 골짜기는 물이 아니라 조각에 들어와도 된다 (동쪽에는 호수가 필요 없음, 사용자 결정)
    private static LakeMask CreateLakeMask(DemoField demo)
    {
        Vector3 lake = MapDemoTransplant.DemoLakePosition;
        float half = MapDemoTransplant.DemoLakeHalfSize;
        int last = demo.Resolution - 1;
        int xMin = Mathf.Clamp(Mathf.CeilToInt((lake.x - half) / demo.SpacingX), 0, last);
        int zMin = Mathf.Clamp(Mathf.CeilToInt((lake.z - half) / demo.SpacingZ), 0, last);
        int xMax = Mathf.Clamp(Mathf.FloorToInt((lake.x + half) / demo.SpacingX), 0, last);
        int zMax = Mathf.Clamp(Mathf.FloorToInt((lake.z + half) / demo.SpacingZ), 0, last);
        var mask = new LakeMask { OriginX = xMin, OriginZ = zMin };
        var isWater = new bool[zMax - zMin + 1, xMax - xMin + 1];

        Vector2Int seed = FindLakeSeed(demo, mask, isWater, lake);
        if (seed.x >= 0) mask.CellCount = FloodLake(demo, mask, isWater, seed, lake.y);
        mask.Prefix = CreatePrefix(isWater);
        return mask;
    }

    // 호수 중심에서 가장 가까운 수면 아래 점 (중심 아래가 물 밖일 수 있어서다, MapDemoTransplant.GetLakeCenter 주석). 없으면 (-1, -1)
    private static Vector2Int FindLakeSeed(DemoField demo, LakeMask mask, bool[,] isWater, Vector3 lake)
    {
        var seed = new Vector2Int(-1, -1);
        float bestDistance = float.MaxValue;
        for (int z = 0; z < isWater.GetLength(0); z++)
        {
            for (int x = 0; x < isWater.GetLength(1); x++)
            {
                if (demo.Heights[mask.OriginZ + z, mask.OriginX + x] >= lake.y) continue;
                var position = new Vector2((mask.OriginX + x) * demo.SpacingX, (mask.OriginZ + z) * demo.SpacingZ);
                float distance = (position - new Vector2(lake.x, lake.z)).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                seed = new Vector2Int(x, z);
            }
        }
        return seed;
    }

    // seed에서 4방향으로 수면 아래 점을 모두 isWater에 표시하고 그 수를 돌려준다
    private static int FloodLake(DemoField demo, LakeMask mask, bool[,] isWater, Vector2Int seed, float waterLevel)
    {
        Vector2Int[] directions = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        var queue = new Queue<Vector2Int>();
        isWater[seed.y, seed.x] = true;
        queue.Enqueue(seed);
        int count = 1;
        while (queue.Count > 0)
        {
            Vector2Int point = queue.Dequeue();
            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = point + direction;
                if (next.x < 0 || next.y < 0 || next.x >= isWater.GetLength(1) || next.y >= isWater.GetLength(0)) continue;
                if (isWater[next.y, next.x] || demo.Heights[mask.OriginZ + next.y, mask.OriginX + next.x] >= waterLevel) continue;

                isWater[next.y, next.x] = true;
                queue.Enqueue(next);
                count++;
            }
        }
        return count;
    }

    // 2차원 누적 합. [z, x] = (0, 0) ~ (z - 1, x - 1) 안 물 점 수
    private static int[,] CreatePrefix(bool[,] isWater)
    {
        int depth = isWater.GetLength(0);
        int width = isWater.GetLength(1);
        var prefix = new int[depth + 1, width + 1];
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
                prefix[z + 1, x + 1] = prefix[z, x + 1] + prefix[z + 1, x] - prefix[z, x] + (isWater[z, x] ? 1 : 0);
        }
        return prefix;
    }

    // 조각이 차지하는 데모 사각형(회전 반영, 회전이 90° 단위라 축 정렬) 안에 실제 호수 물 점이 하나라도 있는지
    private static bool HasLake(DemoField demo, LakeMask lake, Patch patch, Vector2 span)
    {
        int width = lake.Prefix.GetLength(1) - 1;
        int depth = lake.Prefix.GetLength(0) - 1;
        int xMin = Mathf.Max(0, Mathf.FloorToInt(patch.Origin.x / demo.SpacingX) - lake.OriginX);
        int zMin = Mathf.Max(0, Mathf.FloorToInt(patch.Origin.y / demo.SpacingZ) - lake.OriginZ);
        int xMax = Mathf.Min(width, Mathf.CeilToInt((patch.Origin.x + span.x) / demo.SpacingX) - lake.OriginX + 1);
        int zMax = Mathf.Min(depth, Mathf.CeilToInt((patch.Origin.y + span.y) / demo.SpacingZ) - lake.OriginZ + 1);
        if (xMin >= xMax || zMin >= zMax) return false;

        int count = lake.Prefix[zMax, xMax] - lake.Prefix[zMin, xMax] - lake.Prefix[zMax, xMin] + lake.Prefix[zMin, xMin];
        return count > 0;
    }

    // 핵심 구역 표본으로 높이 범위, 잔굴곡, 북쪽이 높은 기울기를, 북쪽 끝 표본으로 C1과의 모양 맞춤을 재고 점수를 낸다
    private static void ScorePatch(DemoField demo, float[] c1Profile, ref Patch patch)
    {
        patch.ShapeMatch = CalculateCorrelation(CalculateNorthProfile(demo, patch), c1Profile);
        ScoreCore(demo, ref patch);
        patch.Score += ShapeWeight * patch.ShapeMatch;
    }

    // 조각 북쪽 끝(핵심 구역 북쪽 끝 ~ 조각 북쪽 끝, 우리 z 250 ~ 370)의 열별 최고 높이 (데모 m, ScoreSampleStep 간격)
    private static float[] CalculateNorthProfile(DemoField demo, Patch patch)
    {
        var profile = new float[Mathf.RoundToInt(PatchWidth / ScoreSampleStep) + 1];
        for (int i = 0; i < profile.Length; i++)
        {
            profile[i] = float.MinValue;
            for (float v = CoreEnd; v <= PatchDepth; v += ScoreSampleStep)
                profile[i] = Mathf.Max(profile[i], SampleDemo(demo, PatchToDemo(patch, i * ScoreSampleStep, v)));
        }
        return profile;
    }

    // 피어슨 상관계수. 높이 차(오프셋)와 배율에 상관없이 x 방향 모양이 같을수록 1에 가깝다
    private static float CalculateCorrelation(float[] a, float[] b)
    {
        float meanA = 0f, meanB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            meanA += a[i] / a.Length;
            meanB += b[i] / b.Length;
        }
        float covariance = 0f, varianceA = 0f, varianceB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            covariance += (a[i] - meanA) * (b[i] - meanB);
            varianceA += (a[i] - meanA) * (a[i] - meanA);
            varianceB += (b[i] - meanB) * (b[i] - meanB);
        }
        float denominator = Mathf.Sqrt(varianceA * varianceB);
        return denominator > 0f ? covariance / denominator : 0f;
    }

    private static void ScoreCore(DemoField demo, ref Patch patch)
    {
        int columns = Mathf.RoundToInt(PatchWidth / ScoreSampleStep) + 1;
        int rows = Mathf.RoundToInt((CoreEnd - CoreStart) / ScoreSampleStep) + 1;
        var samples = new float[rows, columns];
        patch.RawMin = float.MaxValue;
        patch.RawMax = float.MinValue;
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                float height = SampleDemo(demo, PatchToDemo(patch, x * ScoreSampleStep, CoreStart + z * ScoreSampleStep));
                samples[z, x] = height;
                patch.RawMin = Mathf.Min(patch.RawMin, height);
                patch.RawMax = Mathf.Max(patch.RawMax, height);
            }
        }
        patch.Relief = patch.RawMax - patch.RawMin;
        patch.Texture = CalculateTexture(samples);
        patch.Tilt = CalculateRowMean(samples, rows * 2 / 3, rows) - CalculateRowMean(samples, 0, rows / 3);
        patch.Score = patch.Relief + TextureWeight * patch.Texture + TiltWeight * Mathf.Max(0f, patch.Tilt);
    }

    // 표본 하나가 이웃 4개 평균에서 벗어난 양의 평균 (m). 능선, 골짜기가 뚜렷할수록 크다
    private static float CalculateTexture(float[,] samples)
    {
        float sum = 0f;
        int count = 0;
        for (int z = 1; z < samples.GetLength(0) - 1; z++)
        {
            for (int x = 1; x < samples.GetLength(1) - 1; x++)
            {
                float neighborMean = (samples[z - 1, x] + samples[z + 1, x] + samples[z, x - 1] + samples[z, x + 1]) / 4f;
                sum += Mathf.Abs(samples[z, x] - neighborMean);
                count++;
            }
        }
        return sum / count;
    }

    private static float CalculateRowMean(float[,] samples, int fromRow, int toRow)
    {
        float sum = 0f;
        for (int z = fromRow; z < toRow; z++)
        {
            for (int x = 0; x < samples.GetLength(1); x++)
                sum += samples[z, x];
        }
        return sum / ((toRow - fromRow) * samples.GetLength(1));
    }

    // ---------- 높이 계산 ----------

    // 높이맵 일부를 월드 높이 (m)로 읽는다 [z, x]
    private static float[,] ReadMeters(TerrainData data, int x, int z, int width, int depth)
    {
        float[,] heights = data.GetHeights(x, z, width, depth);
        for (int row = 0; row < depth; row++)
        {
            for (int column = 0; column < width; column++)
                heights[row, column] *= data.size.y;
        }
        return heights;
    }

    // 열 radius개 둘레 평균 (양 끝은 있는 열만)
    private static float[] SmoothColumns(float[] values, int radius)
    {
        var prefix = new float[values.Length + 1];
        for (int i = 0; i < values.Length; i++)
            prefix[i + 1] = prefix[i] + values[i];

        var smoothed = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            int from = Mathf.Max(0, i - radius);
            int to = Mathf.Min(values.Length - 1, i + radius);
            smoothed[i] = (prefix[to + 1] - prefix[from]) / (to - from + 1);
        }
        return smoothed;
    }

    // 가장자리 줄 안쪽 점마다 월드 높이 (m). [z - 1, x - 1]이 Range 번호 [z, x]다
    private static float[,] CalculateHeights(ShapeField field)
    {
        var heights = new float[field.Range.height - 1, field.Range.width - 1];
        for (int z = 1; z < field.Range.height; z++)
        {
            for (int x = 1; x < field.Range.width; x++)
            {
                Vector2 world = field.Origin + new Vector2(x, z) * field.Spacing;
                heights[z - 1, x - 1] = BlendToEdge(field, z, x, world, CalculateSurfaceHeight(field, x, world));
            }
        }
        return heights;
    }

    // C2는 새 산맥(남쪽 기슭 포함). 띠는 새 산맥과 원래 C1을 북쪽으로 갈수록 원래 C1 쪽으로 섞는다 (smoothstep이라 양 끝 기울기가 매끄럽다)
    // 섞기는 조각 북쪽 끝(z 370)에서 끝나, 그 북쪽(z 370 ~ 400)은 조각을 읽지 않고 원래 C1 참조만 쓴다.
    // smoothstep이라 z 370에서 가중치 기울기도 0이 되어 참조 쪽으로 매끄럽게 넘어간다
    private static float CalculateSurfaceHeight(ShapeField field, int column, Vector2 world)
    {
        if (world.y <= EastBounds.yMax) return CalculateMountainHeight(field, world);

        float patchNorthZ = EastBounds.yMin + PatchDepth;
        float reference = CalculateNorthReference(field, column, world.y);
        if (world.y >= patchNorthZ) return reference;

        float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(EastBounds.yMax, patchNorthZ, world.y));
        return Mathf.Lerp(CalculateMountainHeight(field, world), reference, weight);
    }

    // 맞춘 데모 높이의 30m 위 높이에서 남쪽 기슭 깎는 양을 빼고, 0 아래는 부드럽게 0 쪽으로 모은다
    private static float CalculateMountainHeight(ShapeField field, Vector2 world)
    {
        float raw = SampleDemo(field.Demo, PatchToDemo(field.Patch, world.x - EastBounds.xMin, world.y - EastBounds.yMin));
        float rise = (raw - field.Patch.RawMin) * field.Scale;
        return MapTerrainSetup.BaseHeight + SoftPositive(rise - CalculateFootCut(world));
    }

    // z FootStartZ에서 0, 남쪽 경계에서 약 PeakHeight - 30 (± 흔들림). t^FootCutPower라 처음엔 조금씩, 남쪽 끝에서 많이 깎는다.
    // 흔들림이 줄여 덜 깎인 곳은 남쪽 경계 감쇠가 30m로 마저 내린다
    private static float CalculateFootCut(Vector2 world)
    {
        float t = Mathf.InverseLerp(FootStartZ, EastBounds.yMin, world.y);
        float perlin = Mathf.PerlinNoise(world.x / FootCutWobblePeriod + FootCutNoiseOffset.x, world.y / FootCutWobblePeriod + FootCutNoiseOffset.y);
        float wobble = 1f + FootCutWobble * Mathf.Clamp(perlin * 2f - 1f, -1f, 1f);
        return Mathf.Pow(t, FootCutPower) * wobble * (PeakHeight - MapTerrainSetup.BaseHeight);
    }

    // 0보다 크면 거의 그대로, 0보다 작으면 0에 가까운 양수 (부드러운 max(0, value))
    private static float SoftPositive(float value)
    {
        return 0.5f * (value + Mathf.Sqrt(value * value + SoftFloorWidth * SoftFloorWidth));
    }

    // 원래 C1 참조 : z 400 줄 높이(고른 값)에서 고른 C1 기울기로 남쪽으로 이어 간다.
    // 이어 간 거리 d에 L(exp(d / L) - 1)을 써서 z 400에서는 C1 기울기 그대로, 남쪽으로 갈수록 기울기가 줄어 떨어지는 양이 기울기 × L을 넘지 않는다.
    // z 400 바로 앞에서만 고르기 전 값으로 돌아가 z 400 줄과 높이가 맞는다
    private static float CalculateNorthReference(ShapeField field, int column, float worldZ)
    {
        float northZ = EastBounds.yMax + BlendDepth;
        float distance = Mathf.Min(0f, worldZ - northZ);
        float extension = NorthExtendLength * (Mathf.Exp(distance / NorthExtendLength) - 1f);
        float residual = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(northZ - ResidualFadeDepth, northZ, worldZ));
        return field.NorthSmooth[column] + field.NorthSlopes[column] * extension
               + (field.NorthTops[column] - field.NorthSmooth[column]) * residual;
    }

    // 남 → 서 → 동 순서로 그 경계 줄 높이 쪽으로 섞는다. C2 구간의 경계 줄은 30m, 띠의 서·동 끝 줄은 원래 높이다.
    // 경계마다 감쇠값이 거리 0에서 0이라 경계 줄에서는 그 줄 높이와 같다
    private static float BlendToEdge(ShapeField field, int row, int column, Vector2 world, float surface)
    {
        float southMask = CalculateEdgeMask(world.y - field.Area.yMin, CalculateFadeWidth(world, EdgeFadeWidth, EdgeFadeWobble));
        float westMask = CalculateEdgeMask(world.x - field.Area.xMin, CalculateFadeWidth(world, SideFadeWidth, SideFadeWobble));
        float eastMask = CalculateEdgeMask(field.Area.xMax - world.x, CalculateFadeWidth(world, SideFadeWidth, SideFadeWobble));
        float height = Mathf.Lerp(field.Frame[0, column], surface, southMask);
        height = Mathf.Lerp(field.Frame[row, 0], height, westMask);
        return Mathf.Lerp(field.Frame[row, field.Range.width], height, eastMask);
    }

    private static float CalculateEdgeMask(float distance, float width)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, width, distance));
    }

    // 위치에 따라 흔들리는 감쇠 폭 (m). 월드 위치로 노이즈를 뽑아 경계를 따라 끊김 없이 이어진다
    private static float CalculateFadeWidth(Vector2 world, float baseWidth, float wobbleRatio)
    {
        float perlin = Mathf.PerlinNoise(
            world.x / EdgeFadeWobblePeriod + EdgeFadeNoiseOffset.x,
            world.y / EdgeFadeWobblePeriod + EdgeFadeNoiseOffset.y);
        float wobble = Mathf.Clamp(perlin * 2f - 1f, -1f, 1f);
        return baseWidth * (1f + wobbleRatio * wobble);
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

    // 점마다 경사 (도). 가운데 차분, 배열 끝은 한쪽 차분
    private static float[,] CalculateSlopes(float[,] heights, float spacing)
    {
        int depth = heights.GetLength(0);
        int width = heights.GetLength(1);
        var slopes = new float[depth, width];
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int left = Mathf.Max(0, x - 1), right = Mathf.Min(width - 1, x + 1);
                int down = Mathf.Max(0, z - 1), up = Mathf.Min(depth - 1, z + 1);
                float dx = (heights[z, right] - heights[z, left]) / ((right - left) * spacing);
                float dz = (heights[up, x] - heights[down, x]) / ((up - down) * spacing);
                slopes[z, x] = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
            }
        }
        return slopes;
    }

    // ---------- 띠 배치물 ----------

    // 띠(C1 z 250 ~ 400) 안에 피벗이 있는 NorthEast 배치물과 쓰기 전 기준 지면 높이. 구역 부모 아래 묶음(Trees, Rocks 등)의 바로 아래 자식만 본다
    private static List<BandObject> CollectBandObjects(Terrain terrain)
    {
        var objects = new List<BandObject>();
        var band = Rect.MinMaxRect(EastBounds.xMin, EastBounds.yMax, EastBounds.xMax, EastBounds.yMax + BlendDepth);
        GameObject region = FindRegion(terrain);
        if (region == null)
        {
            Debug.LogWarning($"[MapEastShaper] 씬에 {NorthEastRegionName} 부모가 없어 띠 안 배치물을 옮기지 않습니다.");
            return objects;
        }

        foreach (Transform group in region.transform)
        {
            float footprint = group.name == RockGroupName ? RockFootprintRadius : 0f;
            foreach (Transform item in group)
            {
                var position = new Vector2(item.position.x, item.position.z);
                if (!band.Contains(position)) continue;

                float radius = footprint * item.localScale.x;
                objects.Add(new BandObject { Target = item, FootprintRadius = radius, GroundBefore = GetReferenceGround(terrain, position, radius) });
            }
        }
        return objects;
    }

    private static GameObject FindRegion(Terrain terrain)
    {
        foreach (GameObject root in terrain.gameObject.scene.GetRootGameObjects())
        {
            if (root.name == NorthEastRegionName) return root;
        }
        return null;
    }

    // 나무 등은 피벗 아래 지면, 바위는 피벗과 바닥 둘레 4곳 중 가장 낮은 지면 (메뉴 8-1이 놓은 기준과 같음)
    private static float GetReferenceGround(Terrain terrain, Vector2 position, float radius)
    {
        float ground = GetGroundHeight(terrain, position);
        if (radius <= 0f) return ground;

        ground = Mathf.Min(ground, GetGroundHeight(terrain, position + Vector2.up * radius));
        ground = Mathf.Min(ground, GetGroundHeight(terrain, position + Vector2.down * radius));
        ground = Mathf.Min(ground, GetGroundHeight(terrain, position + Vector2.left * radius));
        return Mathf.Min(ground, GetGroundHeight(terrain, position + Vector2.right * radius));
    }

    // SampleHeight는 Terrain 기준 높이라 Terrain의 y를 더한다
    private static float GetGroundHeight(Terrain terrain, Vector2 position)
    {
        return terrain.SampleHeight(new Vector3(position.x, 0f, position.y)) + terrain.transform.position.y;
    }

    // 기준 지면이 바뀐 만큼 옮긴다. 원래 지면에서 떠 있거나 묻힌 정도는 그대로 남는다. 다시 실행하면 지면이 같아 움직이지 않는다.
    // 반환 : x = 옮긴 수, y = 옮긴 높이 최대 (m)
    private static Vector2 MoveBandObjects(Terrain terrain, List<BandObject> objects)
    {
        var result = Vector2.zero;
        foreach (BandObject item in objects)
        {
            Vector3 position = item.Target.position;
            float change = GetReferenceGround(terrain, new Vector2(position.x, position.z), item.FootprintRadius) - item.GroundBefore;
            if (Mathf.Abs(change) <= ChangeTolerance) continue;

            Undo.RecordObject(item.Target, "동쪽 지형에 맞춰 C1 띠 배치물 높이 조정");
            item.Target.position = position + Vector3.up * change;
            result.x++;
            result.y = Mathf.Max(result.y, Mathf.Abs(change));
        }
        if (result.x == 0f) return result;

        UnityEngine.SceneManagement.Scene scene = terrain.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return result;
    }

    // ---------- 서식지 후보 ----------

    // 방위 범위 안, 반경 원이 C2 안에 드는 자리 중 반경 안 경사 평균이 낮은 순으로 서로 HabitatCandidateSpacing 떨어진 후보
    private static List<HabitatCandidate> FindHabitatCandidates(ShapeField field, float[,] heights, float[,] slopes)
    {
        var all = new List<HabitatCandidate>();
        for (float z = EastBounds.yMin + HabitatRadius; z <= EastBounds.yMax - HabitatRadius; z += HabitatSearchStep)
        {
            for (float x = EastBounds.xMin + HabitatRadius; x <= EastBounds.xMax - HabitatRadius; x += HabitatSearchStep)
            {
                var center = new Vector2(x, z);
                float bearing = GetBearing(center);
                if (bearing < HabitatBearingMin || bearing > HabitatBearingMax) continue;
                all.Add(new HabitatCandidate { Center = center, MeanSlope = CalculateMeanSlope(field, slopes, center), Height = SampleArray(field, heights, center) });
            }
        }
        all.Sort((a, b) => a.MeanSlope.CompareTo(b.MeanSlope));

        var picked = new List<HabitatCandidate>();
        foreach (HabitatCandidate candidate in all)
        {
            if (picked.Count == HabitatCandidateCount) break;
            if (picked.Exists(other => Vector2.Distance(other.Center, candidate.Center) < HabitatCandidateSpacing)) continue;
            picked.Add(candidate);
        }
        return picked;
    }

    // 거점(원점) 기준 방위 (도, 북 0 → 동 90)
    private static float GetBearing(Vector2 position)
    {
        return (Mathf.Atan2(position.x, position.y) * Mathf.Rad2Deg + 360f) % 360f;
    }

    private static float CalculateMeanSlope(ShapeField field, float[,] slopes, Vector2 center)
    {
        float sum = 0f;
        int count = 0;
        for (float dz = -HabitatRadius; dz <= HabitatRadius; dz += HabitatSlopeSampleStep)
        {
            for (float dx = -HabitatRadius; dx <= HabitatRadius; dx += HabitatSlopeSampleStep)
            {
                if (dx * dx + dz * dz > HabitatRadius * HabitatRadius) continue;
                sum += SampleArray(field, slopes, center + new Vector2(dx, dz));
                count++;
            }
        }
        return sum / count;
    }

    // 계산 배열([z - 1, x - 1] = Range 번호 [z, x])에서 가장 가까운 점의 값
    private static float SampleArray(ShapeField field, float[,] values, Vector2 world)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt((world.x - field.Origin.x) / field.Spacing) - 1, 0, values.GetLength(1) - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt((world.y - field.Origin.y) / field.Spacing) - 1, 0, values.GetLength(0) - 1);
        return values[z, x];
    }

    // ---------- 확인 ----------

    // 쓴 범위 둘레를 OutsideCheckMargin칸 더 넓힌 범위 (높이맵 안으로 자름)
    private static RectInt GetCheckRange(RectInt range, int resolution)
    {
        int xMin = Mathf.Max(0, range.x - OutsideCheckMargin);
        int zMin = Mathf.Max(0, range.y - OutsideCheckMargin);
        int xMax = Mathf.Min(resolution - 1, range.xMax + OutsideCheckMargin);
        int zMax = Mathf.Min(resolution - 1, range.yMax + OutsideCheckMargin);
        return new RectInt(xMin, zMin, xMax - xMin + 1, zMax - zMin + 1);
    }

    // 확인 범위에서 쓴 범위(가장자리 줄 안쪽) 밖인데 높이가 바뀐 점의 수
    private static int CountOutsideChanged(TerrainData data, RectInt checkRange, float[,] before, RectInt range)
    {
        float[,] after = data.GetHeights(checkRange.x, checkRange.y, checkRange.width, checkRange.height);
        int changedCount = 0;
        for (int z = 0; z < checkRange.height; z++)
        {
            for (int x = 0; x < checkRange.width; x++)
            {
                int indexX = checkRange.x + x;
                int indexZ = checkRange.y + z;
                bool isWritten = indexX > range.x && indexX < range.xMax && indexZ > range.y && indexZ < range.yMax;
                if (isWritten) continue;
                if (Mathf.Abs(after[z, x] - before[z, x]) * data.size.y > ChangeTolerance) changedCount++;
            }
        }
        return changedCount;
    }

    // C2 구간(z 250 이하)의 서·동 끝 줄과 남쪽 끝 줄이 바탕 높이에서 가장 많이 벗어난 값 (m)
    private static float GetMaxSideDeviation(TerrainData data, ShapeField field)
    {
        float[,] heights = ReadMeters(data, field.Range.x, field.Range.y, field.Range.width + 1, field.Range.height + 1);
        float maxDeviation = 0f;
        for (int z = 0; z <= field.Range.height; z++)
        {
            if (field.Origin.y + z * field.Spacing > EastBounds.yMax + field.Spacing * 0.5f) break;
            for (int x = 0; x <= field.Range.width; x++)
            {
                bool isSide = x == 0 || x == field.Range.width || z == 0;
                if (!isSide) continue;
                maxDeviation = Mathf.Max(maxDeviation, Mathf.Abs(heights[z, x] - MapTerrainSetup.BaseHeight));
            }
        }
        return maxDeviation;
    }

    // 쓴 뒤 지형의 한 줄(z)에서 x 방향 2차 차분 |h(x-1) - 2h(x) + h(x+1)|의 평균과 최대 (m). 세로 줄무늬가 있으면 커진다
    private static Vector2 MeasureRowRoughness(Terrain terrain, ShapeField field, float worldZ)
    {
        int row = Mathf.RoundToInt((worldZ - terrain.transform.position.z) / field.Spacing);
        float[,] heights = ReadMeters(terrain.terrainData, field.Range.x, row, field.Range.width + 1, 1);
        float sum = 0f;
        float max = 0f;
        for (int x = 1; x < field.Range.width; x++)
        {
            float second = Mathf.Abs(heights[0, x - 1] - 2f * heights[0, x] + heights[0, x + 1]);
            sum += second;
            max = Mathf.Max(max, second);
        }
        return new Vector2(sum / (field.Range.width - 1), max);
    }

    // ---------- 로그 ----------

    private static void LogPatch(ShapeField field, int candidateCount, int lakeSkipCount)
    {
        Patch patch = field.Patch;
        Vector2 span = GetDemoSpan(patch.Rotation);
        Debug.Log($"[MapEastShaper] 데모 조각 : 데모 x {patch.Origin.x:F0} ~ {patch.Origin.x + span.x:F0}, z {patch.Origin.y:F0} ~ {patch.Origin.y + span.y:F0}, " +
                  $"회전 {patch.Rotation}°, 좌우 뒤집기 {(patch.IsMirrored ? "있음" : "없음")}, 점수 {patch.Score:F1} " +
                  $"(모양 맞춤 상관 {patch.ShapeMatch:F3} × {ShapeWeight} + 높이 범위 {patch.Relief:F1} + 잔굴곡 {patch.Texture:F2} × {TextureWeight} + " +
                  $"북쪽 기울기 {patch.Tilt:F1} × {TiltWeight}), 후보 {candidateCount}개(뒤집기 포함) 중 호수로 뺀 것 {lakeSkipCount}개");
        float peak = MapTerrainSetup.BaseHeight + (patch.RawMax - patch.RawMin) * field.Scale;
        Debug.Log($"[MapEastShaper] 핵심 구역 원래 높이 {patch.RawMin:F1} ~ {patch.RawMax:F1}m → 맞춘 높이 {MapTerrainSetup.BaseHeight} ~ {peak:F1}m " +
                  $"(배율 {field.Scale:F3}, 북쪽 끝 ≤ C1 + {NorthAllowance}m 한도 배율 {field.NorthLimitScale:F3}, " +
                  $"쓴 배율에서 한도를 넘는 북쪽 끝 열 {field.NorthOverCount}개 / {Mathf.RoundToInt(PatchWidth / ScoreSampleStep) + 1}개)");
    }

    private static void LogHeights(ShapeField field, float[,] heights, float[,] slopes)
    {
        Vector2 range = new Vector2(float.MaxValue, float.MinValue);
        float slopeMax = 0f;
        float westSlopeMax = 0f;
        var cells = new Dictionary<Vector2Int, Vector3>();
        for (int z = 0; z < heights.GetLength(0); z++)
        {
            Vector2 world = field.Origin + new Vector2(0f, z + 1) * field.Spacing;
            if (world.y > EastBounds.yMax) break;
            for (int x = 0; x < heights.GetLength(1); x++)
            {
                world.x = field.Origin.x + (x + 1) * field.Spacing;
                float height = heights[z, x];
                range = new Vector2(Mathf.Min(range.x, height), Mathf.Max(range.y, height));
                slopeMax = Mathf.Max(slopeMax, slopes[z, x]);
                if (world.x - EastBounds.xMin <= WestSlopeLogWidth) westSlopeMax = Mathf.Max(westSlopeMax, slopes[z, x]);
                var cell = new Vector2Int(Mathf.FloorToInt(world.x / PeakCellSize), Mathf.FloorToInt(world.y / PeakCellSize));
                if (!cells.TryGetValue(cell, out Vector3 top) || height > top.y) cells[cell] = new Vector3(world.x, height, world.y);
            }
        }
        Debug.Log($"[MapEastShaper] 동쪽(C2) 높이 최저 {range.x:F2}m, 최고 {range.y:F1}m, 경사 최대 {slopeMax:F1}°, " +
                  $"서쪽 경계 근처(x {EastBounds.xMin:F0} ~ {EastBounds.xMin + WestSlopeLogWidth:F0}) 경사 최대 {westSlopeMax:F1}°");
        Debug.Log($"[MapEastShaper] 봉우리 상위 {PeakLogCount}곳 (서로 {PeakSpacing}m 이상) : {FormatPeaks(new List<Vector3>(cells.Values))}");
    }

    // 높은 순으로 서로 PeakSpacing 떨어진 칸 최고점
    private static string FormatPeaks(List<Vector3> tops)
    {
        tops.Sort((a, b) => b.y.CompareTo(a.y));
        var picked = new List<Vector3>();
        foreach (Vector3 top in tops)
        {
            if (picked.Count == PeakLogCount) break;
            if (picked.Exists(other => new Vector2(other.x - top.x, other.z - top.z).magnitude < PeakSpacing)) continue;
            picked.Add(top);
        }

        var text = new StringBuilder();
        foreach (Vector3 peak in picked)
            text.Append($"({peak.x:F0}, {peak.z:F0}) {peak.y:F1}m  ");
        return text.ToString();
    }

    // 쓴 뒤 지형에서 읽는다 (z 400 북쪽은 원래 C1)
    private static void LogSections(Terrain terrain)
    {
        foreach (float x in SectionLogXs)
        {
            var section = new StringBuilder();
            for (float z = SectionLogZMin; z <= SectionLogZMax; z += SectionLogStep)
                section.Append($"{z:F0}:{GetGroundHeight(terrain, new Vector2(x, z)):F1} ");
            Debug.Log($"[MapEastShaper] 남북 단면 x {x:F0} (z:높이) {section}");
        }
    }

    // 띠 줄무늬(x 방향 2차 차분)와 z 400 이음 (바로 남쪽, 북쪽 기울기 차 최대)
    private static void LogBand(Terrain terrain, ShapeField field)
    {
        var roughness = new StringBuilder();
        foreach (float z in RoughnessLogZs)
        {
            Vector2 value = MeasureRowRoughness(terrain, field, z);
            roughness.Append($"z {z:F0} 평균 {value.x:F3} 최대 {value.y:F2}m  ");
        }
        Debug.Log($"[MapEastShaper] 띠 거칠기 (x 방향 2차 차분, z 150은 C2, z 450은 원래 C1) {roughness}");

        float[,] rows = ReadMeters(terrain.terrainData, field.Range.x, field.Range.yMax - 1, field.Range.width + 1, 3);
        float jointMax = 0f;
        for (int x = 1; x < field.Range.width; x++)
        {
            float south = Mathf.Atan((rows[1, x] - rows[0, x]) / field.Spacing) * Mathf.Rad2Deg;
            float north = Mathf.Atan((rows[2, x] - rows[1, x]) / field.Spacing) * Mathf.Rad2Deg;
            jointMax = Mathf.Max(jointMax, Mathf.Abs(north - south));
        }
        Debug.Log($"[MapEastShaper] z {EastBounds.yMax + BlendDepth:F0} 이음 남북 기울기 차 최대 {jointMax:F1}° (튀는 단 확인)");
    }

    private static void LogHabitat(ShapeField field, float[,] heights, float[,] slopes)
    {
        var text = new StringBuilder();
        foreach (HabitatCandidate candidate in FindHabitatCandidates(field, heights, slopes))
        {
            text.Append($"({candidate.Center.x:F0}, {candidate.Center.y:F0}) 방위 {GetBearing(candidate.Center):F1}°, " +
                        $"경사 평균 {candidate.MeanSlope:F1}°, 높이 {candidate.Height:F1}m / ");
        }
        Debug.Log($"[MapEastShaper] 서식지 후보 (방위 {HabitatBearingMin} ~ {HabitatBearingMax}°, 반경 {HabitatRadius}m 경사 평균 낮은 순) : {text}");
        Debug.Log($"[MapEastShaper] 현재 서식지 ({HabitatCenter.x:F0}, {HabitatCenter.y:F0}) 방위 {GetBearing(HabitatCenter):F1}°, " +
                  $"경사 평균 {CalculateMeanSlope(field, slopes, HabitatCenter):F1}°, 높이 {SampleArray(field, heights, HabitatCenter):F1}m");
    }

    private static void LogChecks(TerrainData data, ShapeField field, int outsideChangedCount)
    {
        Debug.Log($"[MapEastShaper] 서·남·동 경계 줄 {MapTerrainSetup.BaseHeight}m 대비 최대 차이 {GetMaxSideDeviation(data, field):F3}m (C2 구간)");
        Debug.Log($"[MapEastShaper] C2 + 띠 쓴 범위 밖 바뀐 높이맵 점 {outsideChangedCount}개 (0이어야 함, 둘레 {OutsideCheckMargin}칸까지 확인. " +
                  $"0이면 C1에서 바뀐 점은 모두 z {EastBounds.yMax:F0} ~ {EastBounds.yMax + BlendDepth:F0} 안)");
    }
}
