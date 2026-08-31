using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float targetHeight = 1.2f;
    [SerializeField] private float distance = 6f;
    [SerializeField] private float mouseSensitivity = 3f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 65f;
    [SerializeField] private float positionSmoothTime = 0.08f;
    [SerializeField] private float collisionRadius = 0.25f;
    [SerializeField] private LayerMask collisionLayers = ~0;

    private float yaw;
    private float pitch = 18f;
    private Vector3 positionVelocity;
    private bool cursorCaptured = true;

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

        Vector3 pivot = target.position + Vector3.up * targetHeight;
        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = pivot + orbitRotation * Vector3.back * distance;
        Vector3 direction = desiredPosition - pivot;
        float allowedDistance = FindAllowedDistance(pivot, direction.normalized, direction.magnitude);
        desiredPosition = pivot + direction.normalized * allowedDistance;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref positionVelocity,
            positionSmoothTime);
        transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
    }

    private void FindTarget()
    {
        if (target == null && PlayerMove.Instance != null)
        {
            target = PlayerMove.Instance.transform;
        }
    }

    private void PlaceCameraImmediately()
    {
        Vector3 pivot = target.position + Vector3.up * targetHeight;
        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 direction = orbitRotation * Vector3.back;
        float allowedDistance = FindAllowedDistance(pivot, direction, distance);
        transform.position = pivot + direction * allowedDistance;
        transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
        positionVelocity = Vector3.zero;
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
            collisionRadius,
            direction,
            desiredDistance,
            collisionLayers,
            QueryTriggerInteraction.Ignore);

        float closestDistance = desiredDistance;
        foreach (RaycastHit hit in hits)
        {
            if (target != null && hit.transform.IsChildOf(target))
            {
                continue;
            }

            closestDistance = Mathf.Min(closestDistance, Mathf.Max(0.4f, hit.distance - 0.1f));
        }

        return closestDistance;
    }

    private void SetCursorCaptured(bool captured)
    {
        cursorCaptured = captured;
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
    }
}
