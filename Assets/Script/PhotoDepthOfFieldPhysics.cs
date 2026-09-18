using UnityEngine;

public static class PhotoDepthOfFieldPhysics
{
    private const float AcceptableCircleOfConfusionMillimeters = 0.03f;

    public struct Solution
    {
        public float HyperfocalDistanceMeters;
        public float NearLimitMeters;
        public float FarLimitMeters;
        public float DefocusCircleMillimeters;
        public float DefocusBlurDiameterPixels;
        public float Mtf50Retention;
    }

    public static Solution Solve(
        float focalLengthMillimeters,
        float aperture,
        float focusDistanceMeters,
        float subjectDistanceMeters,
        float pixelPitchMicrometers,
        float sharpMtf50)
    {
        float focalLength = Mathf.Max(1f, focalLengthMillimeters);
        float safeAperture = Mathf.Max(0.7f, aperture);
        float minimumObjectDistance = focalLength * 0.001f + 0.001f;
        float focusDistance = Mathf.Max(
            minimumObjectDistance,
            focusDistanceMeters);
        float subjectDistance = Mathf.Max(
            minimumObjectDistance,
            subjectDistanceMeters);
        float pixelPitchMillimeters =
            Mathf.Max(0.1f, pixelPitchMicrometers) * 0.001f;

        float focusDistanceMillimeters = focusDistance * 1000f;
        float subjectDistanceMillimeters = subjectDistance * 1000f;
        float hyperfocalMillimeters =
            focalLength * focalLength /
            (safeAperture * AcceptableCircleOfConfusionMillimeters) +
            focalLength;
        float nearLimitMillimeters =
            hyperfocalMillimeters * focusDistanceMillimeters /
            (hyperfocalMillimeters +
                focusDistanceMillimeters -
                focalLength);
        float farDenominator =
            hyperfocalMillimeters -
            (focusDistanceMillimeters - focalLength);
        float farLimitMillimeters = farDenominator <= 0f
            ? float.PositiveInfinity
            : hyperfocalMillimeters * focusDistanceMillimeters /
                farDenominator;

        float focusedImageDistance =
            focalLength * focusDistanceMillimeters /
            (focusDistanceMillimeters - focalLength);
        float subjectImageDistance =
            focalLength * subjectDistanceMillimeters /
            (subjectDistanceMillimeters - focalLength);
        float entrancePupilDiameter = focalLength / safeAperture;
        float defocusCircleMillimeters =
            entrancePupilDiameter *
            Mathf.Abs(subjectImageDistance - focusedImageDistance) /
            subjectImageDistance;
        float defocusBlurDiameterPixels =
            defocusCircleMillimeters / pixelPitchMillimeters;

        return new Solution
        {
            HyperfocalDistanceMeters = hyperfocalMillimeters * 0.001f,
            NearLimitMeters = nearLimitMillimeters * 0.001f,
            FarLimitMeters = float.IsPositiveInfinity(farLimitMillimeters)
                ? float.PositiveInfinity
                : farLimitMillimeters * 0.001f,
            DefocusCircleMillimeters = defocusCircleMillimeters,
            DefocusBlurDiameterPixels = defocusBlurDiameterPixels,
            Mtf50Retention = CalculateMtf50Retention(
                defocusBlurDiameterPixels,
                sharpMtf50)
        };
    }

    private static float CalculateMtf50Retention(
        float blurDiameterPixels,
        float sharpMtf50)
    {
        if (blurDiameterPixels <= 0.001f)
        {
            return 1f;
        }

        float sharpFrequency = Mathf.Clamp(
            sharpMtf50,
            0.001f,
            0.5f);
        float gaussianSigma = blurDiameterPixels * 0.25f;
        float defocusTerm =
            2f *
            Mathf.PI *
            Mathf.PI *
            gaussianSigma *
            gaussianSigma *
            sharpFrequency *
            sharpFrequency /
            Mathf.Log(2f);
        return 1f / Mathf.Sqrt(1f + defocusTerm);
    }
}
