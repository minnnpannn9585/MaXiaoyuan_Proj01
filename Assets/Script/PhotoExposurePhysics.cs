using UnityEngine;

public enum PhotoNoiseReductionMode
{
    Disabled,
    Enabled
}

public enum PhotoExposureMode
{
    ProgramAuto,
    AperturePriority,
    ShutterPriority,
    Manual
}

public static class PhotoExposurePhysics
{
    private const float ReferenceIso = 100f;
    private const float BaseNoiseStandardDeviation = 0.0035f;
    private const float ReferenceSignalLevel = 0.18f;
    private static readonly float[] StandardApertures =
    {
        1f, 1.1f, 1.2f, 1.4f, 1.6f, 1.8f, 2f, 2.2f, 2.5f,
        2.8f, 3.2f, 3.5f, 4f, 4.5f, 5f, 5.6f, 6.3f, 7.1f,
        8f, 9f, 10f, 11f, 13f, 14f, 16f, 18f, 20f, 22f
    };
    private static readonly float[] StandardIsoValues =
    {
        100f, 125f, 160f, 200f, 250f, 320f, 400f, 500f,
        640f, 800f, 1000f, 1250f, 1600f, 2000f, 2500f,
        3200f, 4000f, 5000f, 6400f, 8000f, 10000f, 12800f
    };

    public struct Solution
    {
        public float ExposureSeconds;
        public float SensorExposure;
        public float OutputMultiplier;
        public float ExposureValue100;
        public float EffectiveExposureValue;
        public float RelativeStops;
        public float NoiseStandardDeviation;
        public float SignalToNoiseRatioDecibels;
        public float DenoisedMtf50Retention;
    }

    public struct CameraLimits
    {
        public float MaximumAperture;
        public float MinimumAperture;
        public float SlowestShutterSpeed;
        public float FastestShutterSpeed;
        public float MinimumAutoIsoShutterSpeed;
        public float MinimumIso;
        public float MaximumIso;
    }

    public struct CameraSettings
    {
        public float ShutterSpeed;
        public float Aperture;
        public float Iso;
    }

    public static CameraSettings ResolveCameraSettings(
        PhotoExposureMode mode,
        bool autoIso,
        CameraSettings manualSettings,
        CameraLimits limits,
        float meteredSceneExposureValue100,
        float exposureCompensation,
        float focalLengthMillimeters)
    {
        float maximumAperture = Mathf.Max(0.7f, limits.MaximumAperture);
        float minimumAperture = Mathf.Max(
            maximumAperture,
            limits.MinimumAperture);
        float slowestShutter = Mathf.Max(1f, limits.SlowestShutterSpeed);
        float fastestShutter = Mathf.Max(
            slowestShutter,
            limits.FastestShutterSpeed);
        float minimumAutoIsoShutter = Mathf.Clamp(
            limits.MinimumAutoIsoShutterSpeed,
            slowestShutter,
            fastestShutter);
        float minimumIso = Mathf.Max(25f, limits.MinimumIso);
        float maximumIso = Mathf.Max(minimumIso, limits.MaximumIso);

        CameraSettings result = new CameraSettings
        {
            ShutterSpeed = Mathf.Clamp(
                manualSettings.ShutterSpeed,
                slowestShutter,
                fastestShutter),
            Aperture = QuantizeStandardValue(
                Mathf.Clamp(
                manualSettings.Aperture,
                maximumAperture,
                minimumAperture),
                StandardApertures,
                maximumAperture,
                minimumAperture),
            Iso = QuantizeStandardValue(
                Mathf.Clamp(
                manualSettings.Iso,
                minimumIso,
                maximumIso),
                StandardIsoValues,
                minimumIso,
                maximumIso)
        };
        bool hasAutomaticParameter =
            mode != PhotoExposureMode.Manual || autoIso;
        if (!hasAutomaticParameter)
        {
            return result;
        }

        float targetEffectiveExposureValue =
            meteredSceneExposureValue100 - exposureCompensation;
        switch (mode)
        {
            case PhotoExposureMode.AperturePriority:
                ResolveAperturePriority(
                    ref result,
                    autoIso,
                    targetEffectiveExposureValue,
                    slowestShutter,
                    fastestShutter,
                    minimumAutoIsoShutter,
                    minimumIso,
                    maximumIso);
                break;

            case PhotoExposureMode.ShutterPriority:
                ResolveShutterPriority(
                    ref result,
                    autoIso,
                    targetEffectiveExposureValue,
                    maximumAperture,
                    minimumAperture,
                    minimumIso,
                    maximumIso);
                break;

            case PhotoExposureMode.ProgramAuto:
                ResolveProgramAuto(
                    ref result,
                    autoIso,
                    targetEffectiveExposureValue,
                    maximumAperture,
                    minimumAperture,
                    slowestShutter,
                    fastestShutter,
                    minimumAutoIsoShutter,
                    minimumIso,
                    maximumIso,
                    focalLengthMillimeters);
                break;

            case PhotoExposureMode.Manual:
                result.Iso = Mathf.Clamp(
                    CalculateRequiredIso(
                        result.ShutterSpeed,
                        result.Aperture,
                        targetEffectiveExposureValue),
                    minimumIso,
                    maximumIso);
                break;
        }

        result.Aperture = QuantizeStandardValue(
            result.Aperture,
            StandardApertures,
            maximumAperture,
            minimumAperture);
        result.Iso = QuantizeStandardValue(
            result.Iso,
            StandardIsoValues,
            minimumIso,
            maximumIso);
        return result;
    }

