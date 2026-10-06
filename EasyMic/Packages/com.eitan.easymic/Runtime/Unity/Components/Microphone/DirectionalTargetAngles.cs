using System;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono
{
    /// <summary>Shared steering editing rules for array layout UIs; native azimuths remain full-circle.</summary>
    public static class DirectionalTargetAngles
    {
        public static bool TryGetLinearAxis(DirectionalGeometry geometry, int count, out float axis)
        {
            axis = 0f;
            if (geometry == null) return false;
            if (geometry.Preset == DirectionalGeometryPreset.Linear)
            {
                axis = geometry.RotationDegrees;
                return true;
            }
            if (!geometry.TryGetPositions(count, out var positions, out _, out _)) return false;
            return TryGetLinearAxis(geometry.Preset, geometry.RotationDegrees, positions, out axis);
        }

        public static bool TryGetLinearAxis(DirectionalGeometryPreset preset, float rotation, Vector2[] positions, out float axis)
        {
            axis = 0f;
            if (preset == DirectionalGeometryPreset.Linear)
            {
                // Use the configured physical orientation, independent of PCM channel reversal.
                if (float.IsNaN(rotation) || float.IsInfinity(rotation)) return false;
                axis = Mathf.Repeat(rotation, 360f);
                return true;
            }
            if (!DirectionalGeometry.TryGetMirroredAzimuth(positions, 0f, out _)) return false;
            axis = Mathf.Repeat(DirectionalGeometry.AzimuthFromVector(positions[1] - positions[0]), 180f);
            return true;
        }

        public static float FoldLinearAngle(float angle, float axis)
        {
            float relative = Mathf.Repeat(angle - axis, 360f);
            return Mathf.Repeat(axis + (relative > 180f ? 360f - relative : relative), 360f);
        }

        public static float[] GetCommonAngles(DirectionalGeometry geometry, int count)
        {
            if (geometry == null || !geometry.TryGetPositions(count, out var positions, out _, out _))
                return Array.Empty<float>();
            return GetCommonAngles(geometry.Preset, geometry.RotationDegrees, positions);
        }

        public static float[] GetCommonAngles(DirectionalGeometryPreset preset, float rotation, Vector2[] positions)
        {
            if (!DirectionalGeometry.ValidatePositions(positions, out _)) return Array.Empty<float>();
            float offset = preset == DirectionalGeometryPreset.Custom ? 0f : rotation;
            float[] angles;
            if (TryGetLinearAxis(preset, rotation, positions, out float axis))
            {
                offset = axis;
                angles = new[] {45f, 90f, 135f};
            }
            else if (preset == DirectionalGeometryPreset.Circular)
                angles = new[] {0f, 60f, 120f, 180f, 240f, 300f};
            else if (preset == DirectionalGeometryPreset.Rectangular)
                angles = new[] {0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f};
            else
                angles = new[] {0f, 90f, 180f, 270f};
            for (int i = 0; i < angles.Length; i++) angles[i] = Mathf.Repeat(angles[i] + offset, 360f);
            return angles;
        }
    }
}
