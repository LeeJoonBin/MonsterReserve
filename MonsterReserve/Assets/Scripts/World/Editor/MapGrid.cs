using System;
using UnityEngine;

// 맵 격자 좌표 규칙 (Docs/World/MapLayout.md "좌표 규칙").
// 칸 크기는 임시값이므로 여기 한 곳에만 정의하고, 다른 도구는 모두 이 값을 따른다.
public static class MapGrid
{
    public const float CellSize = 500f;
    public const int GridCount = 4;
    public const float MapSize = CellSize * GridCount;

    // "B2" 같은 칸 이름을 받아 칸 중심의 월드 XZ 좌표를 돌려준다. (x = 월드 X, y = 월드 Z)
    public static Vector2 GetCellCenter(string cellName)
    {
        ParseCell(cellName, out int column, out int row);
        float half = GridCount / 2f;
        float x = (column - half + 0.5f) * CellSize;
        float z = (half - row - 0.5f) * CellSize;
        return new Vector2(x, z);
    }

    // 두 칸을 꼭짓점으로 하는 직사각형 범위의 월드 XZ 영역. 예: ("B2", "C3") = 중앙 지역
    public static Rect GetRangeBounds(string fromCell, string toCell)
    {
        Vector2 from = GetCellCenter(fromCell);
        Vector2 to = GetCellCenter(toCell);
        float halfCell = CellSize / 2f;
        float xMin = Mathf.Min(from.x, to.x) - halfCell;
        float xMax = Mathf.Max(from.x, to.x) + halfCell;
        float zMin = Mathf.Min(from.y, to.y) - halfCell;
        float zMax = Mathf.Max(from.y, to.y) + halfCell;
        return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
    }

    // 열 A~D는 서→동 (0~3), 행 1~4는 북→남 (0~3)
    private static void ParseCell(string cellName, out int column, out int row)
    {
        column = char.ToUpperInvariant(cellName[0]) - 'A';
        row = int.Parse(cellName.Substring(1)) - 1;

        // 칸 이름 오타는 배치가 엉뚱한 곳에 되므로 바로 오류를 낸다
        if (column < 0 || column >= GridCount || row < 0 || row >= GridCount)
            throw new ArgumentException($"맵 범위를 벗어난 칸 이름: {cellName}");
    }
}
