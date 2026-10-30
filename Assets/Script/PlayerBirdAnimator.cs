using UnityEngine;

/// <summary>Feeds parameters only. Blending and exit times live in the Animator Controller.</summary>
[RequireComponent(typeof(PlayerMove))]
public sealed class PlayerBirdAnimator : MonoBehaviour
{
    [SerializeField] private Animator birdAnimator;
    [SerializeField, Min(0f)] private float idleDelay = 5f;
    [Header("Glide / 滑翔扑翼")]
    [SerializeField, Min(.1f), InspectorName("滑翔扑翼间隔（秒）")] private float glideFlapInterval = 3f;
    [SerializeField, Min(.1f), InspectorName("每次扑翼时长（秒）")] private float glideFlapDuration = .8f;
    [Header("Bank / 转弯侧倾")]
    [SerializeField, Range(0f, 60f), InspectorName("最大侧倾角度")] private float maximumBankAngle = 28f;
    [SerializeField, Min(.1f), InspectorName("侧倾平滑速度")] private float bankResponse = 5f;
    private Transform bankPivot;
    private float glideTime;
    private float bankAngle;
    private PlayerMove movement;
    private float idleTime;
    private bool ascending;
    private int takeoffSequence;
    public BirdAnimationState CurrentState
    {
        get
        {
            if (birdAnimator == null) return BirdAnimationState.Standing;
            var info = birdAnimator.GetCurrentAnimatorStateInfo(0);
            foreach (BirdAnimationState state in System.Enum.GetValues(typeof(BirdAnimationState)))
                if (info.IsName("Base Layer." + state)) return state;
            return BirdAnimationState.Standing;
        }
    }

    public void Configure(Animator animator, float takeoffSeconds, float idleSeconds) { birdAnimator = animator; }

    private void OnEnable()
    {
        movement = GetComponent<PlayerMove>();
        if (birdAnimator == null) birdAnimator = GetComponentInChildren<Animator>();
        takeoffSequence = movement.TakeoffSequence;
        idleTime = 0f;
        if (birdAnimator != null)
        {
            if (bankPivot == null && birdAnimator.transform != transform)
            {
                bankPivot = new GameObject("FlightBank").transform;
                bankPivot.SetParent(birdAnimator.transform.parent, false);
                // Pivot around the model origin without changing its scale or orientation.
                bankPivot.localPosition = birdAnimator.transform.localPosition;
                birdAnimator.transform.SetParent(bankPivot, false);
                birdAnimator.transform.localPosition = Vector3.zero;
            }
            birdAnimator.applyRootMotion = false;
            birdAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    private void Update()
    {
        if (birdAnimator == null || !birdAnimator.isActiveAndEnabled) return;
        bool moving = new Vector2(movement.Velocity.x, movement.Velocity.z).magnitude > .08f;
        bool looking = birdAnimator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.IdleLookAround") ||
            (birdAnimator.IsInTransition(0) && birdAnimator.GetNextAnimatorStateInfo(0).IsName("Base Layer.IdleLookAround"));
        if (movement.IsCrashRecovering || movement.IsFlying || !movement.IsGrounded || moving || looking) idleTime = 0f;
        else if (!TakePhoto.IsBackpackOpen && (GameManager.Instance == null || GameManager.Instance.IsRunning)) idleTime += Time.deltaTime;
        ascending = movement.IsFlying && movement.Velocity.y > (ascending ? .05f : .15f);
        bool flapping = UpdateGlideFlapping(movement.IsFlying, ascending || movement.IsAdjustingFlightSpeed, Time.deltaTime);
        birdAnimator.SetBool("IsGrounded", movement.IsGrounded && !movement.IsCrashFalling && !movement.IsJumping);
        birdAnimator.SetBool("IsJumping", movement.IsJumping);
        birdAnimator.SetBool("CrashRecovery", movement.IsCrashRecovering);
        birdAnimator.SetBool("TreeCrash", movement.IsTreeCrash);
        birdAnimator.SetBool("IsFlying", movement.IsFlying);
        birdAnimator.SetBool("IsMoving", moving);
        birdAnimator.SetBool("IsAscending", ascending);
        birdAnimator.SetBool("IsFlapping", flapping);
        birdAnimator.SetBool("IdleReady", idleTime >= idleDelay);
        if (takeoffSequence != movement.TakeoffSequence)
        {
            takeoffSequence = movement.TakeoffSequence;
            birdAnimator.SetTrigger("TakeOff");
        }
        if (!movement.IsFlying) birdAnimator.ResetTrigger("TakeOff");
    }

    private bool UpdateGlideFlapping(bool flying, bool climbing, float dt)
    {
        if (!flying || climbing) { glideTime = 0f; return flying && climbing; }
        glideTime = Mathf.Repeat(glideTime + dt, glideFlapInterval + glideFlapDuration);
        return glideTime >= glideFlapInterval;
    }

    private void LateUpdate()
    {
        if (bankPivot == null || movement == null) return;
        bankAngle = Mathf.Lerp(bankAngle, -movement.FlightBankInput * maximumBankAngle,
            1f - Mathf.Exp(-bankResponse * Time.deltaTime));
        bankPivot.localRotation = Quaternion.Euler(0f, 0f, bankAngle);
    }

    private void OnDisable()
    {
        if (bankPivot != null) bankPivot.localRotation = Quaternion.identity;
        bankAngle = glideTime = 0f;
    }
}

public enum BirdAnimationState { Standing, Walk, TakeOff, IdleLookAround, Fly, Glide, Falling, DeathHitTheGround, Jump, TreeCrashFall, TreeCrashImpact }
