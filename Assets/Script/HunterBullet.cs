using UnityEngine;

public class HunterBullet : MonoBehaviour
{
    public const float CollisionRadius = 0.07f;

    private Vector3 velocity;
    private float gravity;
    private float lifetime;
    private float age;
    private float maxDistance;
    private float traveledDistance;
    private float collisionRadius;
    private Transform shooter;
    private MeshRenderer meshRenderer;
    private TrailRenderer trail;
    private Material bulletMaterial;
    private bool resolved;

    public static HunterBullet Create(
        Vector3 position,
        Vector3 direction,
        float speed,
        float gravity,
        float lifetime,
        float maxDistance,
        Transform shooter)
    {
        GameObject bulletObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bulletObject.name = "Hunter Bullet";
        bulletObject.transform.position = position;
        bulletObject.transform.localScale = Vector3.one * (CollisionRadius * 2f);

        Collider primitiveCollider = bulletObject.GetComponent<Collider>();
        if (primitiveCollider != null)
        {
            primitiveCollider.enabled = false;
        }

        HunterBullet bullet = bulletObject.AddComponent<HunterBullet>();
        bullet.Initialize(direction, speed, gravity, lifetime, maxDistance, shooter);
        return bullet;
    }

    private void Initialize(
        Vector3 direction,
        float speed,
        float bulletGravity,
        float maxLifetime,
        float maximumDistance,
        Transform shooterTransform)
    {
        velocity = direction.normalized * speed;
        gravity = bulletGravity;
        lifetime = maxLifetime;
        maxDistance = Mathf.Max(0f, maximumDistance);
        shooter = shooterTransform;
        collisionRadius = transform.localScale.x * 0.5f;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        bulletMaterial = new Material(shader) { name = "HunterBulletMaterial" };
        SetMaterialColor(new Color(1f, 0.55f, 0.08f));

        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = bulletMaterial;

        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.8f;
        trail.startWidth = 0.11f;
        trail.endWidth = 0.01f;
        trail.minVertexDistance = 0.03f;
        trail.numCapVertices = 3;
        trail.alignment = LineAlignment.View;
        trail.startColor = new Color(1f, 0.9f, 0.2f, 1f);
        trail.endColor = new Color(1f, 0.15f, 0.02f, 0f);
        trail.sharedMaterial = bulletMaterial;
    }

    private void Update()
    {
        if (resolved)
        {
            return;
        }

        if (GameManager.Instance != null && !GameManager.Instance.IsRunning)
        {
            Destroy(gameObject);
            return;
        }

        float deltaTime = Time.deltaTime;
        velocity += Vector3.down * gravity * deltaTime;
        Vector3 displacement = velocity * deltaTime;
        float remainingDistance = maxDistance - traveledDistance;
        if (remainingDistance <= 0f)
        {
            Resolve(null);
            return;
        }

        bool reachesRangeLimit = displacement.magnitude >= remainingDistance;
        if (reachesRangeLimit)
        {
            displacement = displacement.normalized * remainingDistance;
        }

        if (TryFindHit(displacement, out RaycastHit hit))
        {
            transform.position = hit.point;
            Resolve(hit.collider.GetComponentInParent<PlayerMove>());
            return;
        }

        transform.position += displacement;
        traveledDistance += displacement.magnitude;
        age += deltaTime;
        if (reachesRangeLimit || age >= lifetime)
        {
            Resolve(null);
        }
    }

    private bool TryFindHit(Vector3 displacement, out RaycastHit closestHit)
    {
        closestHit = default;
        float distance = displacement.magnitude;
        if (distance <= 0.0001f)
        {
            return false;
        }

        RaycastHit[] hits = Physics.SphereCastAll(
            transform.position,
            collisionRadius,
            displacement.normalized,
            distance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        bool found = false;
        float closestDistance = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null ||
                hit.transform == transform ||
                (shooter != null && (hit.transform == shooter || hit.transform.IsChildOf(shooter))))
            {
                continue;
            }

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestHit = hit;
                found = true;
            }
        }

        return found;
    }

    private void Resolve(PlayerMove hitPlayer)
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        if (hitPlayer != null)
        {
            hitPlayer.TakeHit();
        }
        else if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterHunterMiss();
        }

        meshRenderer.enabled = false;
        trail.emitting = false;
        Destroy(gameObject, trail.time);
    }

    private void SetMaterialColor(Color color)
    {
        if (bulletMaterial.HasProperty("_BaseColor"))
        {
            bulletMaterial.SetColor("_BaseColor", color);
        }
        else
        {
            bulletMaterial.color = color;
        }
    }

    private void OnDestroy()
    {
        if (bulletMaterial != null)
        {
            Destroy(bulletMaterial);
        }
    }
}