    public static float SnapAperture(float aperture)
    {
        return QuantizeStandardValue(
            aperture,
            StandardApertures,
            StandardApertures[0],
            StandardApertures[StandardApertures.Length - 1]);
    }

    public static float SnapIso(float iso)
    {
        return QuantizeStandardValue(
            iso,
            StandardIsoValues,
            StandardIsoValues[0],
            StandardIsoValues[StandardIsoValues.Length - 1]);
    }

    public static Solution Solve(
        float shutterSpeed,
        float aperture,
        float iso,
        float calibratedSceneExposureValue100,
        float meteredSceneExposureValue100)
    {
        float safeShutterSpeed = Mathf.Max(1f, shutterSpeed);
        float safeAperture = Mathf.Max(0.7f, aperture);
        float safeIso = Mathf.Max(25f, iso);
        float exposureSeconds = 1f / safeShutterSpeed;

        float exposureValue100 = Mathf.Log(
            safeAperture * safeAperture * safeShutterSpeed,
            2f);
        float isoStops = Mathf.Log(safeIso / ReferenceIso, 2f);
        float effectiveExposureValue = exposureValue100 - isoStops;
        float relativeStops =
            meteredSceneExposureValue100 - effectiveExposureValue;
        float outputMultiplier = Mathf.Pow(
            2f,
            calibratedSceneExposureValue100 -
                effectiveExposureValue);

        float sensorExposure = Mathf.Pow(
            2f,
            meteredSceneExposureValue100 - exposureValue100);
        float isoGain = safeIso / ReferenceIso;
        float noiseStandardDeviation =
            BaseNoiseStandardDeviation *
            Mathf.Sqrt(Mathf.Max(0.0001f, sensorExposure)) *
            isoGain;
        float sceneLuminanceScale = Mathf.Pow(
            2f,
            meteredSceneExposureValue100 -
                calibratedSceneExposureValue100);
        float outputSignal =
            ReferenceSignalLevel *
            sceneLuminanceScale *
            outputMultiplier;
        float signalToNoiseRatio =
            outputSignal /
            Mathf.Max(0.000001f, noiseStandardDeviation);
        float signalToNoiseRatioDecibels =
            20f * Mathf.Log10(signalToNoiseRatio);
        float denoisedMtf50Retention = Mathf.Clamp01(
            0.4f + 0.02f * signalToNoiseRatioDecibels);

        return new Solution
        {
            ExposureSeconds = exposureSeconds,
            SensorExposure = sensorExposure,
            OutputMultiplier = outputMultiplier,
            ExposureValue100 = exposureValue100,
            EffectiveExposureValue = effectiveExposureValue,
            RelativeStops = relativeStops,
            NoiseStandardDeviation = noiseStandardDeviation,
            SignalToNoiseRatioDecibels = signalToNoiseRatioDecibels,
            DenoisedMtf50Retention = denoisedMtf50Retention
        };
    }

