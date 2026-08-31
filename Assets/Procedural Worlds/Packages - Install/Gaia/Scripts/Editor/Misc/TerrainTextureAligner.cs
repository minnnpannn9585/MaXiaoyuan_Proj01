using System.IO;
using Gaia.Internal;
using PWCommon7;
using System;
using System.Collections.Generic;
using Gaia;
using UnityEditor;
using UnityEngine;

namespace Gaia
{
    public class TerrainTextureAligner : EditorWindow, IPWEditor
    {
        private Terrain terrainA;
        private Terrain terrainB;
        private float blendStrength = 1f;
        private int blendWidth = 8;

        private const int LEFT_EDGE = 0;
        private const int RIGHT_EDGE = 1;
        private const int TOP_EDGE = 2;
        private const int BOTTOM_EDGE = 3;

        private EditorUtils m_editorUtils;
        private GaiaSettings m_settings;

        [HideInInspector]
        public bool
            autoManagerProcessing = false; // Flag to indicate automatic workflow of the tool from the Manager Window

        [HideInInspector]
        public bool enabledInManager = false;

        public bool PositionChecked
        {
            get => true;
            set => PositionChecked = value;
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            if (m_editorUtils == null)
            {
                // Get editor utils for this
                m_editorUtils = PWApp.GetEditorUtils(this);
            }

            titleContent = m_editorUtils.GetContent("WindowTitle");
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        public void OnGUI()
        {
            m_editorUtils.Initialize();

            m_editorUtils.Panel("TerrainTextureAligner", DrawTerrainTextureAligner, true);
        }

        private void DrawTerrainTextureAligner(bool helpEnabled)
        {
            if (m_settings == null)
            {
                m_settings = GaiaUtils.GetGaiaSettings();
            }

            if (autoManagerProcessing)
            {
                enabledInManager = EditorGUILayout.ToggleLeft("Enable Terrain Texture Aligner", enabledInManager);
                if (!enabledInManager)
                {
                    return;
                }
            }
            else
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(m_editorUtils.GetTextValue("AssignTwoTerrainsHeader"), EditorStyles.boldLabel);
                terrainA = (Terrain)m_editorUtils.ObjectField("TerrainA", terrainA, typeof(Terrain), true, helpEnabled);
                terrainB = (Terrain)m_editorUtils.ObjectField("TerrainB", terrainB, typeof(Terrain), true, helpEnabled);
            }

            blendStrength = m_editorUtils.Slider("BlendStrength", blendStrength, 0f, 1f, helpEnabled);
            blendWidth = m_editorUtils.IntSlider("BlendWidthPixels", blendWidth, 1, 20, helpEnabled);

            if (terrainA && terrainB)
            {
                SceneView.RepaintAll();
                GUI.backgroundColor = m_settings.GetActionButtonColor();

                if (!autoManagerProcessing && m_editorUtils.Button("AlignTextures"))
                {
                    StartAligning();
                }

                GUI.backgroundColor = Color.white;
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!terrainA || !terrainB)
                return;

            if (!autoManagerProcessing)
            {
                Vector3 centerA = GetTerrainCenter(terrainA);
                Vector3 centerB = GetTerrainCenter(terrainB);
                Vector3 direction = centerB - centerA;

                // Avoid zero vector in LookRotation
                direction = direction.normalized != Vector3.zero ? direction.normalized : Vector3.forward;

                Handles.color = Color.red;
                Handles.ArrowHandleCap(0, centerA, Quaternion.LookRotation(direction), 25, EventType.Repaint);
                Handles.ArrowHandleCap(0, centerB, Quaternion.LookRotation(-direction), 25, EventType.Repaint);

                Handles.color = Color.yellow;
                Handles.DrawLine(centerA, centerB);

                DetectAdjacentEdges(out int edgeA, out int edgeB);
                DrawEdgeDebugSphere(terrainA, edgeA);
                DrawEdgeDebugSphere(terrainB, edgeB);
            }
        }

        private void DrawEdgeDebugSphere(Terrain terrain, int edge)
        {
            Vector3 center = GetEdgeCenter(terrain, edge);
            Handles.color = Color.blue;
            Handles.SphereHandleCap(0, center, Quaternion.identity, 8, EventType.Repaint);
        }

        private Vector3 GetEdgeCenter(Terrain terrain, int edge)
        {
            Vector3 position = terrain.GetPosition();
            Vector3 size = terrain.terrainData.size;

            return edge switch
            {
                BOTTOM_EDGE => position + new Vector3(0, 0, size.z / 2),
                TOP_EDGE => position + new Vector3(size.x, 0, size.z / 2),
                RIGHT_EDGE => position + new Vector3(size.x / 2, 0, size.z),
                LEFT_EDGE => position + new Vector3(size.x / 2, 0, 0),
                _ => position
            };
        }

