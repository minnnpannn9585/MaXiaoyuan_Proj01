using System;
using UnityEditor;
using UnityEngine;

public static class SeagullAnimationChecks
{
    public static string Preview()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        RenderTexture rt = null;
        try
        {
            var original = UnityEngine.Object.FindObjectOfType<PlayerMove>().transform.Find("SeagullVisual");
            var bird = UnityEngine.Object.Instantiate(original.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(bird, scene);
            bird.transform.position = Vector3.zero;
            bird.transform.rotation = Quaternion.identity;
            bird.transform.localScale = original.lossyScale;
            var animator = bird.GetComponent<Animator>();
            var cameraObject = new GameObject("Seagull preview camera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.enabled = false;
            camera.orthographic = true;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 10f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.16f,.20f,.24f);
            var lightObject = new GameObject("Preview light");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2f;
            light.transform.rotation = Quaternion.Euler(45,-35,0);
            rt = new RenderTexture(800,600,24);camera.targetTexture=rt;
            System.IO.Directory.CreateDirectory("Temp/SeagullChecks");
            var report = new System.Text.StringBuilder();
            foreach (string state in new[]{"Standing","Walk","TakeOff","IdleLookAround","Fly","Glide","Falling"})
            {
                animator.Rebind();animator.Play(state,0,state=="Standing"?0f:.4f);animator.Update(0f);
                Bounds b = SeagullPlayerInstaller.BakedBounds(bird);
                report.AppendLine(state + " bounds=" + b);
                camera.orthographicSize = Mathf.Max(.22f,b.size.magnitude*.65f);
                camera.transform.position=b.center+new Vector3(.8f,.35f,1.2f);
                camera.transform.LookAt(b.center);
                // Freeze the sampled skin for this immediate editor render; GPU skinning
                // otherwise keeps the previous frame until the next editor update.
                var frozen = new System.Collections.Generic.List<GameObject>();
                foreach (var skin in bird.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh = new Mesh();
                    skin.BakeMesh(mesh, true);
                    var copy = new GameObject("Sampled skin");
                    copy.transform.SetParent(skin.transform, false);
                    copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                    copy.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                    skin.enabled = false;
                    frozen.Add(copy);
                }
                camera.Render();
                foreach (var copy in frozen)
                {
                    UnityEngine.Object.DestroyImmediate(copy.GetComponent<MeshFilter>().sharedMesh);
                    UnityEngine.Object.DestroyImmediate(copy);
                }
                foreach (var skin in bird.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.enabled = true;
                var previous = RenderTexture.active;RenderTexture.active=rt;
                var image = new Texture2D(800,600,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();
                System.IO.File.WriteAllBytes("Temp/SeagullChecks/"+state+".png",image.EncodeToPNG());
                RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);
            }
            System.IO.File.WriteAllText("Temp/SeagullChecks/report.txt",report.ToString());
            return report.ToString();
        }
        finally
        {
            if(rt!=null) UnityEngine.Object.DestroyImmediate(rt);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    [MenuItem("Tools/Player/Check Seagull Animation Rules")]
    public static void Run()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            var original = UnityEngine.Object.FindObjectOfType<PlayerMove>().transform.Find("SeagullVisual");
            var bird = UnityEngine.Object.Instantiate(original.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(bird, scene);
            var a = bird.GetComponent<Animator>();
            a.Rebind();
            Action<float> advance = seconds => { for (int i = 0; i < Mathf.CeilToInt(seconds / .02f); i++) a.Update(.02f); };
            Action<string> check = state => { if (!a.GetCurrentAnimatorStateInfo(0).IsName("Base Layer." + state))
                throw new Exception("Animator expected " + state + ", got hash " + a.GetCurrentAnimatorStateInfo(0).fullPathHash); };
            a.SetBool("IsGrounded", true);advance(.02f);check("Standing");
            a.SetBool("IsMoving", true);advance(.4f);check("Walk");
            a.SetBool("IsMoving", false);advance(.4f);check("Standing");
            a.SetBool("IdleReady", true);advance(.4f);check("IdleLookAround");
            a.SetBool("IdleReady", false);a.SetBool("IsMoving", true);advance(.4f);check("Walk");
            a.SetBool("IsMoving", false);advance(.4f);
            a.SetBool("IdleReady", true);advance(.4f);a.SetBool("IdleReady", false);advance(4f);check("Standing");
            a.SetBool("IsFlying", true);a.SetBool("IsAscending", true);a.SetTrigger("TakeOff");
            advance(.3f);check("TakeOff");
            a.SetBool("IsGrounded", false);advance(1.3f);check("Fly");
            a.SetBool("IsAscending", false);advance(.4f);check("Glide");
            a.SetBool("IsAscending", true);advance(.4f);check("Fly");
            a.SetBool("IsFlying", false);advance(.4f);check("Falling");
            a.SetBool("IsGrounded", true);advance(.4f);check("Standing");
            Debug.Log("Seagull Animator graph: 12 transition checks passed.");
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }
}
