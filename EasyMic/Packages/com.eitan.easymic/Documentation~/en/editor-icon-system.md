# EasyMic Editor Icon System

EasyMic Editor icons live in `Eitan.EasyMic.Editor.Icons`. The system is Editor-only and exists to give EasyMic components, inspectors, diagnostics, and future documentation tools a consistent Unity-native icon language without shipping third-party image assets.

## Why procedural icons

Icons are generated in code instead of imported from PNG/SVG files. This keeps the package small, avoids external asset licensing, and lets the Editor create exact 16, 32, and 64 pixel variants instead of scaling one low-resolution texture everywhere.

## Requesting icons

Use the semantic facade:

```csharp
using Eitan.EasyMic.Editor.Icons;

Texture2D icon = EasyMicIcons.Get(EasyMicIconId.AudioInput, EasyMicIcons.Small);
GUIContent label = EasyMicIcons.LabeledContent(EasyMicIconId.Diagnostics, "Diagnostics");
```

Do not call `EditorGUIUtility.IconContent` directly from EasyMic UI. If a Unity built-in icon is useful for native consistency, request it through `EasyMicIcons.BuiltInContent(...)` so the fallback remains controlled.

## Adding a semantic icon

1. Add a value to `EasyMicIconId`.
2. Add a drawing case in `EasyMicProceduralIconRenderer`.
3. Keep the drawing simple enough to read at 16x16.
4. If it represents a component script, add it to `EasyMicComponentIconMap`.

Prefer functional symbols tied to real EasyMic UI or components. Avoid decorative icons that are not used by the package.

## Caching

`EasyMicIconCache` lazily creates textures by icon ID, size, Editor theme, and visual state. Textures are marked `HideAndDontSave`, reused across GUI calls, and discarded on domain reload. Call `EasyMicIcons.Invalidate()` only when changing icon style or theme assumptions during Editor tooling work.

## Component icons

`EasyMicComponentIconInstaller` maps these real component scripts:

- `EasyMicrophone`
- `VoiceMicrophone` when the Sherpa integration assembly is present
- `PlaybackAudioSourceBehaviour`
- `SpeechSynthesizer` when the Sherpa integration assembly is present

The installer uses `MonoImporter.SetIcon` for persistent MonoScript icons and `EditorGUIUtility.SetIconForObject` for temporary GameObject/component icons created from EasyMic menu items.

Refresh icons with `EasyMicComponentIconInstaller.RefreshComponentIcons()` from Editor code. The refresh is explicit, idempotent, and does not run automatically on import.

## Validation checklist

Validate in both Unity light and dark themes:

- Inspector component header and custom inspector controls.
- Project window MonoScript assets after refreshing component icons.
- Hierarchy and Scene view for EasyMic GameObjects created from menu items.
- Gizmos with 16, 32, and 64 pixel variants.
- Diagnostics window title and row action icons.

Icons should remain shape-readable without relying only on color. Warning, success, and error use both color and distinct geometry.

## Unity compatibility

The code targets Unity 2021.3+ Editor APIs: `EditorGUIUtility.IconContent`, `EditorGUIUtility.SetIconForObject`, and `MonoImporter.SetIcon`. Built-in Unity icon names are treated as optional helpers. If a built-in icon is unavailable in a Unity version, EasyMic falls back to a procedural semantic icon.

Runtime assemblies do not reference this system and do not reference `UnityEditor`.
