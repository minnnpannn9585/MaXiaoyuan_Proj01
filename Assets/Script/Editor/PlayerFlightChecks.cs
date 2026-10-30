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
            set("body", body);set("bodyCollider", go.GetComponent<Collider>());set("currentStamina", 100f);set("isFlying", true);
            set("targetFlightSpeed", 10f);
            body.useGravity = false;body.velocity = Vector3.forward * 10f;
            for (int i = 0; i < 100; i++) tick(.02f);
            Require(Mathf.Abs(player.CurrentStamina - 94f) < .01f, "glide should cost 3 stamina/sec");
            Require(body.velocity.z > 9f, "no-input flight stopped");
            set("descendInput", true);
            float beforeDescent = player.CurrentStamina;
            for (int i = 0; i < 100; i++) tick(.02f);
            Require(player.CurrentStamina == beforeDescent && body.velocity.y < -3f, "descent stamina/velocity");
            set("descendInput", false);set("climbInput", true);
            for (int i = 0; i < 100; i++) tick(.02f);
            Require(player.CurrentStamina < 100f && body.velocity.y > 0f, "climb did not cost stamina");
            beforeDescent = player.CurrentStamina;
            set("descendInput", true);tick(.02f);
            Require(player.CurrentStamina == beforeDescent, "Shift must be free even when braking a climb");
            set("descendInput", false);set("currentStamina", .01f);tick(.02f);
            Require(!player.IsFlying && player.IsCrashFalling && !player.IsStunned, "exhaustion must arm impact, not stun mid-air");
            typeof(PlayerMove).GetMethod("BeginCrashRecovery", flags).Invoke(player, null);
            Require(player.IsStunned && !player.IsCrashFalling, "impact must start recovery");
            Require((float)typeof(PlayerMove).GetField("crashRecoveryRemaining", flags).GetValue(player) == 3f, "impact must lock movement for three seconds");
            typeof(PlayerMove).GetMethod("EnterFlightMode", flags).Invoke(player, null);
            Require(!player.IsFlying, "cannot take off during crash recovery");
            var fixedTick = typeof(PlayerMove).GetMethod("FixedUpdate", flags);
            int lockedSteps = Mathf.FloorToInt(2.9f / Time.fixedDeltaTime);
            for (int i = 0; i < lockedSteps; i++) fixedTick.Invoke(player, null);
            Require(player.IsStunned, "recovered before three seconds");
            for (int i = 0; i < Mathf.CeilToInt(.2f / Time.fixedDeltaTime); i++) fixedTick.Invoke(player, null);
            Require(!player.IsStunned, "recovery did not expire after three seconds");
            set("currentStamina", 100f);
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
            typeof(PlayerMove).GetMethod("BeginCrashFall", flags).Invoke(player, null);
            Require(player.IsCrashFalling && !player.IsStunned, "manual fall must use the same impact path");
            set("crashFallPending", false);set("isGrounded", true);set("flightLocked", false);
            var press = typeof(PlayerMove).GetMethod("HandleSpacePress", flags);
            float staminaBeforeJump = player.CurrentStamina;
            set("moveInput", Vector2.up);body.velocity = Vector3.forward * 4f;
            press.Invoke(player, new object[] { 10f });
            Require(player.IsJumping && !player.IsFlying && Mathf.Abs(body.velocity.y - Mathf.Sqrt(2f * -Physics.gravity.y * .2f)) < .001f, "jump should use configured height");
            Require(new Vector2(body.velocity.x, body.velocity.z).magnitude <= .801f, "hop must not inherit walking speed");
            set("jumpMoveSpeed", 0f);fixedTick.Invoke(player, null);
            Require(new Vector2(body.velocity.x, body.velocity.z).magnitude < .001f, "zero hop speed should disable horizontal travel");
            Require(player.CurrentStamina == staminaBeforeJump && !player.IsCrashFalling, "jump must not cost flight stamina or arm crash");
            body.velocity = Vector3.up;
            press.Invoke(player, new object[] { 10.5f });
            Require(body.velocity.y == 1f, "airborne single press must not double jump");
            press.Invoke(player, new object[] { 10.6f });
            Require(player.IsFlying && !player.IsJumping && body.velocity.z > 0f, "second tap should convert jump to forward flight");
            set("isFlying", false);set("isGrounded", true);set("crashRecoveryRemaining", 5f);
            press.Invoke(player, new object[] { 11f });
            Require(!player.IsJumping && !player.IsFlying, "stun must block jump and flight input");
            set("crashRecoveryRemaining", 0f);set("isGrounded", true);set("isJumping", false);
            body.velocity = Vector3.forward * 10f;
            typeof(PlayerMove).GetMethod("EnterFlightMode", flags).Invoke(player, null);
            Require(Mathf.Abs(player.TargetFlightSpeed - 2.5f) < .001f && Mathf.Abs(body.velocity.z - 2.5f) < .001f, "takeoff must start slowly, ignoring walk momentum");
            var adjust = typeof(PlayerMove).GetMethod("AdjustFlightSpeed", flags);
            adjust.Invoke(player, new object[] { 100f });Require(player.TargetFlightSpeed == 10f, "maximum throttle clamp");Require(player.IsAdjustingFlightSpeed, "acceleration must flap");
            adjust.Invoke(player, new object[] { -100f });Require(player.TargetFlightSpeed == 2f, "minimum throttle clamp");
            set("moveInput", Vector2.up);set("takeoffLiftRemaining", 0f);set("climbInput", false);body.rotation = Quaternion.identity;
            tick(.02f);Require(Mathf.Abs((float)typeof(PlayerMove).GetField("flightYawRate", flags).GetValue(player)) < .001f, "W must not steer");
            body.velocity = Vector3.forward * 2f;Require(!player.IsAdjustingFlightSpeed, "steady speed should glide");float lowRate=player.CurrentFlightTurnRate;
            body.velocity = Vector3.forward * 10f;Require(player.IsAdjustingFlightSpeed, "deceleration must flap");Require(lowRate > player.CurrentFlightTurnRate && player.CurrentFlightTurnRate == 75f, "speed-dependent turning");
            set("moveInput", Vector2.right);tick(.02f);Require(player.FlightBankInput > 0f, "right turn bank signal");
            var obstacle = new GameObject("Test tree");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obstacle, scene);
            var collider=obstacle.AddComponent<BoxCollider>();var collide=typeof(PlayerMove).GetMethod("HandleFlightCollision", flags);
            obstacle.tag="TreeBranch";collide.Invoke(player,new object[]{collider});Require(player.IsFlying, "branch must not trigger trunk crash");
            obstacle.tag="TreeTrunk";collide.Invoke(player,new object[]{collider});Require(!player.IsFlying && player.IsCrashFalling && !player.IsStunned && body.useGravity, "trunk crash must fall before recovery");
            typeof(PlayerMove).GetMethod("EnterFlightMode", flags).Invoke(player,null);Require(!player.IsFlying,"trunk crash cannot be cancelled in air");
            typeof(PlayerMove).GetMethod("BeginCrashRecovery",flags).Invoke(player,null);Require(player.IsStunned,"trunk landing recovery");
            Require(player.IsTreeCrash, "tree crash cause must persist through landing");
            for (int i=0; i<Mathf.FloorToInt(2.9f/Time.fixedDeltaTime); i++) fixedTick.Invoke(player,null);
            Require(player.IsStunned && player.IsTreeCrash, "fast impact animation must not shorten three-second stun");
            for (int i=0; i<Mathf.CeilToInt(.2f/Time.fixedDeltaTime); i++) fixedTick.Invoke(player,null);
            Require(!player.IsStunned && !player.IsTreeCrash, "tree recovery must clear impact cause");
            var animator=go.AddComponent<PlayerBirdAnimator>();var flap=typeof(PlayerBirdAnimator).GetMethod("UpdateGlideFlapping",flags);
            Require(!(bool)flap.Invoke(animator,new object[]{true,false,2.9f}),"glide interval");
            Require((bool)flap.Invoke(animator,new object[]{true,false,.2f}),"glide flap burst");
            Require(!(bool)flap.Invoke(animator,new object[]{true,false,.8f}),"return to glide");
            Require((bool)flap.Invoke(animator,new object[]{true,true,.1f}),"climb continuous flapping");
            Require(!(bool)flap.Invoke(animator,new object[]{false,false,.1f}),"fall resets flapping");
            return "Flight stamina, throttle clamps, slow takeoff, speed-dependent steering/bank, trunk-vs-branch crash, glide bursts, jump and recovery: PASS";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static void Require(bool pass, string message) { if (!pass) throw new Exception(message); }
}
