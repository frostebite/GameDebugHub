# GameDebugHub

An extensible debug window framework for the Unity Editor that auto-discovers debug tabs from any assembly in the project.

## Overview

GameDebugHub provides a single editor window — **Tools > Game Debug Hub** — that collects and displays debug tabs contributed by any Editor assembly. Tabs are discovered at domain reload using Unity's `TypeCache`, so any submodule or shared code package can add tabs without registering them centrally. The core package contains no project-specific strings and is designed to drop into any Unity project unchanged.

## Features

- **Attribute-based tab discovery** — apply `[DebugHubTab]` to a class and it appears in the hub automatically; no manual registration step required
- **Cross-assembly discovery** — tabs can be defined in any Editor assembly that references `GameDebugHub`
- **Framework grouping** — tab source is inferred from the `Submodules/` folder path, namespace, or assembly name; shown in the debug info overlay
- **Tab ordering** — sort tabs with the `Order` parameter (lower values first); tabs with equal order sort by framework, module, then name
- **Conditional visibility** — `ShouldShow()` hides tabs that are irrelevant to the current context (for example, tabs that require play mode or a specific scene object)
- **Lifecycle hooks** — `OnTabSelected()`, `OnTabDeselected()`, `RequiresUpdate()`, and `OnUpdate()` let tabs respond to selection changes and request continuous repaints
- **Searchable entity lists** — `DebugHubTabBase.DrawEntityList<T>()` provides a reusable foldout list with search, Focus, Select, and Properties buttons
- **Scene view focusing** — `FocusSceneViewOnEntity()` and `FocusSceneViewOnPosition()` helper methods for spatial debugging
- **Error isolation** — an exception in one tab is caught and displayed inline; other tabs continue working
- **Debug info overlay** — toggle "Debug Info" in the window header to see assembly, framework, module, and order metadata for the active tab

## Installation

**Unity Package Manager (recommended)**

```json
{
  "dependencies": {
    "com.frostebite.gamedebugehub": "https://github.com/frostebite/GameDebugHub.git"
  }
}
```

**Git submodule**

```sh
git submodule add https://github.com/frostebite/GameDebugHub.git Assets/_Engine/Submodules/GameDebugHub
```

## Requirements

- Unity 2021.3 or later
- No external dependencies

## Quick Start

Create a class in any Editor assembly, apply `[DebugHubTab]`, and extend `DebugHubTabBase`:

```csharp
using UnityEditor;
using UnityEngine;

[DebugHubTab("My Tab", Order = 10)]
public class MyDebugTab : DebugHubTabBase
{
    public override string TabName => "My Tab";

    public override void OnGUI()
    {
        EditorGUILayout.LabelField("Hello from My Tab", EditorStyles.boldLabel);

        if (GUILayout.Button("Do Something"))
        {
            Debug.Log("My Tab button clicked");
        }
    }
}
```

Your assembly definition must reference `GameDebugHub` so the attribute and base class are available. The tab appears in **Tools > Game Debug Hub** after the next recompile.

Alternatively, implement `IDebugHubTab` directly if you do not want the base class utilities:

```csharp
[DebugHubTab("Minimal Tab")]
public class MinimalTab : IDebugHubTab
{
    public string TabName => "Minimal Tab";

    public void OnGUI()
    {
        EditorGUILayout.LabelField("Minimal tab content");
    }
}
```

## Tab Lifecycle

| Method | When called |
|--------|-------------|
| `OnGUI()` | Every repaint while the tab is selected |
| `ShouldShow()` | Before each repaint; return `false` to hide the tab |
| `OnTabSelected()` | When the user switches to this tab |
| `OnTabDeselected()` | When the user switches away from this tab |
| `RequiresUpdate()` | Each editor update tick; return `true` to request a repaint |
| `OnUpdate()` | Each editor update tick, only if `RequiresUpdate()` returns `true` |

## Conditional Display

Use `ShouldShow()` to hide tabs that are not relevant in the current context:

```csharp
public override bool ShouldShow()
{
    // Only visible during play mode
    return Application.isPlaying;
}
```

```csharp
public override bool ShouldShow()
{
    // Only visible when a specific component exists in the scene
    return Object.FindObjectOfType<MyGameManager>() != null;
}
```

## Continuous Updates

For tabs that display live data, return `true` from `RequiresUpdate()` and update state in `OnUpdate()`:

```csharp
[DebugHubTab("Live Stats", Order = 5)]
public class LiveStatsTab : DebugHubTabBase
{
    private float _fps;

    public override string TabName => "Live Stats";

    public override bool RequiresUpdate() => true;

    public override void OnUpdate()
    {
        if (Time.deltaTime > 0f)
            _fps = 1f / Time.deltaTime;
    }

    public override void OnGUI()
    {
        EditorGUILayout.LabelField($"FPS: {_fps:F1}");
    }
}
```

## Troubleshooting

**Tab does not appear**

1. Confirm the class has `[DebugHubTab]`.
2. Confirm the class implements `IDebugHubTab` or extends `DebugHubTabBase` and is not abstract.
3. Confirm `ShouldShow()` returns `true` in the current context.
4. Confirm your assembly definition references `GameDebugHub`.
5. Enable "Debug Info" in the window to see how many tabs were loaded.
6. Click "Reload Tabs" to force re-discovery without a full recompile.

**Tab throws an exception**

The exception is caught and displayed inside the tab content area. Other tabs are unaffected. Enable "Debug Info" to see the full stack trace.

## License

See LICENSE file.
