using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class PlayerMove : MonoBehaviour
{
    private static readonly List<PlayerMove> activePlayers = new List<PlayerMove>();

    public static PlayerMove Instance { get; private set; }
    public static IReadOnlyList<PlayerMove> ActivePlayers => activePlayers;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 4f;
    [Header("Flight Speed / 飞行调速")]
    [SerializeField, Min(.1f), InspectorName("最大飞行速度")] private float flightSpeed = 10f;
    [SerializeField, Min(.1f), InspectorName("最小飞行速度")] private float minimumFlightSpeed = 2f;
    [SerializeField, Min(.1f), InspectorName("起飞速度")] private float takeoffSpeed = 2.5f;
    [SerializeField, Min(.1f), InspectorName("飞行加减速度")] private float flightAcceleration = 2f;
    [SerializeField, Min(.1f), InspectorName("W/S 每秒调速量")] private float keyboardSpeedAdjustment = 3f;
    [SerializeField, Min(.1f), InspectorName("滚轮调速灵敏度")] private float scrollSpeedAdjustment = 10f;
    [SerializeField] private float flightRiseSpeed = 5f;
    [SerializeField] private float flightDescendSpeed = 4f;
    [SerializeField] private float airControl = 3f;
    [SerializeField] private float acceleration = 22f;
    [SerializeField] private float turnSpeed = 12f;
    [SerializeField] private float doubleTapWindow = 0.3f;
    [Header("Jump / 跳跃控制")]
    [Tooltip("跳跃的目标高度（米）。受 Rigidbody 阻力影响，实际高度会略低。")]
    [SerializeField, Min(0.01f), InspectorName("跳跃高度（米）")] private float jumpHeight = .2f;
    [Tooltip("跳跃中按方向键的最大水平速度；设为 0 可只原地跳。与行走和飞行速度独立。")]
    [SerializeField, Min(0f), InspectorName("跳跃水平速度（米/秒）")] private float jumpMoveSpeed = .8f;
    [Tooltip("跳跃中水平加速、转向及松开方向键后减速的快慢。")]
    [SerializeField, Min(.1f), InspectorName("跳跃水平加减速")] private float jumpAcceleration = 8f;

    [Header("Flight Steering")]
    [SerializeField, Min(1f), InspectorName("高速最大转弯速度（度/秒）")] private float flightTurnRate = 75f;
    [SerializeField, Min(1f), InspectorName("低速最大转弯速度（度/秒）")] private float slowFlightTurnRate = 150f;
    [SerializeField, Min(1f)] private float flightTurnAcceleration = 150f;
    [SerializeField, Min(.05f)] private float flightTurnResponse = .5f;
    [SerializeField, Min(.1f)] private float flightVerticalAcceleration = 6f;
    [SerializeField, Min(0f)] private float takeoffLiftDuration = .35f;
    private float flightYawRate;
    private float takeoffLiftRemaining;
    private bool climbInput;
    private bool descendInput;
    private float targetFlightSpeed;
    private bool treeCrashLocked;
    private float groundDrag;
    public float TargetFlightSpeed => targetFlightSpeed;
    public bool IsAdjustingFlightSpeed => isFlying &&
        Mathf.Abs(new Vector2(Velocity.x, Velocity.z).magnitude - targetFlightSpeed) > .12f;
    public float FlightBankInput => isFlying ? Mathf.Clamp(flightYawRate / Mathf.Max(1f, CurrentFlightTurnRate), -1f, 1f) : 0f;
    public float CurrentFlightTurnRate => Mathf.Lerp(Mathf.Max(flightTurnRate, slowFlightTurnRate), flightTurnRate,
        Mathf.InverseLerp(MinimumFlightSpeed, MaximumFlightSpeed, new Vector2(Velocity.x, Velocity.z).magnitude));
    private float MaximumFlightSpeed => Mathf.Max(.1f, flightSpeed);
    private float MinimumFlightSpeed => Mathf.Clamp(minimumFlightSpeed, .1f, MaximumFlightSpeed);

    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float flightDrainPerSecond = 24f;
    [SerializeField, Min(0f)] private float glideDrainPerSecond = 3f;
    [SerializeField] private float groundedRecoveryPerSecond = 18f;
    [SerializeField] private float flightRestartStamina = 15f;
    [SerializeField, Min(0f)] private float crashRecoverySeconds = 3f;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckDistance = 0.18f;
    [SerializeField] private LayerMask groundLayers = ~0;

    private Rigidbody body;
    private Collider bodyCollider;
    private Vector2 moveInput;
    private float currentStamina;
    private float crashRecoveryRemaining;
    private bool crashFallPending;
    private float lastSpacePressTime = float.NegativeInfinity;
    private bool flightLocked;
    private bool isGrounded;
    private bool isFlying;
    private bool isJumping;
    private bool hasLeftTakeoffSurface;

    public float CurrentStamina => currentStamina;
    public float MaxStamina => maxStamina;
    public float Stamina01 => maxStamina <= 0f ? 0f : currentStamina / maxStamina;
    public float CurrentSpeed => body == null ? 0f : body.velocity.magnitude;
    public float NormalizedSpeed => Mathf.Clamp01(CurrentSpeed / flightSpeed);
    public bool IsGrounded => isGrounded;
    public bool IsFlying => isFlying;
    public bool IsJumping => isJumping;
    public bool IsStunned => crashRecoveryRemaining > 0f;
    public bool IsCrashRecovering => IsStunned;
    public bool IsCrashFalling => crashFallPending;
    public bool IsTreeCrash => treeCrashLocked && (crashFallPending || IsStunned);
    public Vector3 Velocity => body == null ? Vector3.zero : body.velocity;
    public int TakeoffSequence { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }

        body = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        currentStamina = maxStamina;

        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.drag = 0.5f;
        groundDrag = body.drag;
    }

    private void OnEnable()
    {
        if (!activePlayers.Contains(this))
        {
            activePlayers.Add(this);
        }

        if (Instance == null)
        {
            Instance = this;
        }
    }

    private void OnDisable()
    {
        RemoveFromActivePlayers();
    }

    private void OnDestroy()
    {
        RemoveFromActivePlayers();
    }

    private void RemoveFromActivePlayers()
    {
        activePlayers.Remove(this);
        if (Instance == this)
        {
            Instance = activePlayers.Count > 0 ? activePlayers[0] : null;
        }
    }

    private void Update()
    {
        if (!CanAcceptInput())
        {
            moveInput = Vector2.zero;
            climbInput = descendInput = false;
            return;
        }

        moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (!isFlying) moveInput = Vector2.ClampMagnitude(moveInput, 1f);
        climbInput = Input.GetKey(KeyCode.Space);
        descendInput = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        HandleFlightToggleInput();
        if (isFlying)
        {
            AdjustFlightSpeed(moveInput.y * keyboardSpeedAdjustment * Time.deltaTime +
                Input.GetAxis("Mouse ScrollWheel") * scrollSpeedAdjustment);
            // A/D steer in flight; W/S only adjust the throttle.
            moveInput.y = 0f;
        }
    }

    private void FixedUpdate()
    {
        crashRecoveryRemaining = Mathf.Max(0f, crashRecoveryRemaining - Time.fixedDeltaTime);
        if (!crashFallPending && !IsStunned) treeCrashLocked = false;
        isGrounded = CheckGrounded();

        bool gameRunning = GameManager.Instance == null || GameManager.Instance.IsRunning;
        if (!gameRunning && isFlying)
        {
            ExitFlightMode();
        }

        if (isFlying)
        {
            if (!isGrounded)
            {
                hasLeftTakeoffSurface = true;
            }
            else if (hasLeftTakeoffSurface && body.velocity.y <= .05f)
            {
                ExitFlightMode();
            }
        }

        if (!isFlying && isGrounded)
        {
            currentStamina = Mathf.Min(maxStamina, currentStamina + groundedRecoveryPerSecond * Time.fixedDeltaTime);
            if (flightLocked && currentStamina >= flightRestartStamina) flightLocked = false;
        }

        if (!gameRunning || IsStunned)
        {
            if (IsStunned) body.velocity = Vector3.up * body.velocity.y;
            else SlowHorizontalMovement();
            return;
        }
        if (isFlying)
        {
            UpdateFlightMovement(Time.fixedDeltaTime);
            return;
        }
        // Do not route a tree-crash fall through the ordinary airborne movement controller.
        if (treeCrashLocked && crashFallPending)
        {
            moveInput = Vector2.zero;
            climbInput = descendInput = false;
            return;
        }
        if (TakePhoto.IsBackpackOpen)
        {
            SlowHorizontalMovement();
            return;
        }

        Vector3 moveDirection = GetCameraRelativeDirection();
        float targetSpeed = isJumping ? jumpMoveSpeed : isGrounded ? walkSpeed : airControl;
        Vector3 currentHorizontal = new Vector3(body.velocity.x, 0f, body.velocity.z);
        Vector3 nextHorizontal = Vector3.MoveTowards(currentHorizontal, moveDirection * targetSpeed,
            (isJumping ? jumpAcceleration : acceleration) * Time.fixedDeltaTime);
        if (isJumping) nextHorizontal = Vector3.ClampMagnitude(nextHorizontal, jumpMoveSpeed);
        body.velocity = new Vector3(nextHorizontal.x, body.velocity.y, nextHorizontal.z);
        if (moveDirection.sqrMagnitude > .01f)
        {
            body.MoveRotation(Quaternion.Slerp(body.rotation,
                Quaternion.LookRotation(moveDirection, Vector3.up), turnSpeed * Time.fixedDeltaTime));
        }
    }

    private void UpdateFlightMovement(float dt)
    {
        Vector3 desired = Mathf.Abs(moveInput.x) > .01f
            ? Quaternion.AngleAxis(moveInput.x * 90f, Vector3.up) * (body.rotation * Vector3.forward)
            : Vector3.zero;
        Quaternion rotation = StepFlightHeading(body.rotation, desired, ref flightYawRate,
            CurrentFlightTurnRate, flightTurnAcceleration, flightTurnResponse, dt);
        Vector3 forward = rotation * Vector3.forward;
        float horizontalSpeed = new Vector2(body.velocity.x, body.velocity.z).magnitude;
        targetFlightSpeed = Mathf.Clamp(targetFlightSpeed, MinimumFlightSpeed, MaximumFlightSpeed);
        horizontalSpeed = Mathf.MoveTowards(horizontalSpeed, targetFlightSpeed, flightAcceleration * dt);
        // Shift takes priority, including while slowing an earlier climb.
        bool climbing = !descendInput && (climbInput || takeoffLiftRemaining > 0f);
        float targetVertical = descendInput ? -flightDescendSpeed : climbing ? flightRiseSpeed : 0f;
        takeoffLiftRemaining = Mathf.Max(0f, takeoffLiftRemaining - dt);
        float vertical = Mathf.MoveTowards(body.velocity.y, targetVertical, flightVerticalAcceleration * dt);
        body.velocity = forward * horizontalSpeed + Vector3.up * vertical;
        body.MoveRotation(rotation);
        float drain = descendInput ? 0f : climbing || vertical > .05f ? flightDrainPerSecond : glideDrainPerSecond;
        currentStamina = Mathf.Max(0f, currentStamina - drain * dt);
        if (currentStamina <= 0f)
        {
            flightLocked = true;
            BeginCrashFall();
        }
    }

    private void AdjustFlightSpeed(float change)
    {
        targetFlightSpeed = Mathf.Clamp(targetFlightSpeed + change, MinimumFlightSpeed, MaximumFlightSpeed);
    }

    public static Quaternion StepFlightHeading(Quaternion rotation, Vector3 desired, ref float yawRate,
        float maxRate, float angularAcceleration, float responseTime, float dt)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward.y = desired.y = 0f;
        float error = desired.sqrMagnitude > .01f ? Vector3.SignedAngle(forward, desired, Vector3.up) : 0f;
        float targetRate = Mathf.Clamp(error / Mathf.Max(.05f, responseTime), -maxRate, maxRate);
        yawRate = Mathf.MoveTowards(yawRate, targetRate, angularAcceleration * dt);
        float step = yawRate * dt;
        if (desired.sqrMagnitude > .01f && Mathf.Sign(step) == Mathf.Sign(error) && Mathf.Abs(step) > Mathf.Abs(error))
        { step = error; yawRate = 0f; }
        return Quaternion.AngleAxis(step, Vector3.up) * rotation;
    }

    private bool CanAcceptInput()
    {
        return !IsStunned && !treeCrashLocked &&
               !TakePhoto.IsBackpackOpen &&
               (GameManager.Instance == null || GameManager.Instance.IsRunning);
    }

    private void HandleFlightToggleInput()
    {
        if (!Input.GetKeyDown(KeyCode.Space))
        {
            return;
        }

        float currentTime = Time.unscaledTime;
        HandleSpacePress(currentTime);
    }

    private void HandleSpacePress(float currentTime)
    {
        if (!CanAcceptInput()) return;
        if (currentTime - lastSpacePressTime <= doubleTapWindow)
        {
            if (isFlying)
            {
                BeginCrashFall();
            }
            else if (!flightLocked && currentStamina > 0f)
            {
                EnterFlightMode();
            }

            lastSpacePressTime = float.NegativeInfinity;
        }
        else
        {
            lastSpacePressTime = currentTime;
            TryJump();
        }
    }

    private void TryJump()
    {
        if (!CanAcceptInput() || !isGrounded || isFlying || isJumping || crashFallPending) return;
        isJumping = true;
        isGrounded = false;
        body.useGravity = true;
        // Do not carry the faster walking speed into a short hop.
        Vector3 horizontal = moveInput.sqrMagnitude > .001f
            ? Vector3.ClampMagnitude(new Vector3(body.velocity.x, 0f, body.velocity.z), jumpMoveSpeed)
            : Vector3.zero;
        float launchSpeed = Mathf.Sqrt(2f * Mathf.Max(.01f, -Physics.gravity.y) * jumpHeight);
        body.velocity = horizontal + Vector3.up * launchSpeed;
    }

    private void EnterFlightMode()
    {
        if (IsStunned || treeCrashLocked) return;
        bool groundTakeoff = isGrounded || isJumping;
        isJumping = false;
        crashFallPending = false;
        isFlying = true;
        body.useGravity = false;
        groundDrag = body.drag;
        body.drag = 0f;
        flightYawRate = 0f;
        takeoffLiftRemaining = groundTakeoff ? takeoffLiftDuration : 0f;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        float launchSpeed = groundTakeoff ? Mathf.Clamp(takeoffSpeed, MinimumFlightSpeed, MaximumFlightSpeed)
            : Mathf.Clamp(new Vector2(body.velocity.x, body.velocity.z).magnitude, MinimumFlightSpeed, MaximumFlightSpeed);
        targetFlightSpeed = launchSpeed;
        body.velocity = forward * launchSpeed + Vector3.up * body.velocity.y;
        hasLeftTakeoffSurface = !isGrounded;
        if (groundTakeoff)
        {
            TakeoffSequence++;
            body.velocity = new Vector3(body.velocity.x, flightRiseSpeed, body.velocity.z);
        }
    }

    private void ExitFlightMode()
    {
        isFlying = false;
        body.useGravity = true;
        body.drag = groundDrag;
        takeoffLiftRemaining = 0f;
        hasLeftTakeoffSurface = false;
        lastSpacePressTime = float.NegativeInfinity;
    }

    private void BeginCrashFall()
    {
        crashFallPending = true;
        ExitFlightMode();
    }

    private void OnCollisionEnter(Collision collision) => CheckCrashImpact(collision);
    private void OnCollisionStay(Collision collision) => CheckCrashImpact(collision);

    private void CheckCrashImpact(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            var contact = collision.GetContact(i);
            if (TerrainTreeTrunks.IsTrunkContact(contact.otherCollider, contact.point))
                HandleTrunkCrash();
        }
        // Contacts are classified individually: a TerrainCollider may contain both trunk and ground.
        for (int i = 0; i < collision.contactCount; i++)
        {
            var contact = collision.GetContact(i);
            if (TerrainTreeTrunks.IsTrunkContact(contact.otherCollider, contact.point)) continue;
            if ((groundLayers.value & (1 << contact.otherCollider.gameObject.layer)) == 0) continue;
            if (contact.normal.y <= .35f) continue;
            if (isJumping && body.velocity.y <= .05f) isJumping = false;
            BeginCrashRecovery();
            break;
        }
    }

    private bool HandleFlightCollision(Collider hit)
    {
        if (hit == null || !hit.CompareTag("TreeTrunk")) return false;
        return HandleTrunkCrash();
    }

    private bool HandleTrunkCrash()
    {
        if (isFlying)
        {
            treeCrashLocked = true;
            BeginCrashFall();
            moveInput = Vector2.zero;
            climbInput = descendInput = false;
            body.velocity = Vector3.up * Mathf.Min(0f, body.velocity.y);
        }
        // A trunk contact is not a landing; wait for ground support.
        return crashFallPending;
    }

    private void BeginCrashRecovery()
    {
        if (!crashFallPending) return;
        crashFallPending = false;
        crashRecoveryRemaining = crashRecoverySeconds;
        moveInput = Vector2.zero;
        climbInput = descendInput = false;
        lastSpacePressTime = float.NegativeInfinity;
        body.velocity = Vector3.up * Mathf.Min(0f, body.velocity.y);
    }

    private Vector3 GetCameraRelativeDirection()
    {
        Transform cameraTransform = Camera.main == null ? null : Camera.main.transform;
        Vector3 forward = cameraTransform == null ? Vector3.forward : cameraTransform.forward;
        Vector3 right = cameraTransform == null ? Vector3.right : cameraTransform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();
        return Vector3.ClampMagnitude(forward * moveInput.y + right * moveInput.x, 1f);
    }

    private bool CheckGrounded()
    {
        Bounds bounds = bodyCollider.bounds;
        float radius = Mathf.Max(0.05f, Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.8f);
        float distance = bounds.extents.y + groundCheckDistance;
        RaycastHit[] hits = Physics.SphereCastAll(
            bounds.center,
            radius,
            Vector3.down,
            distance,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider != bodyCollider && hit.normal.y > 0.35f &&
                !TerrainTreeTrunks.IsTrunkContact(hit.collider, hit.point))
            {
                return true;
            }
        }

        return false;
    }

    private void SlowHorizontalMovement()
    {
        Vector3 horizontal = new Vector3(body.velocity.x, 0f, body.velocity.z);
        horizontal = Vector3.MoveTowards(horizontal, Vector3.zero, acceleration * Time.fixedDeltaTime);
        body.velocity = new Vector3(horizontal.x, body.velocity.y, horizontal.z);
    }
}
