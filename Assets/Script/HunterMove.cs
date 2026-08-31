using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class HunterMove : MonoBehaviour
{
    private enum HunterState
    {
        Search,
        Investigate,
        Aim,
        Cooldown
    }

    [Header("Search")]
    [SerializeField] private float patrolSpeed = 2.4f;
    [SerializeField] private float patrolTurnSpeed = 8f;
    [SerializeField] private float waypointTolerance = 1f;
    [SerializeField] private float fallbackMapHalfExtent = 18f;
    [SerializeField] private float investigateSpeedMultiplier = 1.15f;
    [SerializeField] private float investigateArrivalDistance = 1.2f;

    [Header("Obstacle Avoidance")]
    [SerializeField] private LayerMask obstacleLayers = ~0;
    [SerializeField] private float obstacleCheckDistance = 2.2f;
    [SerializeField] private float obstacleCheckRadius = 0.38f;
    [SerializeField] private float obstacleCheckHeight = 0.55f;
    [SerializeField, Min(0.02f)] private float minimumObstacleClearance = 0.02f;
    [SerializeField, Range(20f, 85f)] private float avoidanceAngle = 55f;
    [SerializeField] private float avoidanceSideCommitTime = 0.8f;
    [SerializeField] private float stuckCheckInterval = 0.8f;
    [SerializeField] private float stuckMovementThreshold = 0.15f;

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
    private Collider bodyCollider;
    private PlayerMove player;
    private HunterState state;
    private Vector3 patrolTarget;
    private Vector3 lastKnownPlayerPosition;
    private Vector3 aimPoint;
    private Vector3 aimVelocity;
    private Bounds patrolBounds;
    private float stateTimer;
    private float lostSightTimer;
    private float avoidanceSide = 1f;
    private float avoidanceCommitUntil;
    private float stuckCheckTimer;
    private Vector3 lastProgressPosition;
    private bool aimLocked;
    private Transform warningTransform;
    private Mesh warningMesh;
    private Material warningMaterial;
    private Animator hunterAnimator;
    private static readonly int MoveSpeedParameter = Animator.StringToHash("MoveSpeed");
    private static readonly int MovingBackwardParameter = Animator.StringToHash("MovingBackward");
    private static readonly int WantsToMoveParameter = Animator.StringToHash("WantsToMove");
    private static readonly int FireParameter = Animator.StringToHash("Fire");
    private static readonly int ForwardState = Animator.StringToHash("Base Layer.Stepping Forward");
    private static readonly int BackwardState = Animator.StringToHash("Base Layer.Stepping Backward");
    private static readonly int CrouchIdleState = Animator.StringToHash("Base Layer.Crouch Idle");

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        hunterAnimator = GetComponentInChildren<Animator>();
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.drag = 2f;
        lastProgressPosition = transform.position;

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
        if (GameManager.Instance != null && !GameManager.Instance.IsRunning)
        {
            SetWarningVisible(false);
            return;
        }

        switch (state)
        {
            case HunterState.Search:
                UpdateSearch();
                break;
            case HunterState.Investigate:
                UpdateInvestigate();
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

        bool wantsToMove = WantsToMove();
        if (wantsToMove && !IsMovementPoseReady())
        {
            StopHorizontalMovement();
            return;
        }

        if (state == HunterState.Search)
        {
            MoveAlongPatrol();
        }
        else if (state == HunterState.Investigate)
        {
            Vector3 offset = lastKnownPlayerPosition - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > investigateArrivalDistance * investigateArrivalDistance)
            {
                MoveTowardsPosition(lastKnownPlayerPosition, patrolSpeed * investigateSpeedMultiplier);
            }
            else
            {
                StopHorizontalMovement();
            }
        }
        else if (state == HunterState.Aim)
        {
            StopHorizontalMovement();
            if (IsCrouchedPoseReady())
            {
                RotateTowardsPosition(aimPoint);
            }
        }
        else
        {
            StopHorizontalMovement();
        }
    }

    private void LateUpdate()
    {
        UpdateVisualAnimation();

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
        if (TryAcquireVisiblePlayer())
        {
            EnterAim();
            return;
        }

        Vector3 flatOffset = patrolTarget - transform.position;
        flatOffset.y = 0f;
        if (flatOffset.sqrMagnitude <= waypointTolerance * waypointTolerance)
        {
            ChoosePatrolTarget();
        }
    }

    private void UpdateInvestigate()
    {
        SetWarningVisible(false);
        if (TryAcquireVisiblePlayer())
        {
            EnterAim();
            return;
        }

        Vector3 offset = lastKnownPlayerPosition - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude <= investigateArrivalDistance * investigateArrivalDistance)
        {
            EnterSearch();
        }
    }

    private void UpdateAim()
    {
        bool crouchComplete = IsCrouchedPoseReady();
        SetWarningVisible(crouchComplete);
        if (crouchComplete)
        {
            stateTimer += Time.deltaTime;
        }

        bool canSeePlayer = TryGetClearShotPoint(
            player,
            visionDistance,
            out Vector3 visibleAimPoint);
        if (!canSeePlayer)
        {
            PlayerMove newlyVisiblePlayer = FindBestVisiblePlayer();
            if (newlyVisiblePlayer != null && newlyVisiblePlayer != player)
            {
                player = newlyVisiblePlayer;
                EnterAim();
                return;
            }
        }

        if (canSeePlayer)
        {
            RememberPlayerPosition();
            lostSightTimer = 0f;
        }
        else
        {
            lostSightTimer += Time.deltaTime;
            if (lostSightTimer >= lostSightGrace)
            {
                EnterInvestigate();
                return;
            }
        }

        if (!crouchComplete)
        {
            if (canSeePlayer)
            {
                aimPoint = visibleAimPoint;
            }

            return;
        }

        if (!aimLocked)
        {
            SetWarningColor(new Color(0.1f, 1f, 0.15f));

            float followTime = stationaryAimFollowTime + player.NormalizedSpeed * fastTargetFollowPenalty;
            aimPoint = Vector3.SmoothDamp(
                aimPoint,
                canSeePlayer ? visibleAimPoint : lastKnownPlayerPosition,
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
            if (canSeePlayer)
            {
                aimPoint = visibleAimPoint;
            }
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
        bool canTrackPlayer = CanTrackPlayer();
        if (!canTrackPlayer)
        {
            PlayerMove newlyVisiblePlayer = FindBestVisiblePlayer();
            if (newlyVisiblePlayer != null)
            {
                player = newlyVisiblePlayer;
                canTrackPlayer = true;
            }
        }

        if (canTrackPlayer)
        {
            RememberPlayerPosition();
        }

        if (stateTimer >= shotCooldown)
        {
            if (canTrackPlayer)
            {
                EnterAim();
            }
            else
            {
                EnterInvestigate();
            }
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

    private void EnterInvestigate()
    {
        state = HunterState.Investigate;
        stateTimer = 0f;
        lostSightTimer = 0f;
        SetWarningVisible(false);
    }

    private void EnterAim()
    {
        state = HunterState.Aim;
        stateTimer = 0f;
        lostSightTimer = 0f;
        aimPoint = player.transform.position;
        aimVelocity = Vector3.zero;
        aimLocked = false;
        RememberPlayerPosition();
        SetWarningColor(new Color(0.1f, 1f, 0.15f));
        SetWarningVisible(false);
    }

    private void Fire()
    {
        if (hunterAnimator != null)
        {
            hunterAnimator.SetTrigger(FireParameter);
        }

        if (player == null)
        {
            RegisterMiss();
            return;
        }

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
            direction.magnitude,
            transform);
    }

    private void RegisterMiss()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterHunterMiss();
        }
    }

    private bool CanTrackPlayer()
    {
        return HasLineOfSight(player, visionDistance);
    }

    private bool IsPlayerVisible(PlayerMove candidate)
    {
        if (candidate == null || !candidate.isActiveAndEnabled)
        {
            return false;
        }

        Vector3 toPlayer = candidate.transform.position - GetEyePosition();
        float distance = toPlayer.magnitude;
        if (distance > visionDistance || distance < 0.001f)
        {
            return false;
        }

        Vector3 flatForward = transform.forward;
        Vector3 flatToPlayer = toPlayer;
        flatForward.y = 0f;
        flatToPlayer.y = 0f;
        if (flatToPlayer.sqrMagnitude < 0.001f ||
            Vector3.Angle(flatForward, flatToPlayer) > fieldOfView * 0.5f)
        {
            return false;
        }

        return HasLineOfSight(candidate, visionDistance);
    }

    private bool HasLineOfSight(PlayerMove candidate, float maxDistance)
    {
        return TryGetClearShotPoint(candidate, maxDistance, out _);
    }

    private bool TryGetClearShotPoint(
        PlayerMove candidate,
        float maxDistance,
        out Vector3 visiblePoint)
    {
        visiblePoint = Vector3.zero;
        if (candidate == null || !candidate.isActiveAndEnabled)
        {
            return false;
        }

        Collider targetCollider = candidate.GetComponent<Collider>();
        Bounds targetBounds = targetCollider.bounds;
        Vector3 center = targetBounds.center;
        Vector3 upperPoint = new Vector3(
            center.x,
            Mathf.Lerp(targetBounds.min.y, targetBounds.max.y, 0.8f),
            center.z);
        Vector3 lowerPoint = new Vector3(
            center.x,
            Mathf.Lerp(targetBounds.min.y, targetBounds.max.y, 0.2f),
            center.z);

        Vector3 origin = GetEyePosition();
        if (HasClearProjectilePath(origin, center, candidate, maxDistance))
        {
            visiblePoint = center;
            return true;
        }

        if (HasClearProjectilePath(origin, upperPoint, candidate, maxDistance))
        {
            visiblePoint = upperPoint;
            return true;
        }

        if (HasClearProjectilePath(origin, lowerPoint, candidate, maxDistance))
        {
            visiblePoint = lowerPoint;
            return true;
        }

        return false;
    }

    private bool HasClearProjectilePath(
        Vector3 origin,
        Vector3 targetPoint,
        PlayerMove candidate,
        float maxDistance)
    {
        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;
        if (distance < 0.001f || distance > maxDistance)
        {
            return false;
        }

        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            HunterBullet.CollisionRadius,
            direction.normalized,
            distance + 0.1f,
            visionLayers,
            QueryTriggerInteraction.Ignore);

        Collider closestCollider = null;
        float closestDistance = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null ||
                hit.collider == bodyCollider ||
                hit.transform == transform ||
                hit.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestCollider = hit.collider;
            }
        }

        return closestCollider != null &&
               closestCollider.GetComponentInParent<PlayerMove>() == candidate;
    }

    private bool TryAcquireVisiblePlayer()
    {
        PlayerMove visiblePlayer = FindBestVisiblePlayer();
        if (visiblePlayer == null)
        {
            return false;
        }

        player = visiblePlayer;
        RememberPlayerPosition();
        return true;
    }

    private PlayerMove FindBestVisiblePlayer()
    {
        PlayerMove bestPlayer = null;
        float bestDistanceSquared = float.MaxValue;
        foreach (PlayerMove candidate in PlayerMove.ActivePlayers)
        {
            if (!IsPlayerVisible(candidate))
            {
                continue;
            }

            float distanceSquared = (candidate.transform.position - transform.position).sqrMagnitude;
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestPlayer = candidate;
            }
        }

        return bestPlayer;
    }

    private void MoveAlongPatrol()
    {
        MoveTowardsPosition(patrolTarget, patrolSpeed);
    }

    private void MoveTowardsPosition(Vector3 targetPosition, float speed)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
        {
            StopHorizontalMovement();
            return;
        }

        direction.Normalize();
        direction = CalculateAvoidanceDirection(direction, out bool isSeparatingFromObstacle);
        float safeSpeed = isSeparatingFromObstacle
            ? speed
            : CalculateSafeMovementSpeed(direction, speed);
        Vector3 velocity = direction * safeSpeed;
        body.velocity = new Vector3(velocity.x, body.velocity.y, velocity.z);
        UpdateStuckDetection();

        Quaternion desiredRotation = Quaternion.LookRotation(direction, Vector3.up);
        body.MoveRotation(Quaternion.Slerp(
            body.rotation,
            desiredRotation,
            patrolTurnSpeed * Time.fixedDeltaTime));
    }

    private void RotateTowardsPosition(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
        {
            return;
        }

        Quaternion desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        body.MoveRotation(Quaternion.Slerp(
            body.rotation,
            desiredRotation,
            patrolTurnSpeed * Time.fixedDeltaTime));
    }

    private void StopHorizontalMovement()
    {
        body.velocity = new Vector3(0f, body.velocity.y, 0f);
    }

    private Vector3 CalculateAvoidanceDirection(
        Vector3 desiredDirection,
        out bool isSeparatingFromObstacle)
    {
        isSeparatingFromObstacle = false;
        if (TryGetSeparationDirection(out Vector3 separationDirection))
        {
            isSeparatingFromObstacle = true;
            return separationDirection;
        }

        float forwardClearance = GetObstacleClearance(desiredDirection);
        if (forwardClearance >= obstacleCheckDistance)
        {
            return desiredDirection;
        }

        Vector3 leftDirection = Quaternion.AngleAxis(-avoidanceAngle, Vector3.up) * desiredDirection;
        Vector3 rightDirection = Quaternion.AngleAxis(avoidanceAngle, Vector3.up) * desiredDirection;
        float leftClearance = GetObstacleClearance(leftDirection);
        float rightClearance = GetObstacleClearance(rightDirection);

        if (Time.time >= avoidanceCommitUntil)
        {
            avoidanceSide = rightClearance > leftClearance ? 1f : -1f;
            avoidanceCommitUntil = Time.time + avoidanceSideCommitTime;
        }
        else if (avoidanceSide > 0f && rightClearance < 0.1f && leftClearance > rightClearance)
        {
            avoidanceSide = -1f;
            avoidanceCommitUntil = Time.time + avoidanceSideCommitTime;
        }
        else if (avoidanceSide < 0f && leftClearance < 0.1f && rightClearance > leftClearance)
        {
            avoidanceSide = 1f;
            avoidanceCommitUntil = Time.time + avoidanceSideCommitTime;
        }

        float blockedRatio = 1f - Mathf.Clamp01(forwardClearance / obstacleCheckDistance);
        float steerAngle = avoidanceAngle * Mathf.Lerp(0.45f, 1f, blockedRatio) * avoidanceSide;
        return (Quaternion.AngleAxis(steerAngle, Vector3.up) * desiredDirection).normalized;
    }

    private float CalculateSafeMovementSpeed(Vector3 direction, float requestedSpeed)
    {
        float clearance = GetObstacleClearance(direction);
        float plannedDistance = requestedSpeed * Time.fixedDeltaTime;
        if (clearance >= plannedDistance)
        {
            return requestedSpeed;
        }

        return Mathf.Max(0f, clearance - 0.001f) / Time.fixedDeltaTime;
    }

    private float GetObstacleClearance(Vector3 direction)
    {
        Vector3 origin = transform.position + Vector3.up * obstacleCheckHeight;
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            GetRequiredAvoidanceRadius(),
            direction,
            obstacleCheckDistance,
            obstacleLayers,
            QueryTriggerInteraction.Ignore);

        float clearance = obstacleCheckDistance;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null ||
                hit.collider == bodyCollider ||
                hit.transform == transform ||
                hit.transform.IsChildOf(transform) ||
                hit.collider.GetComponentInParent<PlayerMove>() != null)
            {
                continue;
            }

            clearance = Mathf.Min(clearance, hit.distance);
        }

        return clearance;
    }

    private bool TryGetSeparationDirection(out Vector3 separationDirection)
    {
        Vector3 origin = transform.position + Vector3.up * obstacleCheckHeight;
        float requiredRadius = GetRequiredAvoidanceRadius();
        Collider[] overlaps = Physics.OverlapSphere(
            origin,
            requiredRadius,
            obstacleLayers,
            QueryTriggerInteraction.Ignore);

        Vector3 separation = Vector3.zero;
        foreach (Collider obstacle in overlaps)
        {
            if (ShouldIgnoreObstacle(obstacle))
            {
                continue;
            }

            Vector3 closestPoint = obstacle.ClosestPoint(origin);
            Vector3 away = origin - closestPoint;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
            {
                away = transform.position - obstacle.bounds.center;
                away.y = 0f;
            }

            float distance = away.magnitude;
            if (distance > 0.0001f && distance < requiredRadius)
            {
                separation += away.normalized * (requiredRadius - distance);
            }
        }

        separationDirection = separation.sqrMagnitude > 0.0001f
            ? separation.normalized
            : Vector3.zero;
        return separationDirection != Vector3.zero;
    }

    private float GetRequiredAvoidanceRadius()
    {
        Bounds bounds = bodyCollider.bounds;
        float bodyRadius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        return Mathf.Max(obstacleCheckRadius, bodyRadius + minimumObstacleClearance);
    }

    private bool ShouldIgnoreObstacle(Collider obstacle)
    {
        return obstacle == null ||
               obstacle == bodyCollider ||
               obstacle.transform == transform ||
               obstacle.transform.IsChildOf(transform) ||
               obstacle.GetComponentInParent<PlayerMove>() != null;
    }

    private void UpdateStuckDetection()
    {
        stuckCheckTimer += Time.fixedDeltaTime;
        if (stuckCheckTimer < stuckCheckInterval)
        {
            return;
        }

        Vector3 movement = transform.position - lastProgressPosition;
        movement.y = 0f;
        if (movement.magnitude < stuckMovementThreshold)
        {
            avoidanceSide *= -1f;
            avoidanceCommitUntil = Time.time + avoidanceSideCommitTime;
        }

        lastProgressPosition = transform.position;
        stuckCheckTimer = 0f;
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
        float configuredEyeY = transform.position.y + eyeHeight;
        float outsideColliderY = bodyCollider.bounds.max.y + 0.05f;
        return new Vector3(
            transform.position.x,
            Mathf.Max(configuredEyeY, outsideColliderY),
            transform.position.z);
    }

    private void FindPlayer()
    {
        if (player == null && PlayerMove.ActivePlayers.Count > 0)
        {
            player = PlayerMove.ActivePlayers[0];
        }
    }

    private void RememberPlayerPosition()
    {
        if (player != null)
        {
            lastKnownPlayerPosition = player.transform.position;
        }
    }

    private void UpdateVisualAnimation()
    {
        if (hunterAnimator == null)
        {
            hunterAnimator = GetComponentInChildren<Animator>();
        }

        if (hunterAnimator == null)
        {
            return;
        }

        Vector3 horizontalVelocity = body.velocity;
        horizontalVelocity.y = 0f;
        float normalizedSpeed = patrolSpeed > 0.01f
            ? Mathf.Clamp(horizontalVelocity.magnitude / patrolSpeed, 0f, 1.5f)
            : 0f;
        hunterAnimator.SetFloat(MoveSpeedParameter, normalizedSpeed, 0.12f, Time.deltaTime);
        bool movingBackward = horizontalVelocity.sqrMagnitude > 0.01f &&
            Vector3.Dot(transform.forward, horizontalVelocity.normalized) < -0.15f;
        hunterAnimator.SetBool(MovingBackwardParameter, movingBackward);
        hunterAnimator.SetBool(WantsToMoveParameter, WantsToMove());
    }

    private bool WantsToMove()
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsRunning)
        {
            return false;
        }

        if (state == HunterState.Search)
        {
            return true;
        }

        if (state != HunterState.Investigate)
        {
            return false;
        }

        Vector3 offset = lastKnownPlayerPosition - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude >
            investigateArrivalDistance * investigateArrivalDistance;
    }

    private bool IsMovementPoseReady()
    {
        if (hunterAnimator == null || hunterAnimator.runtimeAnimatorController == null)
        {
            return true;
        }

        AnimatorStateInfo stateInfo = hunterAnimator.GetCurrentAnimatorStateInfo(0);
        bool inMovementState =
            stateInfo.fullPathHash == ForwardState ||
            stateInfo.fullPathHash == BackwardState;
        return inMovementState && !hunterAnimator.IsInTransition(0);
    }

    private bool IsCrouchedPoseReady()
    {
        if (hunterAnimator == null || hunterAnimator.runtimeAnimatorController == null)
        {
            return true;
        }

        AnimatorStateInfo stateInfo = hunterAnimator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.fullPathHash == CrouchIdleState &&
            !hunterAnimator.IsInTransition(0);
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
