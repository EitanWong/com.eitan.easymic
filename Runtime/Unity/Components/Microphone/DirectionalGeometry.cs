using System;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono
{
    public enum MicrophoneDirectionalMode { Adaptive = 0, Fixed = 1 }

    public enum DirectionalGeometryPreset
    {
        // Explicit values preserve existing serialized scene and prefab geometries.
        Linear = 1,
        Circular = 0,
        Custom = 2,
        Rectangular = 3
    }

    public enum DirectionalHardwareProfile
    {
        Generic,
        [InspectorName("Seeed ReSpeaker 4-Mic Linear Array Kit")]
        ReSpeaker4MicLinearArrayKit,
        [InspectorName("Seeed ReSpeaker 6-Mic Circular Array Kit")]
        ReSpeaker6MicCircularArrayKit,
        [InspectorName("Seeed ReSpeaker 4-Mic Array for Raspberry Pi")]
        ReSpeaker4MicArrayForRaspberryPi,
        [InspectorName("Seeed ReSpeaker USB Mic Array v2.0")]
        ReSpeakerMicArrayV2,
        [InspectorName("Seeed ReSpeaker XVF3800 USB 4-Mic Array")]
        ReSpeakerXvf3800Usb4MicArray,
        Custom,
        [InspectorName("Seeed ReSpeaker USB Mic Array v3.0")]
        ReSpeakerMicArrayV3
    }

    /// <summary>
    /// Physical microphone locations passed to the native track DOA estimator. Positions use
    /// the array's local X/Y plane in metres, with microphone zero as the timing reference.
    /// </summary>
    [Serializable]
    public sealed class DirectionalGeometry
    {
        [SerializeField] private DirectionalHardwareProfile _hardwareProfile = DirectionalHardwareProfile.Generic;
        [SerializeField] private DirectionalGeometryPreset _preset = DirectionalGeometryPreset.Linear;
        [SerializeField, Min(0.005f)] private float _elementSpacingMeters = 0.05f;
        [SerializeField, Min(0.005f)] private float _radiusMeters = 0.04f;
        [SerializeField, Range(2, 16)] private int _previewChannelCount = 4;
        [SerializeField] private Vector2[] _customPositionsMeters = Array.Empty<Vector2>();

        [SerializeField, Range(2, 16)] private int _rows = 2;
        [SerializeField, Min(0.005f)] private float _rowSpacingMeters = 0.05f;
        [SerializeField] private float _rotationDegrees;
        [SerializeField] private bool _reverseChannelOrder;

        public int Rows => _rows;
        public float RowSpacingMeters => _rowSpacingMeters;
        public float RotationDegrees => _rotationDegrees;
        public bool ReverseChannelOrder => _reverseChannelOrder;

        public void ConfigureOrientation(float rotationDegrees, bool reverseChannelOrder)
        {
            if (!IsFinite(rotationDegrees)) throw new ArgumentOutOfRangeException(nameof(rotationDegrees));
            _rotationDegrees = Mathf.Repeat(rotationDegrees, 360f);
            _reverseChannelOrder = reverseChannelOrder;
        }

        public void ConfigureRectangular(int rows, float columnSpacingMeters, float rowSpacingMeters)
        {
            if (rows < 2 || rows > 16) throw new ArgumentOutOfRangeException(nameof(rows));
            CheckSize(columnSpacingMeters); CheckSize(rowSpacingMeters);
            _preset = DirectionalGeometryPreset.Rectangular;
            _hardwareProfile = DirectionalHardwareProfile.Generic;
            _rows = rows; _elementSpacingMeters = columnSpacingMeters; _rowSpacingMeters = rowSpacingMeters;
        }

        public void ConfigureCustom(Vector2[] positions)
        {
            if (positions == null || positions.Length < 2 || positions.Length > 16)
                throw new ArgumentException("Provide 2-16 positions in PCM channel order.", nameof(positions));
            if (!ValidatePositions(positions, out string error)) throw new ArgumentException(error, nameof(positions));
            _customPositionsMeters = (Vector2[])positions.Clone();
            _preset = DirectionalGeometryPreset.Custom;
            _hardwareProfile = DirectionalHardwareProfile.Custom;
            // Custom coordinates are already in the hardware axes; do not transform twice.
            _rotationDegrees = 0f; _reverseChannelOrder = false;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void CheckSize(float value)
        {
            if (!IsFinite(value) || value < .005f || value > .3f)
                throw new ArgumentOutOfRangeException(nameof(value), "Spacing must be 0.5-30 cm.");
        }

        // Both configuration UIs and capture use this generator, including the same channel order.
        public static Vector2[] GeneratePositions(DirectionalGeometryPreset preset, int count, float spacing,
            float radius, int rows = 2, float rowSpacing = .05f, float rotationDegrees = 0f, bool reverse = false)
        {
            if (count < 2 || count > 16) throw new ArgumentOutOfRangeException(nameof(count));
            if (!IsFinite(rotationDegrees)) throw new ArgumentOutOfRangeException(nameof(rotationDegrees));
            if (preset == DirectionalGeometryPreset.Circular) CheckSize(radius); else CheckSize(spacing);
            if (preset == DirectionalGeometryPreset.Rectangular)
            {
                CheckSize(rowSpacing);
                if (rows < 2 || rows > count || count % rows != 0)
                    throw new ArgumentException("Rectangular rows must divide the capture channel count.", nameof(rows));
            }
            if (preset == DirectionalGeometryPreset.Custom) throw new ArgumentException("Custom arrays use measured coordinates.");
            var result = new Vector2[count];
            float radians = rotationDegrees * Mathf.Deg2Rad;
            for (int i = 0; i < count; i++)
            {
                Vector2 point;
                if (preset == DirectionalGeometryPreset.Linear)
                    point = new Vector2((i - (count - 1) * .5f) * spacing, 0f);
                else if (preset == DirectionalGeometryPreset.Rectangular)
                {
                    int columns = count / rows;
                    // Row-major: channel zero is bottom-left; advance +X, then +Y.
                    point = new Vector2((i % columns - (columns - 1) * .5f) * spacing,
                        (i / columns - (rows - 1) * .5f) * rowSpacing);
                }
                else if (preset == DirectionalGeometryPreset.Circular)
                {
                    float angle = -Mathf.PI * .5f + i * Mathf.PI * 2f / count;
                    point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                }
                else throw new ArgumentOutOfRangeException(nameof(preset));
                result[reverse ? count - 1 - i : i] = new Vector2(point.x * Mathf.Cos(radians) - point.y * Mathf.Sin(radians),
                    point.x * Mathf.Sin(radians) + point.y * Mathf.Cos(radians));
            }
            return result;
        }

        public DirectionalHardwareProfile HardwareProfile => _hardwareProfile;
        public DirectionalGeometryPreset Preset => _preset;
        public float ElementSpacingMeters => _elementSpacingMeters;
        public float RadiusMeters => _radiusMeters;
        public int PreviewChannelCount => _previewChannelCount;

        public int ExpectedMicrophoneCount
        {
            get
            {
                switch (_hardwareProfile)
                {
                    case DirectionalHardwareProfile.ReSpeaker4MicLinearArrayKit:
                    case DirectionalHardwareProfile.ReSpeaker4MicArrayForRaspberryPi:
                    case DirectionalHardwareProfile.ReSpeakerMicArrayV2:
                    case DirectionalHardwareProfile.ReSpeakerXvf3800Usb4MicArray:
                    case DirectionalHardwareProfile.ReSpeakerMicArrayV3:
                        return 4;
                    case DirectionalHardwareProfile.ReSpeaker6MicCircularArrayKit:
                        return 6;
                    default:
                        return 0;
                }
            }
        }

        public void ConfigureHardwareProfile(DirectionalHardwareProfile profile)
        {
            _rotationDegrees = 0f; _reverseChannelOrder = false;
            _hardwareProfile = profile;
            switch (profile)
            {
                case DirectionalHardwareProfile.ReSpeaker4MicLinearArrayKit:
                    _preset = DirectionalGeometryPreset.Linear;
                    _customPositionsMeters = Array.Empty<Vector2>();
                    break;
                case DirectionalHardwareProfile.ReSpeaker6MicCircularArrayKit:
                    _preset = DirectionalGeometryPreset.Circular;
                    _customPositionsMeters = Array.Empty<Vector2>();
                    break;
                case DirectionalHardwareProfile.ReSpeakerXvf3800Usb4MicArray:
                    _preset = DirectionalGeometryPreset.Custom;
                    _customPositionsMeters = GetXvf3800PositionsMeters();
                    break;
                case DirectionalHardwareProfile.ReSpeaker4MicArrayForRaspberryPi:
                case DirectionalHardwareProfile.ReSpeakerMicArrayV2:
                case DirectionalHardwareProfile.ReSpeakerMicArrayV3:
                case DirectionalHardwareProfile.Custom:
                    _preset = DirectionalGeometryPreset.Custom;
                    _customPositionsMeters = Array.Empty<Vector2>();
                    break;
            }
        }

        public static Vector2[] GetXvf3800PositionsMeters()
        {
            return new[]
            {
                new Vector2(0.033f, -0.033f),
                new Vector2(0.033f, 0.033f),
                new Vector2(-0.033f, 0.033f),
                new Vector2(-0.033f, -0.033f)
            };
        }

        public void ConfigurePreset(DirectionalGeometryPreset preset, float sizeMeters)
        {
            if ((preset != DirectionalGeometryPreset.Linear && preset != DirectionalGeometryPreset.Circular) || float.IsNaN(sizeMeters) ||
                float.IsInfinity(sizeMeters) || sizeMeters < 0.005f || sizeMeters > 0.3f)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeMeters));
            }
            _preset = preset;
            if (!(_hardwareProfile == DirectionalHardwareProfile.ReSpeaker4MicLinearArrayKit && preset == DirectionalGeometryPreset.Linear) &&
                !(_hardwareProfile == DirectionalHardwareProfile.ReSpeaker6MicCircularArrayKit && preset == DirectionalGeometryPreset.Circular))
                _hardwareProfile = DirectionalHardwareProfile.Generic;
            if (preset == DirectionalGeometryPreset.Linear) _elementSpacingMeters = sizeMeters;
            else _radiusMeters = sizeMeters;
        }

        public string GetDescription(int channelCount)
        {
            if (_preset == DirectionalGeometryPreset.Custom)
            {
                return _customPositionsMeters != null && _customPositionsMeters.Length == channelCount
                    ? "Custom geometry"
                    : "Custom geometry count does not match capture channels";
            }
            if (_preset == DirectionalGeometryPreset.Rectangular) return $"Rectangular grid ({_rows} rows, {_elementSpacingMeters * 100f:0.#} × {_rowSpacingMeters * 100f:0.#} cm spacing)";
            return _preset == DirectionalGeometryPreset.Circular
                ? $"Circular geometry ({_radiusMeters * 100f:0.#} cm radius)"
                : $"Linear geometry ({_elementSpacingMeters * 100f:0.#} cm spacing)";
        }

        public bool TryGetPositions(int channelCount, out Vector2[] positions, out bool isPlanar, out string error)
        {
            positions = Array.Empty<Vector2>();
            isPlanar = false;
            if (channelCount < 2 || channelCount > 16)
            {
                error = "Direction estimation requires the active 2-16 channel array format.";
                return false;
            }
            if (ExpectedMicrophoneCount > 0 && channelCount != ExpectedMicrophoneCount)
            {
                error = $"{_hardwareProfile} expects {ExpectedMicrophoneCount} microphone channels; the selected capture format has {channelCount}.";
                return false;
            }

            positions = new Vector2[channelCount];
            switch (_preset)
            {
                case DirectionalGeometryPreset.Linear:
                case DirectionalGeometryPreset.Circular:
                case DirectionalGeometryPreset.Rectangular:
                    try
                    {
                        positions = GeneratePositions(_preset, channelCount, _elementSpacingMeters, _radiusMeters,
                            _rows, _rowSpacingMeters, _rotationDegrees, _reverseChannelOrder);
                    }
                    catch (ArgumentException exception) { error = exception.Message; return false; }
                    isPlanar = IsNonCollinear(positions);
                    return ValidatePositions(positions, out error);

                case DirectionalGeometryPreset.Custom:
                    if (_customPositionsMeters == null || _customPositionsMeters.Length != channelCount)
                    {
                        error = "Custom geometry must provide one X/Y position for every active microphone channel.";
                        return false;
                    }
                    Array.Copy(_customPositionsMeters, positions, channelCount);
                    isPlanar = IsNonCollinear(positions);
                    return ValidatePositions(positions, out error);

                default:
                    error = "Unknown geometry preset.";
                    return false;
            }
        }

        public static Vector2 AzimuthVector(float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        public static float AzimuthFromVector(Vector2 direction) =>
            Mathf.Repeat(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, 360f);

        public static bool TryGetMirroredAzimuth(Vector2[] positions, float degrees, out float mirrored)
        {
            mirrored = degrees;
            if (!IsFinite(degrees) || !ValidatePositions(positions, out _) || IsNonCollinear(positions)) return false;
            Vector2 axis = positions[1] - positions[0];
            if (axis.sqrMagnitude < .000004f) return false;
            mirrored = Mathf.Repeat(2f * AzimuthFromVector(axis) - degrees, 360f);
            return true;
        }

        public static bool ValidatePositions(Vector2[] positions, out string error)
        {
            if (positions == null || positions.Length < 2 || positions.Length > 16)
            { error = "Provide 2-16 microphone positions in PCM channel order."; return false; }
            for (int i = 0; i < positions.Length; i++)
            {
                if (float.IsNaN(positions[i].x) || float.IsInfinity(positions[i].x) ||
                    float.IsNaN(positions[i].y) || float.IsInfinity(positions[i].y))
                {
                    error = $"PCM channel {i} requires finite X/Y coordinates in metres.";
                    return false;
                }
                for (int j = 0; j < i; j++)
                {
                    if ((positions[i] - positions[j]).sqrMagnitude > 1f)
                    {
                        error = "Native horizontal DOA supports microphone pair spacing up to 1 metre.";
                        return false;
                    }
                    if ((positions[i] - positions[j]).sqrMagnitude < 0.000004f)
                    {
                        error = $"PCM channels {j} and {i} must be at least 2 mm apart for this estimator.";
                        return false;
                    }
                }
            }
            error = string.Empty;
            return true;
        }

        private static bool IsNonCollinear(Vector2[] positions)
        {
            if (positions.Length < 3)
            {
                return false;
            }
            Vector2 origin = positions[0];
            for (int i = 1; i < positions.Length; i++)
            {
                Vector2 first = positions[i] - origin;
                for (int j = i + 1; j < positions.Length; j++)
                {
                    Vector2 second = positions[j] - origin;
                    if (Mathf.Abs(first.x * second.y - first.y * second.x) > 0.000001f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    [Serializable]
    public sealed class DirectionalArrayDeviceProfile
    {
        [SerializeField] private string _deviceNameContains;
        [SerializeField] private DirectionalGeometry _geometry = new DirectionalGeometry();

        public string DeviceNameContains => _deviceNameContains;
        public DirectionalGeometry Geometry => _geometry;
    }

    public readonly struct DirectionalDirectionEstimate
    {
        public DirectionalDirectionEstimate(
            bool valid,
            bool fullAzimuth,
            float azimuthDegrees,
            float confidence,
            int channelCount,
            long updatedTicks,
            float alternativeAzimuthDegrees = 0f, ulong trackId = 0, uint generation = 0)
        {
            IsValid = valid;
            HasFullAzimuth = fullAzimuth;
            AzimuthDegrees = azimuthDegrees;
            Confidence = confidence;
            ChannelCount = channelCount;
            UpdatedTicks = updatedTicks;
            AlternativeAzimuthDegrees = alternativeAzimuthDegrees;
            TrackId = trackId; Generation = generation;
        }

        public ulong TrackId { get; }
        public uint Generation { get; }
        public bool IsValid { get; }
        public bool HasFullAzimuth { get; }
        public float AzimuthDegrees { get; }
        public float AlternativeAzimuthDegrees { get; }
        public float Confidence { get; }
        public int ChannelCount { get; }
        public long UpdatedTicks { get; }
    }

}
