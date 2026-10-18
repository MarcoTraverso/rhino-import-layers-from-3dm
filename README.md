# rhino-import-layers-from-3dm
A Rhino 8 C# script that lets you bring in objects from a .3dm file, choosing which layers to include.
# Import Layers From 3dm

A Rhino 8 C# script that lets you pick a `.3dm` file and import **only the layers you choose**, without importing everything and deleting the unwanted objects afterwards.

![Layer picker dialog](screenshot.png)

## Features

- Opens the `.3dm` in memory (RhinoCommon `File3dm`), so nothing is imported until you confirm.
- Checkbox tree of the file's layers, with the object count per layer. Ticking a parent also ticks its sublayers.
- Missing parent layers are created; layers that already exist (same full path) are reused.
- Block instances come with their definitions, including nested blocks.
- Materials, linetypes and groups are remapped into the current document.
- Optional scaling when the file's units differ from the current model.
- The whole import is a single undo step.

## Development note

This script was written with the help of an AI assistant (Claude, by Anthropic). I defined the requirements, ran and tested it in Rhino 8 on Windows 11, and iterated on the interface and behavior.

Use it at your own risk. Bug reports and suggestions are welcome through GitHub Issues.

## Requirements

- **Rhino 8** (uses the built-in C# Script Editor).
- Developed and tested on Windows 11. Not tested on Mac.

## Installation

1. Download `ImportLayersFrom3dm.cs` (from the [Releases](../../releases) page or the file list above).
2. Save it in a stable folder, for example `C:\RhinoScripts\`. Use a folder that won't move, because the button or alias points to this path.

## Usage

### Run it once

1. Type `ScriptEditor` in the Rhino command line.
2. In the editor, choose **File > Open** and select the `.cs` file.
3. Press **Run** (green triangle) or **F5**.

Or type `RunScript` and browse to the file.

### Make it a command (alias)

1. Go to **Tools > Options > Aliases**.
2. Add a new alias, e.g. `ImportLayers`.
3. Set the command macro to:

   ```
   ! _-ScriptEditor _R "C:\RhinoScripts\ImportLayersFrom3dm.cs"
   ```

4. Type `ImportLayers` in the command line to run it.

### Make it a toolbar button

1. Open **Tools > Toolbar Layout** (the exact menu location may vary between Rhino 8 builds).
2. Create or select a toolbar and add a new button.
3. Set the left-click command to the same macro:

   ```
   ! _-ScriptEditor _R "C:\RhinoScripts\ImportLayersFrom3dm.cs"
   ```

4. Optional: use `icon.svg` from this repo as the button image. If your build doesn't accept SVG, export a 24x24 or 32x32 PNG from it.

### Keyboard shortcut

**Tools > Options > Keyboard** lets you bind the same macro to a key combination.

## How it works

1. Select the `.3dm` file.
2. Tick the layers to import and click **Import**.
3. If the file's units differ from the current model, choose whether to scale the objects.
4. The selected objects are added to the open document.

## Known limitations

- Layers used only by objects inside a block definition are created even if you did not tick them, because the block needs them.
- Layer-level materials and linetypes are not copied, only object-level ones.
- Dimension styles, named views, lights and render settings are not imported.
- A ticked parent layer imports only its own objects; tick the sublayers too to import their objects.
- Always try it on a copy of your model first.

## License

[MIT](LICENSE) © [Marco Traverso] [2026]
