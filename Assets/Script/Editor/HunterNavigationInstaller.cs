using System;
using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class HunterNavigationInstaller
{
    [MenuItem("Tools/Hunter/Build Scene Navigation")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Build outside Play Mode.");
        var hunters = UnityEngine.Object.FindObjectsOfType<HunterMove>();
        if (hunters.Length == 0) throw new InvalidOperationException("No hunter in scene.");
        foreach (var hunter in hunters)
        {
            var agent = hunter.GetComponent<NavMeshAgent>();
            if (agent == null) agent = Undo.AddComponent<NavMeshAgent>(hunter.gameObject);
            if (hunter.GetComponent<HunterNavigation>() == null) Undo.AddComponent<HunterNavigation>(hunter.gameObject);
            Bounds bodyBounds = hunter.GetComponent<Collider>().bounds;
            agent.agentTypeID = 0;
            agent.radius = Mathf.Max(.5f, Mathf.Max(bodyBounds.extents.x, bodyBounds.extents.z));
            agent.height = Mathf.Max(2f, bodyBounds.size.y);
            agent.baseOffset = (hunter.transform.position.y - bodyBounds.min.y) /
                Mathf.Max(.001f, Mathf.Abs(hunter.transform.lossyScale.y));
            agent.acceleration = 12f;
            agent.updatePosition = true;
            agent.updateRotation = false;
            agent.autoRepath = true;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.enabled = true;
            var body = hunter.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            EditorUtility.SetDirty(body);
            EditorUtility.SetDirty(agent);
        }
        foreach (var player in UnityEngine.Object.FindObjectsOfType<PlayerMove>())
        {
            var modifier = player.GetComponent<NavMeshModifier>();
            if (modifier == null) modifier = Undo.AddComponent<NavMeshModifier>(player.gameObject);
            modifier.ignoreFromBuild = true;
            EditorUtility.SetDirty(modifier);
        }
        int rocksMarked = 0;
        foreach (var collider in UnityEngine.Object.FindObjectsOfType<Collider>())
        {
            // These are the two Gaia rock prefab families in this scene; do not
            // classify bridges, paths or terrain by a broad "stone" substring.
            if (!collider.name.StartsWith("_Sp_PW_Stone_01", StringComparison.Ordinal)) continue;
            var modifier = collider.GetComponent<NavMeshModifier>();
            if (modifier == null) modifier = Undo.AddComponent<NavMeshModifier>(collider.gameObject);
            modifier.ignoreFromBuild = false;
            modifier.overrideArea = true;
            modifier.area = NavMesh.GetAreaFromName("Not Walkable");
            EditorUtility.SetDirty(modifier);
            rocksMarked++;
        }
        var go = GameObject.Find("Hunter Navigation");
        if (go == null) { go = new GameObject("Hunter Navigation"); Undo.RegisterCreatedObjectUndo(go, "Add navigation surface"); }
        var surface = go.GetComponent<NavMeshSurface>();
        if (surface == null) surface = Undo.AddComponent<NavMeshSurface>(go);
        surface.agentTypeID = 0;
        surface.collectObjects = CollectObjects.Volume;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        surface.overrideVoxelSize = true;
        surface.voxelSize = .125f;
        surface.buildHeightMesh = true;
        Bounds bounds = new Bounds(hunters[0].transform.position, new Vector3(100, 100, 100));
        foreach (var terrain in Terrain.activeTerrains)
            bounds.Encapsulate(new Bounds(terrain.transform.position + terrain.terrainData.size * .5f, terrain.terrainData.size));
        surface.center = bounds.center;
        surface.size = bounds.size + Vector3.one * 4f;
        EditorUtility.SetDirty(surface);
        NavMeshAssetManager.instance.StartBakingSurfaces(new UnityEngine.Object[] { surface });
        Debug.Log("Hunter NavMesh bake started; " + rocksMarked + " rocks marked Not Walkable. After completion, run Tools/Hunter/Finish Scene Navigation.");
    }

    [MenuItem("Tools/Hunter/Finish Scene Navigation")]
    public static string Finish()
    {
        var surface = GameObject.Find("Hunter Navigation").GetComponent<NavMeshSurface>();
        if (surface.navMeshData == null) throw new InvalidOperationException("Bake is not complete.");
        foreach (var hunter in UnityEngine.Object.FindObjectsOfType<HunterMove>())
        {
            var agent = hunter.GetComponent<NavMeshAgent>();
            float worldOffset = agent.baseOffset * Mathf.Abs(hunter.transform.lossyScale.y);
            Vector3 foot = hunter.transform.position - Vector3.up * worldOffset;
            if (!NavMesh.SamplePosition(foot, out NavMeshHit hit, 3f, new NavMeshQueryFilter
                { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask }))
                throw new InvalidOperationException(hunter.name + " has no NavMesh near its spawn.");
            Undo.RecordObject(hunter.transform, "Place hunter on NavMesh");
            hunter.transform.position = hit.position + Vector3.up * worldOffset;
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
        EditorSceneManager.SaveScene(surface.gameObject.scene);
        return "Saved hunter navigation. Triangles: " + NavMesh.CalculateTriangulation().indices.Length / 3;
    }
}
