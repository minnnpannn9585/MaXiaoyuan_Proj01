using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class HunterMove : MonoBehaviour
{
    private enum HunterState
    {
        Search,
        Aim,
        Cooldown
    }

    [Header("Search")]
    [SerializeField] private float patrolSpeed = 2.4f;
    [SerializeField] private float patrolTurnSpeed = 8f;
    [SerializeField] private float waypointTolerance = 1f;
    [SerializeField] private float fallbackMapHalfExtent = 18f;

    [Header("Vision")]
    [SerializeField] private float visionDistance = 22f;
    [SerializeField, Range(10f, 180f)] private float fieldOfView = 100f;
    [SerializeField] private float eyeHeight = 0.65f;
    [SerializeField] private float lostSightGrace = 0.45f;
    [SerializeField] private LayerMask visionLayers = ~0;

    [Header("Aim and fire")]
    [SerializeField] private float aimDuration = 1.65f;
    [SerializeField] private float stationaryAimFollowTime = 0.08f;
    [SerializeField] private float fastTargetFollowPenalty = 0.55f;
    [SerializeField] private float lockedWarningDuration = 0.45f;
    [SerializeField] private float shotCooldown = 0.9f;
    [SerializeField, Range(3f, 100f)] private float bulletSpeed = 12f;
    [SerializeField] private float bulletGravity = 0f;
    [SerializeField] private float bulletLifetime = 4.5f;

    private Rigidbody body;
    private PlayerMove player;
    private HunterState state;
    private Vector3 patrolTarget;
    private Vector3 aimPoint;
    private Vector3 aimVelocity;
    private Bounds patrolBounds;
    private float stateTimer;
    private float lostSightTimer;
    private bool aimLocked;
    private Transform warningTransform;
    private Mesh warningMesh;
    private Material warningMaterial;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.drag = 2f;

        ConfigurePatrolBounds();
        CreateWarningIndicator();
    }

    private void Start()
    {
        FindPlayer();
        EnterSearch();
    }

    private void OnDestroy()
    {
        if (warningMesh != null)
        {
            Destroy(warningMesh);
        }

        if (warningMaterial != null)
        {
            Destroy(warningMaterial);
        }
    }

    private void Update()
    {
        FindPlayer();
        if (player == null || (GameManager.Instance != null && !GameManager.Instance.IsRunning))
        {
            SetWarningVisible(false);
            return;
        }

        switch (state)
        {
            case HunterState.Search:
                UpdateSearch();
                break;
            case HunterState.Aim:
                UpdateAim();
                break;
            case HunterState.Cooldown:
                UpdateCooldown();
                break;
        }
    }

    private void FixedUpdate()
    {
        bool gameRunning = GameManager.Instance == null || GameManager.Instance.IsRunning;
        if (!gameRunning)
        {
            StopHorizontalMovement();
            return;
        }

        if (state == HunterState.Search)
        {
            MoveAlongPatrol();
        }
        else
        {
            StopHorizontalMovement();
        }
    }

    private void LateUpdate()
    {
        if (warningTransform != null && warningTransform.gameObject.activeSelf && Camera.main != null)
        {
            Vector3 awayFromCamera = warningTransform.position - Camera.main.transform.position;
            if (awayFromCamera.sqrMagnitude > 0.001f)
            {
                warningTransform.rotation = Quaternion.LookRotation(awayFromCamera, Vector3.up);
            }
        }
    }

    private void UpdateSearch()
    {
        SetWarningVisible(false);
        if (CanSeePlayer())
        {
            EnterAim();
            return;
        }

        Vector3 flatOffset = patrolTarget - transform.position;
        flatOffset.y = 0f;
        if (flatOffset.sqrMagnitude <= waypointTolerance * waypointTolerance || PathAheadIsBlocked())
        {
            ChoosePatrolTarget();
        }
    }

    private void UpdateAim()
    {
        SetWarningVisible(true);
        stateTimer += Time.deltaTime;

        if (!aimLocked)
        {
            SetWarningColor(new Color(0.1f, 1f, 0.15f));

            if (CanSeePlayer())
            {
                lostSightTimer = 0f;
            }
            else
            {
                lostSightTimer += Time.deltaTime;
                if (lostSightTimer >= lostSightGrace)
                {
                    EnterSearch();
                    return;
                }
            }

            float followTime = stationaryAimFollowTime + player.NormalizedSpeed * fastTargetFollowPenalty;
            aimPoint = Vector3.SmoothDamp(
                aimPoint,
                player.transform.position,
                ref aimVelocity,
                followTime);

            if (stateTimer >= aimDuration)
            {
                aimLocked = true;
                stateTimer = 0f;
                aimVelocity = Vector3.zero;
                SetWarningColor(Color.red);
            }
        }
        else
        {
            SetWarningColor(Color.red);
            aimPoint = player.transform.position;
        }

        Vector3 flatDirection = aimPoint - transform.position;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude > 0.01f)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(flatDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                patrolTurnSpeed * Time.deltaTime);
        }

        if (aimLocked && stateTimer >= lockedWarningDuration)
        {
            Fire();
            state = HunterState.Cooldown;
            stateTimer = 0f;
            SetWarningVisible(false);
        }
    }

    private void UpdateCooldown()
    {
        stateTimer += Time.deltaTime;
        if (stateTimer >= shotCooldown)
        {
            EnterSearch();
        }
    }

    private void EnterSearch()
    {
        state = HunterState.Search;
        stateTimer = 0f;
        lostSightTimer = 0f;
        SetWarningVisible(false);
        ChoosePatrolTarget();
    }

    private void EnterAim()
    {
        state = HunterState.Aim;
        stateTimer = 0f;
        lostSightTimer = 0f;
        aimPoint = player.transform.position;
        aimVelocity = Vector3.zero;
        aimLocked = false;
        SetWarningColor(new Color(0.1f, 1f, 0.15f));
        SetWarningVisible(true);
    }

    private void Fire()
    {
        Vector3 origin = GetEyePosition();
        Vector3 direction = aimPoint - origin;
        if (direction.sqrMagnitude < 0.001f)
        {
            RegisterMiss();
            return;
        }

        HunterBullet.Create(
            origin,
            direction.normalized,
            bulletSpeed,
            bulletGravity,
            bulletLifetime,
            Vector3.Distance(origin, player.transform.position),
            transform);
    }

    private void RegisterMiss()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterHunterMiss();
        }
    }

    private bool CanSeePlayer()
    {
        Vector3 origin = GetEyePosition();
        Vector3 toPlayer = player.transform.position - origin;
        float distance = toPlayer.magnitude;
        if (distance > visionDistance || distance < 0.001f)
        {
            return false;
        }

        if (Vector3.Angle(transform.forward, toPlayer) > fieldOfView * 0.5f)
        {
            return false;
        }

        if (!Physics.Raycast(
                origin,
                toPlayer.normalized,
                out RaycastHit hit,
                distance + 0.2f,
                visionLayers,
                QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        return hit.collider.GetComponentInParent<PlayerMove>() == player;
    }

    private void MoveAlongPatrol()
    {
        Vector3 direction = patrolTarget - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
        {
            StopHorizontalMovement();
            return;
        }

        direction.Normalize();
        Vector3 velocity = direction * patrolSpeed;
        body.velocity = new Vector3(velocity.x, body.velocity.y, velocity.z);

        Quaternion desiredRotation = Quaternion.LookRotation(direction, Vector3.up);
        body.MoveRotation(Quaternion.Slerp(
            body.rotation,
            desiredRotation,
            patrolTurnSpeed * Time.fixedDeltaTime));
    }

    private void StopHorizontalMovement()
    {
        body.velocity = new Vector3(0f, body.velocity.y, 0f);
    }

    private bool PathAheadIsBlocked()
    {
        Vector3 origin = transform.position + Vector3.up * 0.45f;
        return Physics.SphereCast(
            origin,
            0.35f,
            transform.forward,
            out RaycastHit hit,
            1.1f,
            visionLayers,
            QueryTriggerInteraction.Ignore) &&
            hit.collider.GetComponentInParent<PlayerMove>() == null;
    }

    private void ConfigurePatrolBounds()
    {
        GameObject ground = GameObject.Find("ground");
        Collider groundCollider = ground == null ? null : ground.GetComponent<Collider>();
        if (groundCollider != null)
        {
            patrolBounds = groundCollider.bounds;
            patrolBounds.Expand(new Vector3(-3f, 0f, -3f));
        }
        else
        {
            patrolBounds = new Bounds(
                transform.position,
                new Vector3(fallbackMapHalfExtent * 2f, 1f, fallbackMapHalfExtent * 2f));
        }
    }

    private void ChoosePatrolTarget()
    {
        patrolTarget = new Vector3(
            Random.Range(patrolBounds.min.x, patrolBounds.max.x),
            transform.position.y,
            Random.Range(patrolBounds.min.z, patrolBounds.max.z));
    }

    private Vector3 GetEyePosition()
    {
        return transform.position + Vector3.up * eyeHeight;
    }

    private void FindPlayer()
    {
        if (player == null)
        {
            player = PlayerMove.Instance;
        }
    }

    private void CreateWarningIndicator()
    {
        GameObject warning = new GameObject("Aim Warning");
        warning.transform.SetParent(transform, false);
        warning.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        warning.transform.localScale = Vector3.one;

        warningMesh = new Mesh { name = "AimWarningTriangle" };
        warningMesh.vertices = new[]
        {
            new Vector3(-0.32f, 0f, 0f),
            new Vector3(0f, 0.55f, 0f),
            new Vector3(0.32f, 0f, 0f)
        };
        warningMesh.triangles = new[] { 0, 1, 2 };
        warningMesh.RecalculateNormals();
        warningMesh.RecalculateBounds();

        MeshFilter filter = warning.AddComponent<MeshFilter>();
        filter.sharedMesh = warningMesh;

        MeshRenderer renderer = warning.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        warningMaterial = new Material(shader) { name = "AimWarningMaterial" };
        SetWarningColor(new Color(0.1f, 1f, 0.15f));

        renderer.sharedMaterial = warningMaterial;
        warningTransform = warning.transform;
        SetWarningVisible(false);
    }

    private void SetWarningColor(Color color)
    {
        if (warningMaterial == null)
        {
            return;
        }

        if (warningMaterial.HasProperty("_BaseColor"))
        {
            warningMaterial.SetColor("_BaseColor", color);
        }
        else
        {
            warningMaterial.color = color;
        }
    }

    private void SetWarningVisible(bool visible)
    {
        if (warningTransform != null && warningTransform.gameObject.activeSelf != visible)
        {
            warningTransform.gameObject.SetActive(visible);
        }
    }
}
