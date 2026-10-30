#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>Opt-in Play Mode regression against actual physics and Terrain-generated tree colliders.</summary>
public sealed class FlightRuntimeChecks : MonoBehaviour
{
    private readonly StringBuilder report = new StringBuilder();
    private PlayerMove player;
    private Rigidbody body;
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private void Set(string name, object value) => typeof(PlayerMove).GetField(name, Flags).SetValue(player, value);
    private void Call(string name) => typeof(PlayerMove).GetMethod(name, Flags).Invoke(player, null);
    private void Record(string line)
    {
        report.AppendLine(line);
        System.IO.Directory.CreateDirectory("Temp/FlightRuntimeChecks");
        System.IO.File.WriteAllText("Temp/FlightRuntimeChecks/result.txt", report.ToString());
    }
    private IEnumerator Start()
    {
        player = GetComponent<PlayerMove>();body = GetComponent<Rigidbody>();
        foreach (var hunter in FindObjectsOfType<HunterMove>()) hunter.enabled = false;
        Record("Runtime physics regression started");
        body.position += Vector3.up * 150f;body.rotation = Quaternion.identity;body.velocity = Vector3.zero;
        Set("isGrounded", false);Set("isJumping", false);Set("currentStamina",100f);Call("EnterFlightMode");
        Set("targetFlightSpeed",6f);
        yield return new WaitForSeconds(3f);
        Record("Cruise speed="+new Vector2(body.velocity.x,body.velocity.z).magnitude+" adjusting="+player.IsAdjustingFlightSpeed+" drag="+body.drag);
        if (player.IsAdjustingFlightSpeed || Mathf.Abs(new Vector2(body.velocity.x,body.velocity.z).magnitude-6f)>.1f)
        {Record("FAIL stable cruise");yield break;}
        var visual=GetComponent<PlayerBirdAnimator>();bool sawGlide=false,sawFly=false;
        float animationDeadline=Time.time+4.5f;
        while(Time.time<animationDeadline)
        {
            sawGlide |= visual.CurrentState==BirdAnimationState.Glide;
            sawFly |= visual.CurrentState==BirdAnimationState.Fly;
            yield return null;
        }
        Record("Stable flight animations: Glide="+sawGlide+" intermittent Fly="+sawFly);
        if(!sawGlide || !sawFly){Record("FAIL glide animation cycle");yield break;}
        var terrain=Terrain.activeTerrain;var data=terrain.terrainData;var prototypes=data.treePrototypes;
        bool found=false;Vector3 treePosition=Vector3.zero;
        foreach(var tree in data.treeInstances)
        {
            if(prototypes[tree.prototypeIndex].prefab.GetComponentInChildren<CapsuleCollider>()==null)continue;
            var pos=terrain.transform.position+Vector3.Scale(tree.position,data.size);
            // A low, clear trunk ray avoids branch collisions during this test.
            RaycastHit hit;
            if(Physics.Raycast(pos+Vector3.up*2f+Vector3.right*3f,Vector3.left,out hit,3f) &&
                TerrainTreeTrunks.IsTrunkContact(hit.collider,hit.point))
            {treePosition=pos;found=true;Record("Terrain trunk ray="+hit.collider.GetType().Name+" tag="+hit.collider.tag+" point="+hit.point);break;}
        }
        if(!found){Record("FAIL no test trunk");yield break;}
        body.position=treePosition+Vector3.up*2f+Vector3.right*2f;
        body.rotation=Quaternion.LookRotation(Vector3.left);body.velocity=Vector3.left*3f;
        Set("targetFlightSpeed",3f);Set("currentStamina",100f);Set("hasLeftTakeoffSurface",true);
        Physics.SyncTransforms();
        float deadline=Time.time+3f;
        while(!player.IsTreeCrash && Time.time<deadline)yield return new WaitForFixedUpdate();
        Record("After collision treeCrash="+player.IsTreeCrash+" pending="+player.IsCrashFalling+" stunned="+player.IsStunned);
        if(!player.IsTreeCrash){Record("FAIL tree crash missing");yield break;}
        Quaternion rotation=body.rotation;
        Set("moveInput",Vector2.right);
        // Exercise FixedUpdate with stale/forced input while the trunk-crash lock is active.
        Call("FixedUpdate");
        Record("Fall steering delta="+Quaternion.Angle(rotation,body.rotation));
        if(Quaternion.Angle(rotation,body.rotation)>.01f){Record("FAIL fall steering");yield break;}
        deadline=Time.time+6f;
        while(!player.IsStunned && Time.time<deadline)yield return new WaitForFixedUpdate();
        if(!player.IsStunned){Record("FAIL landing recovery missing");yield break;}
        Record("Landed: recovery="+typeof(PlayerMove).GetField("crashRecoveryRemaining",Flags).GetValue(player));
        yield return new WaitForSeconds(.2f);
        Record("Landing animation="+visual.CurrentState);
        if(visual.CurrentState!=BirdAnimationState.TreeCrashImpact){Record("FAIL landing animation");yield break;}
        yield return new WaitForSeconds(2.3f);
        if(!player.IsStunned){Record("FAIL early recovery");yield break;}
        yield return new WaitForSeconds(.7f);
        Record(player.IsStunned ? "FAIL recovery not released" : "PASS real physics cruise, Terrain trunk collision, fall lock, landing and 3s recovery");
    }
    private void OnCollisionEnter(Collision c)
    {
        if(player!=null)Record("Contact "+c.collider.GetType().Name+"/"+c.collider.tag+" tree="+player.IsTreeCrash+" pending="+player.IsCrashFalling+" recovery="+player.IsStunned);
    }
}
#endif
