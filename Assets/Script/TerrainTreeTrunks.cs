using System.Collections.Generic;
using UnityEngine;

/// <summary>Resolves terrain-generated tree contacts, which report the terrain's collider/tag.</summary>
public static class TerrainTreeTrunks
{
    private const float CellSize = 16f;
    private struct Capsule { public Vector3 a, b; public float radius; }
    private sealed class Cache
    {
        public TerrainData data;
        public int count;
        public Matrix4x4 matrix;
        public readonly Dictionary<Vector2Int, List<Capsule>> cells = new Dictionary<Vector2Int, List<Capsule>>();
    }
    private static readonly Dictionary<Terrain, Cache> caches = new Dictionary<Terrain, Cache>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ClearCache() { caches.Clear(); }

    private static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));

    public static bool IsTrunkContact(Collider collider, Vector3 point)
    {
        if (collider == null) return false;
        if (collider.CompareTag("TreeTrunk")) return true;
        var terrainCollider = collider as TerrainCollider;
        if (terrainCollider == null) return false;
        var terrain = collider.GetComponent<Terrain>();
        if (terrain == null || terrain.terrainData == null) return false;
        // Ground at a tree's base must still count as a landing surface.
        if (point.y <= terrain.SampleHeight(point) + terrain.transform.position.y + .08f) return false;
        Cache cache;
        if (!caches.TryGetValue(terrain, out cache) || cache.data != terrain.terrainData ||
            cache.count != terrain.terrainData.treeInstanceCount || cache.matrix != terrain.transform.localToWorldMatrix)
        {
            cache = Build(terrain);
            caches[terrain] = cache;
        }
        List<Capsule> candidates;
        if (!cache.cells.TryGetValue(Cell(point), out candidates)) return false;
        foreach (var c in candidates)
        {
            Vector3 ab = c.b - c.a;
            float u = ab.sqrMagnitude < .000001f ? 0f : Mathf.Clamp01(Vector3.Dot(point - c.a, ab) / ab.sqrMagnitude);
            if ((point - (c.a + u * ab)).sqrMagnitude <= (c.radius + .08f) * (c.radius + .08f)) return true;
        }
        return false;
    }

    private static Cache Build(Terrain terrain)
    {
        var data = terrain.terrainData;
        var cache = new Cache { data = data, count = data.treeInstanceCount, matrix = terrain.transform.localToWorldMatrix };
        var prototypes = data.treePrototypes;
        var colliders = new List<CapsuleCollider>[prototypes.Length];
        for (int i = 0; i < prototypes.Length; i++)
        {
            colliders[i] = new List<CapsuleCollider>();
            if (prototypes[i].prefab == null) continue;
            foreach (var c in prototypes[i].prefab.GetComponentsInChildren<CapsuleCollider>())
                if (c.enabled && !c.isTrigger && c.CompareTag("TreeTrunk")) colliders[i].Add(c);
        }
        foreach (var tree in data.treeInstances)
        {
            if (tree.prototypeIndex < 0 || tree.prototypeIndex >= colliders.Length) continue;
            var instance = cache.matrix * Matrix4x4.TRS(Vector3.Scale(tree.position, data.size),
                Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f), new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
            foreach (var source in colliders[tree.prototypeIndex])
            {
                var matrix = instance * source.transform.localToWorldMatrix;
                Vector3 axis = source.direction == 0 ? Vector3.right : source.direction == 1 ? Vector3.up : Vector3.forward;
                Vector3 radialA = source.direction == 0 ? Vector3.up : Vector3.right;
                Vector3 radialB = source.direction == 2 ? Vector3.up : Vector3.forward;
                float radius = source.radius * Mathf.Max(matrix.MultiplyVector(radialA).magnitude, matrix.MultiplyVector(radialB).magnitude);
                Vector3 direction = matrix.MultiplyVector(axis);
                float halfSegment = Mathf.Max(0f, source.height * direction.magnitude * .5f - radius);
                Vector3 center = matrix.MultiplyPoint3x4(source.center);
                var capsule = new Capsule { a = center - direction.normalized * halfSegment, b = center + direction.normalized * halfSegment, radius = radius };
                Vector3 padding = Vector3.one * (radius + .08f);
                var min = Cell(Vector3.Min(capsule.a, capsule.b) - padding);
                var max = Cell(Vector3.Max(capsule.a, capsule.b) + padding);
                for (int x = min.x; x <= max.x; x++)
                for (int z = min.y; z <= max.y; z++)
                {
                    var key = new Vector2Int(x, z);
                    List<Capsule> list;
                    if (!cache.cells.TryGetValue(key, out list)) { list = new List<Capsule>(); cache.cells.Add(key, list); }
                    list.Add(capsule);
                }
            }
        }
        return cache;
    }
}
