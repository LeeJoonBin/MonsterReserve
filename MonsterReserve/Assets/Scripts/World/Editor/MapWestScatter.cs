using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 메뉴 12 : 서쪽 구역(A2, 침엽수림)에 서식지 위치 표시, 랜드마크 4곳, 나무, 바위·소품을 놓는다 (이슈 #7).
// 1) 서식지 : West/Habitat 빈 GameObject를 HabitatCenter의 지면 높이에 둔다. 위치 표시만 하고, 반경 HabitatRadius 안은 빈터로 비운다.
// 2) 랜드마크 : 무너진 신전 터, 큰 바위 무리, 선돌 원, 버려진 사냥꾼 야영지를 Dreamscape prefab으로 직접 구성한다.
//    조각마다 위치, 회전, 크기는 아래 "랜드마크 배치도" 상수를 따르고, 기울기와 크기 흔들림은 고정 seed로 뽑아 다시 실행해도 같다.
//    조각마다 바닥 둘레의 가장 낮은 땅에 맞추고 살짝 묻는다. 위치는 아래 "랜드마크 위치 규칙"으로 찾는다.
//    북쪽(데모 StoneHenge)과 같은 배치가 반복되지 않게, 그리고 LODGroup 경고를 내는 Prefab_WallRuin을 피하려고 데모 씬 묶음을 옮기지 않는다.
// 3) 나무, 바위 : 중앙 배치(메뉴 3)와 같은 노이즈로 숲 / 드문드문 / 빈터를 나누되 비율만 60 / 25 / 15%로 하고, 중앙과 같은 밀도 표로 흩뿌린다.
//    서식지 반경과 랜드마크 둘레는 비운다. A2에는 강, 등산로, 물이 없어 물·길 피하기는 하지 않는다.
// 다시 실행하면 West 아래 Trees, Rocks, Landmarks, Habitat의 자식을 지우고 같은 seed로 다시 만든 뒤 씬을 저장한다.
// 실행 순서 : 9-3 → 11 → 12 → 13 → 10. 11(서쪽 언덕)을 다시 실행하면 지형 높이가 바뀌므로 12, 13도 순서대로 다시 실행한다.
// 지면·바닥 식생(메뉴 13)은 GetWestZone으로 같은 구분을 쓴다.
public static class MapWestScatter
{
    private enum TreeKind
    {
        Main = 0,
        Dead = 1,
        Giant = 2
    }

    // 먼저 놓은 랜드마크가 차지한 자리 (월드 XZ)
    private struct Blocker
    {
        public Vector2 Center;
        public float Radius;
    }

    // 같은 크기 범위로 같은 부모 아래 놓는 prefab 묶음
    private class InstanceGroup
    {
        public GameObject[] Prefabs;
        public Transform Parent;
        public float ScaleMin;
        public float ScaleMax;
        // 바위는 바닥 둘레의 가장 낮은 땅에 맞춰 비탈 아래쪽이 뜨지 않게 한다
        public bool IsRock;
    }

    // 한 번 실행하는 동안 함께 쓰는 값과 결과 개수 묶음. 개수 배열의 순서는 FieldZone (빈터, 드문드문, 숲)
    private class ScatterContext
    {
        public Terrain Terrain;
        public System.Random Random;
        public InstanceGroup[] TreeGroups;
        public InstanceGroup RockGroup;
        public List<Blocker> Landmarks;
        public int[] ZoneCellCounts = new int[3];
        public int[] TreeCounts = new int[3];
        public int[] RockCounts = new int[3];
        public int[] TreeKindCounts = new int[3];
        public int ColliderCount;
        public int HabitatSkipCount;
        public int LandmarkSkipCount;
        public int SlopeSkipCount;
    }

    // 랜드마크 조각 하나의 배치도 (묶음 중심, 묶음 회전 기준)
    private struct PieceSpec
    {
        // MapTreeScatter.PrefabFolder 아래 경로 (확장자 없음)
        public string Path;
        // 묶음 중심 기준 XZ (m)
        public Vector2 Offset;
        public float Yaw;
        // 눕히는 회전 (x = 앞뒤, y = 좌우, 도). 떨어진 바퀴, 꽂힌 도끼처럼 똑바로 서지 않는 조각에만 쓴다
        public Vector2 Tilt;
        public float Scale;
        // true면 고정 seed로 조금 기울이고 크기를 흔든다 (기둥, 선돌)
        public bool IsWobbly;
        // true면 땅이 아니라 바로 앞 조각의 윗면에 올린다 (그루터기의 도끼, 쌓인 바위). 자기 높이 × StackSinkRatio만큼 파묻는다
        public bool IsStacked;
        public float StackSinkRatio;
    }

    private class LandmarkSpec
    {
        public string Label;
        // 배치도 전체를 돌리는 각도 (도)
        public float GroupYaw;
        // 조각 밑면을 바닥 둘레의 가장 낮은 땅보다 (조각 높이 × 이 값)만큼 아래에 맞춘다
        public float SinkRatio;
        // 중심이 이 구분 중 하나인 곳에만 놓는다. null이면 구분을 따지지 않는다
        public MapTreeScatter.FieldZone[] Zones;
        // 구성안의 대략 크기. 로그에서 실제 바닥 반지름과 견주는 용도
        public float ApproxRadius;
        // 배치도 사각형 한 변 (m). 0보다 크면 로그에 이 사각형 기준 밀도(개/m²)를 남긴다
        public float AreaSize;
        public PieceSpec[] Pieces;
    }

    // 놓은 조각 하나
    private class PlacedPiece
    {
        public PieceSpec Spec;
        public Transform Transform;
        // 묶음 중심 기준 렌더러 범위 바닥의 가운데와 둘레 8곳 (XZ). 자리 찾기에서 지면 높이를 잴 곳
        public Vector2[] Points;
        public float Height;
    }

    // 원점에서 조립한 랜드마크 하나. 자리를 찾은 뒤 통째로 옮긴다
    private class LandmarkBuild
    {
        public Transform Group;
        public List<PlacedPiece> Pieces = new List<PlacedPiece>();
        // 중심에서 가장 먼 렌더러 범위 가장자리까지의 수평 거리 (메뉴 13과 같은 계산)
        public float Radius;
        public int ColliderCount;
        public int DisallowCount;
    }

    private struct LandmarkFit
    {
        public Vector2 Position;
        // 땅에 맞추면 많이 묻히는 조각 수
        public int BrokenCount;
        // 묶음 바닥 아래 지면의 가장 높은 곳 - 가장 낮은 곳
        public float GroundRange;
    }

    private class LandmarkContext
    {
        public Terrain Terrain;
        public System.Type DisallowType;
        public Transform Parent;
        public System.Random Random;
        public List<Blocker> Placed = new List<Blocker>();
        public Dictionary<Shader, bool> GpuDrivenSupport = new Dictionary<Shader, bool>();
        public int BrokenCount;
        public int ColliderCount;
        public int DisallowCount;
        public List<string> Notes = new List<string>();
    }

    private const string WestCell = "A2";
    private const string WestRegionName = "West";
    private const string TreeParentName = "Trees";
    private const string RockParentName = "Rocks";
    private const string LandmarkParentName = "Landmarks";
    private const string HabitatName = "Habitat";

    // 서식지 (사용자 승인 임시값). 거점(원점) 기준 서쪽. 반경 안은 빈터로 두고 나무, 바위, 랜드마크를 놓지 않는다
    public static readonly Vector2 HabitatCenter = new Vector2(-750f, 0f);
    public const float HabitatRadius = 80f;

    // 앞 메뉴들의 seed(20261109 ~ 20261126)와 겹치지 않게 둔다
    private const int ScatterSeed = 20261127;

    // 구분 마스크 : 중앙(MapTreeScatter)과 같은 노이즈(주기, 위치)를 써서 B2와 무늬가 이어지게 하고, 비율만 다르게 나눈다.
    // 노이즈 하위 15% = 빈터, 다음 25% = 드문드문, 상위 60% = 숲 (사용자 승인 임시값)
    private const float ZoneNoisePeriod = 250f;
    private static readonly Vector2 ZoneNoiseOffset = new Vector2(371.3f, 912.7f);
    private const float OpenAreaRatio = 0.15f;
    private const float ForestAreaRatio = 0.60f;
    // 분위수를 구할 때 A2를 표본 조사하는 간격 (m)
    private const float ZoneSampleSpacing = 5f;

    // 흩뿌리기 단위 칸 (10m × 10m)과 1ha 넓이 (MapTreeScatter와 같은 단위)
    private const float ScatterCellSize = 10f;
    private const float HectareArea = 10000f;

    // 밀도 표 (1ha당 최소, 최대). 순서는 FieldZone (빈터, 드문드문, 숲). 중앙 지역과 같은 값이다
    private static readonly float[] TreeDensityMin = { 0f, 5f, 100f };
    private static readonly float[] TreeDensityMax = { 0f, 15f, 150f };
    private static readonly float[] RockDensityMin = { 1f, 1f, 10f };
    private static readonly float[] RockDensityMax = { 3f, 3f, 20f };
    private static readonly string[] ZoneLabels = { "빈터", "드문드문", "숲" };

    // 바위 피벗을 이 반지름(× 배율) 안의 가장 낮은 땅에 맞춘다 (북쪽 배치와 같은 값)
    private const float RockFootprintRadius = 1f;

    // ---------- 랜드마크 위치 규칙 ----------
    // A2 안을 LandmarkSearchStep 간격 격자로 돌며 아래 조건을 모두 맞추는 자리 중,
    // (1) 땅에 맞추면 많이 묻히는 조각이 가장 적고 (2) 바닥 아래 지면 높이 차가 가장 작은 곳에 놓는다. 같으면 남서쪽에서 먼저 만난 자리.
    // - 묶음 바닥 전체가 A2 경계에서 LandmarkEdgeMargin 이상 안쪽 (사용자 승인 값)
    // - 묶음 바닥 전체가 서식지 반경 밖
    // - 먼저 놓은 랜드마크와 중심 거리 LandmarkMinSpacing 이상 (사용자 승인 값)
    // - 중심 경사 LandmarkMaxSlope 이하 (북쪽 룬 돌과 같은 값)
    // - 신전 터는 중심이 숲, 야영지는 드문드문 또는 빈터 (숲 가장자리 느낌)
    // 놓는 순서는 LandmarkSpecs 순서다. 조건이 가장 까다로운 신전 터를 먼저 놓는다.
    private const float LandmarkEdgeMargin = 50f;
    private const float LandmarkMinSpacing = 150f;
    private const float LandmarkMaxSlope = 25f;
    private const float LandmarkSearchStep = 10f;
    // 랜드마크 바닥 둘레에 나무, 바위를 두지 않아 멀리서도 보이는 빈터가 되게 한다 (북쪽 룬 돌과 같은 값)
    private const float LandmarkClearance = 8f;

    // ---------- 랜드마크 배치도 (임시값) ----------
    // 좌표는 묶음 중심 기준 (x = 동, z = 북, 묶음 회전 전). 조각 크기는 prefab 원래 크기 기준 배율이다.
    // 앞 메뉴들의 seed(20261109 ~ 20261128)와 겹치지 않게 둔다. 기둥, 선돌의 기울기와 크기 흔들림에 쓴다
    private const int LandmarkSeed = 20261129;
    private const float PieceTiltMax = 4f;
    private const float PieceScaleJitter = 0.1f;
    // 조각 밑면에서 바닥 둘레의 가장 높은 땅까지가 조각 높이 × 이 값을 넘으면 묻힌 것으로 센다
    private const float PieceMaxBuryRatio = 0.5f;
    // 묻는 깊이 비율. 바위 무리는 북쪽 큰 바위, 나머지는 북쪽 룬 돌과 같은 값
    private const float StructureSinkRatio = 0.05f;
    private const float BigRockSinkRatio = 0.25f;

    // 선돌 원 : 키 큰 선돌 9개 (북쪽 룬 돌 목록에서 고름). 가운데는 낮게 누운 08
    private const float StoneCircleRadius = 10f;
    private static readonly string[] StoneCirclePaths =
    {
        "StandingStones/Prefab_RockFormation_01", "StandingStones/Prefab_RockFormation_02", "StandingStones/Prefab_RockFormation_03",
        "StandingStones/Prefab_RockFormation_04", "StandingStones/Prefab_RockFormation_05", "StandingStones/Prefab_RockFormation_06",
        "StandingStones/Prefab_RockFormation_07", "StandingStones/Prefab_RockFormation_09", "StandingStones/Prefab_RockFormation_10"
    };

    // 야영지 불 자리 : 작은 돌을 둥글게 놓는다. 가운데 큰 불 자리와 가장자리 작은 불 자리 두 곳
    private const int BigFirePitStoneCount = 9;
    private const float BigFirePitRadius = 1.2f;
    private const int FirePitStoneCount = 7;
    private const float FirePitRadius = 0.9f;
    private const float FirePitStoneScale = 0.35f;
    private static readonly string[] FirePitPaths = { "Stones/Prefab_SmallRock_01", "Stones/Prefab_SmallRock_02" };
    // 야영지 배치도 사각형 한 변 (묶음 중심 기준 -20 ~ +20m). 로그의 밀도 계산에 쓴다
    private const float CampAreaSize = 40f;
    private static readonly Vector2 SmallFirePitCenter = new Vector2(-14f, 12f);

    // 무너진 신전 터 : 남쪽 입구의 부서진 아치 → 돌계단 → 두 줄 기둥(x = ±4) → 북쪽 끝 무너진 벽. 바닥에 벽돌 조각을 흩는다
    private static readonly PieceSpec[] TemplePieces =
    {
        new PieceSpec { Path = "Ruins/Prefab_DoorArch_Broken", Offset = new Vector2(0f, -12f), Yaw = 0f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_Stairs", Offset = new Vector2(0f, -8f), Yaw = 0f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_PillarFull", Offset = new Vector2(-4f, -3f), Yaw = 0f, Scale = 1f, IsWobbly = true },
        new PieceSpec { Path = "Ruins/Prefab_PillarHalf_Full", Offset = new Vector2(4f, -3f), Yaw = 90f, Scale = 1f, IsWobbly = true },
        new PieceSpec { Path = "Ruins/Prefab_ PillarBroken", Offset = new Vector2(-4f, 2f), Yaw = 35f, Scale = 1f, IsWobbly = true },
        new PieceSpec { Path = "Ruins/Prefab_PillarFull", Offset = new Vector2(4f, 2f), Yaw = 180f, Scale = 1f, IsWobbly = true },
        new PieceSpec { Path = "Ruins/Prefab_ PillarHalf_Broken_01", Offset = new Vector2(-4f, 7f), Yaw = 120f, Scale = 1f, IsWobbly = true },
        new PieceSpec { Path = "Ruins/Prefab_ PillarHalf_Broken_02", Offset = new Vector2(4f, 7f), Yaw = 250f, Scale = 1f, IsWobbly = true },
        // 기둥이 빠진 자리처럼 받침만 남긴다
        new PieceSpec { Path = "Ruins/Prefab_ PillarBase_Round", Offset = new Vector2(-8f, -1f), Yaw = 15f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_ Wall_Left", Offset = new Vector2(-5f, 12f), Yaw = 0f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_Wall_Mid", Offset = new Vector2(0f, 12.5f), Yaw = 0f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_ Wall_Right", Offset = new Vector2(5f, 12f), Yaw = 0f, Scale = 1f },
        // 벽 하나는 무너져 옆으로 돌아간 모양
        new PieceSpec { Path = "Ruins/Prefab_Wall_Mid", Offset = new Vector2(9f, 8f), Yaw = 70f, Scale = 0.9f },
        new PieceSpec { Path = "Ruins/Prefab_Stairs_Brick_01", Offset = new Vector2(2f, -4f), Yaw = 15f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_Stairs_Brick_02", Offset = new Vector2(-2.5f, 0.5f), Yaw = 40f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_Stairs_Brick_03", Offset = new Vector2(1f, 5f), Yaw = 75f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_Stairs_Brick_01", Offset = new Vector2(-1f, 9f), Yaw = 120f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_Stairs_Brick_02", Offset = new Vector2(7f, -6f), Yaw = 200f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_Stairs_Brick_03", Offset = new Vector2(-7f, 4f), Yaw = 300f, Scale = 1f }
    };

    // 큰 바위 무리 : 가운데 절벽 조각으로 솟은 언덕을 만들고, 큰 바위를 동쪽으로 쌓는다. 하나는 바로 앞 바위 위에 올린다
    private static readonly PieceSpec[] BigRockPieces =
    {
        new PieceSpec { Path = "Stones/Prefab_Cliff_04", Offset = new Vector2(0f, 0f), Yaw = 0f, Scale = 0.6f },
        new PieceSpec { Path = "Stones/Prefab_Rock_12", Offset = new Vector2(6f, -2f), Yaw = 30f, Scale = 3f },
        new PieceSpec { Path = "Stones/Prefab_Rock_09", Offset = new Vector2(7f, -1f), Yaw = 160f, Scale = 1.6f, IsStacked = true, StackSinkRatio = 0.5f },
        new PieceSpec { Path = "Stones/Prefab_Rock_09", Offset = new Vector2(10f, 3f), Yaw = 75f, Scale = 2.5f },
        new PieceSpec { Path = "Stones/Prefab_Rock_06", Offset = new Vector2(5f, 6f), Yaw = 210f, Scale = 2.2f },
        new PieceSpec { Path = "Stones/Prefab_Rock_04", Offset = new Vector2(11f, -5f), Yaw = 300f, Scale = 1.8f },
        new PieceSpec { Path = "Stones/Prefab_Rock_06", Offset = new Vector2(14f, 1f), Yaw = 120f, Scale = 1.5f },
        new PieceSpec { Path = "Stones/Prefab_Rock_12", Offset = new Vector2(-5f, 4f), Yaw = 250f, Scale = 2f },
        new PieceSpec { Path = "Stones/Prefab_Rock_04", Offset = new Vector2(-4f, -5f), Yaw = 10f, Scale = 1.5f },
        new PieceSpec { Path = "Stones/Prefab_SmallRock_01", Offset = new Vector2(16f, -7f), Yaw = 45f, Scale = 1f },
        new PieceSpec { Path = "Stones/Prefab_SmallRock_02", Offset = new Vector2(-9f, -2f), Yaw = 135f, Scale = 1.2f },
        new PieceSpec { Path = "Stones/Prefab_SmallRock_01", Offset = new Vector2(2f, -11f), Yaw = 220f, Scale = 1f },
        new PieceSpec { Path = "Stones/Prefab_SmallRock_02", Offset = new Vector2(17f, 6f), Yaw = 310f, Scale = 0.8f }
    };

    // 선돌 원 : 반지름 StoneCircleRadius 원에 선돌을 같은 간격으로 세워 가운데를 보게 하고, 가운데에 낮은 선돌 하나
    private static readonly PieceSpec[] StoneCirclePieces = CreateRing(
            new PieceSpec { Offset = Vector2.zero, Scale = 1f, IsWobbly = true }, StoneCirclePaths, StoneCirclePaths.Length, StoneCircleRadius)
        .Append(new PieceSpec { Path = "StandingStones/Prefab_RockFormation_08", Offset = Vector2.zero, Yaw = 0f, Scale = 1f, IsWobbly = true })
        .ToArray();

    // 버려진 사냥꾼 야영지 : 40 × 40m 안에 역할이 있는 무리로 나눠 둔다. 장작과 통나무만 크게 한다 (1.3 ~ 1.5배)
    // 가운데 큰 불 자리 / 북서 작은 불 자리 / 남동 벌목터 / 장작 쌓아 둔 곳 4무리 / 수레 2대 (북쪽, 남서쪽) / 바깥쪽 해골
    private static readonly PieceSpec[] CampPieces = new[]
        {
            // 가운데 큰 불 자리 둘레 : 앉는 통나무 4개 (불 자리에서 약 3.8m, 네 방향)와 장작 3개
            new PieceSpec { Path = "Objects/Prefab_HollowTrunk", Offset = new Vector2(-3.6f, 1.2f), Yaw = 70f, Scale = 1.4f },
            new PieceSpec { Path = "Objects/Prefab_HollowTrunk", Offset = new Vector2(3.4f, 1.8f), Yaw = 120f, Scale = 1.4f },
            new PieceSpec { Path = "Objects/Prefab_HollowTrunk", Offset = new Vector2(0.5f, -3.8f), Yaw = 5f, Scale = 1.4f },
            new PieceSpec { Path = "Objects/Prefab_HollowTrunk", Offset = new Vector2(-0.5f, 3.9f), Yaw = 175f, Scale = 1.3f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_01", Offset = new Vector2(-5f, -2.5f), Yaw = 0f, Scale = 1.5f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_02", Offset = new Vector2(5f, -1.5f), Yaw = 30f, Scale = 1.3f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_03", Offset = new Vector2(-1.5f, -6f), Yaw = 100f, Scale = 1.4f },

            // 북서 작은 불 자리 둘레 (불 자리 돌은 아래 CreateRing) : 앉는 통나무 1개와 장작 3개
            new PieceSpec { Path = "Objects/Prefab_HollowTrunk", Offset = new Vector2(-11.5f, 10f), Yaw = 40f, Scale = 1.3f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_01", Offset = new Vector2(-16f, 10.5f), Yaw = 60f, Scale = 1.3f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_02", Offset = new Vector2(-12f, 14f), Yaw = 200f, Scale = 1.4f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_03", Offset = new Vector2(-15.5f, 14.5f), Yaw = 310f, Scale = 1.3f },

            // 남동 벌목터 : 그루터기 6개 (그중 2개에 도끼를 비스듬히 꽂음. 도끼는 바로 앞 그루터기 윗면에 올린다), 쓰러진 통나무 3개, 흩어진 장작 3개
            new PieceSpec { Path = "Objects/Prefab_Stump", Offset = new Vector2(8f, -9f), Yaw = 0f, Scale = 1f },
            new PieceSpec { Path = "Objects/Prefab_Stump", Offset = new Vector2(12f, -10f), Yaw = 60f, Scale = 1f },
            new PieceSpec { Path = "Objects/Prefab_Axe", Offset = new Vector2(12f, -10f), Yaw = 30f, Tilt = new Vector2(20f, 0f), Scale = 1f, IsStacked = true, StackSinkRatio = 0.2f },
            new PieceSpec { Path = "Objects/Prefab_Stump", Offset = new Vector2(16f, -12f), Yaw = 140f, Scale = 1f },
            new PieceSpec { Path = "Objects/Prefab_Stump", Offset = new Vector2(10f, -14f), Yaw = 210f, Scale = 1f },
            new PieceSpec { Path = "Objects/Prefab_Axe", Offset = new Vector2(10f, -14f), Yaw = 250f, Tilt = new Vector2(20f, 0f), Scale = 1f, IsStacked = true, StackSinkRatio = 0.2f },
            new PieceSpec { Path = "Objects/Prefab_Stump", Offset = new Vector2(14f, -16f), Yaw = 280f, Scale = 1f },
            new PieceSpec { Path = "Objects/Prefab_Stump", Offset = new Vector2(18f, -17f), Yaw = 330f, Scale = 1f },
            new PieceSpec { Path = "Objects/Prefab_HollowTrunk", Offset = new Vector2(5f, -13f), Yaw = 30f, Scale = 1.5f },
            new PieceSpec { Path = "Objects/Prefab_HollowTrunk", Offset = new Vector2(17f, -7f), Yaw = 100f, Scale = 1.4f },
            new PieceSpec { Path = "Objects/Prefab_HollowTrunk", Offset = new Vector2(5.5f, -6.5f), Yaw = 150f, Scale = 1.3f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_01", Offset = new Vector2(10f, -11.5f), Yaw = 20f, Scale = 1.3f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_02", Offset = new Vector2(13.5f, -13.5f), Yaw = 170f, Scale = 1.4f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_03", Offset = new Vector2(8f, -17.5f), Yaw = 290f, Scale = 1.3f },

            // 장작 쌓아 둔 곳 4무리 (무리마다 01 ~ 03을 가까이 모은다) : 북동, 서, 남서, 북쪽 수레 옆
            new PieceSpec { Path = "Objects/Prefab_Firewood_01", Offset = new Vector2(11f, 7f), Yaw = 10f, Scale = 1.5f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_02", Offset = new Vector2(12.5f, 8.5f), Yaw = 80f, Scale = 1.4f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_03", Offset = new Vector2(13.5f, 7f), Yaw = 140f, Scale = 1.3f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_01", Offset = new Vector2(-17f, -5f), Yaw = 190f, Scale = 1.4f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_02", Offset = new Vector2(-15.5f, -3.5f), Yaw = 250f, Scale = 1.5f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_03", Offset = new Vector2(-16.5f, -2f), Yaw = 320f, Scale = 1.3f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_01", Offset = new Vector2(-7f, -16f), Yaw = 45f, Scale = 1.3f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_02", Offset = new Vector2(-5.5f, -14.5f), Yaw = 110f, Scale = 1.4f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_03", Offset = new Vector2(-4.5f, -16.5f), Yaw = 230f, Scale = 1.5f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_01", Offset = new Vector2(-4f, 13f), Yaw = 300f, Scale = 1.4f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_02", Offset = new Vector2(-2.5f, 14.5f), Yaw = 20f, Scale = 1.3f },
            new PieceSpec { Path = "Objects/Prefab_Firewood_03", Offset = new Vector2(-3f, 12f), Yaw = 160f, Scale = 1.5f },

            // 수레 2대 : 북쪽 수레 옆에 누운 바퀴, 남서쪽 수레는 바퀴가 빠져 바로 옆에 누워 있다
            new PieceSpec { Path = "Objects/Prefab_Cart", Offset = new Vector2(2f, 15f), Yaw = 20f, Scale = 1f },
            new PieceSpec { Path = "Objects/Prefab_CartWheel", Offset = new Vector2(5f, 13f), Yaw = 80f, Tilt = new Vector2(0f, 80f), Scale = 1f },
            new PieceSpec { Path = "Objects/Prefab_Cart", Offset = new Vector2(-12f, -10f), Yaw = 200f, Scale = 1f },
            new PieceSpec { Path = "Objects/Prefab_CartWheel", Offset = new Vector2(-9.5f, -8f), Yaw = 230f, Tilt = new Vector2(0f, 85f), Scale = 1f },

            // 바깥쪽에 드문드문 해골 3개
            new PieceSpec { Path = "Objects/Prefab_Skull", Offset = new Vector2(-18f, 17f), Yaw = 140f, Scale = 1f },
            new PieceSpec { Path = "Objects/Prefab_Skull", Offset = new Vector2(18f, 2f), Yaw = 260f, Scale = 1f },
            new PieceSpec { Path = "Objects/Prefab_Skull", Offset = new Vector2(-2f, -19f), Yaw = 20f, Scale = 1f }
        }
        .Concat(CreateRing(new PieceSpec { Offset = Vector2.zero, Scale = FirePitStoneScale }, FirePitPaths, BigFirePitStoneCount, BigFirePitRadius))
        .Concat(CreateRing(new PieceSpec { Offset = SmallFirePitCenter, Scale = FirePitStoneScale }, FirePitPaths, FirePitStoneCount, FirePitRadius))
        .ToArray();

    private static readonly LandmarkSpec[] LandmarkSpecs =
    {
        new LandmarkSpec
        {
            Label = "TempleRuin", GroupYaw = 20f, SinkRatio = StructureSinkRatio, ApproxRadius = 15f,
            Zones = new[] { MapTreeScatter.FieldZone.Forest }, Pieces = TemplePieces
        },
        new LandmarkSpec { Label = "BigRockCluster", GroupYaw = 135f, SinkRatio = BigRockSinkRatio, ApproxRadius = 20f, Pieces = BigRockPieces },
        new LandmarkSpec { Label = "StoneCircle", GroupYaw = 0f, SinkRatio = StructureSinkRatio, ApproxRadius = 13f, Pieces = StoneCirclePieces },
        new LandmarkSpec
        {
            // 40 × 40m 사각형의 반 대각선이 약 28m
            Label = "AbandonedHunterCamp", GroupYaw = 250f, SinkRatio = StructureSinkRatio, ApproxRadius = 28f, AreaSize = CampAreaSize,
            Zones = new[] { MapTreeScatter.FieldZone.Sparse, MapTreeScatter.FieldZone.Open }, Pieces = CampPieces
        }
    };

    // 렌더러 범위 둘레 8곳은 AABB보다 FootprintInset만큼 안쪽을 본다 (MapDemoObjectTransplant와 같은 값)
    private const float FootprintInset = 0.7f;
    private static readonly Vector2[] RingOffsets =
    {
        new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 0f),
        new Vector2(1f, 0f), new Vector2(-1f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f)
    };
    private const float MaxBuryGap = 3f;
    // 뜸 검수 (계산 오차만 허용) : 나무·바위는 피벗이 피벗 아래 지면보다, 랜드마크 조각은 정점 밑면이 바닥 둘레의 가장 낮은 땅보다 이만큼 넘게 높으면 뜬 것으로 센다
    private const float HeightTolerance = 0.01f;

    // GPU Resident Drawer로 그리면 몸체가 안 보이는 것으로 확인된 셰이더 (MapDemoObjectTransplant와 같은 목록)
    private static readonly string[] GpuDrivenBrokenShaderNames = { "Polyart/Dreamscape CliffAuto", "M_Water_Lake_Amp" };
    private const string DisallowGpuDrivenTypeName = "UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime";

    // 거점 기준 8방위. 0도 = 북(+Z), 시계 방향
    private static readonly string[] DirectionLabels = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };

    // 노이즈 값의 구분 경계 [드문드문 시작, 숲 시작]. A2를 표본 조사해 면적 비율이 맞도록 한 번만 계산한다
    private static readonly float[] ZoneThresholds = CalculateZoneThresholds();

    // 같은 위치는 항상 같은 구분을 돌려준다 (지면·바닥 식생 메뉴 13이 같은 구분을 쓰기 위함). 서식지 반경 안은 빈터다
    public static MapTreeScatter.FieldZone GetWestZone(Vector2 worldXZ)
    {
        if (IsInHabitat(worldXZ)) return MapTreeScatter.FieldZone.Open;

        float noise = SampleZoneNoise(worldXZ);
        if (noise >= ZoneThresholds[1]) return MapTreeScatter.FieldZone.Forest;
        if (noise >= ZoneThresholds[0]) return MapTreeScatter.FieldZone.Sparse;
        return MapTreeScatter.FieldZone.Open;
    }

    [MenuItem("Tools/Map/12. 서쪽 나무·바위·랜드마크·서식지 배치")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        Scene scene = terrain.gameObject.scene;
        if (scene != SceneManager.GetActiveScene())
        {
            Debug.LogError("[MapWestScatter] Game 씬을 활성 씬으로 두고 실행하세요.");
            return;
        }

        GameObject westRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == WestRegionName);
        if (westRoot == null)
        {
            Debug.LogError("씬 루트에 West가 없습니다. 먼저 'Tools/Map/1. 지형과 구역 부모 생성'을 실행하세요.");
            return;
        }

        System.Type disallowType = System.Type.GetType(DisallowGpuDrivenTypeName);
        if (disallowType == null)
        {
            Debug.LogError($"{DisallowGpuDrivenTypeName} 타입을 찾지 못했습니다. URP 패키지 버전이 바뀌었는지 확인하세요.");
            return;
        }

        Transform treeParent = ResetParent(westRoot.transform, TreeParentName);
        Transform rockParent = ResetParent(westRoot.transform, RockParentName);
        Transform landmarkParent = ResetParent(westRoot.transform, LandmarkParentName);
        Transform habitat = ResetParent(westRoot.transform, HabitatName);
        habitat.position = ToWorld(HabitatCenter, GetGroundHeight(terrain, HabitatCenter));

        var landmarkContext = new LandmarkContext
        {
            Terrain = terrain,
            DisallowType = disallowType,
            Parent = landmarkParent,
            Random = new System.Random(LandmarkSeed)
        };
        foreach (LandmarkSpec spec in LandmarkSpecs)
            TryPlaceLandmark(landmarkContext, spec);
        ScatterContext scatterContext = ScatterTreesAndRocks(terrain, treeParent, rockParent, landmarkContext.Placed);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        LogZones(scatterContext);
        LogLandmarks(landmarkContext);
        LogPlacementCheck(terrain, new[] { treeParent, rockParent }, landmarkParent, landmarkContext.BrokenCount);
    }

    // 부모가 없으면 만들고, 있으면 자식을 모두 지운다
    private static Transform ResetParent(Transform root, string name)
    {
        Transform parent = root.Find(name);
        if (parent == null)
        {
            parent = new GameObject(name).transform;
            parent.SetParent(root, false);
        }
        for (int i = parent.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(parent.GetChild(i).gameObject);
        return parent;
    }

    // ---------- 구분 (숲 / 드문드문 / 빈터) ----------

    private static float SampleZoneNoise(Vector2 worldXZ)
    {
        return Mathf.PerlinNoise(worldXZ.x / ZoneNoisePeriod + ZoneNoiseOffset.x, worldXZ.y / ZoneNoisePeriod + ZoneNoiseOffset.y);
    }

    // Perlin 노이즈는 값이 가운데에 몰려 있어 고정 임계값으로는 비율을 맞추기 어려워 분위수로 경계를 정한다.
    // 서식지 반경은 노이즈와 상관없이 빈터이므로, 반경 밖 표본만으로 경계를 정하고 빈터 몫에서 서식지 넓이를 뺀다.
    // 그래야 서식지를 합친 A2 전체 비율이 60 / 25 / 15%에 맞는다
    private static float[] CalculateZoneThresholds()
    {
        Rect west = MapGrid.GetRangeBounds(WestCell, WestCell);
        int columns = Mathf.RoundToInt(west.width / ZoneSampleSpacing);
        int rows = Mathf.RoundToInt(west.height / ZoneSampleSpacing);
        var outside = new List<float>(columns * rows);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var position = new Vector2(west.xMin + (x + 0.5f) * ZoneSampleSpacing, west.yMin + (z + 0.5f) * ZoneSampleSpacing);
                if (!IsInHabitat(position)) outside.Add(SampleZoneNoise(position));
            }
        }
        outside.Sort();

        int total = columns * rows;
        int habitatCount = total - outside.Count;
        int openCount = Mathf.Max(0, Mathf.RoundToInt(total * OpenAreaRatio) - habitatCount);
        int forestCount = Mathf.RoundToInt(total * ForestAreaRatio);
        return new[] { outside[openCount], outside[outside.Count - forestCount] };
    }

    private static bool IsInHabitat(Vector2 position)
    {
        return Vector2.Distance(position, HabitatCenter) < HabitatRadius;
    }

    // ---------- 랜드마크 ----------

    private static void TryPlaceLandmark(LandmarkContext context, LandmarkSpec spec)
    {
        LandmarkBuild build = BuildLandmark(context, spec);
        if (build == null) return;

        LandmarkFit? fit = FindLandmarkSpot(context, build, spec);
        if (fit == null)
        {
            Debug.LogWarning($"[MapWestScatter] '{spec.Label}' 자리를 찾지 못했습니다 (바닥 반지름 {build.Radius:F1}m). 위치 규칙의 조건을 확인하세요.");
            Object.DestroyImmediate(build.Group.gameObject);
            return;
        }

        PlaceLandmark(context, build, spec, fit.Value);
    }

    // 배치도대로 월드 원점에 조립한다. 묶음 중심이 원점이라 조각의 월드 XZ가 곧 중심 기준 위치가 되어, 자리 찾기에서 그대로 쓴다
    private static LandmarkBuild BuildLandmark(LandmarkContext context, LandmarkSpec spec)
    {
        var build = new LandmarkBuild { Group = new GameObject(spec.Label).transform };
        build.Group.SetParent(context.Parent, false);
        build.Group.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, spec.GroupYaw, 0f));

        foreach (PieceSpec piece in spec.Pieces)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + piece.Path + ".prefab");
            if (prefab == null)
            {
                Debug.LogError($"[MapWestScatter] {piece.Path} prefab을 찾지 못해 '{spec.Label}'을 놓지 못했습니다. 배치도의 경로를 확인하세요.");
                Object.DestroyImmediate(build.Group.gameObject);
                return null;
            }

            GameObject instance = CreatePiece(context, piece, prefab, build.Group);
            FinishInstance(context, build, instance, prefab.name);

            Bounds bounds = GetRendererBounds(instance);
            (float bottom, float top) = GetMeshHeightRange(instance);
            build.Pieces.Add(new PlacedPiece { Spec = piece, Transform = instance.transform, Points = GetFootprintPoints(bounds), Height = top - bottom });
            build.Radius = Mathf.Max(build.Radius, ToXZ(bounds.center).magnitude + Mathf.Max(bounds.extents.x, bounds.extents.z));
        }
        return build;
    }

    // prefab 연결을 유지한 채 놓는다. 흔들림 값은 조각 순서대로 같은 seed에서 뽑아 다시 실행해도 같다
    private static GameObject CreatePiece(LandmarkContext context, PieceSpec piece, GameObject prefab, Transform group)
    {
        Quaternion rotation = Quaternion.Euler(piece.Tilt.x, piece.Yaw, piece.Tilt.y);
        float scale = piece.Scale;
        if (piece.IsWobbly)
        {
            float tilt = RandomRange(context.Random, 0f, PieceTiltMax);
            Vector3 tiltAxis = Quaternion.Euler(0f, RandomRange(context.Random, 0f, 360f), 0f) * Vector3.right;
            rotation = Quaternion.AngleAxis(tilt, tiltAxis) * rotation;
            scale *= RandomRange(context.Random, 1f - PieceScaleJitter, 1f + PieceScaleJitter);
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
        instance.transform.localPosition = new Vector3(piece.Offset.x, 0f, piece.Offset.y);
        instance.transform.localRotation = rotation;
        instance.transform.localScale = Vector3.one * scale;
        return instance;
    }

    // template의 Offset을 원 중심으로, 원 둘레에 count개를 같은 간격(북쪽부터 시계 방향)으로 놓고 가운데를 보게 돌린다. paths는 차례로 돌려 쓴다
    private static IEnumerable<PieceSpec> CreateRing(PieceSpec template, string[] paths, int count, float radius)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = i * 360f / count;
            float radian = angle * Mathf.Deg2Rad;
            PieceSpec piece = template;
            piece.Path = paths[i % paths.Length];
            piece.Offset = template.Offset + new Vector2(Mathf.Sin(radian), Mathf.Cos(radian)) * radius;
            piece.Yaw = angle + 180f;
            yield return piece;
        }
    }

    private static LandmarkFit? FindLandmarkSpot(LandmarkContext context, LandmarkBuild build, LandmarkSpec spec)
    {
        Rect west = MapGrid.GetRangeBounds(WestCell, WestCell);
        float inset = LandmarkEdgeMargin + build.Radius;
        LandmarkFit? best = null;
        for (float z = west.yMin + inset; z <= west.yMax - inset; z += LandmarkSearchStep)
        {
            for (float x = west.xMin + inset; x <= west.xMax - inset; x += LandmarkSearchStep)
            {
                var position = new Vector2(x, z);
                if (!CanPlaceLandmark(context, build.Radius, spec, position)) continue;

                LandmarkFit fit = MeasureLandmark(context.Terrain, build, spec.SinkRatio, position);
                if (best == null || IsBetterFit(fit, best.Value)) best = fit;
            }
        }
        return best;
    }

    private static bool CanPlaceLandmark(LandmarkContext context, float radius, LandmarkSpec spec, Vector2 position)
    {
        if (Vector2.Distance(position, HabitatCenter) < HabitatRadius + radius) return false;
        if (context.Placed.Any(other => Vector2.Distance(other.Center, position) < LandmarkMinSpacing)) return false;
        if (GetSlope(context.Terrain, position) > LandmarkMaxSlope) return false;
        return spec.Zones == null || spec.Zones.Contains(GetWestZone(position));
    }

    // 묶음 중심을 position에 옮겼을 때 조각마다 바닥 아래 지면을 재서, 땅에 맞추면 많이 묻히는 조각 수를 센다 (PlaceLandmark와 같은 계산).
    // 올린 조각은 앞 조각 위에 놓이므로 지면을 재지 않는다
    private static LandmarkFit MeasureLandmark(Terrain terrain, LandmarkBuild build, float sinkRatio, Vector2 position)
    {
        float groundMin = float.MaxValue;
        float groundMax = float.MinValue;
        int brokenCount = 0;
        foreach (PlacedPiece piece in build.Pieces)
        {
            if (piece.Spec.IsStacked) continue;

            float[] grounds = piece.Points.Select(point => GetGroundHeight(terrain, point + position)).ToArray();
            float lowest = grounds.Min();
            float highest = grounds.Max();
            groundMin = Mathf.Min(groundMin, lowest);
            groundMax = Mathf.Max(groundMax, highest);
            if (IsBuried(lowest, highest, piece.Height, sinkRatio)) brokenCount++;
        }
        return new LandmarkFit { Position = position, BrokenCount = brokenCount, GroundRange = groundMax - groundMin };
    }

    // 밑면을 (가장 낮은 땅 - 높이 × sinkRatio)에 맞췄을 때, 가장 높은 땅이 밑면에서 높이 × PieceMaxBuryRatio를 넘으면 많이 묻힌 것이다
    private static bool IsBuried(float lowest, float highest, float height, float sinkRatio)
    {
        return highest - (lowest - height * sinkRatio) > height * PieceMaxBuryRatio;
    }

    private static bool IsBetterFit(LandmarkFit fit, LandmarkFit best)
    {
        if (fit.BrokenCount != best.BrokenCount) return fit.BrokenCount < best.BrokenCount;
        return fit.GroundRange < best.GroundRange;
    }

    // 조립한 묶음을 찾은 자리로 옮긴 뒤 조각마다 높이를 맞춘다. 묶음 GameObject는 중심의 지면 높이에 둔다 (메뉴 13이 중심으로 쓴다)
    private static void PlaceLandmark(LandmarkContext context, LandmarkBuild build, LandmarkSpec spec, LandmarkFit fit)
    {
        float centerGround = GetGroundHeight(context.Terrain, fit.Position);
        build.Group.position = ToWorld(fit.Position, centerGround);

        int brokenCount = 0;
        int stackedCount = 0;
        for (int i = 0; i < build.Pieces.Count; i++)
        {
            PlacedPiece piece = build.Pieces[i];
            if (piece.Spec.IsStacked)
            {
                StackOnPiece(piece, build.Pieces[i - 1]);
                stackedCount++;
            }
            else if (GroundPiece(context.Terrain, piece, spec.SinkRatio))
            {
                brokenCount++;
            }
        }

        context.Placed.Add(new Blocker { Center = fit.Position, Radius = build.Radius + LandmarkClearance });
        context.BrokenCount += brokenCount;
        context.ColliderCount += build.ColliderCount;
        context.DisallowCount += build.DisallowCount;

        string prefabNames = string.Join(", ", build.Pieces
            .GroupBy(piece => piece.Transform.name)
            .OrderByDescending(names => names.Count())
            .Select(names => $"{names.Key} × {names.Count()}"));
        context.Notes.Add($"{spec.Label} : 중심 ({fit.Position.x:F0}, {fit.Position.y:F0}) 지면 {centerGround:F1}m, " +
                          $"바닥 반지름 {build.Radius:F1}m (구성안 약 {spec.ApproxRadius:F0}m), 구분 {ZoneLabels[(int)GetWestZone(fit.Position)]}, " +
                          $"바닥 아래 지면 높이 차 {fit.GroundRange:F1}m, 경사 {GetSlope(context.Terrain, fit.Position):F1}도, " +
                          $"조각 {build.Pieces.Count}개 (뜨거나 묻힘 {brokenCount}, 앞 조각 위에 올림 {stackedCount}), " +
                          $"Disallow 붙임 {build.DisallowCount}, 충돌체 추가 {build.ColliderCount}" +
                          (spec.AreaSize > 0f
                              ? $", {spec.AreaSize:F0} × {spec.AreaSize:F0}m 기준 밀도 {build.Pieces.Count / (spec.AreaSize * spec.AreaSize):F3}개/m²"
                              : "") +
                          $"\n  prefab : {prefabNames}");
    }

    // 밑면을 바닥 둘레의 가장 낮은 땅보다 (높이 × sinkRatio)만큼 아래에 맞춘다. 비탈 아래쪽이 뜨지 않고 위쪽은 땅에 묻힌다 (북쪽 큰 바위와 같은 방식).
    // 둘레 표본 위치(XZ)는 렌더러 범위로, 밑면과 높이는 메시 정점으로 잰다 (Prefab_Cart처럼 메시 범위가 실제 모양보다 아래로 잡힌 prefab이 있어서).
    // 옮긴 뒤 정점 밑면을 다시 재서 가장 낮은 땅보다 높으면 뜬 것으로 본다. 뜨거나 많이 묻혔으면 true
    private static bool GroundPiece(Terrain terrain, PlacedPiece piece, float sinkRatio)
    {
        GameObject instance = piece.Transform.gameObject;
        float[] grounds = GetFootprintPoints(GetRendererBounds(instance)).Select(point => GetGroundHeight(terrain, point)).ToArray();
        float lowest = grounds.Min();
        (float bottom, float top) = GetMeshHeightRange(instance);
        float height = top - bottom;
        piece.Transform.position += Vector3.up * (lowest - height * sinkRatio - bottom);

        bool isFloating = GetMeshHeightRange(instance).Bottom > lowest + HeightTolerance;
        return isFloating || IsBuried(lowest, grounds.Max(), height, sinkRatio);
    }

    // 앞 조각의 정점 윗면에 정점 밑면을 맞추고 (자기 높이 × StackSinkRatio)만큼 파묻는다
    private static void StackOnPiece(PlacedPiece piece, PlacedPiece below)
    {
        float belowTop = GetMeshHeightRange(below.Transform.gameObject).Top;
        (float bottom, float top) = GetMeshHeightRange(piece.Transform.gameObject);
        piece.Transform.position += Vector3.up * (belowTop - (top - bottom) * piece.Spec.StackSinkRatio - bottom);
    }

    // 실제 메시 정점의 가장 낮은, 높은 월드 Y. LODGroup이 있으면 LOD0 렌더러만 본다 (LOD1 이하는 같은 자리의 낮은 단계 메시라 겹친다)
    private static (float Bottom, float Top) GetMeshHeightRange(GameObject instance)
    {
        var lowerLodRenderers = new HashSet<Renderer>(instance.GetComponentsInChildren<LODGroup>()
            .SelectMany(group => group.GetLODs().Skip(1))
            .SelectMany(lod => lod.renderers));

        float bottom = float.MaxValue;
        float top = float.MinValue;
        foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
        {
            if (lowerLodRenderers.Contains(renderer)) continue;

            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                float y = toWorld.MultiplyPoint3x4(vertex).y;
                bottom = Mathf.Min(bottom, y);
                top = Mathf.Max(top, y);
            }
        }
        return (bottom, top);
    }

    private static Bounds GetRendererBounds(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
            bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    // 렌더러 범위 바닥의 가운데와 둘레 8곳 (월드 XZ)
    private static Vector2[] GetFootprintPoints(Bounds bounds)
    {
        Vector2 center = ToXZ(bounds.center);
        var points = new Vector2[RingOffsets.Length + 1];
        points[0] = center;
        for (int i = 0; i < RingOffsets.Length; i++)
        {
            Vector2 offset = RingOffsets[i] * FootprintInset;
            points[i + 1] = center + new Vector2(offset.x * bounds.extents.x, offset.y * bounds.extents.z);
        }
        return points;
    }

    // 충돌체 추가, Static 끔, GPU Resident Drawer 제외 (원본 prefab은 고치지 않는다)
    private static void FinishInstance(LandmarkContext context, LandmarkBuild build, GameObject instance, string prefabName)
    {
        if (MapTreeScatter.AddMissingCollider(instance, prefabName)) build.ColliderCount++;

        // Static Batching과 섞지 않는다 (MapLayout.md 성능 기준). 원본 prefab에 Static이 켜진 것(Prefab_ Wall_Right)이 있어 끈다
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetStaticEditorFlags(child.gameObject) != 0) GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
        }

        foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.sharedMaterials.All(material => material == null || SupportsGpuDriven(context, material.shader))) continue;
            if (renderer.GetComponent(context.DisallowType) != null) continue;

            renderer.gameObject.AddComponent(context.DisallowType);
            build.DisallowCount++;
        }
    }

    private static bool SupportsGpuDriven(LandmarkContext context, Shader shader)
    {
        if (context.GpuDrivenSupport.TryGetValue(shader, out bool isSupported)) return isSupported;

        isSupported = !GpuDrivenBrokenShaderNames.Contains(shader.name);
        context.GpuDrivenSupport[shader] = isSupported;
        return isSupported;
    }

    // ---------- 나무, 바위 ----------

    private static ScatterContext ScatterTreesAndRocks(Terrain terrain, Transform treeParent, Transform rockParent, List<Blocker> landmarks)
    {
        var context = new ScatterContext
        {
            Terrain = terrain,
            Random = new System.Random(ScatterSeed),
            Landmarks = landmarks,
            TreeGroups = new[]
            {
                CreateGroup(MapTreeScatter.MainTreePaths, treeParent, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax, false),
                CreateGroup(MapTreeScatter.DeadTreePaths, treeParent, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax, false),
                CreateGroup(MapTreeScatter.GiantTreePaths, treeParent, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax, false)
            },
            RockGroup = CreateGroup(MapTreeScatter.RockPaths, rockParent, MapTreeScatter.RockScaleMin, MapTreeScatter.RockScaleMax, true)
        };

        Rect west = MapGrid.GetRangeBounds(WestCell, WestCell);
        int columns = Mathf.RoundToInt(west.width / ScatterCellSize);
        int rows = Mathf.RoundToInt(west.height / ScatterCellSize);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var cellMin = new Vector2(west.xMin + x * ScatterCellSize, west.yMin + z * ScatterCellSize);
                MapTreeScatter.FieldZone zone = GetWestZone(cellMin + Vector2.one * (ScatterCellSize / 2f));
                context.ZoneCellCounts[(int)zone]++;
                ScatterTrees(context, cellMin, zone);
                ScatterRocks(context, cellMin, zone);
            }
        }
        return context;
    }

    private static InstanceGroup CreateGroup(string[] paths, Transform parent, float scaleMin, float scaleMax, bool isRock)
    {
        GameObject[] prefabs = paths
            .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + path + ".prefab"))
            .ToArray();
        return new InstanceGroup { Prefabs = prefabs, Parent = parent, ScaleMin = scaleMin, ScaleMax = scaleMax, IsRock = isRock };
    }

    private static void ScatterTrees(ScatterContext context, Vector2 cellMin, MapTreeScatter.FieldZone zone)
    {
        int count = RollCount(context.Random, TreeDensityMin[(int)zone], TreeDensityMax[(int)zone]);
        for (int i = 0; i < count; i++)
        {
            TreeKind kind = PickTreeKind(context.Random, zone);
            if (!TryAddInstance(context, cellMin, context.TreeGroups[(int)kind])) continue;

            context.TreeKindCounts[(int)kind]++;
            context.TreeCounts[(int)zone]++;
        }
    }

    private static void ScatterRocks(ScatterContext context, Vector2 cellMin, MapTreeScatter.FieldZone zone)
    {
        int count = RollCount(context.Random, RockDensityMin[(int)zone], RockDensityMax[(int)zone]);
        for (int i = 0; i < count; i++)
        {
            if (TryAddInstance(context, cellMin, context.RockGroup)) context.RockCounts[(int)zone]++;
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

    // 고사목은 모든 구분, 거대목은 숲에서만 나온다 (중앙과 같은 규칙)
    private static TreeKind PickTreeKind(System.Random random, MapTreeScatter.FieldZone zone)
    {
        double roll = random.NextDouble();
        if (zone == MapTreeScatter.FieldZone.Forest)
        {
            if (roll < MapTreeScatter.GiantTreeRatio) return TreeKind.Giant;
            roll -= MapTreeScatter.GiantTreeRatio;
        }
        return roll < MapTreeScatter.DeadTreeRatio ? TreeKind.Dead : TreeKind.Main;
    }

    // 서식지 반경, 랜드마크 둘레, 급경사에는 놓지 않는다. 무작위 값은 조건과 상관없이 같은 순서로 뽑아 결과가 항상 같게 한다
    private static bool TryAddInstance(ScatterContext context, Vector2 cellMin, InstanceGroup group)
    {
        System.Random random = context.Random;
        var position = new Vector2(
            cellMin.x + RandomRange(random, 0f, ScatterCellSize),
            cellMin.y + RandomRange(random, 0f, ScatterCellSize));
        GameObject prefab = group.Prefabs[random.Next(group.Prefabs.Length)];
        float scale = RandomRange(random, group.ScaleMin, group.ScaleMax);
        float yaw = RandomRange(random, 0f, 360f);

        if (IsInHabitat(position))
        {
            context.HabitatSkipCount++;
            return false;
        }
        if (context.Landmarks.Any(blocker => Vector2.Distance(blocker.Center, position) < blocker.Radius))
        {
            context.LandmarkSkipCount++;
            return false;
        }
        if (GetSlope(context.Terrain, position) > MapTreeScatter.MaxSlope)
        {
            context.SlopeSkipCount++;
            return false;
        }

        float ground = group.IsRock
            ? GetRingPoints(position, RockFootprintRadius * scale).Append(position).Min(point => GetGroundHeight(context.Terrain, point))
            : GetGroundHeight(context.Terrain, position);

        // prefab 연결을 유지한 채 놓는다. Static 플래그는 켜지 않는다 (GPU Resident Drawer로 그림)
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.Parent);
        instance.transform.SetPositionAndRotation(ToWorld(position, ground), Quaternion.Euler(0f, yaw, 0f));
        instance.transform.localScale = Vector3.one * scale;
        if (MapTreeScatter.AddMissingCollider(instance, prefab.name)) context.ColliderCount++;
        return true;
    }

    // ---------- 지형, 계산 ----------

    // SampleHeight는 Terrain 기준 높이라 Terrain의 y를 더한다
    private static float GetGroundHeight(Terrain terrain, Vector2 position)
    {
        return terrain.SampleHeight(ToWorld(position, 0f)) + terrain.transform.position.y;
    }

    private static float GetSlope(Terrain terrain, Vector2 position)
    {
        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        return terrain.terrainData.GetSteepness((position.x - origin.x) / size.x, (position.y - origin.z) / size.z);
    }

    // 동서남북 네 곳
    private static IEnumerable<Vector2> GetRingPoints(Vector2 center, float radius)
    {
        yield return center + Vector2.up * radius;
        yield return center + Vector2.down * radius;
        yield return center + Vector2.left * radius;
        yield return center + Vector2.right * radius;
    }

    // 0도 = 북(+Z), 시계 방향
    private static float GetAzimuth(Vector2 position)
    {
        return (Mathf.Atan2(position.x, position.y) * Mathf.Rad2Deg + 360f) % 360f;
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    private static Vector2 ToXZ(Vector3 world)
    {
        return new Vector2(world.x, world.z);
    }

    private static Vector3 ToWorld(Vector2 position, float height)
    {
        return new Vector3(position.x, height, position.y);
    }

    // ---------- 로그 ----------
    // 아래 로그는 검수(비율, 밀도가 승인 값 안인지, 랜드마크 위치, 서식지 비움, 뜸·묻힘, 성능 측정용 배치 수)에 쓰는 값이라 남긴다

    private static void LogZones(ScatterContext context)
    {
        float totalCells = context.ZoneCellCounts.Sum();
        var lines = new List<string>();
        for (int zone = 0; zone < ZoneLabels.Length; zone++)
        {
            float area = context.ZoneCellCounts[zone] * ScatterCellSize * ScatterCellSize / HectareArea;
            lines.Add($"{ZoneLabels[zone]} {area:F1}ha ({context.ZoneCellCounts[zone] / totalCells:P1}) : " +
                      $"나무 {context.TreeCounts[zone]}그루 {FormatDensity(context.TreeCounts[zone], area, TreeDensityMin[zone], TreeDensityMax[zone])}, " +
                      $"바위·소품 {context.RockCounts[zone]}개 {FormatDensity(context.RockCounts[zone], area, RockDensityMin[zone], RockDensityMax[zone])}");
        }
        int[] kinds = context.TreeKindCounts;
        Debug.Log($"[MapWestScatter] 서쪽(A2) 구분별 면적과 밀도 (승인 비율 숲 {ForestAreaRatio:P0} / 드문드문 {1f - ForestAreaRatio - OpenAreaRatio:P0} / 빈터 {OpenAreaRatio:P0}) :\n" +
                  string.Join("\n", lines));
        Debug.Log($"[MapWestScatter] 나무 {kinds.Sum()}그루 (주력 {kinds[(int)TreeKind.Main]}, 고사목 {kinds[(int)TreeKind.Dead]} ({kinds[(int)TreeKind.Dead] / Mathf.Max(1f, kinds.Sum()):P1}), " +
                  $"거대목 {kinds[(int)TreeKind.Giant]}), 바위·소품 {context.RockCounts.Sum()}개, 충돌체 추가 {context.ColliderCount}개 / " +
                  $"건너뜀 서식지 {context.HabitatSkipCount}, 랜드마크 둘레 {context.LandmarkSkipCount}, 경사 {context.SlopeSkipCount}");
    }

    private static string FormatDensity(int count, float area, float min, float max)
    {
        float density = area > 0f ? count / area : 0f;
        bool isInRange = density >= min && density <= max;
        return $"(1ha당 {density:F1}, 표 {min:F0} ~ {max:F0} {(isInRange ? "안" : "밖")})";
    }

    private static void LogLandmarks(LandmarkContext context)
    {
        Debug.Log($"[MapWestScatter] 랜드마크 {context.Notes.Count}/{LandmarkSpecs.Length}곳, 충돌체 추가 {context.ColliderCount}개, Disallow 붙임 {context.DisallowCount}개 :\n" +
                  string.Join("\n", context.Notes));
    }

    // 놓인 오브젝트를 다시 읽어 서식지 반경 안 개수(0이어야 함)와 나무·바위의 뜸, 묻힘을 센다. 랜드마크의 뜸, 묻힘은 놓을 때 잰 값을 더한다
    private static void LogPlacementCheck(Terrain terrain, Transform[] scatterParents, Transform landmarkParent, int landmarkBrokenCount)
    {
        List<Transform> scattered = scatterParents.SelectMany(parent => parent.Cast<Transform>()).ToList();
        List<Transform> landmarkItems = landmarkParent.Cast<Transform>().SelectMany(group => group.Cast<Transform>()).ToList();

        int inHabitatCount = scattered.Concat(landmarkItems).Count(child => IsInHabitat(ToXZ(child.position)));
        int floatingCount = 0;
        int buriedCount = 0;
        foreach (Transform child in scattered)
        {
            float gap = child.position.y - GetGroundHeight(terrain, ToXZ(child.position));
            if (gap > HeightTolerance) floatingCount++;
            if (gap < -MaxBuryGap) buriedCount++;
        }

        float azimuth = GetAzimuth(HabitatCenter);
        string direction = DirectionLabels[Mathf.RoundToInt(azimuth / 45f) % DirectionLabels.Length];
        Debug.Log($"[MapWestScatter] 서식지 ({HabitatCenter.x:F0}, {GetGroundHeight(terrain, HabitatCenter):F1}, {HabitatCenter.y:F0}), " +
                  $"거점 기준 방위각 {azimuth:F1}도 ({direction}), 반경 {HabitatRadius}m 안 오브젝트 {inHabitatCount}개 (0이어야 한다)");
        Debug.Log($"[MapWestScatter] 뜨거나 묻힌 오브젝트 : 나무·바위 뜸 {floatingCount}, 묻힘 {buriedCount} / 랜드마크 {landmarkBrokenCount}");

        // 랜드마크 묶음 GameObject와 서식지 표시(1)도 씬 GameObject라 함께 센다
        int total = scattered.Count + landmarkItems.Count + landmarkParent.childCount + 1;
        Debug.Log($"[MapWestScatter] West 배치 GameObject 총 {total}개 (나무·바위 {scattered.Count}, 랜드마크 오브젝트 {landmarkItems.Count}, " +
                  $"랜드마크 묶음 {landmarkParent.childCount}, 서식지 표시 1)");
    }
}
