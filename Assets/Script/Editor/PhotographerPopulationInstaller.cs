using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Explicit opt-in, never changes a scene just by importing this script.
[InitializeOnLoad]
public static class PhotographerPopulationInstaller
{
    const string Folder = "Temp/PhotographerPopulation";
    static int testStage;
    static double testDeadline;
    static HunterMove[] testHunters;
    static Vector3[] testPositions;
    static string testReport;
    static int initialPhotos, initialScore;
    static bool previousBackground;
    static PhotographerPopulationInstaller() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Folder+"/request")) return;
        string command=File.ReadAllText(Folder+"/request").Trim();
        if(command=="verify")
        {
            try { VerifyTick(); }
            catch(Exception e) { FinishTest(e.ToString());Debug.LogException(e); }
            return;
        }
        File.Delete(Folder+"/request");
        try { File.WriteAllText(Folder+"/result.txt", command=="metering-half-stop-baseline" ? SetExposureTarget() : command=="rebalance" ? Rebalance() : command=="install" ? Install() : command=="preview" ? PreviewColors() : Inspect()); }
        catch(Exception e) { File.WriteAllText(Folder+"/result.txt",e.ToString()); Debug.LogException(e); }
    }
    static void VerifyTick()
    {
        if(!EditorApplication.isPlaying){EditorApplication.isPlaying=true;return;}
        EditorApplication.QueuePlayerLoopUpdate();
        if(testStage==0)
        {
            previousBackground=Application.runInBackground;Application.runInBackground=true;
            testHunters=UnityEngine.Object.FindObjectsOfType<HunterMove>().OrderBy(h=>h.name).ToArray();
            if(testHunters.Length!=10)throw new Exception("Expected ten photographers");
            testPositions=testHunters.Select(h=>h.transform.position).ToArray();
            testReport="Ten distinct photographers, navigation and shared album runtime checks\n";
            GameFlow.Instance.Resume();testDeadline=EditorApplication.timeSinceStartup+10;testStage=1;return;
        }
        if(testStage==1)
        {
            if(EditorApplication.timeSinceStartup<testDeadline)return;
            foreach(var h in testHunters)
            {
                var agent=h.GetComponent<NavMeshAgent>();
                if(!agent.isOnNavMesh)throw new Exception(h.name+" not on NavMesh");
                var navigation=h.GetComponent<HunterNavigation>();
                var water=GameObject.Find("Unity URP Water");
                if(water!=null)
                {
                    if(navigation.IsDryPoint(new Vector3(0,water.transform.position.y-1,0)))throw new Exception("Water guard not configured");
                    foreach(var terrain in Terrain.activeTerrains)
                    {
                        Vector3 sea=terrain.transform.position+new Vector3(terrain.terrainData.size.x-3,0,terrain.terrainData.size.z-3);
                        sea.y=terrain.SampleHeight(sea)+terrain.transform.position.y;
                        if(sea.y<water.transform.position.y-2)
                        {
                            if(navigation.TryPatrolPoint(sea+Vector3.up*512,out _))throw new Exception(h.name+" chose submerged patrol target");
                            navigation.MoveTo(sea+Vector3.up*5,2.4f,.5f);
                            if(!agent.isStopped)throw new Exception(h.name+" pursued target into water");
                            testReport+=h.name+": submerged patrol/sea target rejected PASS\n";
                        }
                    }
                }
                float distance=Vector3.Distance(h.transform.position,testPositions[Array.IndexOf(testHunters,h)]);
                testReport+=$"{h.name}: moved {distance:F2}m, NavMesh={agent.isOnNavMesh}\n";
                if(distance<.2f)throw new Exception(h.name+" did not move during patrol test");
                h.enabled=false;
            }
            initialPhotos=TakePhoto.Instance.Photos.Count;
            initialScore=GameManager.Instance.ClearPlayerPhotos+GameManager.Instance.FailedPlayerPhotos;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            foreach(var h in new[]{testHunters[1],testHunters[8]})
            {
                typeof(HunterMove).GetField("aimPoint",flags).SetValue(h,PlayerMove.Instance.transform.position+Vector3.up*.35f);
                typeof(HunterMove).GetMethod("Fire",flags).Invoke(h,null);
            }
            testDeadline=EditorApplication.timeSinceStartup+60;testStage=2;return;
        }
        if(testHunters.Any(h=>h.GetComponent<TakePhoto>().IsProcessing))
        {
            if(EditorApplication.timeSinceStartup>testDeadline)throw new Exception("Concurrent capture timeout");return;
        }
        if(TakePhoto.Instance.Photos.Count-initialPhotos!=2)throw new Exception("Concurrent photos did not reach shared album");
        if(GameManager.Instance.ClearPlayerPhotos+GameManager.Instance.FailedPlayerPhotos-initialScore!=2)throw new Exception("Concurrent score did not count exactly twice");
        if(testHunters.Any(h=>!ReferenceEquals(h.GetComponent<TakePhoto>().Photos,TakePhoto.Instance.Photos)))throw new Exception("Photographers expose different albums");
        FinishTest("PASS\n"+testReport+"Two different photographers captured concurrently: shared album +2, score +2, all ten expose the same album.\n");
    }
    static void FinishTest(string result)
    {
        File.WriteAllText(Folder+"/verification.txt",result);File.Delete(Folder+"/request");
        Application.runInBackground=previousBackground;EditorApplication.isPlaying=false;testStage=0;
    }
    static string Inspect()
    {
        var s=new StringBuilder(); var scene=SceneManager.GetActiveScene();
        s.AppendLine($"Scene {scene.path} dirty={scene.isDirty} playing={Application.isPlaying}");
        foreach(var h in UnityEngine.Object.FindObjectsOfType<HunterMove>())
        {
            var agent=h.GetComponent<NavMeshAgent>();
            s.AppendLine($"Hunter {h.name} pos={h.transform.position} scale={h.transform.lossyScale} agent={agent.agentTypeID} offset={agent.baseOffset} radius={agent.radius}");
            foreach(var r in h.GetComponentsInChildren<Renderer>(true))
                s.AppendLine($"  Renderer {r.name} enabled={r.enabled} materials="+string.Join(",",r.sharedMaterials.Select(m=>m==null?"null":m.name+" ["+m.shader.name+"] "+AssetDatabase.GetAssetPath(m))));
        }
        foreach(var t in Terrain.activeTerrains)s.AppendLine($"Terrain {t.name} at={t.transform.position} size={t.terrainData.size}");
        var tri=NavMesh.CalculateTriangulation();s.AppendLine($"Nav vertices={tri.vertices.Length} triangles={tri.indices.Length/3}");
        if(tri.vertices.Length>0){var b=new Bounds(tri.vertices[0],Vector3.zero);foreach(var v in tri.vertices)b.Encapsulate(v);s.AppendLine("Nav bounds="+b);}
        return s.ToString();
    }
    static string SetExposureTarget()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Set exposure outside Play Mode.");
        var scene=SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene,Folder+"/before-metering-half-stop.unity",true);
        var report=new StringBuilder();
        foreach(var hunter in UnityEngine.Object.FindObjectsOfType<HunterMove>().Where(h=>h.gameObject.scene==scene))
        {
            var data=new SerializedObject(hunter);var compensation=data.FindProperty("exposureCompensation");
            float old=compensation.floatValue;compensation.floatValue=0f;data.FindProperty("meteringTargetLuminance").floatValue=.12727922f;data.ApplyModifiedProperties();
            report.AppendLine(hunter.name+": exposure compensation "+old+" -> "+compensation.floatValue+" EV; metering target=0.12727922");
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return report.ToString();
    }
    static string PreviewColors()
    {
        var hunters=UnityEngine.Object.FindObjectsOfType<HunterMove>().OrderBy(h=>h.name).ToArray();
        var scene=EditorSceneManager.NewPreviewScene();var active=RenderTexture.active;
        var target=new RenderTexture(1600,400,24);var read=new Texture2D(1600,400,TextureFormat.RGB24,false);
        try
        {
            for(int i=0;i<hunters.Length;i++)
            {
                var visual=UnityEngine.Object.Instantiate(hunters[i].transform.Find("HunterVisual").gameObject);
                SceneManager.MoveGameObjectToScene(visual,scene);visual.transform.position=new Vector3(i*2,0,0);
                visual.transform.rotation=Quaternion.identity;
            }
            var cameraObject=new GameObject("Palette preview camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
            camera.transform.position=new Vector3(9,1,15);camera.transform.rotation=Quaternion.Euler(0,180,0);
            camera.orthographic=true;camera.orthographicSize=2.7f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.12f,.12f);
            var lightObject=new GameObject("Palette light");SceneManager.MoveGameObjectToScene(lightObject,scene);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;
            light.transform.rotation=Quaternion.Euler(35,160,0);
            target.Create();camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            read.ReadPixels(new Rect(0,0,1600,400),0,0,false);read.Apply();File.WriteAllBytes(Folder+"/colors.png",read.EncodeToPNG());
            return "Saved preview to "+Folder+"/colors.png";
        }
        finally{RenderTexture.active=active;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(read);EditorSceneManager.ClosePreviewScene(scene);}
    }

    [MenuItem("Tools/Hunter/Populate Ten Photographers")]
    public static string Install()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Populate outside Play Mode.");
        var scene=SceneManager.GetActiveScene();
        var hunters=UnityEngine.Object.FindObjectsOfType<HunterMove>().Where(h=>h.gameObject.scene==scene).OrderBy(h=>h.name).ToList();
        if(hunters.Count!=1) throw new InvalidOperationException("Expected one source photographer; will not duplicate an already populated scene.");
        // Includes unsaved user edits; Save As Copy does not switch the active scene.
        EditorSceneManager.SaveScene(scene,Folder+"/SampleScene.unsaved-backup.unity",true);
        var original=hunters[0];var agent=original.GetComponent<NavMeshAgent>();
        var filter=new NavMeshQueryFilter{agentTypeID=agent.agentTypeID,areaMask=agent.areaMask};
        float offset=agent.baseOffset*Mathf.Abs(original.transform.lossyScale.y);
        if(!NavMesh.SamplePosition(original.transform.position-Vector3.up*offset,out var origin,4,filter))
            throw new InvalidOperationException("Original photographer has no navigable spawn.");
        var tri=NavMesh.CalculateTriangulation(); var candidates=new System.Collections.Generic.List<Vector3>();
        var path=new NavMeshPath();
        for(int i=0;i<tri.indices.Length;i+=9)
        {
            Vector3 p=(tri.vertices[tri.indices[i]]+tri.vertices[tri.indices[i+1]]+tri.vertices[tri.indices[i+2]])/3;
            // Inland points: avoid low shoreline, narrow edges and unreachable NavMesh islands.
            if(p.y<Mathf.Max(8,origin.position.y-12) || !NavMesh.SamplePosition(p,out var hit,1,filter))continue;
            if(!NavMesh.FindClosestEdge(hit.position,out var edge,filter) || edge.distance<1.25f)continue;
            if(!NavMesh.CalculatePath(origin.position,hit.position,filter,path) || path.status!=NavMeshPathStatus.PathComplete)continue;
            candidates.Add(hit.position);
        }
        var points=new System.Collections.Generic.List<Vector3>{origin.position};
        AddInteriorPoints(points,candidates);
        string[] names={"Red","Orange","Gold","Green","Teal","Cyan","Blue","Purple","Pink","Ivory"};
        string[] hex={"D84A42","EF822F","E6BE3E","58AA52","27A58D","49BFD9","426FDB","9261CB","DD70A4","DDD6BC"};
        const string matFolder="Assets/Materials/Photographers";
        if(!AssetDatabase.IsValidFolder("Assets/Materials"))AssetDatabase.CreateFolder("Assets","Materials");
        if(!AssetDatabase.IsValidFolder(matFolder))AssetDatabase.CreateFolder("Assets/Materials","Photographers");
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Populate ten photographers");
        for(int i=1;i<10;i++)
        {
            var clone=UnityEngine.Object.Instantiate(original.gameObject,original.transform.parent);
            Undo.RegisterCreatedObjectUndo(clone,"Add photographer");hunters.Add(clone.GetComponent<HunterMove>());
        }
        var report=new StringBuilder("Ten photographers placed on connected NavMesh; minimum separation 25m.\n");
        for(int i=0;i<10;i++)
        {
            var h=hunters[i];Undo.RecordObject(h.gameObject,"Name photographer");
            h.name=$"Photographer_{i+1:00}_{names[i]}";
            Undo.RecordObject(h.transform,"Place photographer");
            h.transform.position=points[i]+Vector3.up*offset;
            if(i>0)h.transform.rotation=Quaternion.Euler(0,i*137.5f,0);
            ColorUtility.TryParseHtmlString("#"+hex[i],out Color tint);
            foreach(var r in h.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Undo.RecordObject(r,"Color photographer");var materials=r.sharedMaterials;
                for(int slot=0;slot<materials.Length;slot++)
                {
                    var m=new Material(materials[slot]);m.name=$"Photographer_{i+1:00}_{names[i]}_{r.name}_{slot}";
                    Color color=r.name.Contains("Joints")?Color.Lerp(tint,Color.black,.6f):tint;
                    if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);
                    if(m.HasProperty("_Color"))m.SetColor("_Color",color);
                    AssetDatabase.CreateAsset(m,matFolder+"/"+m.name+".mat");materials[slot]=m;
                }
                r.sharedMaterials=materials;
            }
            report.AppendLine($"{h.name} position={h.transform.position:F2} color=#{hex[i]}");
        }
        Undo.CollapseUndoOperations(group);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Selection.objects=hunters.Select(h=>(UnityEngine.Object)h.gameObject).ToArray();
        return report.ToString();
    }
    static void AddInteriorPoints(System.Collections.Generic.List<Vector3> points,System.Collections.Generic.List<Vector3> candidates)
    {
        // Deliberate coverage of the island interior, rather than farthest-point sampling
        // which pushes most photographers onto the perimeter of a convex map.
        Vector2[] targets={new Vector2(-50,25),new Vector2(45,-30),new Vector2(-35,-65),
            new Vector2(60,60),new Vector2(-90,70),new Vector2(90,-80),new Vector2(0,130),
            new Vector2(-140,-70),new Vector2(140,15)};
        foreach(var target in targets)
        {
            var options=candidates.Where(p=>points.All(q=>new Vector2(p.x-q.x,p.z-q.z).sqrMagnitude>=30*30))
                .OrderBy(p=>(new Vector2(p.x,p.z)-target).sqrMagnitude).ToArray();
            if(options.Length==0 || (new Vector2(options[0].x,options[0].z)-target).sqrMagnitude>35*35)
                throw new InvalidOperationException("No reachable interior spawn near "+target);
            points.Add(options[0]);
        }
    }
    [MenuItem("Tools/Hunter/Rebalance Photographers Into Interior")]
    public static string Rebalance()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Rebalance outside Play Mode.");
        var scene=SceneManager.GetActiveScene();
        var hunters=UnityEngine.Object.FindObjectsOfType<HunterMove>().Where(h=>h.gameObject.scene==scene).OrderBy(h=>h.name).ToArray();
        if(hunters.Length!=10)throw new InvalidOperationException("Expected ten existing photographers.");
        var agent=hunters[0].GetComponent<NavMeshAgent>();
        var filter=new NavMeshQueryFilter{agentTypeID=agent.agentTypeID,areaMask=agent.areaMask};
        float offset=agent.baseOffset*Mathf.Abs(hunters[0].transform.lossyScale.y);
        if(!NavMesh.SamplePosition(hunters[0].transform.position-Vector3.up*offset,out var origin,4,filter))throw new Exception("Original spawn not on NavMesh");
        var tri=NavMesh.CalculateTriangulation();var candidates=new System.Collections.Generic.List<Vector3>();var path=new NavMeshPath();
        for(int i=0;i<tri.indices.Length;i+=3)
        {
            Vector3 p=(tri.vertices[tri.indices[i]]+tri.vertices[tri.indices[i+1]]+tri.vertices[tri.indices[i+2]])/3;
            if(p.y<10 || new Vector2(p.x,p.z).sqrMagnitude>180*180 || !NavMesh.SamplePosition(p,out var hit,1,filter))continue;
            if(!NavMesh.FindClosestEdge(hit.position,out var edge,filter) || edge.distance<1.25f)continue;
            if(!NavMesh.CalculatePath(origin.position,hit.position,filter,path) || path.status!=NavMeshPathStatus.PathComplete)continue;
            candidates.Add(hit.position);
        }
        var points=new System.Collections.Generic.List<Vector3>{origin.position};AddInteriorPoints(points,candidates);
        EditorSceneManager.SaveScene(scene,Folder+"/SampleScene.before-rebalance.unity",true);
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Rebalance photographer positions");
        var report=new StringBuilder("Interior layout; all spawns connected, edge clearance >=1.25m, separation >=30m.\n");
        for(int i=0;i<hunters.Length;i++)
        {
            Undo.RecordObject(hunters[i].transform,"Move photographer into interior");
            var a=hunters[i].GetComponent<NavMeshAgent>();
            hunters[i].transform.position=points[i]+Vector3.up*a.baseOffset*Mathf.Abs(hunters[i].transform.lossyScale.y);
            report.AppendLine(hunters[i].name+" "+hunters[i].transform.position.ToString("F2"));
        }
        Undo.CollapseUndoOperations(group);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return report.ToString();
    }
}
