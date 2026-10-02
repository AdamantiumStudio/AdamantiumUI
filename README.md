# Adamantium UI

A .NET UI framework on its own Vulkan renderer, where a 3D scene is an ordinary element of the markup: drawn by the
same renderer, in the same process, under the same popups, clips and transforms as the interface around it.

> **Alpha.** Windows only for now, and the API will change between releases.

![An engine scene in a RenderTargetPanel: the tool panel on the left, the move gizmo on a selected light, and the light's properties on the right](https://raw.githubusercontent.com/AdamantiumStudio/AdamantiumUI/main/.github/images/hero.png)

## What it is for

Applications where a 3D view and the interface around it are one thing: CAD and engineering tools, scientific
visualization, simulators, control rooms, industrial panels, level editors.

In WPF and Avalonia the 3D renderer is the application's own, and so is the work of wiring it in: a `D3DImage` or a
child window (`HwndHost`) in WPF, GPU interop or a native host (`NativeControlHost`) in Avalonia. In Adamantium UI the
scene comes from the engine the interface is drawn with, and its panel is an element like any other:

- **The scene is an element of the tree.** `RenderTargetPanel` hosts a universe of the
  [Adamantium Engine](https://github.com/AdamantiumStudio/AdamantiumEngine)'s entity-component system. Menus, tooltips
  and popups draw over it; opacity, transforms and rounded clips apply to it.
- **No copy of the frame.** The scene renders into a surface it exports, and the panel imports it by an OS handle
  through a `SharedSurfaceDescriptor`.
- **Input follows focus.** The hosted scene gets keyboard and mouse only while its panel has focus, with mouse-look
  and cursor-capture modes for camera control.
- **One renderer for everything,** so the interface gets what a game renderer has: acrylic, glass and mica materials
  over a real blurred backdrop, shadows and auras, mesh gradients, noise and fractal brushes, nine-slice images,
  analytic antialiasing, and its own font stack with MSDF glyph atlases.

| | Adamantium UI | WPF | Avalonia |
|---|---|---|---|
| A 3D scene in the window | an engine scene in a `RenderTargetPanel` | your own renderer, through `D3DImage` or `HwndHost` | your own renderer, through GPU interop or `NativeControlHost` |
| Renderer | its own, on Vulkan | Direct3D 9 | Skia |
| Docking, ribbon, tab tear-off | included | third-party suites | third-party libraries |
| Platforms | Windows | Windows | Windows, macOS, Linux, iOS, Android, browser |

## Getting started

Requirements:

- **.NET 10 SDK.**
- **Windows 10 or 11, x64.**
- **A GPU and driver with Vulkan 1.4** and the extensions listed in the
  [engine's requirements](https://github.com/AdamantiumStudio/AdamantiumEngine#requirements). Developed and tested on an
  NVIDIA Quadro RTX 4000; other GPUs have not been tested.

```
dotnet new install Adamantium.UI.Templates
dotnet new adamantium-app -n MyApp --theme Fluent
cd MyApp
dotnet run
```

| Template | Creates |
|---|---|
| `adamantium-app` | An application: a window bound to its view-model. `--theme` picks `Fluent`, `EditorPro` or `macOS`. |
| `adamantium-viewlib` | A library of views, each with its view-model, for an application to place or navigate to. |
| `adamantium-controllib` | A library of templated controls that bring their look into every theme. |

**Rider.** The *Adamantium AUML* plugin adds a live preview beside the markup, drawn by the framework itself from the
project's own build and in its own theme, plus completion, diagnostics and go to definition.
<!-- TODO: link the plugin's JetBrains Marketplace page once it is published. -->

## A window in AUML

AUML is XML in the shape of XAML, compiled to C# by a source generator at build time - no parsing at startup, and a
typo in the markup is a build error.

```xml
<Window xmlns="http://adamantium/ui"
        xmlns:x="http://adamantium/ui/xaml/extensions"
        xmlns:local="clr-namespace:MyApp"
        xmlns:vm="clr-namespace:MyApp.ViewModels"
        x:ViewModel="{x:Type vm:MainWindowViewModel}"
        Title="Viewer"
        Width="1280"
        Height="720">
    <Grid ColumnDefinitions="*, 280">
        <RenderTargetPanel MouseLookMode="Drag">
            <RenderTargetPanel.Behaviors>
                <local:SceneBehavior/>
            </RenderTargetPanel.Behaviors>
        </RenderTargetPanel>
        <StackPanel Grid.Column="1"
                    Margin="16">
            <TextBlock Text="Camera speed"/>
            <Slider Value="{Binding CameraSpeed, Mode=TwoWay}"
                    Minimum="0.1"
                    Maximum="10"/>
            <Button Content="Reset camera"
                    Command="{Binding ResetCameraCommand}"/>
        </StackPanel>
    </Grid>
</Window>
```

`SceneBehavior` is the application's own: it creates the engine universe that draws into the panel. The sandbox's
[`DemoUniverseBehavior`](https://github.com/AdamantiumStudio/AdamantiumUI/blob/main/Adamantium.UI.Sandbox/Behaviors/DemoUniverseBehavior.cs)
is a complete one.

The view-model is plain C# with source-generated properties and commands:

```csharp
[ViewModel]
public partial class MainWindowViewModel
{
    [Bindable]
    private double _cameraSpeed = 4;

    [Command]
    private void ResetCamera() => CameraSpeed = 4;
}
```

The same data grid in the three themes:

| Fluent | Editor Pro | macOS |
|---|---|---|
| ![The data grid in Fluent](https://raw.githubusercontent.com/AdamantiumStudio/AdamantiumUI/main/.github/images/theme-fluent.png) | ![The data grid in Editor Pro](https://raw.githubusercontent.com/AdamantiumStudio/AdamantiumUI/main/.github/images/theme-editor-pro.png) | ![The data grid in macOS](https://raw.githubusercontent.com/AdamantiumStudio/AdamantiumUI/main/.github/images/theme-macos.png) |

## What is in the box

- **Controls.** Buttons, toggles, check boxes, radio buttons, text input, numeric input, drop-downs, sliders, lists,
  virtualized trees, a data grid (tree rows, frozen columns, filters, a column chooser, undo, CSV and XLSX export), a
  property grid, tabs (pinning, tear-off, overflow, virtualization), docking (a compass, saved layouts), a ribbon (key
  tips, galleries), menus, a color picker with a color wheel, a data pager, an infinite canvas with a node graph, a
  zoom box, slide panels, busy indicators.
- **Markup and data.** Styles with selectors by type, class and property conditions; property, data and multi
  triggers; bindings, multi-bindings and converters, bindings to an ancestor, to itself and to a template;
  `x:Load` for content built only when needed.
- **Themes.** Fluent, Editor Pro and macOS, each with light and dark variants, switched live. A library brings its
  controls' look into every theme with one call.
- **MVVM.** `[ViewModel]`, `[Bindable]` and `[Command]` generate the boilerplate; dependency injection, navigation
  into regions, dialogs and window shells driven from view-models.
- **Tooling.** `dotnet new` templates, the Rider plugin with the live preview, and a Visual Studio Code extension with
  completion and diagnostics.

## Status

Alpha: what is listed above works, and the API will change. Not there yet:

- accessibility (UI Automation);
- localization with right-to-left layout;
- touch and pen input;
- Linux, mobile and the browser. The macOS code exists but has not been built and run recently, so it is not claimed.

## Packages

| Package | What it is |
|---|---|
| `Adamantium.UI` | The one to reference: the controls, the themes, the AUML code generator and the designer host, with MVVM |
| `Adamantium.UI.Core` | The element tree, layout, input, styles, bindings, resources and the AUML runtime loader |
| `Adamantium.UI.Controls` | The controls and panels |
| `Adamantium.UI.Themes` | Fluent, Editor Pro and macOS |
| `Adamantium.UI.FX` | The UI's shaders |
| `Adamantium.UI.Markup` | The AUML parser and document model |
| `Adamantium.Navigation` | Navigation, regions and dialogs driven from view-models |
| `Adamantium.UI.Templates` | The `dotnet new` templates |

All packages share one version and are released together.

## License

[Apache-2.0](https://github.com/AdamantiumStudio/AdamantiumUI/blob/main/LICENSE). Third-party components are listed
in [THIRD-PARTY-NOTICES.md](https://github.com/AdamantiumStudio/AdamantiumUI/blob/main/THIRD-PARTY-NOTICES.md).