    public static void ApplyToPixels(
        Color32[] pixels,
        Solution exposure,
        int noiseSeed,
        int width,
        int height,
        PhotoNoiseReductionMode noiseReductionMode)
    {
        uint randomState = (uint)Mathf.Max(1, noiseSeed);
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 source = pixels[i];
            float red = Mathf.GammaToLinearSpace(source.r / 255f);
            float green = Mathf.GammaToLinearSpace(source.g / 255f);
            float blue = Mathf.GammaToLinearSpace(source.b / 255f);

            float luminance =
                red * 0.2126f +
                green * 0.7152f +
                blue * 0.0722f;
            float shotNoiseScale =
                exposure.NoiseStandardDeviation *
                Mathf.Sqrt(Mathf.Max(0.02f, luminance));
            float commonNoise =
                NextSignedNoise(ref randomState) *
                shotNoiseScale;
            float colorNoiseScale =
                exposure.NoiseStandardDeviation * 0.35f;

            red =
                red * exposure.OutputMultiplier +
                commonNoise +
                NextSignedNoise(ref randomState) * colorNoiseScale;
            green =
                green * exposure.OutputMultiplier +
                commonNoise +
                NextSignedNoise(ref randomState) * colorNoiseScale;
            blue =
                blue * exposure.OutputMultiplier +
                commonNoise +
                NextSignedNoise(ref randomState) * colorNoiseScale;

            pixels[i] = new Color32(
                ToGammaByte(red),
                ToGammaByte(green),
                ToGammaByte(blue),
                byte.MaxValue);
        }

