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

    private readonly List<Texture2D> photos = new List<Texture2D>();
    private readonly List<bool> hunterRendererStates = new List<bool>();
    private readonly List<Renderer> hunterRenderers = new List<Renderer>();

    private Camera photoCamera;
    private RenderTexture photoTarget;
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

        for (int i = 0; i < photos.Count; i++)
        {
            if (photos[i] != null)
            {
                Destroy(photos[i]);
            }
        }

        photos.Clear();
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

    public void Capture(Vector3 origin, Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        EnsurePhotoCamera();
        ConfigurePhotoCameraFromMain();

        Vector3 lookDirection = direction.normalized;
        photoCamera.transform.SetPositionAndRotation(
            origin + lookDirection * cameraForwardOffset,
            Quaternion.LookRotation(lookDirection, Vector3.up));

        HideHunterFromCapture();
        RenderTexture previousActive = RenderTexture.active;
        Texture2D photo = null;
        try
        {
            photoCamera.targetTexture = photoTarget;
            photoCamera.Render();

            RenderTexture.active = photoTarget;
            photo = new Texture2D(photoWidth, photoHeight, TextureFormat.RGB24, false)
            {
                name = $"HunterPhoto_{photos.Count + 1}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            photo.ReadPixels(new Rect(0f, 0f, photoWidth, photoHeight), 0, 0);
            photo.Apply(false, false);
        }
        finally
        {
            RenderTexture.active = previousActive;
            photoCamera.targetTexture = null;
            RestoreHunterAfterCapture();
        }

        if (photo == null)
        {
            return;
        }

        StorePhoto(photo);
        lastCaptureTime = Time.unscaledTime;
    }

    private void StorePhoto(Texture2D photo)
    {
        photos.Add(photo);
        while (photos.Count > maxStoredPhotos)
        {
            Texture2D oldest = photos[0];
            photos.RemoveAt(0);
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

    private void ConfigurePhotoCameraFromMain()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            photoCamera.fieldOfView = photoFieldOfView;
            return;
        }

        photoCamera.farClipPlane = mainCamera.farClipPlane;
        photoCamera.allowHDR = mainCamera.allowHDR;
        photoCamera.clearFlags = mainCamera.clearFlags;
        photoCamera.backgroundColor = mainCamera.backgroundColor;
        photoCamera.cullingMask = mainCamera.cullingMask;
        photoCamera.fieldOfView = photoFieldOfView;
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
            "Photo saved to backpack");
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
                $"Photo {photoIndex + 1}");
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
}
