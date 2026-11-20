using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class TakePhoto : MonoBehaviour
{
    private static string T(string zh,string en) => GameLanguage.Text(zh,en);
    private struct MtfEdgeCandidate
    {
        public int X;
        public int Y;
        public float GradientX;
        public float GradientY;
        public float Strength;
    }

    public static TakePhoto Instance { get; private set; }
    private static readonly List<TakePhoto> cameras = new List<TakePhoto>();
    private static int captureSequence;
    public static bool AnyProcessing => cameras.Exists(camera => camera != null && camera.IsProcessing);
    public static bool IsBackpackOpen => Instance != null && Instance.backpackOpen;
    private static bool ReviewingResult => GameFlow.Instance != null && GameFlow.Instance.CurrentPage == GameFlow.Page.Result;

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
    private readonly List<bool> photoSubjectsInFrame = new List<bool>();
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
    private readonly List<Rect> photoSubjectBounds = new List<Rect>();
    private bool showSubjectBounds = true;
    private readonly List<float> photoMinimumFrameCoverages =
        new List<float>();
    private readonly List<float> photoSubjectVisibilityFractions =
        new List<float>();
    private readonly List<float> photoMinimumVisibilityFractions =
        new List<float>();
    private readonly List<bool> hunterRendererStates = new List<bool>();
    private readonly List<Renderer> hunterRenderers = new List<Renderer>();

    private readonly PhotoVisibility photoVisibility = new PhotoVisibility();
    private readonly PhotoVisibility trackingVisibility = new PhotoVisibility();
    private Camera photoCamera;
    private RenderTexture photoTarget;
    private Texture2D exposureReadback;
    private readonly PhotoGpuDevelopment gpuDevelopment = new PhotoGpuDevelopment();
    private PhotoShutterSnapshot shutterSnapshot;
    [Header("Background Photo Processing")]
    [SerializeField, Range(32, 512)] private int processingRowsPerFrame = 128;
    public int LastShutterFrame { get; private set; }
    public int LastCompletionFrame { get; private set; }
    public float LastSubjectCoverage { get; private set; }
    public float LastSubjectVisibility { get; private set; }
    private RenderTexture meterTarget;
    private Material exposureAccumulator;
    private AsyncGPUReadbackRequest pendingReadback;
    private bool readbackPending;
    public bool IsProcessing { get; private set; }
    public float LastShutterMilliseconds { get; private set; }
    public float LastMeterMilliseconds { get; private set; }
    public float LastExposureRenderMilliseconds { get; private set; }
    public float LastVisibilityMilliseconds { get; private set; }
    public float LastProcessingMilliseconds { get; private set; }
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

    public IReadOnlyList<Texture2D> Photos => Instance != null ? Instance.photos : photos;

    private void Awake()
    {
        if (Instance == null) { Instance = this; captureSequence = 0; }
        cameras.Add(this);
        // Large capture buffers are allocated only when this photographer shoots.
    }

    private void OnDestroy()
    {
        cameras.Remove(this);
        if (readbackPending) pendingReadback.WaitForCompletion();
        gpuDevelopment.Dispose();
        shutterSnapshot?.Dispose();
        if (meterTarget != null) { meterTarget.Release(); Destroy(meterTarget); }
        if (exposureAccumulator != null) Destroy(exposureAccumulator);
        photoVisibility.Dispose();
        trackingVisibility.Dispose();
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
        photoSubjectsInFrame.Clear();
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
        photoSubjectBounds.Clear();
        photoMinimumFrameCoverages.Clear();
        photoSubjectVisibilityFractions.Clear();
        photoMinimumVisibilityFractions.Clear();
    }

    private void Update()
    {
        if (Instance != this) return;
        if(GameFlow.BlocksGameplay && !ReviewingResult)return;
        if (Input.GetKeyDown(KeyCode.B))
        {
            SetBackpackOpen(!backpackOpen);
        }

        if (GameFlow.Instance==null && backpackOpen && selectedPhotoIndex >= 0 && Input.GetKeyDown(KeyCode.Escape))
        {
            selectedPhotoIndex = -1;
        }
    }

    public float MeasureTrackingVisibility(Vector3 origin, Vector3 direction, Transform subject,
        float focalLengthMillimeters)
    {
        return MeasureTrackingVisibility(origin, direction, subject, focalLengthMillimeters, out _);
    }

    public float MeasureTrackingVisibility(Vector3 origin, Vector3 direction, Transform subject,
        float focalLengthMillimeters, out float frameCoverage)
    {
        frameCoverage = 0f;
        if (subject == null || direction.sqrMagnitude < 0.0001f) return 0f;
        EnsurePhotoCamera();
        ConfigurePhotoCameraFromMain(focalLengthMillimeters);
        photoCamera.transform.SetPositionAndRotation(
            origin + direction.normalized * cameraForwardOffset,
            Quaternion.LookRotation(direction.normalized, Vector3.up));
        HideHunterFromCapture();
        try
        {
            float visibility = trackingVisibility.Measure(photoCamera, subject, 320,
                Mathf.Max(1, Mathf.RoundToInt(320f * photoHeight / photoWidth)));
            frameCoverage = trackingVisibility.SubjectFrameCoverage;
            return visibility;
        }
        finally
        {
            RestoreHunterAfterCapture();
        }
    }

    public IEnumerator Capture(
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
        float meteringTargetLuminance,
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
        float minimumVisibleSubjectFraction,
        System.Action<bool> onCompleted)
    {
        if (IsProcessing || (GameManager.Instance != null && !GameManager.Instance.IsRunning)) yield break;
        IsProcessing = true;
        try
        {
            int captureSeed = ++captureSequence;
            var shutterTimer = System.Diagnostics.Stopwatch.StartNew();
            if (direction.sqrMagnitude < 0.0001f)
            {
                yield break;
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
            int captureWidth = photoWidth, captureHeight = photoHeight;
            int baseOpticsSamples = physicalDepthOfFieldSamples;
            int rowsPerFrame = Mathf.Clamp(processingRowsPerFrame, 32, 512);
            bool depthOfFieldEnabled = enablePhysicalDepthOfField;
            float sensorHeight = GetEffectiveSensorHeightMillimeters();
            LastShutterFrame = Time.frameCount;
            Vector3 subjectFocusPoint = GetSubjectFocusPoint(
                subject,
                cameraPosition);
            float subjectDistanceMeters = Mathf.Max(
                0.01f,
                Vector3.Dot(
                    subjectFocusPoint - cameraPosition,
                    cameraRotation * Vector3.forward));
            TryGetSubjectPixelRect(subject, out RectInt shutterPixelRect);
            EnsureShutterSnapshot();
            EnsureExposureAccumulator();
            EnsureExposureReadback();
            RenderTexture previousActive = RenderTexture.active;
            var photoData = photoCamera.GetUniversalAdditionalCameraData();
            bool previousDepth = photoData.requiresDepthTexture;
            Texture2D photo = null;
            float subjectVisibilityFraction = 0f;
            HideHunterFromCapture();
            try
            {
                photoData.requiresDepthTexture = true;
                photoCamera.targetTexture = photoTarget;
                // A single image/depth capture freezes bird pose, vegetation, light and camera time.
                photoCamera.Render();
                shutterSnapshot.CopyDepth(photoCamera);
                RenderTexture.active = shutterSnapshot.Color;
                GL.Clear(false, true, Color.clear);
                exposureAccumulator.SetFloat("_Weight", 1f);
                Graphics.Blit(photoTarget, shutterSnapshot.Color, exposureAccumulator);
                // Meter exactly the saved shutter image, without a second scene render.
                Graphics.Blit(photoTarget, meterTarget);
                LastExposureRenderMilliseconds = (float)shutterTimer.Elapsed.TotalMilliseconds;
                photoVisibility.BeginMeasure(photoCamera, subject, captureWidth, captureHeight);
                photoVisibility.CopyVisibleSnapshot(shutterSnapshot);
            }
            finally
            {
                photoData.requiresDepthTexture = previousDepth;
                photoCamera.targetTexture = null;
                RenderTexture.active = previousActive;
                RestoreHunterAfterCapture();
            }
            LastShutterMilliseconds = (float)shutterTimer.Elapsed.TotalMilliseconds;
            LastVisibilityMilliseconds = LastShutterMilliseconds - LastExposureRenderMilliseconds;
            Color32[] meterPixels = null;
            int meterWidth = meterTarget.width, meterHeight = meterTarget.height;
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                pendingReadback = AsyncGPUReadback.Request(meterTarget, 0, TextureFormat.RGBA32);
                readbackPending = true;
                // Always hand control back immediately after freezing the shutter.
                yield return null;
                while (!pendingReadback.done) yield return null;
                readbackPending = false;
                if (!pendingReadback.hasError) meterPixels = pendingReadback.GetData<Color32>().ToArray();
            }
            else yield return null;
            if (meterPixels == null)
            {
                var active = RenderTexture.active;
                try
                {
                    RenderTexture.active = meterTarget;
                    exposureReadback.ReadPixels(new Rect(0, 0, meterWidth, meterHeight), 0, 0, false);
                    meterPixels = exposureReadback.GetPixels32();
                }
                finally { RenderTexture.active = active; }
            }
            var meterTimer = System.Diagnostics.Stopwatch.StartNew();
            float meteredSceneExposureValue100 = MeterSavedPixels(meterPixels, meterWidth, meterHeight, sceneExposureValue100);
            // Meter target and sensor calibration are separate. Changing the sensor
            // calibration on both sides would cancel out and leave photos equally bright.
            float meteringTargetOffset = Mathf.Log(.18f / Mathf.Clamp(meteringTargetLuminance,.01f,.5f),2f);
            LastMeterMilliseconds = (float)meterTimer.Elapsed.TotalMilliseconds;
            while (!photoVisibility.TryComplete(out subjectVisibilityFraction)) yield return null;
            LastSubjectCoverage = photoVisibility.SubjectFrameCoverage;
            LastSubjectVisibility = subjectVisibilityFraction;
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
                    meteredSceneExposureValue100 + meteringTargetOffset,
                    exposureCompensation,
                    focalLengthMillimeters);
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
                    sensorHeight,
                    captureHeight,
                    motionBlurScale);
            PhotoExposurePhysics.Solution exposureSolution =
                PhotoExposurePhysics.Solve(
                    cameraSettings.ShutterSpeed,
                    cameraSettings.Aperture,
                    cameraSettings.Iso,
                    sceneExposureValue100,
                    meteredSceneExposureValue100);
            // Exposure status is relative to the selected metering target; sensor
            // signal/noise still uses the actual measured scene illumination.
            exposureSolution.RelativeStops += meteringTargetOffset;
            PhotoDepthOfFieldPhysics.Solution depthOfFieldSolution =
                PhotoDepthOfFieldPhysics.Solve(
                    focalLengthMillimeters,
                    cameraSettings.Aperture,
                    opticalFocusDistanceMeters,
                    subjectDistanceMeters,
                    sensorPixelPitchMicrometers,
                    sharpMtf50);
            float subjectImageTravelPixels = blurSolution.BlurLengthPixels;
            int opticsSamples = Mathf.Clamp(Mathf.Max(baseOpticsSamples * 4,
                CalculateExposureSampleCount(blurSolution.SamplingBlurLengthPixels)), 8, 64);
            Quaternion inverseCamera = Quaternion.Inverse(cameraRotation);
            float focalPixels = focalLengthMillimeters * captureHeight / sensorHeight;
            for (int row = 0; row < captureHeight; row += rowsPerFrame)
            {
                shutterSnapshot.DevelopRows(row, rowsPerFrame, opticsSamples,
                    focalLengthMillimeters, cameraSettings.Aperture, opticalFocusDistanceMeters,
                    sensorHeight, blurSolution.ExposureSeconds * blurSolution.TrajectoryScale,
                    focalPixels, depthOfFieldEnabled, inverseCamera * cameraLinearVelocity,
                    inverseCamera * subjectVelocity, inverseCamera * cameraAngularVelocity * Mathf.Deg2Rad);
                yield return null;
            }
            bool gpuDeveloped = PhotoGpuDevelopment.Supported;
            RenderTexture developedTarget = gpuDeveloped
                ? gpuDevelopment.Process(shutterSnapshot.Developed, exposureSolution, captureSeed, noiseReductionMode) : shutterSnapshot.Developed;
            Color32[] pixels = null;
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                pendingReadback = AsyncGPUReadback.Request(developedTarget, 0, TextureFormat.RGBA32);
                readbackPending = true;
                while (!pendingReadback.done) yield return null;
                readbackPending = false;
                if (!pendingReadback.hasError) pixels = pendingReadback.GetData<Color32>().ToArray();
            }
            if (pixels == null)
            {
                // Compatibility fallback for graphics devices without asynchronous readback.
                var active = RenderTexture.active;
                var fallback = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false, true);
                try
                {
                    RenderTexture.active = developedTarget;
                    fallback.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0, false);
                    pixels = fallback.GetPixels32();
                }
                finally { RenderTexture.active = active; Destroy(fallback); }
            }
            while (!photoVisibility.TryComplete(out subjectVisibilityFraction)) yield return null;
            Color32[] sharpPixels = null, subjectMask = null;
            yield return ReadFrozenPixels(shutterSnapshot.Color, data => sharpPixels = data);
            yield return ReadFrozenPixels(shutterSnapshot.Subject, data => subjectMask = data);
            int width = captureWidth, height = captureHeight, seed = captureSeed;
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
            var processingTimer = System.Diagnostics.Stopwatch.StartNew();
            // This task owns only managed pixels and value snapshots; no scene/texture access.
            var processing = Task.Run(() =>
            {
                if (!gpuDeveloped) PhotoExposurePhysics.ApplyToPixels(pixels, exposureSolution, seed, width, height, noiseReductionMode);
                return MeasureSubjectMtf50Retention(pixels, sharpPixels, subjectMask, width, shutterPixelRect, predictedMtf50Retention);
            });
            while (!processing.IsCompleted) yield return null;
            if (processing.IsFaulted) { Debug.LogException(processing.Exception); yield break; }
            LastProcessingMilliseconds = (float)processingTimer.Elapsed.TotalMilliseconds;
            photo = new Texture2D(width, height, TextureFormat.RGB24, false)
            { name = $"HunterPhoto_{seed}", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            photo.SetPixels32(pixels);
            photo.Apply(false, false);

            bool capturedPlayer =
                subjectVisibilityFraction > 0f &&
                subjectVisibilityFraction >=
                minimumVisibleSubjectFraction;
            // Tight 2D bounds from the unobstructed shutter-midpoint silhouette.
            // Occlusion remains a separate pixel-visibility requirement.
            float subjectFrameCoverage = photoVisibility.SubjectFrameCoverage;
            float measuredMtf50Retention = processing.Result;
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
                minimumVisibleSubjectFraction,
                photoVisibility.SubjectViewportBounds,
                photoVisibility.SubjectPixelCount >= 1f);
            lastCaptureTime = Time.unscaledTime;
            LastCompletionFrame = Time.frameCount;
            // A pause must not discard an in-flight score. Scene destruction stops the coroutine.
            while (GameFlow.BlocksGameplay && GameManager.Instance != null && !GameManager.Instance.HasEnded)
                yield return null;
            onCompleted?.Invoke(successfulPhoto);
        }
        finally { IsProcessing = false; }
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

    private IEnumerator ReadFrozenPixels(RenderTexture source, System.Action<Color32[]> completed)
    {
        if(SystemInfo.supportsAsyncGPUReadback)
        {
            pendingReadback=AsyncGPUReadback.Request(source,0,TextureFormat.RGBA32);readbackPending=true;
            while(!pendingReadback.done)yield return null;
            readbackPending=false;
            if(!pendingReadback.hasError){completed(pendingReadback.GetData<Color32>().ToArray());yield break;}
        }
        var active=RenderTexture.active;
        var read=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,true);
        try{RenderTexture.active=source;read.ReadPixels(new Rect(0,0,source.width,source.height),0,0,false);completed(read.GetPixels32());}
        finally{RenderTexture.active=active;Destroy(read);}
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

    private static float MeasureSubjectMtf50Retention(
        Color32[] pixels, Color32[] reference, Color32[] mask, int photoWidth, RectInt pixelRect, float fallback)
    {
        int width = pixelRect.width;
        int height = pixelRect.height;
        if (width < 7 || height < 7)
        {
            return fallback;
        }

        float[] luminance = new float[width * height];
        float[] developed = new float[width * height];
        // Integral image of non-subject pixels: every profile sample (including
        // tangent averaging) must be well inside the visible bird, never background.
        int stride=width+1;
        int[] invalid=new int[(width+1)*(height+1)];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = (pixelRect.yMin + y) * photoWidth;
            int targetRow = y * width;
            for (int x = 0; x < width; x++)
            {
                int source=sourceRow + pixelRect.xMin + x;
                Color32 pixel = reference[source];
                float red = Mathf.GammaToLinearSpace(pixel.r / 255f);
                float green = Mathf.GammaToLinearSpace(pixel.g / 255f);
                float blue = Mathf.GammaToLinearSpace(pixel.b / 255f);
                luminance[targetRow + x] =
                    red * 0.2126f +
                    green * 0.7152f +
                    blue * 0.0722f;
                Color32 output=pixels[source];
                developed[targetRow+x]=Mathf.GammaToLinearSpace(output.r/255f)*.2126f+
                    Mathf.GammaToLinearSpace(output.g/255f)*.7152f+Mathf.GammaToLinearSpace(output.b/255f)*.0722f;
                invalid[(y+1)*stride+x+1]=(mask[source].r<250?1:0)+invalid[y*stride+x+1]+invalid[(y+1)*stride+x]-invalid[y*stride+x];
            }
        }

        List<MtfEdgeCandidate> candidates =
            new List<MtfEdgeCandidate>();
        int profileRadius = Mathf.Clamp(Mathf.Min(width,height)/24,3,6);
        const int grid=6, perRegion=3;
        int[] regionArea=new int[grid*grid];
        for (int y = profileRadius; y < height - profileRadius; y++)
        {
            for (int x = profileRadius; x < width - profileRadius; x++)
            {
                int x0=x-profileRadius,y0=y-profileRadius,x1=x+profileRadius+1,y1=y+profileRadius+1;
                if(invalid[y1*stride+x1]-invalid[y0*stride+x1]-invalid[y1*stride+x0]+invalid[y0*stride+x0]!=0)continue;
                regionArea[(y*grid/height)*grid+x*grid/width]++;
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
        var regions=new List<float>[grid*grid];
        for(int r=0;r<regions.Length;r++)regions[r]=new List<float>();
        for (int i = 0;
            i < candidates.Count;
            i++)
        {
            MtfEdgeCandidate candidate = candidates[i];
            int region=(candidate.Y*grid/height)*grid+candidate.X*grid/width;
            if(regions[region].Count>=perRegion)continue;
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
                    out float referenceFrequency, profileRadius))
            {
                continue;
            }

            acceptedLocations.Add(location);
            float retention;
            if(TryMeasureEdgeMtf50(developed,width,height,candidate,out float measuredFrequency,profileRadius,.008f))
                retention=Mathf.Clamp01(measuredFrequency/Mathf.Max(.001f,referenceFrequency));
            else
            {
                // Only a genuinely flat, non-clipped patch proves a reference detail
                // vanished. Low contrast, clipping or irregular/noisy profiles are unknown.
                float low=1f,high=0f;
                for(int yy=candidate.Y-profileRadius;yy<=candidate.Y+profileRadius;yy++)
                    for(int xx=candidate.X-profileRadius;xx<=candidate.X+profileRadius;xx++)
                    {float v=developed[yy*width+xx];low=Mathf.Min(low,v);high=Mathf.Max(high,v);}
                retention=high-low<.004f && low>.015f && high<.95f ? 0f : float.NaN;
            }
            regions[region].Add(retention);
        }
        var estimates=new List<Vector2>();float totalArea=0;
        for(int r=0;r<regions.Length;r++)
        {
            if(regionArea[r]==0)continue;
            // Every interior region has a vote, including those without measurable
            // texture. A few measurable sharp regions cannot replace the rest of the bird.
            var valid=regions[r].FindAll(v=>!float.IsNaN(v));
            valid.Sort();
            float score=valid.Count==0 ? fallback : valid.Count%2==1 ? valid[valid.Count/2]
                : (valid[valid.Count/2-1]+valid[valid.Count/2])*.5f;
            // A lone edge is weak evidence; use the physical estimate for the remainder.
            float confidence=Mathf.Clamp01(valid.Count/2f);
            score=Mathf.Lerp(fallback,score,confidence);
            estimates.Add(new Vector2(Mathf.Min(fallback,score),regionArea[r]));
            totalArea+=regionArea[r];
        }
        if(totalArea==0)return fallback;
        estimates.Sort((a,b)=>a.x.CompareTo(b.x));
        // Area-weighted 10% trimmed mean: retain broad detail evidence without
        // either the worst patch or a handful of sharp tips dominating the result.
        float start=totalArea*.1f,end=totalArea*.9f,cumulative=0,sum=0,weightSum=0;
        foreach(var estimate in estimates)
        {
            float next=cumulative+estimate.y;
            float weight=Mathf.Max(0,Mathf.Min(end,next)-Mathf.Max(start,cumulative));
            sum+=weight*estimate.x;weightSum+=weight;cumulative=next;
        }
        return weightSum>0?Mathf.Clamp01(sum/weightSum):fallback;
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

    private static bool TryMeasureEdgeMtf50(
        float[] luminance,
        int width,
        int height,
        MtfEdgeCandidate candidate,
        out float mtf50, int radius=6, float minimumContrast=.04f)
    {
        mtf50 = 0f;
        Vector2 normal = new Vector2(
            candidate.GradientX,
            candidate.GradientY).normalized;
        Vector2 tangent = new Vector2(-normal.y, normal.x);
        int profileCount = 4*(radius-1)+1;
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
        if (Mathf.Abs(edgeContrast) < minimumContrast)
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

    private void EnsureShutterSnapshot()
    {
        if (shutterSnapshot != null && shutterSnapshot.Width == photoWidth && shutterSnapshot.Height == photoHeight) return;
        shutterSnapshot?.Dispose();
        shutterSnapshot = new PhotoShutterSnapshot(photoWidth, photoHeight);
    }

    private void EnsureExposureReadback()
    {
        // Metering needs brightness statistics, not a second full-resolution photo.
        int width = Mathf.Max(1, photoWidth / 8), height = Mathf.Max(1, photoHeight / 8);
        if (exposureReadback != null && exposureReadback.width == width && exposureReadback.height == height) return;
        if (exposureReadback != null) Destroy(exposureReadback);
        if (meterTarget != null) { meterTarget.Release(); Destroy(meterTarget); }
        meterTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        { name = "PhotoMeter", hideFlags = HideFlags.HideAndDontSave };
        meterTarget.Create();
        exposureReadback = new Texture2D(width, height, TextureFormat.RGB24, false)
        { name = "HunterExposureReadback", hideFlags = HideFlags.HideAndDontSave };
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

    private void EnsureExposureAccumulator()
    {
        if (exposureAccumulator == null)
        {
            var shader = Resources.Load<Shader>("PhotoExposureAccumulate");
            if (shader == null || !shader.isSupported)
                throw new System.InvalidOperationException("Photo accumulation shader unavailable.");
            exposureAccumulator = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

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
        float minimumVisibilityFraction,
        Rect subjectBounds,
        bool subjectInFrame)
    {
        // Keep a single album/UI, while each photographer owns its capture buffers.
        if (Instance != null && Instance != this)
        {
            Instance.StorePhoto(photo, accepted, capturedPlayer, mtf50Retention, blurLengthPixels,
                exposureStops, usableExposure, signalToNoiseRatioDecibels, noiseReductionMode,
                usableNoiseQuality, focusDistanceMeters, defocusBlurDiameterPixels, exposureMode,
                autoIso, cameraSettings, subjectFrameCoverage, minimumFrameCoverage,
                subjectVisibilityFraction, minimumVisibilityFraction, subjectBounds, subjectInFrame);
            return;
        }
        lastCaptureTime = Time.unscaledTime;
        photos.Add(photo);
        photoAccepted.Add(accepted);
        photoCapturedPlayers.Add(capturedPlayer);
        photoSubjectsInFrame.Add(subjectInFrame);
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
        photoSubjectBounds.Add(subjectBounds);
        photoMinimumFrameCoverages.Add(minimumFrameCoverage);
        photoSubjectVisibilityFractions.Add(subjectVisibilityFraction);
        photoMinimumVisibilityFractions.Add(minimumVisibilityFraction);
        while (photos.Count > maxStoredPhotos)
        {
            Texture2D oldest = photos[0];
            photos.RemoveAt(0);
            photoAccepted.RemoveAt(0);
            photoCapturedPlayers.RemoveAt(0);
            photoSubjectsInFrame.RemoveAt(0);
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
            photoSubjectBounds.RemoveAt(0);
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

    public void CloseBackpack() { SetBackpackOpen(false); }
    public void OpenBackpack() { SetBackpackOpen(true); }

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
            cameraController.SetCursorCaptured(!open && !GameFlow.BlocksGameplay);
        }
        else
        {
            bool capture = !open && !GameFlow.BlocksGameplay;
            Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !capture;
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

    private static float MeterSavedPixels(Color32[] pixels, int meterWidth, int meterHeight,
        float calibratedSceneExposureValue100)
    {
        double weightedLogLuminance = 0.0;
        double totalWeight = 0.0;
        const int sampleStride = 1;
        for (int y = sampleStride / 2; y < meterHeight; y += sampleStride)
        {
            float normalizedY =
                (y + 0.5f) / meterHeight * 2f - 1f;
            for (int x = sampleStride / 2; x < meterWidth; x += sampleStride)
            {
                float normalizedX =
                    (x + 0.5f) / meterWidth * 2f - 1f;
                float radius = Mathf.Sqrt(
                    normalizedX * normalizedX +
                    normalizedY * normalizedY);
                float weight = Mathf.Lerp(
                    0.2f,
                    1f,
                    1f - Mathf.Clamp01(radius));
                Color32 pixel = pixels[y * meterWidth + x];
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
        if (Instance != this) return;
        if(GameFlow.BlocksGameplay && !(ReviewingResult && backpackOpen))return;
        Font oldFont=GUI.skin.font;
        if(GameLanguage.Font!=null)GUI.skin.font=GameLanguage.Font;
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
        GUI.skin.font=oldFont;
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
        if (AnyProcessing && !backpackOpen)
        {
            GUI.Box(new Rect(screenWidth - 306f, screenHeight - 82f, 290f, 54f),
                T("照片处理中…\n可以继续移动", "Processing photo…\nKeep playing"));
            return;
        }
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
            T("摄影师的照片", "Photographer Backpack")+$"  {photos.Count}/{maxStoredPhotos}"+(AnyProcessing?T(" · 处理中…", " · Processing…"):string.Empty),
            overlayTitleStyle);
        GUI.Label(
            new Rect(panel.xMax - 360f, panel.y + 20f, 336f, 28f),
            ReviewingResult ? T("点击照片放大    B / Esc 返回结算", "Click to enlarge    B / Esc Results") : T("点击照片放大    B 关闭", "Click a photo to enlarge    B Close"),
            overlayHintStyle);

        if(ReviewingResult && GUI.Button(new Rect(panel.center.x-100f,panel.yMax-58f,200f,36f),T("返回结算","Back to results")))
        {
            CloseBackpack();return;
        }

        if (photos.Count == 0)
        {
            GUI.Label(
                new Rect(panel.x, panel.y + 80f, panel.width, 80f),
                T("还没有照片。摄影师拍摄后，照片会出现在这里。", "No photos yet. The photographer's shots will appear here."),
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
                T("照片", "Photo")+$" {photoIndex + 1}  {GetPhotoResultLabel(photoIndex)}");
            GUI.Label(
                new Rect(cell.x, cell.yMax + 22f, cell.width, 20f),
                GetPhotoSettingsLabel(photoIndex));
        }

        if (pageCount > 1)
        {
            float navY = panel.yMax - 56f;
            GUI.enabled = backpackPage > 0;
            if (GUI.Button(new Rect(panel.x + 28f, navY, 90f, 32f), T("上一页", "Prev")))
            {
                backpackPage--;
            }

            GUI.enabled = backpackPage < pageCount - 1;
            if (GUI.Button(new Rect(panel.xMax - 118f, navY, 90f, 32f), T("下一页", "Next")))
            {
                backpackPage++;
            }

            GUI.enabled = true;
            GUI.Label(
                new Rect(panel.x, ReviewingResult ? navY-32f : navY, panel.width, 32f),
                T("页码", "Page")+$" {backpackPage + 1}/{pageCount}",
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
        float photoAspect = photo.width / (float)photo.height;
        float height = width / photoAspect;
        if (height > maxHeight)
        {
            height = maxHeight;
            width = height * photoAspect;
        }

        Rect photoRect = new Rect(
            (screenWidth - width) * 0.5f,
            (screenHeight - height) * 0.5f - 10f,
            width,
            height);
        GUI.Box(new Rect(photoRect.x - 8f, photoRect.y - 8f, photoRect.width + 16f, photoRect.height + 16f), string.Empty);
        GUI.DrawTexture(photoRect, photo, ScaleMode.ScaleToFit);
        Rect subjectBounds = photoSubjectBounds[photoIndex];
        showSubjectBounds = GUI.Toggle(new Rect(photoRect.x, photoRect.y - 36f, 220f, 26f),
            showSubjectBounds, T("显示主体构图框", "Show composition bounds"));
        if (showSubjectBounds && subjectBounds.width > 0f && subjectBounds.height > 0f)
        {
            Rect box = new Rect(photoRect.x + subjectBounds.x * photoRect.width,
                photoRect.y + (1f - subjectBounds.yMax) * photoRect.height,
                subjectBounds.width * photoRect.width, subjectBounds.height * photoRect.height);
            Color previous = GUI.color;
            GUI.color = Color.cyan;
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.yMax - 2f, box.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.y, 2f, box.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.xMax - 2f, box.y, 2f, box.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }
        string autoIsoLabel =
            photoAutoIsoStates[photoIndex]
                ? T(" + 自动 ISO", " + Auto ISO")
                : string.Empty;
        GUI.Label(
            new Rect(
                photoRect.x,
                photoRect.yMax + 12f,
                photoRect.width,
                24f),
            ExposureModeName(photoExposureModes[photoIndex])+$"{autoIsoLabel}    " +
            $"1/{photoShutterSpeeds[photoIndex]:F0}s    " +
            $"f/{photoApertures[photoIndex]:F1}    " +
            $"ISO {photoIsoValues[photoIndex]:F0}    " +
            T("对焦 ", "Focus ")+$"{photoFocusDistances[photoIndex]:F1}m",
            overlayHintStyle);
        GUI.Label(
            new Rect(
                photoRect.x,
                photoRect.yMax + 34f,
                photoRect.width,
                24f),
            $"{GetPhotoResultLabel(photoIndex)}    " +
            T("占比 ", "Area ")+$"{photoSubjectFrameCoverages[photoIndex]:P1} [W {subjectBounds.width:P1} x H {subjectBounds.height:P1}]    " +
            T("可见 ", "Visible ")+$"{photoSubjectVisibilityFractions[photoIndex]:P0} / " +
            T("遮挡 ", "Occluded ")+$"{1f - photoSubjectVisibilityFractions[photoIndex]:P0}    " +
            T("运动模糊 ", "Motion ")+$"{photoBlurLengths[photoIndex]:F1}px    " +
            T("失焦 ", "Defocus ")+$"{photoDefocusBlurDiameters[photoIndex]:F1}px",
            overlayHintStyle);

        Rect backButtonRect = new Rect(photoRect.xMax - 90f, photoRect.y - 44f, 90f, 32f);
        float scale = Mathf.Clamp(Screen.height / 720f, 0.8f, 1.5f);
        Vector2 mouse = Event.current.mousePosition;
        if (Event.current.type == EventType.MouseDown &&
            !photoRect.Contains(mouse) &&
            !backButtonRect.Contains(mouse))
        {
            selectedPhotoIndex = -1;
            Event.current.Use();
            return;
        }

        if (GUI.Button(backButtonRect, T("返回", "Back")))
        {
            selectedPhotoIndex = -1;
        }
    }

    private static int GetPhotosPerPage()
    {
        return 8;
    }

    private static string ExposureModeName(PhotoExposureMode mode)
    {
        switch(mode)
        {
            case PhotoExposureMode.AperturePriority:return T("光圈优先","Aperture priority");
            case PhotoExposureMode.ShutterPriority:return T("快门优先","Shutter priority");
            case PhotoExposureMode.ProgramAuto:return T("程序自动","Program auto");
            default:return T("手动","Manual");
        }
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
            photoAutoIsoStates[photoIndex] ? T("+自动ISO","+AutoISO") : string.Empty;
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
                    ? T("已降噪  ","DENOISED  ")+T("清晰度保留率 ","Sharpness retained ")+$"{photoMtf50Retentions[photoIndex]:P0}"
                    : T("清晰度保留率 ","Sharpness retained ")+$"{photoMtf50Retentions[photoIndex]:P0}  " +
                        $"SNR {photoSignalToNoiseRatios[photoIndex]:F0}dB";
            return
                T("合格  ","CLEAR  ")+$"{quality}  " +
                GetExposureLabel(photoIndex);
        }

        if (!photoCapturedPlayers[photoIndex])
        {
            if (photoSubjectsInFrame[photoIndex])
            {
                return
                    T("失败  遮挡 ","FAILED  OCCLUDED ") +
                    $"{photoSubjectVisibilityFractions[photoIndex]:P0} " +
                    $"<{photoMinimumVisibilityFractions[photoIndex]:P0}  " +
                    GetExposureLabel(photoIndex);
            }

            return
                T("失败  未拍到玩家  ","FAILED  NO PLAYER  ") +
                GetExposureLabel(photoIndex);
        }

        if (photoSubjectFrameCoverages[photoIndex] <
            photoMinimumFrameCoverages[photoIndex])
        {
            return
                T("失败  主体占比 ","FAILED  SUBJECT FRAME ") +
                $"{photoSubjectFrameCoverages[photoIndex]:P1} " +
                $"<{photoMinimumFrameCoverages[photoIndex]:P1}  " +
                GetExposureLabel(photoIndex);
        }

        if (!photoUsableExposures[photoIndex])
        {
            return
                T("失败  曝光  ","FAILED  EXPOSURE  ") +
                GetExposureLabel(photoIndex);
        }

        if (!photoUsableNoiseQuality[photoIndex])
        {
            return
                T("失败  信噪比 ","FAILED  SNR ") +
                $"{photoSignalToNoiseRatios[photoIndex]:F1}dB  " +
                GetExposureLabel(photoIndex);
        }

        string denoiseLabel =
            photoNoiseReductionModes[photoIndex] ==
            PhotoNoiseReductionMode.Enabled
                ? T("已降噪  ","DENOISED  ")
                : string.Empty;
        return
            T("失败  ","FAILED  ")+$"{denoiseLabel}" +
            T("清晰度保留率 ","Sharpness retained ")+$"{photoMtf50Retentions[photoIndex]:P0}  " +
            $"{photoBlurLengths[photoIndex]:F1}px  " +
            GetExposureLabel(photoIndex);
    }

    private string GetExposureLabel(int photoIndex)
    {
        float stops = photoExposureStops[photoIndex];
        if (stops > 0.05f)
        {
            return T("过曝 ","OVER ")+$"+{stops:F1} EV";
        }

        if (stops < -0.05f)
        {
            return T("欠曝 ","UNDER ")+$"{stops:F1} EV";
        }

        return T("曝光 0.0 EV","EXPOSURE 0.0 EV");
    }
}
