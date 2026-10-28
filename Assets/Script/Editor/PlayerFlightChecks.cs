using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayerFlightChecks
{
    [MenuItem("Tools/Player/Check Forward Flight")]
    public static string Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var go = new GameObject("Flight test player");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<SphereCollider>();
            var body = go.AddComponent<Rigidbody>();
            var player = go.AddComponent<PlayerMove>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Action<string, object> set = (name, value) => typeof(PlayerMove).GetField(name, flags).SetValue(player, value);
            Action<float> tick = dt => typeof(PlayerMove).GetMethod("UpdateFlightMovement", flags).Invoke(player, new object[] { dt });
            set("body", body);set("currentStamina", 100f);set("isFlying", true);
            body.useGravity = false;body.velocity = Vector3.forward * 10f;
            for (int i = 0; i < 100; i++) tick(.02f);
            Require(Mathf.Abs(player.CurrentStamina - 100f) < .001f, "gliding spent stamina");
            Require(body.velocity.z > 9f, "no-input flight stopped");
            set("descendInput", true);
            for (int i = 0; i < 100; i++) tick(.02f);
            Require(player.CurrentStamina == 100f && body.velocity.y < -3f, "descent stamina/velocity");
            set("descendInput", false);set("climbInput", true);
            for (int i = 0; i < 100; i++) tick(.02f);
            Require(player.CurrentStamina < 100f && body.velocity.y > 0f, "climb did not cost stamina");
            float yawRate = 0f;Quaternion rotation = Quaternion.identity;
            for (int i = 0; i < 100; i++)
            {
                Quaternion before = rotation;float previousRate = yawRate;
                rotation = PlayerMove.StepFlightHeading(rotation, Vector3.back, ref yawRate, 75, 150, .5f, .02f);
                Require(Quaternion.Angle(before, rotation) <= 1.51f, "sharp turn exceeded angular limit");
                Require(Mathf.Abs(yawRate - previousRate) <= 3.01f, "turn acceleration exceeded limit");
            }
            Require(Quaternion.Angle(Quaternion.identity, rotation) > 60f && Quaternion.Angle(Quaternion.identity, rotation) < 170f,
                "U-turn should take time");
            set("isGrounded", true);set("climbInput", false);body.velocity = Vector3.zero;
            typeof(PlayerMove).GetMethod("EnterFlightMode", flags).Invoke(player, null);
            Require(body.velocity.z > 0f && body.velocity.y > 0f, "takeoff lacks forward speed/lift");
            return "Forward glide, free descent, climb cost, bounded turn speed/acceleration and forward takeoff: PASS";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void Require(bool pass, string message) { if (!pass) throw new Exception(message); }
}
