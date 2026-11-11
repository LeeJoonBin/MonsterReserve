using System;
using UnityEngine;

// 맵 격자 좌표 규칙 (Docs/World/MapLayout.md "좌표 규칙").
// 3 × 3 비균일 격자 : 가운데 열 B와 가운데 행 2는 InnerCellSize, 바깥 열 A, C와 바깥 행 1, 3은 OuterCellSize.
// 칸 크기는 임시값이므로 여기 한 곳에만 정의하고, 다른 도구는 모두 이 값을 따른다.
public static class MapGrid
{
    public const float InnerCellSize = 500f;
    public const float OuterCellSize = 1000f;
    public const int GridCount = 3;
    public const float MapSize = OuterCellSize * 2f + InnerCellSize;

    // "B2" 같은 칸 이름을 받아 칸 중심의 월드 XZ 좌표를 돌려준다. (x = 월드 X, y = 월드 Z)
    public static Vector2 GetCellCenter(string cellName)
    {
        return GetCellBounds(cellName).center;
    }

    // 두 칸을 꼭짓점으로 하는 직사각형 범위의 월드 XZ 영역. 예: ("B1", "C1") = 북, 북동 구역
    public static Rect GetRangeBounds(string fromCell, string toCell)
    {
        Rect from = GetCellBounds(fromCell);
        Rect to = GetCellBounds(toCell);
        return Rect.MinMaxRect(
            Mathf.Min(from.xMin, to.xMin), Mathf.Min(from.yMin, to.yMin),
            Mathf.Max(from.xMax, to.xMax), Mathf.Max(from.yMax, to.yMax));
    }

    // 칸 하나의 월드 XZ 영역 (x = 월드 X, y = 월드 Z)
    private static Rect GetCellBounds(string cellName)
    {
        ParseCell(cellName, out int column, out int row);
        float xMin = GetLineOffset(column) - MapSize / 2f;
        float xMax = xMin + GetCellLength(column);

        // 행 번호는 북(+Z)에서 남으로 커지므로 맵 북쪽 끝에서 빼 내려간다
        float zMax = MapSize / 2f - GetLineOffset(row);
        float zMin = zMax - GetCellLength(row);
        return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
    }

    // 가운데 줄(1번)만 안쪽 칸 크기, 나머지는 바깥 칸 크기
    private static float GetCellLength(int index)
    {
        return index == GridCount / 2 ? InnerCellSize : OuterCellSize;
    }

    // 맵 서쪽(또는 북쪽) 끝에서 index번 줄이 시작하는 거리
    private static float GetLineOffset(int index)
    {
        float offset = 0f;
        for (int i = 0; i < index; i++)
            offset += GetCellLength(i);
        return offset;
    }

    // 열 A~C는 서→동 (0~2), 행 1~3은 북→남 (0~2)
    private static void ParseCell(string cellName, out int column, out int row)
    {
        column = char.ToUpperInvariant(cellName[0]) - 'A';
        row = int.Parse(cellName.Substring(1)) - 1;

        // 칸 이름 오타는 배치가 엉뚱한 곳에 되므로 바로 오류를 낸다
        if (column < 0 || column >= GridCount || row < 0 || row >= GridCount)
            throw new ArgumentException($"맵 범위를 벗어난 칸 이름: {cellName}");
    }
}
