using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;

[InitializeOnLoad]
public static class HunterWaterChecks
{
    const string Folder="Temp/HunterWaterChecks";
    static bool baking;
    static string report;
    static HunterWaterChecks(){EditorApplication.update+=Tick;}
    static void Tick()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Folder+"/request"))return;
        try
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play Mode before fixing baked navigation.");
            var surface=GameObject.Find("Hunter Navigation").GetComponent<NavMeshSurface>();
            var water=GameObject.Find("Unity URP Water");
            if(!baking)
            {
                var before=NavMesh.CalculateTriangulation();
                report=$"Water {water.name} world height={water.transform.position.y:F3}; old vertices below water={before.vertices.Count(p=>p.y<water.transform.position.y)}/{before.vertices.Length}\n";
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Folder+"/before.unity",true);
                File.Copy(AssetDatabase.GetAssetPath(surface.navMeshData),Folder+"/before-navmesh.asset",true);
                HunterNavigationInstaller.Build();baking=true;return;
            }
            if(NavMeshAssetManager.instance.IsSurfaceBaking(surface))return;
            report+=HunterNavigationInstaller.Finish()+"\n";
            var tri=NavMesh.CalculateTriangulation();
            int wet=tri.vertices.Count(p=>p.y<water.transform.position.y);
            report+=$"New vertices below water={wet}/{tri.vertices.Length}; minimum y={tri.vertices.Min(p=>p.y):F3}\n";
            if(wet!=0)throw new Exception("Submerged NavMesh vertices remain.");
            foreach(var h in UnityEngine.Object.FindObjectsOfType<HunterNavigation>())
            {
                var agent=h.GetComponent<NavMeshAgent>();var filter=new NavMeshQueryFilter{agentTypeID=agent.agentTypeID,areaMask=agent.areaMask};
                var foot=h.transform.position-Vector3.up*agent.baseOffset*Mathf.Abs(h.transform.lossyScale.y);
                if(!NavMesh.SamplePosition(foot,out var hit,2,filter) || !h.IsDryPoint(hit.position))throw new Exception(h.name+" invalid dry spawn");
                report+=h.name+" dry spawn PASS\n";
            }
            File.WriteAllText(Folder+"/result.txt","PASS\n"+report);File.Delete(Folder+"/request");baking=false;
        }
        catch(Exception e){File.WriteAllText(Folder+"/result.txt",report+e);File.Delete(Folder+"/request");baking=false;Debug.LogException(e);}
    }
}

