using System;
using UnityEngine;

// 배치도의 칸 좌표 (예: "E2")를 월드 좌표로 바꾼다. 좌표 규칙은 Docs/World/MapLayout.md의 "좌표 규칙"을 따른다.
public static class MapGrid
{
    // 한 칸 크기 (임시값). 맵 전체 크기는 이 값 하나로 따라간다.
    public const float CellSize = 500f;
    public const int GridCount = 10;
    public const float MapSize = CellSize * GridCount;

    // 월드 원점이 거점 중심 (E5, F5, E6, F6이 만나는 점)이 되도록 격자의 절반만큼 옮긴다.
    private const float HalfGridCount = GridCount / 2f;
    private const char FirstColumn = 'A';

    // 칸 중심의 월드 좌표 (y = 0). 열은 서 -> 동 (+X), 행은 북 -> 남 (-Z).
    public static Vector3 GetCellCenter(string cellName)
    {
        ParseCell(cellName, out int columnIndex, out int rowIndex);

        float x = (columnIndex - HalfGridCount + 0.5f) * CellSize;
        float z = (HalfGridCount - rowIndex - 0.5f) * CellSize;
        return new Vector3(x, 0f, z);
    }

    // 두 모서리 칸 (예: "D1", "G3")으로 정한 범위의 월드 중심과 크기 (y = 0).
    public static Bounds GetRangeBounds(string firstCellName, string lastCellName)
    {
        ParseCell(firstCellName, out int firstColumn, out int firstRow);
        ParseCell(lastCellName, out int lastColumn, out int lastRow);

        Vector3 center = (GetCellCenter(firstCellName) + GetCellCenter(lastCellName)) / 2f;
        float sizeX = (Mathf.Abs(lastColumn - firstColumn) + 1) * CellSize;
        float sizeZ = (Mathf.Abs(lastRow - firstRow) + 1) * CellSize;
        return new Bounds(center, new Vector3(sizeX, 0f, sizeZ));
    }

    // "E2" -> 열 인덱스 4, 행 인덱스 1 (A열, 1행이 0)
    private static void ParseCell(string cellName, out int columnIndex, out int rowIndex)
    {
        columnIndex = char.ToUpperInvariant(cellName[0]) - FirstColumn;
        bool hasRow = int.TryParse(cellName.Substring(1), out int rowNumber);
        rowIndex = rowNumber - 1;

        // 잘못된 칸 이름은 엉뚱한 위치에 배치되기 전에 바로 알린다.
        bool isInside = columnIndex >= 0 && columnIndex < GridCount && rowIndex >= 0 && rowIndex < GridCount;
        if (!hasRow || !isInside)
            throw new ArgumentException($"잘못된 칸 이름입니다: {cellName}");
    }
}
