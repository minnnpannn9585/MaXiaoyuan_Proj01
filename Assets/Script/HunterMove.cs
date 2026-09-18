using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class HunterMove : MonoBehaviour
{
    private const float PhotoSightProbeRadius = 0.07f;

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
    [Tooltip("Time for tracking oscillation to settle by approximately 99 percent.")]
    [SerializeField] private float aimDuration = 1.65f;
    [SerializeField, Min(0f)] private float initialTrackingErrorDegrees = 3f;
    [SerializeField, Min(0f)] private float residualTrackingErrorDegrees = 0.6f;
    [SerializeField, Min(0f)] private float movingTargetResidualErrorDegrees = 1.2f;
    [SerializeField, Min(0.01f)] private float trackingOscillationFrequency = 1.4f;
    [SerializeField, Range(0f, 1f)] private float verticalTrackingErrorRatio = 0.45f;
    [SerializeField, Min(0f)] private float captureAimErrorDegrees = 2.2f;
    [SerializeField, Min(0f)] private float maximumPreferredSubjectBlurPixels = 12f;
    [SerializeField] private float lockedWarningDuration = 0.2f;
    [SerializeField] private float shotCooldown = 0.9f;

    [Header("Composition Approach")]
    [SerializeField, Range(0f, 1f)]
    private float preferredFrameCoverage = 0.2f;
    [SerializeField, Min(0f)]
    private float stationaryApproachSpeedThreshold = 0.25f;

    [Header("Photography")]
    [Tooltip("P, A, S, or M determines which exposure settings are automatic.")]
    [SerializeField] private PhotoExposureMode exposureMode =
        PhotoExposureMode.Manual;
    [Tooltip("Allows the camera to adjust ISO within the configured range.")]
    [SerializeField] private bool autoIso;
    [Tooltip("Reciprocal exposure time. 640 means 1/640 second; lower values create more motion blur and a brighter exposure.")]
    [SerializeField, Min(1f)] private float shutterSpeed = 640f;
    [Tooltip("Lens f-number. Higher values reduce sensor exposure quadratically.")]
    [SerializeField, Min(0.7f)] private float aperture = 8f;
    [Tooltip("Sensor output gain. ISO changes brightness and noise, not incoming light.")]
    [SerializeField, Min(25f)] private float iso = 100f;
    [Tooltip("EV100 calibration when the metered rendered luminance is 18 percent gray.")]
    [SerializeField] private float sceneExposureValue100 = 15.32f;
    [Tooltip("Additional output exposure adjustment in stops.")]
    [SerializeField] private float exposureCompensation = 0f;
    [Tooltip("Photos darker than this relative exposure fail the clarity check.")]
    [SerializeField] private float minimumUsableExposureStops = -2f;
    [Tooltip("Photos brighter than this relative exposure fail the clarity check.")]
    [SerializeField] private float maximumUsableExposureStops = 2f;
    [Tooltip("Disabled checks both SNR and MTF50. Enabled denoises the image and judges the resulting MTF50.")]
    [SerializeField] private PhotoNoiseReductionMode noiseReductionMode =
        PhotoNoiseReductionMode.Disabled;
    [Tooltip("Minimum usable signal-to-noise ratio when noise reduction is disabled.")]
    [SerializeField, Min(0f)] private float minimumSignalToNoiseRatioDecibels = 20f;
    [Header("Automatic Exposure Limits")]
    [Tooltip("Smallest f-number the lens can select.")]
    [SerializeField, Min(0.7f)] private float lensMaximumAperture = 2.8f;
    [Tooltip("Largest f-number the lens can select.")]
    [SerializeField, Min(0.7f)] private float lensMinimumAperture = 22f;
    [SerializeField, Min(1f)] private float slowestAutomaticShutterSpeed = 30f;
    [SerializeField, Min(1f)] private float fastestAutomaticShutterSpeed = 8000f;
    [Tooltip("In A/P + Auto ISO, ISO rises before shutter becomes slower than this.")]
    [SerializeField, Min(1f)] private float minimumAutoIsoShutterSpeed = 500f;
    [SerializeField, Min(25f)] private float minimumAutomaticIso = 100f;
    [SerializeField, Min(25f)] private float maximumAutomaticIso = 12800f;
    [Header("Optics and Image Quality")]
    [Tooltip("Physical focal length on a 36 x 24 mm full-frame sensor. The saved photo is center-cropped to its output aspect ratio.")]
    [SerializeField, Min(1f)] private float photoFocalLength = 200f;
    [Tooltip("Sensor pixel pitch in micrometers. Canon 80D reference: 3.72.")]
    [SerializeField, Min(0.1f)] private float sensorPixelPitchMicrometers = 3.72f;
    [Tooltip("Calibration multiplier applied after the physical blur calculation.")]
    [SerializeField, Min(0.1f)] private float motionBlurScale = 1f;
    [Tooltip("Static-camera MTF50 baseline in cycles per output pixel.")]
    [SerializeField, Range(0.05f, 0.5f)] private float sharpMtf50 = 0.35f;
    [Tooltip("Minimum MTF50 retention required for the player photo to count.")]
    [SerializeField, Range(0f, 1f)] private float minimumMtf50Retention = 0.8f;
    [Tooltip("Minimum linear player projection required for a successful photo.")]
    [SerializeField, Range(0f, 1f)]
    private float minimumSuccessfulFrameCoverage = 0.2f;

    [Header("Autofocus")]
    [Tooltip("Time in seconds for autofocus to settle by approximately 99 percent.")]
    [SerializeField, Min(0f)] private float autofocusTime = 0.03f;
    [Tooltip("Focus distance used before the hunter has taken its first photo.")]
    [SerializeField, Min(0.21f)] private float initialFocusDistance = 20f;
    [SerializeField, Min(0.21f)] private float minimumFocusDistance = 0.3f;
    [SerializeField, Min(0.3f)] private float maximumFocusDistance = 200f;
    [SerializeField, Min(0f)] private float autofocusReadyTolerance = 0.1f;

    private Rigidbody body;
    private Collider bodyCollider;
    private PlayerMove player;
    private HunterState state;
    private Vector3 patrolTarget;
    private Vector3 lastKnownPlayerPosition;
    private Vector3 aimPoint;
    private Vector3 previousPhotoDirection;
    private Vector3 photoCameraAngularVelocity;
    private bool hasPreviousPhotoDirection;
    private float currentFocusDistance;
    private Bounds patrolBounds;
    private float stateTimer;
    private float trackingElapsedTime;
    private float captureReadyTimer;
    private float currentTrackingErrorDegrees;
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

    private void OnValidate()
    {
        lensMaximumAperture =
            PhotoExposurePhysics.SnapAperture(lensMaximumAperture);
        lensMinimumAperture =
            PhotoExposurePhysics.SnapAperture(lensMinimumAperture);
        lensMinimumAperture = Mathf.Max(
            lensMaximumAperture,
            lensMinimumAperture);
        aperture = Mathf.Clamp(
            PhotoExposurePhysics.SnapAperture(aperture),
            lensMaximumAperture,
            lensMinimumAperture);

        minimumAutomaticIso =
            PhotoExposurePhysics.SnapIso(minimumAutomaticIso);
        maximumAutomaticIso =
            PhotoExposurePhysics.SnapIso(maximumAutomaticIso);
        maximumAutomaticIso = Mathf.Max(
            minimumAutomaticIso,
            maximumAutomaticIso);
        iso = Mathf.Clamp(
            PhotoExposurePhysics.SnapIso(iso),
            minimumAutomaticIso,
            maximumAutomaticIso);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        hunterAnimator = GetComponentInChildren<Animator>();
        if (GetComponent<TakePhoto>() == null)
        {
            gameObject.AddComponent<TakePhoto>();
        }

        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.drag = 2f;
        lastProgressPosition = transform.position;
        currentFocusDistance = Mathf.Clamp(
            initialFocusDistance,
            minimumFocusDistance,
            maximumFocusDistance);

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
            if (ShouldApproachForComposition(player))
            {
                EnterInvestigate();
            }
            else
            {
                EnterAim();
            }
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
            if (!ShouldApproachForComposition(player))
            {
                EnterAim();
            }
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
        trackingElapsedTime += Time.deltaTime;
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
            if (ShouldApproachForComposition(player))
            {
                EnterInvestigate();
                return;
            }
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

        Vector3 trackingTarget = canSeePlayer
            ? visibleAimPoint
            : lastKnownPlayerPosition;
        UpdateOscillatingTracking(trackingTarget);
        Vector3 focusPoint = canSeePlayer
            ? GetVisualFocusPoint(
                player.transform,
                GetEyePosition())
            : trackingTarget;
        UpdateAutoFocus(focusPoint);
        UpdatePhotoCameraMotion();

        if (!crouchComplete)
        {
            aimLocked = false;
            captureReadyTimer = 0f;
            return;
        }

        float predictedSubjectBlurPixels =
            EstimateSubjectMotionBlurPixels();
        bool readyToCapture =
            canSeePlayer &&
            IsAutoFocusReady(focusPoint) &&
            currentTrackingErrorDegrees <= captureAimErrorDegrees &&
            predictedSubjectBlurPixels <=
                maximumPreferredSubjectBlurPixels;
        if (readyToCapture)
        {
            aimLocked = true;
            captureReadyTimer += Time.deltaTime;
            SetWarningColor(Color.red);
        }
        else
        {
            aimLocked = false;
            captureReadyTimer = Mathf.Max(
                0f,
                captureReadyTimer - Time.deltaTime * 0.5f);
            SetWarningColor(new Color(0.1f, 1f, 0.15f));
        }

        if (aimLocked &&
            captureReadyTimer >= lockedWarningDuration)
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
                if (ShouldApproachForComposition(player))
                {
                    EnterInvestigate();
                }
                else
                {
                    EnterAim();
                }
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
        trackingElapsedTime = 0f;
        captureReadyTimer = 0f;
        currentTrackingErrorDegrees = 180f;
        photoCameraAngularVelocity = Vector3.zero;
        hasPreviousPhotoDirection = false;
        aimLocked = false;
        RememberPlayerPosition();
        SetWarningColor(new Color(0.1f, 1f, 0.15f));
        SetWarningVisible(false);
    }

    private void UpdateOscillatingTracking(Vector3 targetPoint)
    {
        Vector3 eyePosition = GetEyePosition();
        Vector3 targetOffset = targetPoint - eyePosition;
        float targetDistance = targetOffset.magnitude;
        if (targetDistance <= 0.0001f)
        {
            aimPoint = targetPoint;
            currentTrackingErrorDegrees = 0f;
            return;
        }

        Vector3 targetDirection = targetOffset / targetDistance;
        Quaternion targetRotation = Quaternion.LookRotation(
            targetDirection,
            Vector3.up);
        float settleDuration = Mathf.Max(0.01f, aimDuration);
        float decay = Mathf.Exp(
            -4.6f * trackingElapsedTime / settleDuration);
        float residualError =
            residualTrackingErrorDegrees +
            player.NormalizedSpeed *
            movingTargetResidualErrorDegrees;
        float errorEnvelope =
            residualError +
            Mathf.Max(
                0f,
                initialTrackingErrorDegrees - residualError) *
            decay;
        float phase =
            trackingElapsedTime *
            trackingOscillationFrequency *
            Mathf.PI *
            2f -
            Mathf.PI *
            0.5f;
        float yawError = Mathf.Sin(phase) * errorEnvelope;
        float pitchError =
            Mathf.Sin(phase * 0.83f + 1.2f) *
            errorEnvelope *
            verticalTrackingErrorRatio;
        Quaternion errorRotation =
            Quaternion.AngleAxis(
                yawError,
                targetRotation * Vector3.up) *
            Quaternion.AngleAxis(
                pitchError,
                targetRotation * Vector3.right);
        Vector3 trackedDirection =
            errorRotation * targetDirection;
        aimPoint =
            eyePosition +
            trackedDirection * targetDistance;
        currentTrackingErrorDegrees =
            Vector3.Angle(trackedDirection, targetDirection);
    }

    private void UpdateAutoFocus(Vector3 focusPoint)
    {
        float minimumDistance = Mathf.Max(0.21f, minimumFocusDistance);
        float maximumDistance = Mathf.Max(
            minimumDistance,
            maximumFocusDistance);
        float targetFocusDistance = Mathf.Clamp(
            Vector3.Distance(GetEyePosition(), focusPoint),
            minimumDistance,
            maximumDistance);
        if (autofocusTime <= 0.0001f)
        {
            currentFocusDistance = targetFocusDistance;
            return;
        }

        float focusBlend =
            1f -
            Mathf.Exp(-4.6f * Time.deltaTime / autofocusTime);
        currentFocusDistance = Mathf.Lerp(
            currentFocusDistance,
            targetFocusDistance,
            focusBlend);
    }

    private bool IsAutoFocusReady(Vector3 focusPoint)
    {
        float targetFocusDistance = Mathf.Clamp(
            Vector3.Distance(GetEyePosition(), focusPoint),
            Mathf.Max(0.21f, minimumFocusDistance),
            Mathf.Max(minimumFocusDistance, maximumFocusDistance));
        float tolerance = Mathf.Max(
            autofocusReadyTolerance,
            targetFocusDistance * 0.002f);
        return Mathf.Abs(
            currentFocusDistance - targetFocusDistance) <= tolerance;
    }

    private float EstimateSubjectMotionBlurPixels()
    {
        if (player == null)
        {
            return float.PositiveInfinity;
        }

        Vector3 eyePosition = GetEyePosition();
        Vector3 direction = aimPoint - eyePosition;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return float.PositiveInfinity;
        }

        Rigidbody playerBody = player.GetComponent<Rigidbody>();
        Vector3 playerVelocity = playerBody == null
            ? Vector3.zero
            : playerBody.velocity;
        PhotoBlurPhysics.Solution blur =
            PhotoBlurPhysics.Solve(
                eyePosition,
                Quaternion.LookRotation(
                    direction.normalized,
                    Vector3.up),
                body.velocity,
                photoCameraAngularVelocity,
                player.transform.position,
                playerVelocity,
                photoFocalLength,
                shutterSpeed,
                sensorPixelPitchMicrometers,
                24f,
                720,
                motionBlurScale);
        return blur.BlurLengthPixels;
    }

    private static Vector3 GetVisualFocusPoint(
        Transform target,
        Vector3 cameraPosition)
    {
        if (target == null)
        {
            return Vector3.zero;
        }

        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds combinedBounds = default;
        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer == null || !targetRenderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                combinedBounds = targetRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(targetRenderer.bounds);
            }
        }

        Vector3 center =
            hasBounds ? combinedBounds.center : target.position;
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
            target.GetComponentsInChildren<Collider>(true);
        float closestDistance = float.PositiveInfinity;
        Vector3 closestPoint = center;
        foreach (Collider targetCollider in colliders)
        {
            if (targetCollider != null &&
                targetCollider.enabled &&
                targetCollider.Raycast(
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

    private void UpdatePhotoCameraMotion()
    {
        Vector3 currentDirection = aimPoint - GetEyePosition();
        if (currentDirection.sqrMagnitude < 0.0001f)
        {
            photoCameraAngularVelocity = Vector3.zero;
            hasPreviousPhotoDirection = false;
            return;
        }

        currentDirection.Normalize();
        if (hasPreviousPhotoDirection && Time.deltaTime > 0.0001f)
        {
            Quaternion directionDelta = Quaternion.FromToRotation(
                previousPhotoDirection,
                currentDirection);
            directionDelta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
            {
                angle -= 360f;
            }

            photoCameraAngularVelocity =
                Mathf.Abs(angle) < 0.0001f
                    ? Vector3.zero
                    : axis.normalized * (angle / Time.deltaTime);
        }
        else
        {
            photoCameraAngularVelocity = Vector3.zero;
        }

        previousPhotoDirection = currentDirection;
        hasPreviousPhotoDirection = true;
    }

    private void Fire()
    {
        if (hunterAnimator != null)
        {
            hunterAnimator.SetTrigger(FireParameter);
        }

        if (player == null)
        {
            RegisterFailedPhoto();
            return;
        }

        Vector3 origin = GetEyePosition();
        Vector3 direction = aimPoint - origin;
        if (direction.sqrMagnitude < 0.001f)
        {
            RegisterFailedPhoto();
            return;
        }

        Vector3 fireDirection = direction.normalized;
        TakePhoto takePhoto = GetComponent<TakePhoto>();
        bool capturedPlayerClearly = false;
        if (takePhoto != null)
        {
            Rigidbody playerBody = player.GetComponent<Rigidbody>();
            Vector3 playerVelocity = playerBody == null
                ? Vector3.zero
                : playerBody.velocity;
            capturedPlayerClearly = takePhoto.Capture(
                origin,
                fireDirection,
                player.transform,
                playerVelocity,
                shutterSpeed,
                photoFocalLength,
                sensorPixelPitchMicrometers,
                motionBlurScale,
                body.velocity,
                photoCameraAngularVelocity,
                aperture,
                iso,
                sceneExposureValue100,
                exposureCompensation,
                exposureMode,
                autoIso,
                lensMaximumAperture,
                lensMinimumAperture,
                slowestAutomaticShutterSpeed,
                fastestAutomaticShutterSpeed,
                minimumAutoIsoShutterSpeed,
                minimumAutomaticIso,
                maximumAutomaticIso,
                minimumUsableExposureStops,
                maximumUsableExposureStops,
                noiseReductionMode,
                minimumSignalToNoiseRatioDecibels,
                currentFocusDistance,
                sharpMtf50,
                minimumMtf50Retention,
                minimumSuccessfulFrameCoverage);
        }

        if (GameManager.Instance != null)
        {
            if (capturedPlayerClearly)
            {
                GameManager.Instance.RegisterClearPlayerPhoto();
            }
            else
            {
                GameManager.Instance.RegisterFailedPlayerPhoto();
            }
        }
    }

    private void RegisterFailedPhoto()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterFailedPlayerPhoto();
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

    private bool ShouldApproachForComposition(PlayerMove candidate)
    {
        if (candidate == null ||
            (candidate.IsFlying &&
                candidate.CurrentSpeed >
                stationaryApproachSpeedThreshold))
        {
            return false;
        }

        Vector3 eyePosition = GetEyePosition();
        Vector3 focusPoint = GetVisualFocusPoint(
            candidate.transform,
            eyePosition);
        Vector3 direction = focusPoint - eyePosition;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float frameCoverage =
            TakePhoto.CalculateSubjectFrameCoverage(
                eyePosition,
                Quaternion.LookRotation(
                    direction.normalized,
                    Vector3.up),
                candidate.transform,
                photoFocalLength,
                16f / 9f);
        return frameCoverage < preferredFrameCoverage;
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
            PhotoSightProbeRadius,
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
