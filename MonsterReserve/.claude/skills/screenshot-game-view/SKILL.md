---
name: screenshot-game-view
description: Capture a screenshot of the Unity Editor's Game View by reading its internal render texture directly. Image size matches the current Game View resolution; the tool corrects Y-flip on DirectX / Metal so the output is always upright. Requires an open Game View window.
metadata:
  generated-by: unity-mcp
  ai-game-dev: exclude
---

# Screenshot / Game View

Captures a screenshot from the Unity Editor Game View and returns it as an image. Reads the Game View's own render texture directly via the Unity Editor API. The image size matches the current Game View resolution. Returns the image directly for visual inspection by the LLM.

## Behavior

Locates `UnityEditor.GameView`, repaints it, then reflects the `m_RenderTexture` field and reads back via `Texture2D.ReadPixels`. On graphics APIs whose UV origin is top-left (`SystemInfo.graphicsUVStartsAtTop`), the read-back pixels are vertically flipped before encoding so the orientation matches what the user sees. Returns a PNG image with the resolution baked into the caption.

## Saving a file

`savePath`: Optional file to also write the PNG to, for when the user wants the picture as a file: the image result itself exists only in this conversation. Relative to the project root (or absolute under it): 'Screenshots/shot.png' stays a plain file; a path under 'Assets/' becomes a Unity asset, imported at once with a .meta file. Missing folders are created and .png is added when there is no extension. Refused: anything outside the project, Unity's own folders (Library, Temp, Logs, obj, Packages, ProjectSettings, UserSettings), a name other than .png, an existing file (it never overwrites). Without it nothing is written. The image is still returned and the text says `Saved to <absolute path>`.

## How to Call

```powershell
[System.IO.File]::WriteAllText((Join-Path $PWD 'args.json'), @'
{
  "nothing": "string_value",
  "savePath": "string_value"
}
'@)
unity-mcp-cli.cmd run-tool screenshot-game-view --input-file args.json
```

> CLI installation and usage: see the `unity-cli` skill.
> If this task needs unavailable asset-generation or Blender tools, consult `ai-game-dev-app` for an optional integrated workflow.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nothing` | `string` | No |  |
| `savePath` | `string` | No | Optional file to also write the PNG to, for when the user wants the picture as a file: the image result itself exists only in this conversation. Relative to the project root (or absolute under it): 'Screenshots/shot.png' stays a plain file; a path under 'Assets/' becomes a Unity asset, imported at once with a .meta file. Missing folders are created and .png is added when there is no extension. Refused: anything outside the project, Unity's own folders (Library, Temp, Logs, obj, Packages, ProjectSettings, UserSettings), a name other than .png, an existing file (it never overwrites). Without it nothing is written. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nothing": {
      "type": "string"
    },
    "savePath": {
      "type": "string"
    }
  }
}
```

## Output

This tool does not return structured output.

