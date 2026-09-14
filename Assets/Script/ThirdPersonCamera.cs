using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ThirdPersonCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [Header("Over-shoulder View")]
    [SerializeField] private float focusHeight = 0.09f;
    [SerializeField] private float cameraDistance = 0.85f;
    [SerializeField] private float shoulderOffset = 0.16f;
    [SerializeField] private float lookAheadDistance = 0.28f;
    [SerializeField] private float fieldOfView = 68f;

    [Header("Orbit")]
    [SerializeField] private float mouseSensitivity = 3f;
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 72f;

    [Header("Camera Collision")]
    [SerializeField] private float positionSmoothTime = 0.045f;
    [SerializeField] private float cameraCollisionRadius = 0.07f;
    [SerializeField] private float collisionPadding = 0.025f;
    [SerializeField] private LayerMask collisionLayers = ~0;

    [Header("Player Fade")]
    [SerializeField] private float fadeStartDistance = 0.24f;
    [SerializeField] private float fadeEndDistance = 0.02f;
    [SerializeField, Range(0f, 1f)] private float minimumPlayerAlpha = 0.2f;
    [SerializeField] private float playerFadeSpeed = 5f;

    private float yaw;
    private float pitch = 18f;
    private Vector3 positionVelocity;
    private bool cursorCaptured = true;
    private Transform cachedFadeTarget;
    private Renderer[] targetRenderers;
    private readonly List<FadeMaterial> fadeMaterials = new List<FadeMaterial>();
    private float currentPlayerAlpha = 1f;

    private sealed class FadeMaterial
    {
        public Material Material;
        public int ColorProperty;
        public Color OriginalColor;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToActiveMainCamera()
    {
        Camera activeCamera = Camera.main;
        if (activeCamera == null)
        {
            return;
        }

        ThirdPersonCamera controller = activeCamera.GetComponent<ThirdPersonCamera>();
        if (controller == null)
        {
            controller = activeCamera.gameObject.AddComponent<ThirdPersonCamera>();
        }

        controller.DisableConflictingCameraControls();
    }

    private void Awake()
    {
        DisableConflictingCameraControls();
        Camera attachedCamera = GetComponent<Camera>();
        if (attachedCamera != null)
        {
            attachedCamera.fieldOfView = fieldOfView;
            attachedCamera.nearClipPlane = Mathf.Min(attachedCamera.nearClipPlane, 0.08f);
        }
    }

    private void Start()
    {
        FindTarget();
        if (target != null)
        {
            yaw = target.eulerAngles.y;
            PlaceCameraImmediately();
        }

        SetCursorCaptured(true);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SetCursorCaptured(!cursorCaptured);
        }

        if (cursorCaptured && (GameManager.Instance == null || GameManager.Instance.IsRunning))
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }
    }

    private void LateUpdate()
    {
        FindTarget();
        if (target == null)
        {
            return;
        }

        CalculateCameraPose(out Vector3 focusPoint, out Vector3 lookPoint, out Vector3 desiredPosition);

        Vector3 smoothedPosition = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref positionVelocity,
            positionSmoothTime);
        transform.position = ClampCameraPosition(focusPoint, smoothedPosition);

        Vector3 lookDirection = lookPoint - transform.position;
        if (lookDirection.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        }

        UpdatePlayerFade();
    }

    private void FindTarget()
    {
        if (target == null && PlayerMove.Instance != null)
        {
            target = PlayerMove.Instance.transform;
        }

        if (target != cachedFadeTarget)
        {
            CachePlayerFadeMaterials();
        }
    }

    private void PlaceCameraImmediately()
    {
        CalculateCameraPose(out Vector3 focusPoint, out Vector3 lookPoint, out Vector3 desiredPosition);
        transform.position = ClampCameraPosition(focusPoint, desiredPosition);
        transform.rotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
        positionVelocity = Vector3.zero;
    }

    private void CalculateCameraPose(
        out Vector3 focusPoint,
        out Vector3 lookPoint,
        out Vector3 desiredPosition)
    {
        focusPoint = target.position + Vector3.up * focusHeight;
        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 shoulder = orbitRotation * Vector3.right * shoulderOffset;
        Vector3 backward = orbitRotation * Vector3.back * cameraDistance;
        desiredPosition = focusPoint + shoulder + backward;

        Vector3 direction = desiredPosition - focusPoint;
        float allowedDistance = FindAllowedDistance(
            focusPoint,
            direction.normalized,
            direction.magnitude);
        desiredPosition = focusPoint + direction.normalized * allowedDistance;
        lookPoint = focusPoint + orbitRotation * Vector3.forward * lookAheadDistance;
    }

    private Vector3 ClampCameraPosition(Vector3 focusPoint, Vector3 candidatePosition)
    {
        Vector3 direction = candidatePosition - focusPoint;
        float distanceToCandidate = direction.magnitude;
        if (distanceToCandidate < 0.001f)
        {
            return candidatePosition;
        }

        float allowedDistance = FindAllowedDistance(
            focusPoint,
            direction / distanceToCandidate,
            distanceToCandidate);
        return focusPoint + direction.normalized * allowedDistance;
    }

    private void DisableConflictingCameraControls()
    {
        MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null || behaviour == this)
            {
                continue;
            }

            if (behaviour.GetType().FullName == "Gaia.FreeCamera")
            {
                behaviour.enabled = false;
            }
        }
    }

    private float FindAllowedDistance(Vector3 origin, Vector3 direction, float desiredDistance)
    {
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            cameraCollisionRadius,
            direction,
            desiredDistance,
            collisionLayers,
            QueryTriggerInteraction.Ignore);

        float closestDistance = desiredDistance;
        foreach (RaycastHit hit in hits)
        {
            if (target != null &&
                (hit.transform == target ||
                 hit.transform.IsChildOf(target) ||
                 hit.collider.GetComponentInParent<PlayerMove>() != null))
            {
                continue;
            }

            closestDistance = Mathf.Min(
                closestDistance,
                Mathf.Max(0.01f, hit.distance - collisionPadding));
        }

        Vector3 candidate = origin + direction * closestDistance;
        if (!HasBlockingOverlap(candidate))
        {
            return closestDistance;
        }

        float safeDistance = 0f;
        float blockedDistance = closestDistance;
        for (int i = 0; i < 10; i++)
        {
            float testDistance = (safeDistance + blockedDistance) * 0.5f;
            Vector3 testPosition = origin + direction * testDistance;
            if (HasBlockingOverlap(testPosition))
            {
                blockedDistance = testDistance;
            }
            else
            {
                safeDistance = testDistance;
            }
        }

        closestDistance = Mathf.Max(0.01f, safeDistance - collisionPadding);
        return closestDistance;
    }

    private bool HasBlockingOverlap(Vector3 position)
    {
        Collider[] overlaps = Physics.OverlapSphere(
            position,
            cameraCollisionRadius + collisionPadding,
            collisionLayers,
            QueryTriggerInteraction.Ignore);

        foreach (Collider overlap in overlaps)
        {
            if (IsPlayerCollider(overlap))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool IsPlayerCollider(Collider candidate)
    {
        return target != null &&
               (candidate.transform == target ||
                candidate.transform.IsChildOf(target) ||
                candidate.GetComponentInParent<PlayerMove>() != null);
    }

    private void CachePlayerFadeMaterials()
    {
        cachedFadeTarget = target;
        targetRenderers = target == null
            ? null
            : target.GetComponentsInChildren<Renderer>(true);
        fadeMaterials.Clear();
        currentPlayerAlpha = 1f;

        if (targetRenderers == null)
        {
            return;
        }

        foreach (Renderer targetRenderer in targetRenderers)
        {
            Material[] materials = targetRenderer.materials;
            foreach (Material material in materials)
            {
                int colorProperty = GetColorProperty(material);
                if (colorProperty == -1)
                {
                    continue;
                }

                fadeMaterials.Add(new FadeMaterial
                {
                    Material = material,
                    ColorProperty = colorProperty,
                    OriginalColor = material.GetColor(colorProperty)
                });
                ConfigureTransparentMaterial(material);
            }
        }
    }

    private static int GetColorProperty(Material material)
    {
        int baseColor = Shader.PropertyToID("_BaseColor");
        if (material.HasProperty(baseColor))
        {
            return baseColor;
        }

        int color = Shader.PropertyToID("_Color");
        return material.HasProperty(color) ? color : -1;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private void UpdatePlayerFade()
    {
        if (targetRenderers == null || fadeMaterials.Count == 0)
        {
            return;
        }

        float closestRendererDistance = float.PositiveInfinity;
        foreach (Renderer targetRenderer in targetRenderers)
        {
            if (targetRenderer == null || !targetRenderer.enabled)
            {
                continue;
            }

            float distance = Mathf.Sqrt(targetRenderer.bounds.SqrDistance(transform.position));
            closestRendererDistance = Mathf.Min(closestRendererDistance, distance);
        }

        float fade01 = Mathf.InverseLerp(
            fadeEndDistance,
            fadeStartDistance,
            closestRendererDistance);
        float targetAlpha = Mathf.Lerp(minimumPlayerAlpha, 1f, fade01);
        currentPlayerAlpha = Mathf.MoveTowards(
            currentPlayerAlpha,
            targetAlpha,
            playerFadeSpeed * Time.deltaTime);

        foreach (FadeMaterial fadeMaterial in fadeMaterials)
        {
            if (fadeMaterial.Material == null)
            {
                continue;
            }

            Color color = fadeMaterial.OriginalColor;
            color.a *= currentPlayerAlpha;
            fadeMaterial.Material.SetColor(fadeMaterial.ColorProperty, color);
        }
    }

    private void SetCursorCaptured(bool captured)
    {
        cursorCaptured = captured;
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
    }
}