        if (noiseReductionMode == PhotoNoiseReductionMode.Enabled)
        {
            ApplyDenoise(
                pixels,
                width,
                height,
                exposure.DenoisedMtf50Retention);
        }
    }

    private static void ApplyDenoise(
        Color32[] pixels,
        int width,
        int height,
        float mtf50Retention)
    {
        float strength = Mathf.Clamp01(
            (1f - mtf50Retention) * 2.5f);
        if (strength <= 0.001f ||
            width < 3 ||
            height < 3)
        {
            return;
        }

        Color32[] source = (Color32[])pixels.Clone();
        for (int y = 1; y < height - 1; y++)
        {
            int row = y * width;
            for (int x = 1; x < width - 1; x++)
            {
                int centerIndex = row + x;
                int red = 0;
                int green = 0;
                int blue = 0;
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    int sampleRow = centerIndex + offsetY * width;
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        Color32 sample = source[sampleRow + offsetX];
                        red += sample.r;
                        green += sample.g;
                        blue += sample.b;
                    }
                }

                Color32 center = source[centerIndex];
                pixels[centerIndex] = new Color32(
                    (byte)Mathf.RoundToInt(
                        Mathf.Lerp(center.r, red / 9f, strength)),
                    (byte)Mathf.RoundToInt(
                        Mathf.Lerp(center.g, green / 9f, strength)),
                    (byte)Mathf.RoundToInt(
                        Mathf.Lerp(center.b, blue / 9f, strength)),
                    byte.MaxValue);
            }
        }
    }

    private static float NextSignedNoise(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state / (float)uint.MaxValue * 2f - 1f;
    }

    private static byte ToGammaByte(float linear)
    {
        float gamma = Mathf.LinearToGammaSpace(
            Mathf.Clamp01(linear));
        return (byte)Mathf.Clamp(
            Mathf.RoundToInt(gamma * 255f),
            0,
            255);
    }

    private static void ResolveAperturePriority(
        ref CameraSettings result,
        bool autoIso,
        float targetExposureValue,
        float slowestShutter,
        float fastestShutter,
        float minimumAutoIsoShutter,
        float minimumIso,
        float maximumIso)
    {
        float activeIso = autoIso ? minimumIso : result.Iso;
        float requiredShutter = CalculateRequiredShutterSpeed(
            result.Aperture,
            activeIso,
            targetExposureValue);
        if (!autoIso || requiredShutter >= minimumAutoIsoShutter)
        {
            result.ShutterSpeed = Mathf.Clamp(
                requiredShutter,
                slowestShutter,
                fastestShutter);
            result.Iso = activeIso;
            return;
        }

        result.ShutterSpeed = minimumAutoIsoShutter;
        float requiredIso = CalculateRequiredIso(
            result.ShutterSpeed,
            result.Aperture,
            targetExposureValue);
        result.Iso = Mathf.Clamp(
            requiredIso,
            minimumIso,
            maximumIso);
        if (requiredIso > maximumIso)
        {
            result.ShutterSpeed = Mathf.Clamp(
                CalculateRequiredShutterSpeed(
                    result.Aperture,
                    maximumIso,
                    targetExposureValue),
                slowestShutter,
                fastestShutter);
        }
    }

    private static void ResolveShutterPriority(
        ref CameraSettings result,
        bool autoIso,
        float targetExposureValue,
        float maximumAperture,
        float minimumAperture,
        float minimumIso,
        float maximumIso)
    {
        float activeIso = autoIso ? minimumIso : result.Iso;
        float requiredAperture = CalculateRequiredAperture(
            result.ShutterSpeed,
            activeIso,
            targetExposureValue);
        result.Aperture = Mathf.Clamp(
            requiredAperture,
            maximumAperture,
            minimumAperture);
        result.Iso = activeIso;
        if (autoIso && requiredAperture < maximumAperture)
        {
            result.Aperture = maximumAperture;
            result.Iso = Mathf.Clamp(
                CalculateRequiredIso(
                    result.ShutterSpeed,
                    result.Aperture,
                    targetExposureValue),
                minimumIso,
                maximumIso);
        }
    }

    private static void ResolveProgramAuto(
        ref CameraSettings result,
        bool autoIso,
        float targetExposureValue,
        float maximumAperture,
        float minimumAperture,
        float slowestShutter,
        float fastestShutter,
        float minimumAutoIsoShutter,
        float minimumIso,
        float maximumIso,
        float focalLengthMillimeters)
    {
        float activeIso = autoIso ? minimumIso : result.Iso;
        float minimumProgramShutter =
            autoIso ? minimumAutoIsoShutter : slowestShutter;
        float preferredProgramShutter = autoIso
            ? Mathf.Max(
                minimumAutoIsoShutter,
                focalLengthMillimeters)
            : focalLengthMillimeters;
        float programShutter = Mathf.Clamp(
            preferredProgramShutter,
            minimumProgramShutter,
            fastestShutter);
        float aperture = Mathf.Clamp(
            CalculateRequiredAperture(
                programShutter,
                activeIso,
                targetExposureValue),
            maximumAperture,
            minimumAperture);
        float shutter = Mathf.Clamp(
            CalculateRequiredShutterSpeed(
                aperture,
                activeIso,
                targetExposureValue),
            minimumProgramShutter,
            fastestShutter);
        aperture = Mathf.Clamp(
            CalculateRequiredAperture(
                shutter,
                activeIso,
                targetExposureValue),
            maximumAperture,
            minimumAperture);

        result.ShutterSpeed = shutter;
        result.Aperture = aperture;
        if (!autoIso)
        {
            result.Iso = activeIso;
            return;
        }

        float requiredIso = CalculateRequiredIso(
            shutter,
            aperture,
            targetExposureValue);
        result.Iso = Mathf.Clamp(
            requiredIso,
            minimumIso,
            maximumIso);
        if (requiredIso > maximumIso)
        {
            result.Aperture = maximumAperture;
            result.ShutterSpeed = Mathf.Clamp(
                CalculateRequiredShutterSpeed(
                    maximumAperture,
                    maximumIso,
                    targetExposureValue),
                slowestShutter,
                fastestShutter);
        }
    }

    private static float CalculateRequiredShutterSpeed(
        float aperture,
        float iso,
        float targetEffectiveExposureValue)
    {
        float isoStops = Mathf.Log(
            Mathf.Max(25f, iso) / ReferenceIso,
            2f);
        return Mathf.Pow(
                2f,
                targetEffectiveExposureValue + isoStops) /
            (aperture * aperture);
    }

    private static float CalculateRequiredAperture(
        float shutterSpeed,
        float iso,
        float targetEffectiveExposureValue)
    {
        float isoStops = Mathf.Log(
            Mathf.Max(25f, iso) / ReferenceIso,
            2f);
        return Mathf.Sqrt(
            Mathf.Pow(
                2f,
                targetEffectiveExposureValue + isoStops) /
            Mathf.Max(1f, shutterSpeed));
    }

    private static float CalculateRequiredIso(
        float shutterSpeed,
        float aperture,
        float targetEffectiveExposureValue)
    {
        float exposureValue100 = Mathf.Log(
            aperture * aperture * Mathf.Max(1f, shutterSpeed),
            2f);
        return ReferenceIso * Mathf.Pow(
            2f,
            exposureValue100 - targetEffectiveExposureValue);
    }

    private static float QuantizeStandardValue(
        float value,
        float[] standardValues,
        float minimum,
        float maximum)
    {
        float clampedValue = Mathf.Clamp(value, minimum, maximum);
        float closestValue = clampedValue;
        float closestStopDistance = float.PositiveInfinity;
        foreach (float candidate in standardValues)
        {
            if (candidate < minimum - 0.0001f ||
                candidate > maximum + 0.0001f)
            {
                continue;
            }

            float stopDistance = Mathf.Abs(
                Mathf.Log(
                    candidate / Mathf.Max(0.0001f, clampedValue),
                    2f));
            if (stopDistance < closestStopDistance)
            {
                closestStopDistance = stopDistance;
                closestValue = candidate;
            }
        }

        return closestValue;
    }
}
