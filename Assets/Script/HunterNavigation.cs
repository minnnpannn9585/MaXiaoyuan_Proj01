using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>NavMesh owns translation; HunterMove owns decisions and facing.</summary>
[RequireComponent(typeof(NavMeshAgent), typeof(Rigidbody), typeof(Collider))]
public sealed class HunterNavigation : MonoBehaviour
{
    [SerializeField, Min(.1f)] private float repathInterval = .4f;
    [SerializeField, Min(.1f)] private float destinationSampleRadius = 2f;
    private NavMeshAgent agent;
    private NavMeshPath path;
    private Vector3 requestedTarget;
    private Vector3 pathEnd;
    private float nextPathTime;
    private float nextBindTime;
    private bool requested;
    private bool failed;
    private bool warned;

    public bool Ready => agent != null && agent.enabled && agent.isOnNavMesh;
    public Vector3 Velocity => Ready && !agent.isStopped ? agent.velocity : Vector3.zero;
    public bool AtPathEnd => requested && Ready && !agent.pathPending &&
        (failed || (agent.hasPath ? agent.remainingDistance <= agent.stoppingDistance + .15f :
            Vector2.Distance(new Vector2(transform.position.x, transform.position.z),
                new Vector2(pathEnd.x, pathEnd.z)) <= agent.stoppingDistance + .15f));
    private NavMeshQueryFilter Filter => new NavMeshQueryFilter
        { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        path = new NavMeshPath();
        var body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
        agent.updatePosition = true;
        agent.updateRotation = false;
        agent.autoRepath = true;
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
    }

    private void OnDisable() { Stop(); }

    public bool EnsureReady()
    {
        if (Ready) return true;
        if (agent == null || !agent.enabled || Time.time < nextBindTime) return false;
        nextBindTime = Time.time + 1f;
        float worldOffset = agent.baseOffset * Mathf.Abs(transform.lossyScale.y);
        Vector3 foot = transform.position - Vector3.up * worldOffset;
        if (NavMesh.SamplePosition(foot, out NavMeshHit hit, destinationSampleRadius, Filter) &&
            agent.Warp(hit.position + Vector3.up * worldOffset))
        {
            warned = false;
            return Ready;
        }
        if (!warned)
        {
            Debug.LogWarning("Hunter has no nearby baked NavMesh. Bake the Hunter Navigation surface or place the hunter on it.", this);
            warned = true;
        }
        return false;
    }

    public void Stop()
    {
        if (Ready)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }
    }

    public void MoveTo(Vector3 target, float speed, float stoppingDistance)
    {
        if (!EnsureReady()) return;
        agent.speed = speed;
        agent.stoppingDistance = Mathf.Max(.05f, stoppingDistance);
        bool changed = !requested || (target - requestedTarget).sqrMagnitude > .25f;
        if (changed || Time.time >= nextPathTime)
        {
            nextPathTime = Time.time + repathInterval;
            requestedTarget = target;
            requested = true;
            failed = !TryProjectTarget(target, out Vector3 destination) ||
                !agent.CalculatePath(destination, path) || path.status == NavMeshPathStatus.PathInvalid ||
                path.corners.Length == 0;
            if (failed) agent.ResetPath();
            else
            {
                pathEnd = path.corners[path.corners.Length - 1];
                failed = !agent.SetPath(path); // Partial paths stop at the reachable edge.
            }
        }
        agent.isStopped = failed;
    }

    public bool CannotGetCloserTo(Vector3 target)
    {
        return requested && (target - requestedTarget).sqrMagnitude <= .25f && AtPathEnd;
    }

    public bool TryPatrolPoint(Vector3 candidate, out Vector3 destination)
    {
        destination = default;
        return EnsureReady() && TryProjectTarget(candidate, out destination) &&
            agent.CalculatePath(destination, path) && path.status == NavMeshPathStatus.PathComplete &&
            (destination - transform.position).sqrMagnitude > 4f;
    }

    private bool TryProjectTarget(Vector3 target, out Vector3 destination)
    {
        // Flying players are projected onto physical ground/platforms below them.
        // Sampling a huge sphere would select unrelated floors or the far side of walls.
        var hits = Physics.RaycastAll(target + Vector3.up * .5f, Vector3.down, 1024f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<PlayerMove>() != null ||
                hit.collider.GetComponentInParent<HunterMove>() != null || hit.normal.y < .5f) continue;
            if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, destinationSampleRadius, Filter))
            {
                destination = navHit.position;
                return true;
            }
        }
        if (NavMesh.SamplePosition(target, out NavMeshHit nearby, destinationSampleRadius, Filter))
        {
            destination = nearby.position;
            return true;
        }
        destination = default;
        return false;
    }
}
