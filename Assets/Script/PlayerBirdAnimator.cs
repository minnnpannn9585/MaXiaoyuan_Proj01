using UnityEngine;

/// <summary>Feeds parameters only. Blending and exit times live in the Animator Controller.</summary>
[RequireComponent(typeof(PlayerMove))]
public sealed class PlayerBirdAnimator : MonoBehaviour
{
    [SerializeField] private Animator birdAnimator;
    [SerializeField, Min(0f)] private float idleDelay = 5f;
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
        if (movement.IsFlying || !movement.IsGrounded || moving || looking) idleTime = 0f;
        else if (!TakePhoto.IsBackpackOpen && (GameManager.Instance == null || GameManager.Instance.IsRunning)) idleTime += Time.deltaTime;
        ascending = movement.IsFlying && movement.Velocity.y > (ascending ? .05f : .15f);
        birdAnimator.SetBool("IsGrounded", movement.IsGrounded);
        birdAnimator.SetBool("IsFlying", movement.IsFlying);
        birdAnimator.SetBool("IsMoving", moving);
        birdAnimator.SetBool("IsAscending", ascending);
        birdAnimator.SetBool("IdleReady", idleTime >= idleDelay);
        if (takeoffSequence != movement.TakeoffSequence)
        {
            takeoffSequence = movement.TakeoffSequence;
            birdAnimator.SetTrigger("TakeOff");
        }
        if (!movement.IsFlying) birdAnimator.ResetTrigger("TakeOff");
    }
}

public enum BirdAnimationState { Standing, Walk, TakeOff, IdleLookAround, Fly, Glide, Falling }
