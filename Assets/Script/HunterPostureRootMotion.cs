using UnityEngine;

[RequireComponent(typeof(Animator))]
public class HunterPostureRootMotion : MonoBehaviour
{
    private static readonly int CrouchingState =
        Animator.StringToHash("Base Layer.Crouching");

    private Animator animator;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = true;
    }

    private void OnAnimatorMove()
    {
        if (animator == null)
        {
            return;
        }

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        bool applyPostureRootMotion = IsPostureState(currentState);

        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
            applyPostureRootMotion |= IsPostureState(nextState);
        }

        if (!applyPostureRootMotion)
        {
            return;
        }

        transform.position += animator.deltaPosition;
        transform.rotation *= animator.deltaRotation;
    }

    private static bool IsPostureState(AnimatorStateInfo stateInfo)
    {
        return stateInfo.fullPathHash == CrouchingState;
    }
}
