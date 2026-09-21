using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class TakePhoto : MonoBehaviour
{
    private struct MtfEdgeCandidate
    {
        public int X;
        public int Y;
        public float GradientX;
        public float GradientY;
        public float Strength;
    }

    public static TakePhoto Instance { get; private set; }
    public static bool IsBackpackOpen => Instance != null && Instance.backpackOpen;

    [Header("Capture")]
    [SerializeField] private int photoWidth = 1280;
    [SerializeField] private int photoHeight = 720;
    [SerializeField] private float photoFieldOfView = 55f;
    [SerializeField] private float cameraForwardOffset = 0.18f;
    [SerializeField] private int maxStoredPhotos = 30;
    [SerializeField] private bool enablePhotoPostProcessing = false;

    [Header("Physical Depth of Field")]
    [SerializeField] private bool enablePhysicalDepthOfField = true;
    [SerializeField, Range(2, 16)]
    private int physicalDepthOfFieldSamples = 8;

    private const float FullFrameSensorWidthMillimeters = 36f;
    private const float FullFrameSensorHeightMillimeters = 24f;
    private const int MinimumExposureSamples = 2;
    private const int MaximumExposureSamples = 24;
    private const float BlurPixelsPerSample = 2f;
    private const int PhotoVolumeLayer = 31;

    private readonly List<Texture2D> photos = new List<Texture2D>();
    private readonly List<bool> photoAccepted = new List<bool>();
    private readonly List<bool> photoCapturedPlayers = new List<bool>();
    private readonly List<float> photoMtf50Retentions = new List<float>();
    private readonly List<float> photoBlurLengths = new List<float>();
    private readonly List<float> photoExposureStops = new List<float>();
    private readonly List<bool> photoUsableExposures = new List<bool>();
    private readonly List<float> photoSignalToNoiseRatios = new List<float>();
    private readonly List<PhotoNoiseReductionMode> photoNoiseReductionModes =
        new List<PhotoNoiseReductionMode>();
    private readonly List<bool> photoUsableNoiseQuality = new List<bool>();
    private readonly List<float> photoFocusDistances = new List<float>();
    private readonly List<float> photoDefocusBlurDiameters = new List<float>();
    private readonly List<PhotoExposureMode> photoExposureModes =
        new List<PhotoExposureMode>();
    private readonly List<bool> photoAutoIsoStates = new List<bool>();
    private readonly List<float> photoShutterSpeeds = new List<float>();
    private readonly List<float> photoApertures = new List<float>();
    private readonly List<float> photoIsoValues = new List<float>();
    private readonly List<float> photoSubjectFrameCoverages =
        new List<float>();
    private readonly List<float> photoMinimumFrameCoverages =
        new List<float>();
    private readonly List<float> photoSubjectVisibilityFractions =
        new List<float>();
    private readonly List<float> photoMinimumVisibilityFractions =
        new List<float>();
    private readonly List<bool> hunterRendererStates = new List<bool>();
    private readonly List<Renderer> hunterRenderers = new List<Renderer>();

    private Camera photoCamera;
    private RenderTexture photoTarget;
    private Texture2D exposureReadback;
    private Volume photoFocusVolume;
    private VolumeProfile photoFocusProfile;
    private DepthOfField photoDepthOfField;
    private bool backpackOpen;
    private int selectedPhotoIndex = -1;
    private int backpackPage;
    private float lastCaptureTime = float.NegativeInfinity;
    private GUIStyle overlayTitleStyle;
    private GUIStyle overlayHintStyle;
    private GUIStyle emptyStyle;

    public IReadOnlyList<Texture2D> Photos => photos;

    private void Awake()
    {
        Instance = this;
        EnsurePhotoCamera();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (photoTarget != null)
        {
            photoTarget.Release();
            Destroy(photoTarget);
        }

        if (photoCamera != null)
        {
            Destroy(photoCamera.gameObject);
        }

        if (photoFocusVolume != null)
        {
            Destroy(photoFocusVolume.gameObject);
        }

        if (photoFocusProfile != null)
        {
            Destroy(photoFocusProfile);
        }

        if (exposureReadback != null)
        {
            Destroy(exposureReadback);
        }

        for (int i = 0; i < photos.Count; i++)
        {
            if (photos[i] != null)
            {
                Destroy(photos[i]);
            }
        }

        photos.Clear();
        photoAccepted.Clear();
        photoCapturedPlayers.Clear();
        photoMtf50Retentions.Clear();
        photoBlurLengths.Clear();
        photoExposureStops.Clear();
        photoUsableExposures.Clear();
        photoSignalToNoiseRatios.Clear();
        photoNoiseReductionModes.Clear();
        photoUsableNoiseQuality.Clear();
        photoFocusDistances.Clear();
        photoDefocusBlurDiameters.Clear();
        photoExposureModes.Clear();
        photoAutoIsoStates.Clear();
        photoShutterSpeeds.Clear();
        photoApertures.Clear();
        photoIsoValues.Clear();
        photoSubjectFrameCoverages.Clear();
        photoMinimumFrameCoverages.Clear();
        photoSubjectVisibilityFractions.Clear();
        photoMinimumVisibilityFractions.Clear();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.B))
        {
            SetBackpackOpen(!backpackOpen);
        }

        if (backpackOpen && selectedPhotoIndex >= 0 && Input.GetKeyDown(KeyCode.Escape))
        {
            selectedPhotoIndex = -1;
        }
    }

    public bool Capture(
        Vector3 origin,
        Vector3 direction,
        Transform subject,
        Vector3 subjectVelocity,
        float shutterSpeed,
        float focalLengthMillimeters,
        float sensorPixelPitchMicrometers,
        float motionBlurScale,
        Vector3 cameraLinearVelocity,
        Vector3 cameraAngularVelocity,
        float aperture,
        float iso,
        float sceneExposureValue100,
        float exposureCompensation,
        PhotoExposureMode exposureMode,
        bool autoIso,
        float lensMaximumAperture,
        float lensMinimumAperture,
        float slowestAutomaticShutterSpeed,
        float fastestAutomaticShutterSpeed,
        float minimumAutoIsoShutterSpeed,
        float minimumAutomaticIso,
        float maximumAutomaticIso,
        float minimumUsableExposureStops,
        float maximumUsableExposureStops,
        PhotoNoiseReductionMode noiseReductionMode,
        float minimumSignalToNoiseRatioDecibels,
        float focusDistanceMeters,
        float sharpMtf50,
        float minimumMtf50Retention,
        float minimumSubjectFrameCoverage,
        float minimumVisibleSubjectFraction)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        EnsurePhotoCamera();
        ConfigurePhotoCameraFromMain(focalLengthMillimeters);

        Vector3 lookDirection = direction.normalized;
        photoCamera.transform.SetPositionAndRotation(
            origin + lookDirection * cameraForwardOffset,
            Quaternion.LookRotation(lookDirection, Vector3.up));

        Vector3 cameraPosition = photoCamera.transform.position;
        Quaternion cameraRotation = photoCamera.transform.rotation;
        Vector3 subjectPosition = subject == null ? Vector3.zero : subject.position;
        float opticalFocusDistanceMeters = Mathf.Max(
            focalLengthMillimeters * 0.001f + 0.001f,
            focusDistanceMeters - cameraForwardOffset);
        float meteredSceneExposureValue100 =
            MeasureSceneExposureValue100(sceneExposureValue100);
        PhotoExposurePhysics.CameraSettings cameraSettings =
            PhotoExposurePhysics.ResolveCameraSettings(
                exposureMode,
                autoIso,
                new PhotoExposurePhysics.CameraSettings
                {
                    ShutterSpeed = shutterSpeed,
                    Aperture = aperture,
                    Iso = iso
                },
                new PhotoExposurePhysics.CameraLimits
                {
                    MaximumAperture = lensMaximumAperture,
                    MinimumAperture = lensMinimumAperture,
                    SlowestShutterSpeed = slowestAutomaticShutterSpeed,
                    FastestShutterSpeed = fastestAutomaticShutterSpeed,
                    MinimumAutoIsoShutterSpeed =
                        minimumAutoIsoShutterSpeed,
                    MinimumIso = minimumAutomaticIso,
                    MaximumIso = maximumAutomaticIso
                },
                meteredSceneExposureValue100,
                exposureCompensation,
                focalLengthMillimeters);
        Vector3 subjectFocusPoint = GetSubjectFocusPoint(
            subject,
            cameraPosition);
        float subjectDistanceMeters = Mathf.Max(
            0.01f,
            Vector3.Dot(
                subjectFocusPoint - cameraPosition,
                cameraRotation * Vector3.forward));
        PhotoBlurPhysics.Solution blurSolution =
            PhotoBlurPhysics.Solve(
                cameraPosition,
                cameraRotation,
                cameraLinearVelocity,
                cameraAngularVelocity,
                subjectPosition,
                subjectVelocity,
                focalLengthMillimeters,
                cameraSettings.ShutterSpeed,
                sensorPixelPitchMicrometers,
                GetEffectiveSensorHeightMillimeters(),
                photoHeight,
                motionBlurScale);
        PhotoExposurePhysics.Solution exposureSolution =
            PhotoExposurePhysics.Solve(
                cameraSettings.ShutterSpeed,
                cameraSettings.Aperture,
                cameraSettings.Iso,
                sceneExposureValue100,
                meteredSceneExposureValue100);
        PhotoDepthOfFieldPhysics.Solution depthOfFieldSolution =
            PhotoDepthOfFieldPhysics.Solve(
                focalLengthMillimeters,
                cameraSettings.Aperture,
                opticalFocusDistanceMeters,
                subjectDistanceMeters,
                sensorPixelPitchMicrometers,
                sharpMtf50);
        ConfigurePhotoDepthOfField(
            focalLengthMillimeters,
            cameraSettings.Aperture,
            opticalFocusDistanceMeters);
        int motionSampleCount = blurSolution.SamplingBlurLengthPixels > 0.01f
            ? CalculateExposureSampleCount(
                blurSolution.SamplingBlurLengthPixels)
            : 1;
        int depthOfFieldSampleCount = enablePhysicalDepthOfField
            ? Mathf.Max(2, physicalDepthOfFieldSamples)
            : 1;
        int sampleCount = Mathf.Max(
            motionSampleCount,
            depthOfFieldSampleCount);
        float apertureRadiusMeters =
            focalLengthMillimeters /
            Mathf.Max(1f, cameraSettings.Aperture) *
            0.0005f;
        float subjectImageTravelPixels =
            blurSolution.BlurLengthPixels;

        HideHunterFromCapture();
        RenderTexture previousActive = RenderTexture.active;
        Texture2D photo = null;
        try
        {
            if (photoFocusVolume != null &&
                enablePhotoPostProcessing)
            {
                photoFocusVolume.enabled = true;
            }

            photoCamera.targetTexture = photoTarget;
            EnsureExposureReadback();

            int pixelCount = photoWidth * photoHeight;
            int[] red = new int[pixelCount];
            int[] green = new int[pixelCount];
            int[] blue = new int[pixelCount];

            for (int sample = 0; sample < sampleCount; sample++)
            {
                float exposurePosition = sampleCount == 1
                    ? 0f
                    : sample / (sampleCount - 1f) - 0.5f;
                float sampleTime =
                    exposurePosition *
                    blurSolution.ExposureSeconds;
                float renderedSampleTime =
                    sampleTime *
                    blurSolution.TrajectoryScale;

                Vector3 motionPosition =
                    cameraPosition +
                    cameraLinearVelocity * renderedSampleTime;
                Quaternion motionRotation =
                    PhotoBlurPhysics.IntegrateAngularVelocity(
                        cameraRotation,
                        cameraAngularVelocity,
                        renderedSampleTime);
                Vector2 apertureOffset = GetApertureSampleOffset(
                    sample,
                    sampleCount,
                    apertureRadiusMeters,
                    depthOfFieldSampleCount > 1);
                Vector3 lensPosition =
                    motionPosition +
                    motionRotation * new Vector3(
                        apertureOffset.x,
                        apertureOffset.y,
                        0f);
                Vector3 focalPoint =
                    motionPosition +
                    motionRotation *
                    (Vector3.forward * opticalFocusDistanceMeters);
                Quaternion lensRotation = apertureOffset.sqrMagnitude >
                    0.0000000001f
                    ? Quaternion.LookRotation(
                        focalPoint - lensPosition,
                        motionRotation * Vector3.up)
                    : motionRotation;
                photoCamera.transform.SetPositionAndRotation(
                    lensPosition,
                    lensRotation);

                if (subject != null)
                {
                    subject.position =
                        subjectPosition +
                        subjectVelocity * renderedSampleTime;
                }

                photoCamera.Render();
                RenderTexture.active = photoTarget;
                exposureReadback.ReadPixels(
                    new Rect(0f, 0f, photoWidth, photoHeight),
                    0,
                    0,
                    false);
                exposureReadback.Apply(false, false);
                AccumulateExposure(
                    exposureReadback.GetPixels32(),
                    red,
                    green,
                    blue);
            }

            photo = CreateExposurePhoto(
                red,
                green,
                blue,
                sampleCount,
                exposureSolution,
                noiseReductionMode);
        }
        finally
        {
            if (subject != null)
            {
                subject.position = subjectPosition;
            }

            photoCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            RenderTexture.active = previousActive;
            photoCamera.targetTexture = null;
            if (photoFocusVolume != null)
            {
                photoFocusVolume.enabled = false;
            }

            RestoreHunterAfterCapture();
        }

        if (photo == null)
        {
            return false;
        }

        float subjectVisibilityFraction =
            CalculateSubjectVisibilityFraction(subject);
        bool capturedPlayer =
            subjectVisibilityFraction >=
            minimumVisibleSubjectFraction;
        float subjectFrameCoverage =
            CalculateSubjectFrameCoverage(
                photoCamera.transform.position,
                photoCamera.transform.rotation,
                subject,
                focalLengthMillimeters,
                photoWidth / (float)photoHeight);
        float motionMtf50Retention = CalculateMtf50Retention(
            subjectImageTravelPixels,
            sharpMtf50);
        float predictedMtf50Retention =
            motionMtf50Retention *
            depthOfFieldSolution.Mtf50Retention;
        if (noiseReductionMode == PhotoNoiseReductionMode.Enabled)
        {
            predictedMtf50Retention *=
                exposureSolution.DenoisedMtf50Retention;
        }
        float measuredMtf50Retention =
            MeasureSubjectMtf50Retention(
                photo,
                subject,
                sharpMtf50);
        float mtf50Retention = Mathf.Min(
            predictedMtf50Retention,
            measuredMtf50Retention);
        bool usableExposure =
            exposureSolution.RelativeStops >= minimumUsableExposureStops &&
            exposureSolution.RelativeStops <= maximumUsableExposureStops;
        bool usableNoiseQuality =
            noiseReductionMode == PhotoNoiseReductionMode.Enabled ||
            exposureSolution.SignalToNoiseRatioDecibels >=
                minimumSignalToNoiseRatioDecibels;
        bool successfulPhoto =
            capturedPlayer &&
            subjectFrameCoverage >= minimumSubjectFrameCoverage &&
            usableExposure &&
            usableNoiseQuality &&
            mtf50Retention >= minimumMtf50Retention;
        StorePhoto(
            photo,
            successfulPhoto,
            capturedPlayer,
            mtf50Retention,
            subjectImageTravelPixels,
            exposureSolution.RelativeStops,
            usableExposure,
            exposureSolution.SignalToNoiseRatioDecibels,
            noiseReductionMode,
            usableNoiseQuality,
            opticalFocusDistanceMeters,
            depthOfFieldSolution.DefocusBlurDiameterPixels,
            exposureMode,
            autoIso,
            cameraSettings,
            subjectFrameCoverage,
            minimumSubjectFrameCoverage,
            subjectVisibilityFraction,
            minimumVisibleSubjectFraction);
        lastCaptureTime = Time.unscaledTime;
        return successfulPhoto;
    }

    private static Vector3 GetSubjectFocusPoint(
        Transform subject,
        Vector3 cameraPosition)
    {
        if (subject == null)
        {
            return Vector3.zero;
        }

        Renderer[] renderers =
            subject.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds combinedBounds = default;
        foreach (Renderer subjectRenderer in renderers)
        {
            if (subjectRenderer == null || !subjectRenderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                combinedBounds = subjectRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(subjectRenderer.bounds);
            }
        }

        Vector3 center = hasBounds
            ? combinedBounds.center
            : subject.position;
        Vector3 direction = center - cameraPosition;
        float distance = direction.magnitude;
        if (distance <= 0.0001f)
        {
            return center;
        }

        Ray focusRay = new Ray(
            cameraPosition,
            direction / distance);
        Collider[] colliders =
            subject.GetComponentsInChildren<Collider>(true);
        float closestDistance = float.PositiveInfinity;
        Vector3 closestPoint = center;
        foreach (Collider subjectCollider in colliders)
        {
            if (subjectCollider != null &&
                subjectCollider.enabled &&
                subjectCollider.Raycast(
                    focusRay,
                    out RaycastHit hit,
                    distance + 1f) &&
                hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestPoint = hit.point;
            }
        }

        return closestPoint;
    }

    private float CalculateSubjectVisibilityFraction(Transform subject)
    {
        if (subject == null ||
            !TryGetSubjectPixelRect(
                subject,
                out RectInt subjectRect,
                0))
        {
            return 0f;
        }

        Collider[] subjectColliders =
            subject.GetComponentsInChildren<Collider>(true);
        if (subjectColliders.Length == 0)
        {
            return IsSubjectVisibleInPhoto(subject) ? 1f : 0f;
        }

        const int samplesPerAxis = 9;
        int subjectSamples = 0;
        int visibleSamples = 0;
        for (int y = 0; y < samplesPerAxis; y++)
        {
            for (int x = 0; x < samplesPerAxis; x++)
            {
                float pixelX = Mathf.Lerp(
                    subjectRect.xMin,
                    subjectRect.xMax,
                    (x + 0.5f) / samplesPerAxis);
                float pixelY = Mathf.Lerp(
                    subjectRect.yMin,
                    subjectRect.yMax,
                    (y + 0.5f) / samplesPerAxis);
                Ray sampleRay = photoCamera.ViewportPointToRay(
                    new Vector3(
                        pixelX / photoWidth,
                        pixelY / photoHeight,
                        0f));
                float subjectDistance = float.PositiveInfinity;
                foreach (Collider subjectCollider in subjectColliders)
                {
                    if (subjectCollider != null &&
                        subjectCollider.enabled &&
                        subjectCollider.Raycast(
                            sampleRay,
                            out RaycastHit subjectHit,
                            photoCamera.farClipPlane) &&
                        subjectHit.distance < subjectDistance)
                    {
                        subjectDistance = subjectHit.distance;
                    }
                }

                if (float.IsPositiveInfinity(subjectDistance))
                {
                    continue;
                }

                subjectSamples++;
                RaycastHit[] hits = Physics.RaycastAll(
                    sampleRay,
                    Mathf.Max(
                        photoCamera.nearClipPlane,
                        subjectDistance - 0.001f),
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore);
                bool blocked = false;
                foreach (RaycastHit hit in hits)
                {
                    if (hit.collider == null ||
                        hit.transform == transform ||
                        hit.transform.IsChildOf(transform) ||
                        hit.transform == subject ||
                        hit.transform.IsChildOf(subject))
                    {
                        continue;
                    }

                    blocked = true;
                    break;
                }

                if (!blocked)
                {
                    visibleSamples++;
                }
            }
        }

        return subjectSamples <= 0
            ? 0f
            : visibleSamples / (float)subjectSamples;
    }

    private bool IsSubjectVisibleInPhoto(Transform subject)
    {
        if (subject == null)
        {
            return false;
        }

        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(photoCamera);
        Renderer[] renderers = subject.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer subjectRenderer in renderers)
        {
            if (subjectRenderer == null ||
                !subjectRenderer.enabled ||
                !GeometryUtility.TestPlanesAABB(
                    frustumPlanes,
                    subjectRenderer.bounds))
            {
                continue;
            }

            Bounds bounds = subjectRenderer.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents * 0.85f;
            Vector3[] visibilityPoints =
            {
                center,
                center + Vector3.up * extents.y,
                center - Vector3.up * extents.y,
                center + Vector3.right * extents.x,
                center - Vector3.right * extents.x,
                center + Vector3.forward * extents.z,
                center - Vector3.forward * extents.z
            };

            foreach (Vector3 point in visibilityPoints)
            {
                Vector3 viewport = photoCamera.WorldToViewportPoint(point);
                if (viewport.z <= photoCamera.nearClipPlane ||
                    viewport.x < 0f ||
                    viewport.x > 1f ||
                    viewport.y < 0f ||
                    viewport.y > 1f)
                {
                    continue;
                }

                if (HasClearPhotoLine(point, subject))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool HasClearPhotoLine(Vector3 point, Transform subject)
    {
        Vector3 direction = point - photoCamera.transform.position;
        float distance = direction.magnitude;
        if (distance <= photoCamera.nearClipPlane)
        {
            return true;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            photoCamera.transform.position,
            direction / distance,
            distance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null ||
                hit.transform == transform ||
                hit.transform.IsChildOf(transform) ||
                hit.transform == subject ||
                hit.transform.IsChildOf(subject))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static float CalculateMtf50Retention(
        float blurLengthPixels,
        float sharpMtf50)
    {
        float sharpFrequency = Mathf.Clamp(sharpMtf50, 0.001f, 0.5f);
        if (blurLengthPixels <= 0.001f)
        {
            return 1f;
        }

        float lowFrequency = 0f;
        float highFrequency = sharpFrequency;
        for (int iteration = 0; iteration < 24; iteration++)
        {
            float frequency = (lowFrequency + highFrequency) * 0.5f;
            float normalizedFrequency = frequency / sharpFrequency;
            float staticMtf = Mathf.Exp(
                -Mathf.Log(2f) *
                normalizedFrequency *
                normalizedFrequency);
            float motionArgument =
                Mathf.PI * frequency * blurLengthPixels;
            float motionMtf = Mathf.Abs(
                Mathf.Sin(motionArgument) /
                Mathf.Max(0.000001f, motionArgument));
            float totalMtf = staticMtf * motionMtf;

            if (totalMtf > 0.5f)
            {
                lowFrequency = frequency;
            }
            else
            {
                highFrequency = frequency;
            }
        }

        float blurredMtf50 = (lowFrequency + highFrequency) * 0.5f;
        return Mathf.Clamp01(blurredMtf50 / sharpFrequency);
    }

    private float MeasureSubjectMtf50Retention(
        Texture2D photo,
        Transform subject,
        float sharpMtf50)
    {
        if (photo == null ||
            !TryGetSubjectPixelRect(subject, out RectInt pixelRect))
        {
            return 0f;
        }

        Color32[] pixels = photo.GetPixels32();
        int width = pixelRect.width;
        int height = pixelRect.height;
        if (width < 15 || height < 15)
        {
            return 0f;
        }

        float[] luminance = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = (pixelRect.yMin + y) * photo.width;
            int targetRow = y * width;
            for (int x = 0; x < width; x++)
            {
                Color32 pixel =
                    pixels[sourceRow + pixelRect.xMin + x];
                float red = Mathf.GammaToLinearSpace(pixel.r / 255f);
                float green = Mathf.GammaToLinearSpace(pixel.g / 255f);
                float blue = Mathf.GammaToLinearSpace(pixel.b / 255f);
                luminance[targetRow + x] =
                    red * 0.2126f +
                    green * 0.7152f +
                    blue * 0.0722f;
            }
        }

        List<MtfEdgeCandidate> candidates =
            new List<MtfEdgeCandidate>();
        const int profileRadius = 6;
        for (int y = profileRadius; y < height - profileRadius; y++)
        {
            for (int x = profileRadius; x < width - profileRadius; x++)
            {
                float gradientX =
                    -GetLuminance(luminance, width, x - 1, y - 1) -
                    2f * GetLuminance(luminance, width, x - 1, y) -
                    GetLuminance(luminance, width, x - 1, y + 1) +
                    GetLuminance(luminance, width, x + 1, y - 1) +
                    2f * GetLuminance(luminance, width, x + 1, y) +
                    GetLuminance(luminance, width, x + 1, y + 1);
                float gradientY =
                    -GetLuminance(luminance, width, x - 1, y - 1) -
                    2f * GetLuminance(luminance, width, x, y - 1) -
                    GetLuminance(luminance, width, x + 1, y - 1) +
                    GetLuminance(luminance, width, x - 1, y + 1) +
                    2f * GetLuminance(luminance, width, x, y + 1) +
                    GetLuminance(luminance, width, x + 1, y + 1);
                float strength = Mathf.Sqrt(
                    gradientX * gradientX +
                    gradientY * gradientY);
                if (strength >= 0.04f)
                {
                    candidates.Add(new MtfEdgeCandidate
                    {
                        X = x,
                        Y = y,
                        GradientX = gradientX,
                        GradientY = gradientY,
                        Strength = strength
                    });
                }
            }
        }

        candidates.Sort(
            (left, right) =>
                right.Strength.CompareTo(left.Strength));
        List<Vector2> acceptedLocations = new List<Vector2>();
        List<float> measuredFrequencies = new List<float>();
        for (int i = 0;
            i < candidates.Count && measuredFrequencies.Count < 32;
            i++)
        {
            MtfEdgeCandidate candidate = candidates[i];
            Vector2 location = new Vector2(
                candidate.X,
                candidate.Y);
            bool tooClose = false;
            for (int accepted = 0;
                accepted < acceptedLocations.Count;
                accepted++)
            {
                if ((acceptedLocations[accepted] - location).sqrMagnitude <
                    25f)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose ||
                !TryMeasureEdgeMtf50(
                    luminance,
                    width,
                    height,
                    candidate,
                    out float measuredFrequency))
            {
                continue;
            }

            acceptedLocations.Add(location);
            measuredFrequencies.Add(measuredFrequency);
        }

        if (measuredFrequencies.Count == 0)
        {
            return 0f;
        }

        measuredFrequencies.Sort();
        float medianMtf50 =
            measuredFrequencies[measuredFrequencies.Count / 2];
        return Mathf.Clamp01(
            medianMtf50 /
            Mathf.Clamp(sharpMtf50, 0.001f, 0.5f));
    }

    private bool TryGetSubjectPixelRect(
        Transform subject,
        out RectInt pixelRect,
        int margin = 8)
    {
        pixelRect = default;
        if (subject == null)
        {
            return false;
        }

        float minimumX = float.PositiveInfinity;
        float minimumY = float.PositiveInfinity;
        float maximumX = float.NegativeInfinity;
        float maximumY = float.NegativeInfinity;
        bool foundPoint = false;
        Renderer[] renderers =
            subject.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer subjectRenderer in renderers)
        {
            if (subjectRenderer == null || !subjectRenderer.enabled)
            {
                continue;
            }

            Bounds bounds = subjectRenderer.bounds;
            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new Vector3(
                    (corner & 1) == 0 ? minimum.x : maximum.x,
                    (corner & 2) == 0 ? minimum.y : maximum.y,
                    (corner & 4) == 0 ? minimum.z : maximum.z);
                Vector3 viewport =
                    photoCamera.WorldToViewportPoint(point);
                if (viewport.z <= photoCamera.nearClipPlane)
                {
                    continue;
                }

                minimumX = Mathf.Min(minimumX, viewport.x);
                minimumY = Mathf.Min(minimumY, viewport.y);
                maximumX = Mathf.Max(maximumX, viewport.x);
                maximumY = Mathf.Max(maximumY, viewport.y);
                foundPoint = true;
            }
        }

        if (!foundPoint)
        {
            return false;
        }

        int xMin = Mathf.Clamp(
            Mathf.FloorToInt(minimumX * photoWidth) - margin,
            0,
            photoWidth - 1);
        int yMin = Mathf.Clamp(
            Mathf.FloorToInt(minimumY * photoHeight) - margin,
            0,
            photoHeight - 1);
        int xMax = Mathf.Clamp(
            Mathf.CeilToInt(maximumX * photoWidth) + margin,
            xMin + 1,
            photoWidth);
        int yMax = Mathf.Clamp(
            Mathf.CeilToInt(maximumY * photoHeight) + margin,
            yMin + 1,
            photoHeight);
        pixelRect = new RectInt(
            xMin,
            yMin,
            xMax - xMin,
            yMax - yMin);
        return true;
    }

    public static float CalculateSubjectFrameCoverage(
        Vector3 cameraPosition,
        Quaternion cameraRotation,
        Transform subject,
        float focalLengthMillimeters,
        float outputAspect)
    {
        if (subject == null)
        {
            return 0f;
        }

        float sensorHeightMillimeters =
            GetEffectiveSensorHeightMillimeters(outputAspect);
        float focalLength = Mathf.Max(1f, focalLengthMillimeters);
        Quaternion worldToCameraRotation =
            Quaternion.Inverse(cameraRotation);
        List<Vector2> projectedSensorPoints =
            new List<Vector2>();
        Renderer[] renderers =
            subject.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer subjectRenderer in renderers)
        {
            if (subjectRenderer == null || !subjectRenderer.enabled)
            {
                continue;
            }

            Bounds bounds = subjectRenderer.bounds;
            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 worldPoint = new Vector3(
                    (corner & 1) == 0 ? minimum.x : maximum.x,
                    (corner & 2) == 0 ? minimum.y : maximum.y,
                    (corner & 4) == 0 ? minimum.z : maximum.z);
                Vector3 cameraPoint =
                    worldToCameraRotation *
                    (worldPoint - cameraPosition);
                if (cameraPoint.z <= 0.01f)
                {
                    continue;
                }

                projectedSensorPoints.Add(new Vector2(
                    Mathf.Clamp(
                        focalLength * cameraPoint.x / cameraPoint.z,
                        -FullFrameSensorWidthMillimeters * 0.5f,
                        FullFrameSensorWidthMillimeters * 0.5f),
                    Mathf.Clamp(
                        focalLength * cameraPoint.y / cameraPoint.z,
                        -sensorHeightMillimeters * 0.5f,
                        sensorHeightMillimeters * 0.5f)));
            }
        }

        float maximumProjectedLengthMillimeters = 0f;
        for (int first = 0;
            first < projectedSensorPoints.Count;
            first++)
        {
            for (int second = first + 1;
                second < projectedSensorPoints.Count;
                second++)
            {
                maximumProjectedLengthMillimeters = Mathf.Max(
                    maximumProjectedLengthMillimeters,
                    Vector2.Distance(
                        projectedSensorPoints[first],
                        projectedSensorPoints[second]));
            }
        }

        return Mathf.Clamp01(
            maximumProjectedLengthMillimeters /
            FullFrameSensorWidthMillimeters);
    }

    private static bool TryMeasureEdgeMtf50(
        float[] luminance,
        int width,
        int height,
        MtfEdgeCandidate candidate,
        out float mtf50)
    {
        mtf50 = 0f;
        Vector2 normal = new Vector2(
            candidate.GradientX,
            candidate.GradientY).normalized;
        Vector2 tangent = new Vector2(-normal.y, normal.x);
        const int profileCount = 21;
        const float sampleSpacing = 0.5f;
        float[] profile = new float[profileCount];
        for (int sample = 0; sample < profileCount; sample++)
        {
            float offset =
                (sample - (profileCount - 1) * 0.5f) *
                sampleSpacing;
            Vector2 center =
                new Vector2(candidate.X, candidate.Y) +
                normal * offset;
            profile[sample] =
                (SampleLuminance(
                    luminance,
                    width,
                    height,
                    center - tangent) +
                SampleLuminance(
                    luminance,
                    width,
                    height,
                    center) +
                SampleLuminance(
                    luminance,
                    width,
                    height,
                    center + tangent)) /
                3f;
        }

        float edgeContrast =
            profile[profileCount - 1] - profile[0];
        if (Mathf.Abs(edgeContrast) < 0.04f)
        {
            return false;
        }

        float direction = Mathf.Sign(edgeContrast);
        float[] lineSpread = new float[profileCount - 1];
        float totalVariation = 0f;
        float dc = 0f;
        for (int i = 0; i < lineSpread.Length; i++)
        {
            lineSpread[i] =
                (profile[i + 1] - profile[i]) * direction;
            totalVariation += Mathf.Abs(lineSpread[i]);
            dc += lineSpread[i];
        }

        if (dc <= 0.0001f ||
            totalVariation > dc * 3f)
        {
            return false;
        }

        float previousFrequency = 0f;
        float previousMtf = 1f;
        const float frequencyStep = 0.01f;
        for (float frequency = frequencyStep;
            frequency <= 0.5001f;
            frequency += frequencyStep)
        {
            float real = 0f;
            float imaginary = 0f;
            for (int i = 0; i < lineSpread.Length; i++)
            {
                float position =
                    (i - (lineSpread.Length - 1) * 0.5f) *
                    sampleSpacing;
                float angle =
                    -2f * Mathf.PI * frequency * position;
                real += lineSpread[i] * Mathf.Cos(angle);
                imaginary += lineSpread[i] * Mathf.Sin(angle);
            }

            float mtf = Mathf.Sqrt(
                real * real +
                imaginary * imaginary) /
                dc;
            if (mtf <= 0.5f)
            {
                float interpolation = Mathf.InverseLerp(
                    previousMtf,
                    mtf,
                    0.5f);
                mtf50 = Mathf.Lerp(
                    previousFrequency,
                    frequency,
                    interpolation);
                return true;
            }

            previousFrequency = frequency;
            previousMtf = mtf;
        }

        mtf50 = 0.5f;
        return true;
    }

    private static float GetLuminance(
        float[] luminance,
        int width,
        int x,
        int y)
    {
        return luminance[y * width + x];
    }

    private static float SampleLuminance(
        float[] luminance,
        int width,
        int height,
        Vector2 position)
    {
        float x = Mathf.Clamp(position.x, 0f, width - 1.001f);
        float y = Mathf.Clamp(position.y, 0f, height - 1.001f);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(width - 1, x0 + 1);
        int y1 = Mathf.Min(height - 1, y0 + 1);
        float blendX = x - x0;
        float blendY = y - y0;
        float lower = Mathf.Lerp(
            luminance[y0 * width + x0],
            luminance[y0 * width + x1],
            blendX);
        float upper = Mathf.Lerp(
            luminance[y1 * width + x0],
            luminance[y1 * width + x1],
            blendX);
        return Mathf.Lerp(lower, upper, blendY);
    }

    private void EnsureExposureReadback()
    {
        if (exposureReadback != null &&
            exposureReadback.width == photoWidth &&
            exposureReadback.height == photoHeight)
        {
            return;
        }

        if (exposureReadback != null)
        {
            Destroy(exposureReadback);
        }

        exposureReadback = new Texture2D(
            photoWidth,
            photoHeight,
            TextureFormat.RGB24,
            false)
        {
            name = "HunterExposureReadback",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
    }

    private static int CalculateExposureSampleCount(float blurLengthPixels)
    {
        int requiredSamples =
            Mathf.CeilToInt(blurLengthPixels / BlurPixelsPerSample) + 1;
        return Mathf.Clamp(
            requiredSamples,
            MinimumExposureSamples,
            MaximumExposureSamples);
    }

    private static Vector2 GetApertureSampleOffset(
        int sampleIndex,
        int sampleCount,
        float apertureRadiusMeters,
        bool enabled)
    {
        if (!enabled || apertureRadiusMeters <= 0f)
        {
            return Vector2.zero;
        }

        const float goldenAngleRadians = 2.39996323f;
        float normalizedRadius = Mathf.Sqrt(
            (sampleIndex + 0.5f) / Mathf.Max(1, sampleCount));
        float angle = sampleIndex * goldenAngleRadians;
        return new Vector2(
            Mathf.Cos(angle),
            Mathf.Sin(angle)) *
            (normalizedRadius * apertureRadiusMeters);
    }

    private static void AccumulateExposure(
        Color32[] pixels,
        int[] red,
        int[] green,
        int[] blue)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            red[i] += pixels[i].r;
            green[i] += pixels[i].g;
            blue[i] += pixels[i].b;
        }
    }

    private Texture2D CreateExposurePhoto(
        int[] red,
        int[] green,
        int[] blue,
        int sampleCount,
        PhotoExposurePhysics.Solution exposure,
        PhotoNoiseReductionMode noiseReductionMode)
    {
        Color32[] averagedPixels = new Color32[red.Length];
        for (int i = 0; i < averagedPixels.Length; i++)
        {
            averagedPixels[i] = new Color32(
                (byte)(red[i] / sampleCount),
                (byte)(green[i] / sampleCount),
                (byte)(blue[i] / sampleCount),
                byte.MaxValue);
        }

        PhotoExposurePhysics.ApplyToPixels(
            averagedPixels,
            exposure,
            photos.Count + 1,
            photoWidth,
            photoHeight,
            noiseReductionMode);

        Texture2D photo = new Texture2D(
            photoWidth,
            photoHeight,
            TextureFormat.RGB24,
            false)
        {
            name = $"HunterPhoto_{photos.Count + 1}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        photo.SetPixels32(averagedPixels);
        photo.Apply(false, false);
        return photo;
    }

    private void StorePhoto(
        Texture2D photo,
        bool accepted,
        bool capturedPlayer,
        float mtf50Retention,
        float blurLengthPixels,
        float exposureStops,
        bool usableExposure,
        float signalToNoiseRatioDecibels,
        PhotoNoiseReductionMode noiseReductionMode,
        bool usableNoiseQuality,
        float focusDistanceMeters,
        float defocusBlurDiameterPixels,
        PhotoExposureMode exposureMode,
        bool autoIso,
        PhotoExposurePhysics.CameraSettings cameraSettings,
        float subjectFrameCoverage,
        float minimumFrameCoverage,
        float subjectVisibilityFraction,
        float minimumVisibilityFraction)
    {
        photos.Add(photo);
        photoAccepted.Add(accepted);
        photoCapturedPlayers.Add(capturedPlayer);
        photoMtf50Retentions.Add(mtf50Retention);
        photoBlurLengths.Add(blurLengthPixels);
        photoExposureStops.Add(exposureStops);
        photoUsableExposures.Add(usableExposure);
        photoSignalToNoiseRatios.Add(signalToNoiseRatioDecibels);
        photoNoiseReductionModes.Add(noiseReductionMode);
        photoUsableNoiseQuality.Add(usableNoiseQuality);
        photoFocusDistances.Add(focusDistanceMeters);
        photoDefocusBlurDiameters.Add(defocusBlurDiameterPixels);
        photoExposureModes.Add(exposureMode);
        photoAutoIsoStates.Add(autoIso);
        photoShutterSpeeds.Add(cameraSettings.ShutterSpeed);
        photoApertures.Add(cameraSettings.Aperture);
        photoIsoValues.Add(cameraSettings.Iso);
        photoSubjectFrameCoverages.Add(subjectFrameCoverage);
        photoMinimumFrameCoverages.Add(minimumFrameCoverage);
        photoSubjectVisibilityFractions.Add(subjectVisibilityFraction);
        photoMinimumVisibilityFractions.Add(minimumVisibilityFraction);
        while (photos.Count > maxStoredPhotos)
        {
            Texture2D oldest = photos[0];
            photos.RemoveAt(0);
            photoAccepted.RemoveAt(0);
            photoCapturedPlayers.RemoveAt(0);
            photoMtf50Retentions.RemoveAt(0);
            photoBlurLengths.RemoveAt(0);
            photoExposureStops.RemoveAt(0);
            photoUsableExposures.RemoveAt(0);
            photoSignalToNoiseRatios.RemoveAt(0);
            photoNoiseReductionModes.RemoveAt(0);
            photoUsableNoiseQuality.RemoveAt(0);
            photoFocusDistances.RemoveAt(0);
            photoDefocusBlurDiameters.RemoveAt(0);
            photoExposureModes.RemoveAt(0);
            photoAutoIsoStates.RemoveAt(0);
            photoShutterSpeeds.RemoveAt(0);
            photoApertures.RemoveAt(0);
            photoIsoValues.RemoveAt(0);
            photoSubjectFrameCoverages.RemoveAt(0);
            photoMinimumFrameCoverages.RemoveAt(0);
            photoSubjectVisibilityFractions.RemoveAt(0);
            photoMinimumVisibilityFractions.RemoveAt(0);
            Destroy(oldest);
            if (selectedPhotoIndex >= 0)
            {
                selectedPhotoIndex = Mathf.Max(-1, selectedPhotoIndex - 1);
            }
        }

        backpackPage = Mathf.Max(0, (photos.Count - 1) / GetPhotosPerPage());
    }

    private void SetBackpackOpen(bool open)
    {
        backpackOpen = open;
        if (!open)
        {
            selectedPhotoIndex = -1;
        }

        ThirdPersonCamera cameraController = Camera.main == null
            ? null
            : Camera.main.GetComponent<ThirdPersonCamera>();
        if (cameraController != null)
        {
            cameraController.SetCursorCaptured(!open);
        }
        else
        {
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }
    }

    private void EnsurePhotoCamera()
    {
        if (photoCamera != null && photoTarget != null)
        {
            return;
        }

        if (photoCamera == null)
        {
            GameObject cameraObject = new GameObject("Hunter Photo Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            photoCamera = cameraObject.AddComponent<Camera>();
            photoCamera.enabled = false;
            photoCamera.orthographic = false;
            photoCamera.fieldOfView = photoFieldOfView;
            photoCamera.aspect = photoWidth / (float)photoHeight;
            photoCamera.nearClipPlane = 0.08f;
            photoCamera.farClipPlane = 1000f;
            photoCamera.allowHDR = true;
            photoCamera.allowMSAA = true;
            photoCamera.depth = -100;
            photoCamera.clearFlags = CameraClearFlags.Skybox;
            photoCamera.cullingMask = ~0;
            photoCamera.stereoTargetEye = StereoTargetEyeMask.None;
        }

        if (photoTarget == null)
        {
            photoTarget = new RenderTexture(photoWidth, photoHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "HunterPhotoTarget",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            photoTarget.Create();
        }

        EnsurePhotoFocusVolume();
        photoCamera.targetTexture = photoTarget;
        UniversalAdditionalCameraData photoData = photoCamera.GetUniversalAdditionalCameraData();
        photoData.renderType = CameraRenderType.Base;
        photoData.renderPostProcessing = enablePhotoPostProcessing;
        photoData.renderShadows = true;
        photoCamera.targetTexture = null;
    }

    private void EnsurePhotoFocusVolume()
    {
        if (photoFocusVolume != null &&
            photoFocusProfile != null &&
            photoDepthOfField != null)
        {
            return;
        }

        GameObject volumeObject = new GameObject(
            "Hunter Photo Focus Volume");
        volumeObject.transform.SetParent(transform, false);
        volumeObject.layer = PhotoVolumeLayer;
        volumeObject.hideFlags = HideFlags.HideAndDontSave;

        photoFocusVolume = volumeObject.AddComponent<Volume>();
        photoFocusVolume.isGlobal = true;
        photoFocusVolume.priority = 10000f;
        photoFocusVolume.weight = 1f;
        photoFocusProfile =
            ScriptableObject.CreateInstance<VolumeProfile>();
        photoFocusProfile.hideFlags = HideFlags.HideAndDontSave;
        photoFocusVolume.sharedProfile = photoFocusProfile;

        photoDepthOfField =
            photoFocusProfile.Add<DepthOfField>(true);
        photoDepthOfField.active = true;
        photoDepthOfField.mode.overrideState = true;
        photoDepthOfField.mode.value = DepthOfFieldMode.Bokeh;
        photoDepthOfField.focusDistance.overrideState = true;
        photoDepthOfField.aperture.overrideState = true;
        photoDepthOfField.focalLength.overrideState = true;
        photoDepthOfField.highQualitySampling.overrideState = true;
        photoDepthOfField.highQualitySampling.value = true;
        photoFocusVolume.enabled = false;
    }

    private void ConfigurePhotoDepthOfField(
        float focalLengthMillimeters,
        float aperture,
        float focusDistanceMeters)
    {
        EnsurePhotoFocusVolume();
        photoDepthOfField.focusDistance.value =
            Mathf.Max(0.1f, focusDistanceMeters);
        photoDepthOfField.aperture.value =
            Mathf.Clamp(aperture, 1f, 32f);
        photoDepthOfField.focalLength.value =
            Mathf.Clamp(focalLengthMillimeters, 1f, 300f);
    }

    private float MeasureSceneExposureValue100(
        float calibratedSceneExposureValue100)
    {
        EnsureExposureReadback();
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = photoCamera.targetTexture;
        try
        {
            photoCamera.targetTexture = photoTarget;
            photoCamera.Render();
            RenderTexture.active = photoTarget;
            exposureReadback.ReadPixels(
                new Rect(0f, 0f, photoWidth, photoHeight),
                0,
                0,
                false);
            exposureReadback.Apply(false, false);
        }
        finally
        {
            RenderTexture.active = previousActive;
            photoCamera.targetTexture = previousTarget;
        }

        Color32[] pixels = exposureReadback.GetPixels32();
        double weightedLogLuminance = 0.0;
        double totalWeight = 0.0;
        const int sampleStride = 8;
        for (int y = sampleStride / 2; y < photoHeight; y += sampleStride)
        {
            float normalizedY =
                (y + 0.5f) / photoHeight * 2f - 1f;
            for (int x = sampleStride / 2; x < photoWidth; x += sampleStride)
            {
                float normalizedX =
                    (x + 0.5f) / photoWidth * 2f - 1f;
                float radius = Mathf.Sqrt(
                    normalizedX * normalizedX +
                    normalizedY * normalizedY);
                float weight = Mathf.Lerp(
                    0.2f,
                    1f,
                    1f - Mathf.Clamp01(radius));
                Color32 pixel = pixels[y * photoWidth + x];
                float red = Mathf.GammaToLinearSpace(pixel.r / 255f);
                float green = Mathf.GammaToLinearSpace(pixel.g / 255f);
                float blue = Mathf.GammaToLinearSpace(pixel.b / 255f);
                float luminance = Mathf.Max(
                    0.0001f,
                    red * 0.2126f +
                    green * 0.7152f +
                    blue * 0.0722f);
                weightedLogLuminance +=
                    Mathf.Log(luminance) * weight;
                totalWeight += weight;
            }
        }

        float averageLuminance = Mathf.Exp(
            (float)(weightedLogLuminance /
                System.Math.Max(0.0001, totalWeight)));
        return calibratedSceneExposureValue100 +
            Mathf.Log(
                Mathf.Max(0.0001f, averageLuminance) / 0.18f,
                2f);
    }

    private float GetEffectiveSensorHeightMillimeters()
    {
        return GetEffectiveSensorHeightMillimeters(
            photoWidth / (float)photoHeight);
    }

    private static float GetEffectiveSensorHeightMillimeters(
        float outputAspect)
    {
        float fullFrameAspect =
            FullFrameSensorWidthMillimeters /
            FullFrameSensorHeightMillimeters;
        return outputAspect >= fullFrameAspect
            ? FullFrameSensorWidthMillimeters / outputAspect
            : FullFrameSensorHeightMillimeters;
    }

    private void ConfigurePhotoCameraFromMain(float focalLengthMillimeters)
    {
        float sensorHeightMillimeters =
            GetEffectiveSensorHeightMillimeters();
        photoCamera.fieldOfView = Camera.FocalLengthToFieldOfView(
            Mathf.Max(1f, focalLengthMillimeters),
            sensorHeightMillimeters);

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        photoCamera.farClipPlane = mainCamera.farClipPlane;
        photoCamera.allowHDR = mainCamera.allowHDR;
        photoCamera.clearFlags = mainCamera.clearFlags;
        photoCamera.backgroundColor = mainCamera.backgroundColor;
        photoCamera.cullingMask = mainCamera.cullingMask;
        photoCamera.aspect = photoWidth / (float)photoHeight;

        UniversalAdditionalCameraData mainData = mainCamera.GetUniversalAdditionalCameraData();
        UniversalAdditionalCameraData photoData = photoCamera.GetUniversalAdditionalCameraData();
        photoData.renderPostProcessing = enablePhotoPostProcessing;
        photoData.volumeLayerMask =
            mainData.volumeLayerMask |
            (1 << PhotoVolumeLayer);
        photoData.volumeTrigger = mainData.volumeTrigger;
        photoData.renderShadows = mainData.renderShadows;
        photoData.antialiasing = mainData.antialiasing;
        photoData.antialiasingQuality = mainData.antialiasingQuality;
    }

    private void HideHunterFromCapture()
    {
        hunterRenderers.Clear();
        hunterRendererStates.Clear();
        GetComponentsInChildren(true, hunterRenderers);
        for (int i = 0; i < hunterRenderers.Count; i++)
        {
            hunterRendererStates.Add(hunterRenderers[i].enabled);
            hunterRenderers[i].enabled = false;
        }
    }

    private void RestoreHunterAfterCapture()
    {
        for (int i = 0; i < hunterRenderers.Count; i++)
        {
            if (hunterRenderers[i] != null)
            {
                hunterRenderers[i].enabled = hunterRendererStates[i];
            }
        }

        hunterRenderers.Clear();
        hunterRendererStates.Clear();
    }

    private void OnGUI()
    {
        float scale = Mathf.Clamp(Screen.height / 720f, 0.8f, 1.5f);
        Matrix4x4 previousMatrix = GUI.matrix;
        int previousDepth = GUI.depth;
        GUI.depth = -20;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        float width = Screen.width / scale;
        float height = Screen.height / scale;
        EnsureGuiStyles();
        DrawCaptureToast(width, height);

        if (backpackOpen)
        {
            DrawBackpack(width, height);
        }

        GUI.matrix = previousMatrix;
        GUI.depth = previousDepth;
    }

    private void EnsureGuiStyles()
    {
        if (overlayTitleStyle != null)
        {
            return;
        }

        overlayTitleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 22,
            fontStyle = FontStyle.Bold
        };
        overlayHintStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = 14
        };
        emptyStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18
        };
    }

    private void DrawCaptureToast(float screenWidth, float screenHeight)
    {
        if (photos.Count == 0 || Time.unscaledTime - lastCaptureTime > 2.4f || backpackOpen)
        {
            return;
        }

        Texture2D latest = photos[photos.Count - 1];
        int latestIndex = photos.Count - 1;
        float toastWidth = 288f;
        float toastImageHeight = toastWidth * 9f / 16f;
        float toastHeight = toastImageHeight + 44f;
        Rect toastRect = new Rect(
            screenWidth - toastWidth - 16f,
            screenHeight - toastHeight - 28f,
            toastWidth,
            toastHeight);
        GUI.Box(toastRect, string.Empty);
        GUI.DrawTexture(
            new Rect(
                toastRect.x + 6f,
                toastRect.y + 6f,
                toastRect.width - 12f,
                toastImageHeight - 12f),
            latest,
            ScaleMode.ScaleToFit);
        GUI.Label(
            new Rect(
                toastRect.x + 8f,
                toastRect.y + toastImageHeight,
                toastRect.width - 16f,
                20f),
            GetPhotoResultLabel(latestIndex));
        GUI.Label(
            new Rect(
                toastRect.x + 8f,
                toastRect.y + toastImageHeight + 20f,
                toastRect.width - 16f,
                20f),
            GetPhotoSettingsLabel(latestIndex));
    }

    private void DrawBackpack(float screenWidth, float screenHeight)
    {
        GUI.Box(new Rect(0f, 0f, screenWidth, screenHeight), string.Empty);

        float panelWidth = 920f;
        float panelHeight = 560f;
        Rect panel = new Rect(
            (screenWidth - panelWidth) * 0.5f,
            (screenHeight - panelHeight) * 0.5f,
            panelWidth,
            panelHeight);
        GUI.Box(panel, string.Empty);

        GUI.Label(
            new Rect(panel.x + 24f, panel.y + 16f, 420f, 36f),
            $"Photographer Backpack  {photos.Count}/{maxStoredPhotos}",
            overlayTitleStyle);
        GUI.Label(
            new Rect(panel.xMax - 360f, panel.y + 20f, 336f, 28f),
            "Click a photo to enlarge    B Close",
            overlayHintStyle);

        if (photos.Count == 0)
        {
            GUI.Label(
                new Rect(panel.x, panel.y + 80f, panel.width, 80f),
                "No photos yet. The hunter will take a 16:9 shot when firing.",
                emptyStyle);
            return;
        }

        const int columns = 4;
        const int rows = 2;
        int photosPerPage = columns * rows;
        int pageCount = Mathf.Max(1, Mathf.CeilToInt(photos.Count / (float)photosPerPage));
        backpackPage = Mathf.Clamp(backpackPage, 0, pageCount - 1);

        float gridLeft = panel.x + 28f;
        float gridTop = panel.y + 68f;
        float cellWidth = 208f;
        float cellHeight = cellWidth * 9f / 16f;
        float xGap = 12f;
        float yGap = 16f;

        int startIndex = backpackPage * photosPerPage;
        for (int i = 0; i < photosPerPage; i++)
        {
            int photoIndex = startIndex + i;
            if (photoIndex >= photos.Count)
            {
                break;
            }

            int column = i % columns;
            int row = i / columns;
            Rect cell = new Rect(
                gridLeft + column * (cellWidth + xGap),
                gridTop + row * (cellHeight + yGap + 42f),
                cellWidth,
                cellHeight);

            if (GUI.Button(cell, GUIContent.none))
            {
                selectedPhotoIndex = photoIndex;
            }

            GUI.DrawTexture(cell, photos[photoIndex], ScaleMode.ScaleToFit);
            GUI.Label(
                new Rect(cell.x, cell.yMax + 2f, cell.width, 20f),
                $"Photo {photoIndex + 1}  {GetPhotoResultLabel(photoIndex)}");
            GUI.Label(
                new Rect(cell.x, cell.yMax + 22f, cell.width, 20f),
                GetPhotoSettingsLabel(photoIndex));
        }

        if (pageCount > 1)
        {
            float navY = panel.yMax - 56f;
            GUI.enabled = backpackPage > 0;
            if (GUI.Button(new Rect(panel.x + 28f, navY, 90f, 32f), "Prev"))
            {
                backpackPage--;
            }

            GUI.enabled = backpackPage < pageCount - 1;
            if (GUI.Button(new Rect(panel.xMax - 118f, navY, 90f, 32f), "Next"))
            {
                backpackPage++;
            }

            GUI.enabled = true;
            GUI.Label(
                new Rect(panel.x, navY, panel.width, 32f),
                $"Page {backpackPage + 1}/{pageCount}",
                emptyStyle);
        }

        if (selectedPhotoIndex >= 0 && selectedPhotoIndex < photos.Count)
        {
            DrawEnlargedPhoto(
                screenWidth,
                screenHeight,
                selectedPhotoIndex);
        }
    }

    private void DrawEnlargedPhoto(
        float screenWidth,
        float screenHeight,
        int photoIndex)
    {
        Texture2D photo = photos[photoIndex];
        GUI.Box(new Rect(0f, 0f, screenWidth, screenHeight), string.Empty);

        float maxWidth = screenWidth * 0.82f;
        float maxHeight = screenHeight * 0.78f;
        float width = maxWidth;
        float height = width * 9f / 16f;
        if (height > maxHeight)
        {
            height = maxHeight;
            width = height * 16f / 9f;
        }

        Rect photoRect = new Rect(
            (screenWidth - width) * 0.5f,
            (screenHeight - height) * 0.5f - 10f,
            width,
            height);
        GUI.Box(new Rect(photoRect.x - 8f, photoRect.y - 8f, photoRect.width + 16f, photoRect.height + 16f), string.Empty);
        GUI.DrawTexture(photoRect, photo, ScaleMode.ScaleToFit);
        string autoIsoLabel =
            photoAutoIsoStates[photoIndex]
                ? " + Auto ISO"
                : string.Empty;
        GUI.Label(
            new Rect(
                photoRect.x,
                photoRect.yMax + 12f,
                photoRect.width,
                24f),
            $"{photoExposureModes[photoIndex]}{autoIsoLabel}    " +
            $"1/{photoShutterSpeeds[photoIndex]:F0}s    " +
            $"f/{photoApertures[photoIndex]:F1}    " +
            $"ISO {photoIsoValues[photoIndex]:F0}    " +
            $"Focus {photoFocusDistances[photoIndex]:F1}m",
            overlayHintStyle);
        GUI.Label(
            new Rect(
                photoRect.x,
                photoRect.yMax + 34f,
                photoRect.width,
                24f),
            $"{GetPhotoResultLabel(photoIndex)}    " +
            $"Frame {photoSubjectFrameCoverages[photoIndex]:P0}    " +
            $"Visible {photoSubjectVisibilityFractions[photoIndex]:P0}    " +
            $"Motion {photoBlurLengths[photoIndex]:F1}px    " +
            $"Defocus {photoDefocusBlurDiameters[photoIndex]:F1}px",
            overlayHintStyle);

        Rect backButtonRect = new Rect(photoRect.xMax - 90f, photoRect.y - 44f, 90f, 32f);
        float scale = Mathf.Clamp(Screen.height / 720f, 0.8f, 1.5f);
        Vector2 mouse = Event.current.mousePosition / scale;
        if (Event.current.type == EventType.MouseDown &&
            !photoRect.Contains(mouse) &&
            !backButtonRect.Contains(mouse))
        {
            selectedPhotoIndex = -1;
            Event.current.Use();
            return;
        }

        if (GUI.Button(backButtonRect, "Back"))
        {
            selectedPhotoIndex = -1;
        }
    }

    private static int GetPhotosPerPage()
    {
        return 8;
    }

    private string GetPhotoSettingsLabel(int photoIndex)
    {
        string modeLabel;
        switch (photoExposureModes[photoIndex])
        {
            case PhotoExposureMode.ProgramAuto:
                modeLabel = "P";
                break;
            case PhotoExposureMode.AperturePriority:
                modeLabel = "A";
                break;
            case PhotoExposureMode.ShutterPriority:
                modeLabel = "S";
                break;
            default:
                modeLabel = "M";
                break;
        }

        string autoIsoLabel =
            photoAutoIsoStates[photoIndex] ? "+AutoISO" : string.Empty;
        return
            $"{modeLabel}{autoIsoLabel}  " +
            $"1/{photoShutterSpeeds[photoIndex]:F0}s  " +
            $"f/{photoApertures[photoIndex]:F1}  " +
            $"ISO {photoIsoValues[photoIndex]:F0}";
    }

    private string GetPhotoResultLabel(int photoIndex)
    {
        if (photoIndex < 0 ||
            photoIndex >= photos.Count ||
            photoIndex >= photoAccepted.Count)
        {
            return string.Empty;
        }

        if (photoAccepted[photoIndex])
        {
            string quality =
                photoNoiseReductionModes[photoIndex] ==
                PhotoNoiseReductionMode.Enabled
                    ? $"DENOISED  MTF50 {photoMtf50Retentions[photoIndex]:P0}"
                    : $"MTF50 {photoMtf50Retentions[photoIndex]:P0}  " +
                        $"SNR {photoSignalToNoiseRatios[photoIndex]:F0}dB";
            return
                $"CLEAR  {quality}  " +
                GetExposureLabel(photoIndex);
        }

        if (!photoCapturedPlayers[photoIndex])
        {
            if (photoSubjectVisibilityFractions[photoIndex] > 0f)
            {
                return
                    $"FAILED  OCCLUDED " +
                    $"{photoSubjectVisibilityFractions[photoIndex]:P0} " +
                    $"<{photoMinimumVisibilityFractions[photoIndex]:P0}  " +
                    GetExposureLabel(photoIndex);
            }

            return
                $"FAILED  NO PLAYER  " +
                GetExposureLabel(photoIndex);
        }

        if (photoSubjectFrameCoverages[photoIndex] <
            photoMinimumFrameCoverages[photoIndex])
        {
            return
                $"FAILED  SUBJECT " +
                $"{photoSubjectFrameCoverages[photoIndex]:P0} " +
                $"<{photoMinimumFrameCoverages[photoIndex]:P0}  " +
                GetExposureLabel(photoIndex);
        }

        if (!photoUsableExposures[photoIndex])
        {
            return
                $"FAILED  EXPOSURE  " +
                GetExposureLabel(photoIndex);
        }

        if (!photoUsableNoiseQuality[photoIndex])
        {
            return
                $"FAILED  SNR " +
                $"{photoSignalToNoiseRatios[photoIndex]:F1}dB  " +
                GetExposureLabel(photoIndex);
        }

        string denoiseLabel =
            photoNoiseReductionModes[photoIndex] ==
            PhotoNoiseReductionMode.Enabled
                ? "DENOISED  "
                : string.Empty;
        return
            $"FAILED  {denoiseLabel}" +
            $"MTF50 {photoMtf50Retentions[photoIndex]:P0}  " +
            $"{photoBlurLengths[photoIndex]:F1}px  " +
            GetExposureLabel(photoIndex);
    }

    private string GetExposureLabel(int photoIndex)
    {
        float stops = photoExposureStops[photoIndex];
        if (stops > 0.05f)
        {
            return $"OVER +{stops:F1} EV";
        }

        if (stops < -0.05f)
        {
            return $"UNDER {stops:F1} EV";
        }

        return "EXPOSURE 0.0 EV";
    }
}
