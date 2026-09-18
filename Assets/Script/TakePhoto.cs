using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class TakePhoto : MonoBehaviour
{
    public static TakePhoto Instance { get; private set; }
    public static bool IsBackpackOpen => Instance != null && Instance.backpackOpen;

    [Header("Capture")]
    [SerializeField] private int photoWidth = 1280;
    [SerializeField] private int photoHeight = 720;
    [SerializeField] private float photoFieldOfView = 55f;
    [SerializeField] private float cameraForwardOffset = 0.18f;
    [SerializeField] private int maxStoredPhotos = 30;

    private const float FullFrameSensorWidthMillimeters = 36f;
    private const float FullFrameSensorHeightMillimeters = 24f;
    private const int MinimumExposureSamples = 2;
    private const int MaximumExposureSamples = 24;
    private const float BlurPixelsPerSample = 2f;

    private readonly List<Texture2D> photos = new List<Texture2D>();
    private readonly List<bool> photoAccepted = new List<bool>();
    private readonly List<bool> photoCapturedPlayers = new List<bool>();
    private readonly List<float> photoMtf50Retentions = new List<float>();
    private readonly List<float> photoBlurLengths = new List<float>();
    private readonly List<bool> hunterRendererStates = new List<bool>();
    private readonly List<Renderer> hunterRenderers = new List<Renderer>();

    private Camera photoCamera;
    private RenderTexture photoTarget;
    private Texture2D exposureReadback;
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
        float sharpMtf50,
        float minimumMtf50Retention)
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
        PhotoBlurPhysics.Solution blurSolution =
            PhotoBlurPhysics.Solve(
                cameraPosition,
                cameraRotation,
                cameraLinearVelocity,
                cameraAngularVelocity,
                subjectPosition,
                subjectVelocity,
                focalLengthMillimeters,
                shutterSpeed,
                sensorPixelPitchMicrometers,
                GetEffectiveSensorHeightMillimeters(),
                photoHeight,
                motionBlurScale);
        int sampleCount = blurSolution.SamplingBlurLengthPixels > 0.01f
            ? CalculateExposureSampleCount(
                blurSolution.SamplingBlurLengthPixels)
            : 1;
        float subjectImageTravelPixels =
            blurSolution.BlurLengthPixels;

        HideHunterFromCapture();
        RenderTexture previousActive = RenderTexture.active;
        Texture2D photo = null;
        try
        {
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

                photoCamera.transform.SetPositionAndRotation(
                    cameraPosition +
                    cameraLinearVelocity * renderedSampleTime,
                    PhotoBlurPhysics.IntegrateAngularVelocity(
                        cameraRotation,
                        cameraAngularVelocity,
                        renderedSampleTime));

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

            photo = CreateExposurePhoto(red, green, blue, sampleCount);
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
            RestoreHunterAfterCapture();
        }

        if (photo == null)
        {
            return false;
        }

        bool capturedPlayer = IsSubjectVisibleInPhoto(subject);
        float mtf50Retention = CalculateMtf50Retention(
            subjectImageTravelPixels,
            sharpMtf50);
        bool successfulPhoto =
            capturedPlayer &&
            mtf50Retention >= minimumMtf50Retention;
        StorePhoto(
            photo,
            successfulPhoto,
            capturedPlayer,
            mtf50Retention,
            subjectImageTravelPixels);
        lastCaptureTime = Time.unscaledTime;
        return successfulPhoto;
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
        int sampleCount)
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
        float blurLengthPixels)
    {
        photos.Add(photo);
        photoAccepted.Add(accepted);
        photoCapturedPlayers.Add(capturedPlayer);
        photoMtf50Retentions.Add(mtf50Retention);
        photoBlurLengths.Add(blurLengthPixels);
        while (photos.Count > maxStoredPhotos)
        {
            Texture2D oldest = photos[0];
            photos.RemoveAt(0);
            photoAccepted.RemoveAt(0);
            photoCapturedPlayers.RemoveAt(0);
            photoMtf50Retentions.RemoveAt(0);
            photoBlurLengths.RemoveAt(0);
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

        photoCamera.targetTexture = photoTarget;
        UniversalAdditionalCameraData photoData = photoCamera.GetUniversalAdditionalCameraData();
        photoData.renderType = CameraRenderType.Base;
        photoData.renderPostProcessing = true;
        photoData.renderShadows = true;
        photoCamera.targetTexture = null;
    }

    private float GetEffectiveSensorHeightMillimeters()
    {
        float outputAspect = photoWidth / (float)photoHeight;
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
        photoData.renderPostProcessing = mainData.renderPostProcessing;
        photoData.volumeLayerMask = mainData.volumeLayerMask;
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
        float toastWidth = 288f;
        float toastHeight = toastWidth * 9f / 16f;
        Rect toastRect = new Rect(
            screenWidth - toastWidth - 16f,
            screenHeight - toastHeight - 28f,
            toastWidth,
            toastHeight);
        GUI.Box(toastRect, string.Empty);
        GUI.DrawTexture(
            new Rect(toastRect.x + 6f, toastRect.y + 6f, toastRect.width - 12f, toastRect.height - 28f),
            latest,
            ScaleMode.ScaleToFit);
        GUI.Label(
            new Rect(toastRect.x + 8f, toastRect.yMax - 24f, toastRect.width - 16f, 20f),
            GetPhotoResultLabel(photos.Count - 1));
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
                gridTop + row * (cellHeight + yGap + 22f),
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
            DrawEnlargedPhoto(screenWidth, screenHeight, photos[selectedPhotoIndex]);
        }
    }

    private void DrawEnlargedPhoto(float screenWidth, float screenHeight, Texture2D photo)
    {
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
            return $"CLEAR  MTF50 {photoMtf50Retentions[photoIndex]:P0}";
        }

        if (!photoCapturedPlayers[photoIndex])
        {
            return "FAILED  NO PLAYER";
        }

        return
            $"FAILED  MTF50 {photoMtf50Retentions[photoIndex]:P0}  " +
            $"{photoBlurLengths[photoIndex]:F1}px";
    }
}
