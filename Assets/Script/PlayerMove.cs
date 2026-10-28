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
    [SerializeField] private float flightSpeed = 10f;
    [SerializeField] private float flightRiseSpeed = 5f;
    [SerializeField] private float flightDescendSpeed = 4f;
    [SerializeField] private float airControl = 3f;
    [SerializeField] private float acceleration = 22f;
    [SerializeField] private float turnSpeed = 12f;
    [SerializeField] private float doubleTapWindow = 0.3f;

    [Header("Flight Steering")]
    [SerializeField, Min(1f)] private float flightTurnRate = 75f;
    [SerializeField, Min(1f)] private float flightTurnAcceleration = 150f;
    [SerializeField, Min(.05f)] private float flightTurnResponse = .5f;
    [SerializeField, Min(.1f)] private float flightVerticalAcceleration = 6f;
    [SerializeField, Range(.1f, 1f)] private float takeoffForwardSpeedRatio = .45f;
    [SerializeField, Min(0f)] private float takeoffLiftDuration = .35f;
    private float flightYawRate;
    private float takeoffLiftRemaining;
    private bool climbInput;
    private bool descendInput;

    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float flightDrainPerSecond = 24f;
    [SerializeField] private float groundedRecoveryPerSecond = 18f;
    [SerializeField] private float flightRestartStamina = 15f;
    [SerializeField] private float exhaustionStunSeconds = 1f;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckDistance = 0.18f;
    [SerializeField] private LayerMask groundLayers = ~0;

    private Rigidbody body;
    private Collider bodyCollider;
    private Vector2 moveInput;
    private float currentStamina;
    private float stunUntil;
    private float lastSpacePressTime = float.NegativeInfinity;
    private bool flightLocked;
    private bool isGrounded;
    private bool isFlying;
    private bool hasLeftTakeoffSurface;

    public float CurrentStamina => currentStamina;
    public float MaxStamina => maxStamina;
    public float Stamina01 => maxStamina <= 0f ? 0f : currentStamina / maxStamina;
    public float CurrentSpeed => body == null ? 0f : body.velocity.magnitude;
    public float NormalizedSpeed => Mathf.Clamp01(CurrentSpeed / flightSpeed);
    public bool IsGrounded => isGrounded;
    public bool IsFlying => isFlying;
    public bool IsStunned => Time.time < stunUntil;
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
        moveInput = Vector2.ClampMagnitude(moveInput, 1f);
        climbInput = Input.GetKey(KeyCode.Space);
        descendInput = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        HandleFlightToggleInput();
    }

    private void FixedUpdate()
    {
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
            else if (hasLeftTakeoffSurface)
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
            SlowHorizontalMovement();
            return;
        }
        if (isFlying)
        {
            UpdateFlightMovement(Time.fixedDeltaTime);
            return;
        }
        if (TakePhoto.IsBackpackOpen)
        {
            SlowHorizontalMovement();
            return;
        }

        Vector3 moveDirection = GetCameraRelativeDirection();
        float targetSpeed = isGrounded ? walkSpeed : airControl;
        Vector3 currentHorizontal = new Vector3(body.velocity.x, 0f, body.velocity.z);
        Vector3 nextHorizontal = Vector3.MoveTowards(currentHorizontal, moveDirection * targetSpeed,
            acceleration * Time.fixedDeltaTime);
        body.velocity = new Vector3(nextHorizontal.x, body.velocity.y, nextHorizontal.z);
        if (moveDirection.sqrMagnitude > .01f)
        {
            body.MoveRotation(Quaternion.Slerp(body.rotation,
                Quaternion.LookRotation(moveDirection, Vector3.up), turnSpeed * Time.fixedDeltaTime));
        }
    }

    private void UpdateFlightMovement(float dt)
    {
        Vector3 desired = GetCameraRelativeDirection();
        Quaternion rotation = StepFlightHeading(body.rotation, desired, ref flightYawRate,
            flightTurnRate, flightTurnAcceleration, flightTurnResponse, dt);
        Vector3 forward = rotation * Vector3.forward;
        float horizontalSpeed = new Vector2(body.velocity.x, body.velocity.z).magnitude;
        horizontalSpeed = Mathf.MoveTowards(horizontalSpeed, flightSpeed, acceleration * dt);
        // No input maintains forward flight. S asks for a gradual U-turn, never reverse thrust.
        float targetVertical = climbInput || takeoffLiftRemaining > 0f ? flightRiseSpeed :
            descendInput ? -flightDescendSpeed : 0f;
        takeoffLiftRemaining = Mathf.Max(0f, takeoffLiftRemaining - dt);
        float vertical = Mathf.MoveTowards(body.velocity.y, targetVertical, flightVerticalAcceleration * dt);
        body.velocity = forward * horizontalSpeed + Vector3.up * vertical;
        body.MoveRotation(rotation);
        if (vertical > .05f)
        {
            currentStamina = Mathf.Max(0f, currentStamina - flightDrainPerSecond * dt);
            if (currentStamina <= 0f)
            {
                flightLocked = true;
                stunUntil = Time.time + exhaustionStunSeconds;
                ExitFlightMode();
            }
        }
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
        return !IsStunned &&
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
        if (currentTime - lastSpacePressTime <= doubleTapWindow)
        {
            if (isFlying)
            {
                ExitFlightMode();
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
        }
    }

    private void EnterFlightMode()
    {
        isFlying = true;
        body.useGravity = false;
        flightYawRate = 0f;
        takeoffLiftRemaining = isGrounded ? takeoffLiftDuration : 0f;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        float launchSpeed = Mathf.Max(new Vector2(body.velocity.x, body.velocity.z).magnitude,
            flightSpeed * takeoffForwardSpeedRatio);
        body.velocity = forward * launchSpeed + Vector3.up * body.velocity.y;
        hasLeftTakeoffSurface = !isGrounded;
        if (isGrounded)
        {
            TakeoffSequence++;
            body.velocity = new Vector3(body.velocity.x, flightRiseSpeed, body.velocity.z);
        }
    }

    private void ExitFlightMode()
    {
        isFlying = false;
        body.useGravity = true;
        takeoffLiftRemaining = 0f;
        hasLeftTakeoffSurface = false;
        lastSpacePressTime = float.NegativeInfinity;
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
            if (hit.collider != bodyCollider && hit.normal.y > 0.35f)
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
