using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.TerrainTools;
using UnityEngine;

namespace Gaia
{
    #if GAIA_2023_PRO
    [CustomEditor(typeof(FloatingPointFix))]
    public class FloatingPointFixEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            FloatingPointFix fpf = (FloatingPointFix)target;
            GUI.enabled = false;
            EditorGUILayout.Vector3Field("Current Offset", fpf.currentOffset);
        }
    }
#endif
}