        private Vector3 GetTerrainCenter(Terrain terrain)
        {
            return terrain.GetPosition() +
                   new Vector3(terrain.terrainData.size.x / 2, 0, terrain.terrainData.size.z / 2);
        }

        public void StartAligningProcess(Terrain terrainAInput, Terrain terrainBInput)
        {
            terrainA = terrainAInput;
            terrainB = terrainBInput;

            if (!terrainA || !terrainB)
            {
                Debug.LogWarning("Terrains are not assigned.");
            }
            else
            {
                StartAligning();
            }
        }

        public void StartAligning()
        {
            if (!terrainA || !terrainB)
                return;

            Undo.RecordObject(terrainA.terrainData, "Align Textures");
            Undo.RecordObject(terrainB.terrainData, "Align Textures");

            // Proceed only if layers match
            DetectAdjacentEdges(out int edgeA, out int edgeB);
            if (edgeA == -1 || edgeB == -1)
            {
                Debug.LogWarning("Terrains are not adjacent.");
                return;
            }

            ApplyProceduralTextureBlending(edgeA, edgeB);
        }

        private void ApplyProceduralTextureBlending(int edgeA, int edgeB)
        {
            // Validate terrains and terrain data before proceeding
            if (terrainA == null || terrainB == null)
            {
                Debug.LogWarning("TerrainTextureAligner: One or both terrains are null.");
                return;
            }
            
            TerrainData dataA = terrainA.terrainData;
            TerrainData dataB = terrainB.terrainData;
            
            if (dataA == null || dataB == null)
            {
                Debug.LogWarning("TerrainTextureAligner: One or both terrain data objects are null.");
                return;
            }
            
            int resolution = Mathf.Min(dataA.alphamapResolution, dataB.alphamapResolution);
            int numLayers = Mathf.Min(dataA.alphamapLayers, dataB.alphamapLayers);

            // Ensure edgeA/edgeB are valid
            if (edgeA < 0 || edgeB < 0)
                return;

            float[,,] splatA = dataA.GetAlphamaps(0, 0, resolution, resolution);
            float[,,] splatB = dataB.GetAlphamaps(0, 0, resolution, resolution);

            bool horizontal = (edgeA == LEFT_EDGE || edgeA == RIGHT_EDGE);

            // Calculate start positions safely
            int startA = edgeA switch
            {
                RIGHT_EDGE => resolution - 1,
                TOP_EDGE => resolution - 1,
                _ => 0
            };

            int startB = edgeB switch
            {
                LEFT_EDGE => 0,
                BOTTOM_EDGE => 0,
                _ => resolution - 1
            };

            for (int i = 0; i < blendWidth; i++)
            {
                float distanceA = i; // Distance from edge A
                float distanceB = i; // Distance from edge B

                // Blend factors start strong at the edge and fade inward
                float blendFactorA = Mathf.SmoothStep(blendStrength, 0, distanceA / blendWidth);
                float blendFactorB = Mathf.SmoothStep(blendStrength, 0, distanceB / blendWidth);

                for (int j = 0; j < resolution; j++)
                {
                    // Generate synchronized noise for both terrains
                    float noise;
                    if (horizontal)
                        noise = Mathf.PerlinNoise(j * 0.1f, i * 0.1f);
                    else
                        noise = Mathf.PerlinNoise(i * 0.1f, j * 0.1f);
                    noise = noise * 0.5f + 0.5f;

                    // Calculate coordinates with clamping
                    int xA = horizontal ? (edgeA == LEFT_EDGE ? startA + i : startA - i) : j;
                    int yA = horizontal ? j : (edgeA == BOTTOM_EDGE ? startA + i : startA - i);
                    xA = Mathf.Clamp(xA, 0, resolution - 1);
                    yA = Mathf.Clamp(yA, 0, resolution - 1);

                    int xB = horizontal ? (edgeB == LEFT_EDGE ? startB + i : startB - i) : j;
                    int yB = horizontal ? j : (edgeB == BOTTOM_EDGE ? startB + i : startB - i);
                    xB = Mathf.Clamp(xB, 0, resolution - 1);
                    yB = Mathf.Clamp(yB, 0, resolution - 1);

                    // Clamp indices to prevent out-of-bounds
                    xA = Mathf.Clamp(xA, 0, resolution - 1);
                    yA = Mathf.Clamp(yA, 0, resolution - 1);
                    xB = Mathf.Clamp(xB, 0, resolution - 1);
                    yB = Mathf.Clamp(yB, 0, resolution - 1);

                    // Bidirectional blending with noise-modulated weights
                    for (int layer = 0; layer < numLayers; layer++)
                    {
                        float a = splatA[xA, yA, layer];
                        float b = splatB[xB, yB, layer];

                        // Noise determines how much each terrain contributes
                        float weightA = noise * blendFactorA;
                        float weightB = (1 - noise) * blendFactorB;

                        // Blend terrain A towards terrain B and vice versa
                        splatA[xA, yA, layer] = a * (1 - weightA) + b * weightA;
                        splatB[xB, yB, layer] = b * (1 - weightB) + a * weightB;
                    }

                    // Normalize texture weights
                    float sumA = 0, sumB = 0;
                    for (int layer = 0; layer < numLayers; layer++)
                    {
                        sumA += splatA[xA, yA, layer];
                        sumB += splatB[xB, yB, layer];
                    }

                    if (sumA > 0 && sumB > 0)
                    {
                        for (int layer = 0; layer < numLayers; layer++)
                        {
                            splatA[xA, yA, layer] /= sumA;
                            splatB[xB, yB, layer] /= sumB;
                        }
                    }
                }
            }

            // Validate terrains and terrain data are still valid before applying changes
            if (terrainA != null && terrainB != null && dataA != null && dataB != null)
            {
                try
                {
                    dataA.SetAlphamaps(0, 0, splatA);
                    dataB.SetAlphamaps(0, 0, splatB);
                    terrainA.Flush();
                    terrainB.Flush();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"TerrainTextureAligner: Error setting alphamaps - {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning("TerrainTextureAligner: Terrains or terrain data became null during processing.");
            }

            terrainA = null;
            terrainB = null;
        }

        private void DetectAdjacentEdges(out int edgeA, out int edgeB)
        {
            edgeA = -1;
            edgeB = -1;
            if (!terrainA || !terrainB)
                return;

            Vector3 posA = terrainA.GetPosition();
            Vector3 posB = terrainB.GetPosition();
            Vector3 sizeA = terrainA.terrainData.size;
            Vector3 sizeB = terrainB.terrainData.size;

            const float tolerance = 1.5f; // Tolerance for adjacency detection (accounts for floating point precision and slight movements)

            // Calculate actual edge positions in world space
            // Terrain pivot is at -x -z corner, so:
            // min = position, max = position + size
            float aMinX = posA.x;
            float aMaxX = posA.x + sizeA.x;
            float aMinZ = posA.z;
            float aMaxZ = posA.z + sizeA.z;
            
            float bMinX = posB.x;
            float bMaxX = posB.x + sizeB.x;
            float bMinZ = posB.z;
            float bMaxZ = posB.z + sizeB.z;

            // Check horizontal adjacency (left/right on Z axis)
            // For horizontal adjacency, X positions should overlap or be very close
            bool xOverlaps = (bMaxX > aMinX && bMinX < aMaxX) || 
                            (Mathf.Abs(bMaxX - aMinX) < tolerance) || 
                            (Mathf.Abs(bMinX - aMaxX) < tolerance) ||
                            (Mathf.Abs(aMinX - bMinX) < tolerance) ||
                            (Mathf.Abs(aMaxX - bMaxX) < tolerance);
            
            if (xOverlaps)
            {
                // Check if terrain B is to the right (positive Z) of terrain A
                if (Mathf.Abs(aMaxZ - bMinZ) < tolerance)
                {
                    edgeA = RIGHT_EDGE;
                    edgeB = LEFT_EDGE;
                    return;
                }
                // Check if terrain B is to the left (negative Z) of terrain A
                if (Mathf.Abs(aMinZ - bMaxZ) < tolerance)
                {
                    edgeA = LEFT_EDGE;
                    edgeB = RIGHT_EDGE;
                    return;
                }
            }
            
            // Check vertical adjacency (top/bottom on X axis)
            // For vertical adjacency, Z positions should overlap or be very close
            bool zOverlaps = (bMaxZ > aMinZ && bMinZ < aMaxZ) || 
                            (Mathf.Abs(bMaxZ - aMinZ) < tolerance) || 
                            (Mathf.Abs(bMinZ - aMaxZ) < tolerance) ||
                            (Mathf.Abs(aMinZ - bMinZ) < tolerance) ||
                            (Mathf.Abs(aMaxZ - bMaxZ) < tolerance);
            
            if (zOverlaps)
            {
                // Check if terrain B is to the top (positive X) of terrain A
                if (Mathf.Abs(aMaxX - bMinX) < tolerance)
                {
                    edgeA = TOP_EDGE;
                    edgeB = BOTTOM_EDGE;
                    return;
                }
                // Check if terrain B is to the bottom (negative X) of terrain A
                if (Mathf.Abs(aMinX - bMaxX) < tolerance)
                {
                    edgeA = BOTTOM_EDGE;
                    edgeB = TOP_EDGE;
                    return;
                }
            }
        }
    }
}