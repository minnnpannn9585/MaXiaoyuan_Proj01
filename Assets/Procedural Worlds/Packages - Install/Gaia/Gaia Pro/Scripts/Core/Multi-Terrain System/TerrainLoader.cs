using System.Collections;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Experimental.GraphView;
#endif
using UnityEngine;

namespace Gaia
{
    public enum LoadMode { Disabled, EditorSelected, EditorAlways, RuntimeAlways }
    [ExecuteInEditMode]
    public class TerrainLoader : MonoBehaviour
    {
        public BoundsDouble m_loadingBoundsRegular = new BoundsDouble();
        public BoundsDouble m_loadingBoundsImpostor = new BoundsDouble();
        public BoundsDouble m_loadingBoundsCollider = new BoundsDouble();
        [SerializeField]
        [HideInInspector]
        private LoadMode m_loadMode = LoadMode.EditorSelected;
        public LoadMode LoadMode {
                                    get {
                                            return m_loadMode;
                                        }
                                    set
                                    {
                                        m_loadMode = value;
                                        UpdateTerrains();
                                    }
                                    }
        [HideInInspector]
        public bool m_isSelected = false;
        [HideInInspector]
        public bool m_beingDragged = false;
        public float m_minRefreshDistance = 100f;
        public float m_maxRefreshDistance = 2000f;
        public float m_minRefreshMS = 100f;
        public float m_maxRefreshMS = 5000f;
        public bool m_followTransform = true;
        public Camera m_frustumCamera;

        private BoundsDouble m_shiftedBoundsRegular = new BoundsDouble();
        private BoundsDouble m_shiftedBoundsImpostor = new BoundsDouble();
        private BoundsDouble m_shiftedBoundsCollider = new BoundsDouble();

        private Vector3 m_lastUpdatePosition = Vector3.zero;
        private BoundsDouble m_nextUpdateBoundsRegular = null;
        private BoundsDouble m_nextUpdateBoundsRegularGizmo;
        private BoundsDouble m_nextUpdateBoundsImpostor = null;
        private BoundsDouble m_nextUpdateBoundsImpostorGizmo;
        private GaiaSessionManager m_sessionManager;
        public bool m_drawLoadRanges = true;
        public bool m_drawUpdateRanges = false;
        public Color m_updateBoundsRegularColor = Color.red;
        public Color m_updateBoundsImpostorColor = Color.white;
        public Color m_regularLoadRangeActiveColor = Color.magenta;
        public Color m_impostorLoadRangeActiveColor = Color.green;
        public Color m_regularLoadRangeInActiveColor = Color.black;
        public Color m_impostorLoadRangeInActiveColor = Color.gray;


        private GaiaSessionManager SessionManager
        {
            get
            {
                if (m_sessionManager == null)
                {
                    m_sessionManager = GaiaSessionManager.GetSessionManager(false);
                }
                return m_sessionManager;
            }
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR

            if (TerrainLoaderManager.Instance.ShowLocalTerrain && ((m_loadMode == LoadMode.EditorSelected && m_isSelected) || m_loadMode == LoadMode.EditorAlways || (m_loadMode == LoadMode.RuntimeAlways && (Application.isPlaying || m_isSelected))))
            {
                if (m_drawLoadRanges)
                { 
                    Color regularColor;
                    Color impostorColor;

                    if (m_loadMode == LoadMode.RuntimeAlways && !Application.isPlaying)
                    {
                        regularColor = m_regularLoadRangeInActiveColor;
                        impostorColor = m_impostorLoadRangeInActiveColor;
                    }
                    else
                    {
                        regularColor = m_regularLoadRangeActiveColor;
                        impostorColor = m_impostorLoadRangeActiveColor;
                    }
                    //m_loadingBounds.center = transform.position;
                    if (m_followTransform)
                    {
                        m_loadingBoundsRegular.center = transform.position;
                        m_loadingBoundsImpostor.center = transform.position;
                    }
                    Gizmos.color = regularColor;
                    Gizmos.DrawWireCube(m_loadingBoundsRegular.center, m_loadingBoundsRegular.size);
                    Gizmos.color = impostorColor;
                    Gizmos.DrawWireCube(m_loadingBoundsImpostor.center, m_loadingBoundsImpostor.size);
                }

                if (m_drawUpdateRanges)
                {
                    if (m_nextUpdateBoundsRegular != null)
                    {
                        Gizmos.color = m_updateBoundsRegularColor;
                        Gizmos.DrawWireCube(m_nextUpdateBoundsRegularGizmo.center, m_nextUpdateBoundsRegularGizmo.size);
                    }
                    if (m_nextUpdateBoundsImpostor != null)
                    {
                        Gizmos.color = m_updateBoundsImpostorColor;
                        Gizmos.DrawWireCube(m_nextUpdateBoundsImpostorGizmo.center, m_nextUpdateBoundsImpostorGizmo.size);
                    }
                }
            }
#endif
        }

        private void Update()
        {
            if (m_followTransform)
            {
                m_loadingBoundsRegular.center = transform.position;
                m_loadingBoundsImpostor.center = transform.position;
            }

            if (m_loadMode == LoadMode.RuntimeAlways && Application.isPlaying)
            {
                if (TerrainLoaderManager.Instance.m_assumeGridLayout)
                {
                    if (m_nextUpdateBoundsRegular == null || !m_nextUpdateBoundsRegular.Contains(m_loadingBoundsRegular.center) || m_nextUpdateBoundsImpostor ==null || !m_nextUpdateBoundsImpostor.Contains(m_loadingBoundsImpostor.center))
                    {
                        UpdateTerrains();
                        //evaluate when the next update would be since we are in a grid layout
                        CalculcateNextUpdateBounds();
                    }
                }
                else
                { 
                    //No grid layout? We always need to run an update then
                    UpdateTerrains();
                }
            }
        }

        /// <summary>
        /// Forces an re-evaluation of the terrains that need to be loaded for this loader.
        /// </summary>
        public void Refresh()
        {
            UpdateTerrains();
            //evaluate when the next update would be if we are in a grid layout
            if (TerrainLoaderManager.Instance.m_assumeGridLayout)
            {
                CalculcateNextUpdateBounds();
            }
        }


        public void CalculcateNextUpdateBounds()
        {
            m_lastUpdatePosition = m_loadingBoundsRegular.center;

            m_nextUpdateBoundsRegular = TerrainLoaderManager.Instance.GetNextUpdateBounds(m_loadingBoundsRegular);
            m_nextUpdateBoundsRegularGizmo = new BoundsDouble(m_nextUpdateBoundsRegular.center, new Vector3Double(m_nextUpdateBoundsRegular.size.x, TerrainLoaderManager.Instance.TerrainSceneStorage.m_terrainTilesSize, m_nextUpdateBoundsRegular.size.z));
            m_nextUpdateBoundsImpostor = TerrainLoaderManager.Instance.GetNextUpdateBounds(m_loadingBoundsImpostor);
            m_nextUpdateBoundsImpostorGizmo = new BoundsDouble(m_nextUpdateBoundsImpostor.center, new Vector3Double(m_nextUpdateBoundsImpostor.size.x, TerrainLoaderManager.Instance.TerrainSceneStorage.m_terrainTilesSize, m_nextUpdateBoundsImpostor.size.z));
        }

        public void Start()
        {
#if UNITY_EDITOR
            if (EditorApplication.isPlaying)
            {
                //Display Error when Loaders are not activated in the Loader Manager
                if (m_loadMode == LoadMode.RuntimeAlways && !TerrainLoaderManager.Instance.TerrainSceneStorage.m_terrainLoadingEnabled)
                {
                    Debug.LogError("Terrain Loaders are currently disabled under Gaia Runtime > Terrain Loader. The Terrain Loader on the object '" + transform.name + "' will not be able to load any terrains.");
                }
            }
#endif
        }

        public void Awake()
        {
#if UNITY_EDITOR
            if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
            {
                //Auto-Deselect non runtime loaders on entering playmode
                //otherwise the tool might interfere with runtime loading
                if(m_loadMode!=LoadMode.RuntimeAlways && m_isSelected)
                {
                    Selection.objects = new Object[1] { GaiaUtils.GetGaiaGameObject()};
                }
            }
            UpdateTerrains();
#endif
        }


        public void UnloadTerrains()
        {
            if (this!=null && gameObject != null)
            {
                foreach (TerrainScene terrainScene in TerrainLoaderManager.TerrainScenes)
                {
                    if (terrainScene.HasRegularReference(gameObject))
                    {
                        terrainScene.RemoveRegularReference(gameObject);
                    }
                    if (terrainScene.HasImpostorReference(gameObject))
                    {
                        terrainScene.RemoveImpostorReference(gameObject);
                    }
                }
            }
        }

        public void UpdateTerrains()
        {
            if (this == null || gameObject == null)
            {
                return;
            }

            if (!enabled || !gameObject.activeInHierarchy)
            {
                return;
            }

            if (m_loadMode == LoadMode.Disabled || (m_loadMode == LoadMode.EditorSelected && !m_isSelected) || (m_loadMode==LoadMode.RuntimeAlways && !Application.isPlaying))
            {
                UnloadTerrains();
                return;
            }
            if (TerrainLoaderManager.ColliderOnlyLoadingActive)
            {
                m_shiftedBoundsCollider.extents = m_loadingBoundsCollider.extents - new Vector3Double(0.001, 0.001, 0.001);
                m_shiftedBoundsCollider.center = m_loadingBoundsCollider.center + TerrainLoaderManager.Instance.GetOrigin();
                TerrainLoaderManager.Instance.UpdateTerrainLoadState(m_shiftedBoundsCollider, null, gameObject, m_minRefreshDistance, m_maxRefreshDistance, m_minRefreshMS, m_maxRefreshMS, m_frustumCamera);
            }
            else
            {
                //shave off a tiny little bit of the loading size. - when a tool such as a spawner has a range that lies directly
                //on the edge of the terrain border, this leads to up to 9 additional terrains being loaded that are not required.
                //This results in slower spawning speeds, spawn issues, etc.
                m_shiftedBoundsRegular.extents = m_loadingBoundsRegular.extents - new Vector3Double(0.001, 0.001, 0.001);
                m_shiftedBoundsRegular.center = m_loadingBoundsRegular.center + TerrainLoaderManager.Instance.GetOrigin();

                m_shiftedBoundsImpostor.extents = m_loadingBoundsImpostor.extents - new Vector3Double(0.001, 0.001, 0.001);
                m_shiftedBoundsImpostor.center = m_loadingBoundsImpostor.center + TerrainLoaderManager.Instance.GetOrigin();

                TerrainLoaderManager.Instance.UpdateTerrainLoadState(m_shiftedBoundsRegular, m_shiftedBoundsImpostor, gameObject, m_minRefreshDistance, m_maxRefreshDistance, m_minRefreshMS, m_maxRefreshMS, m_frustumCamera);
            }
        }

        public void Teleport(Vector3Double newLocation)
        {
            transform.position = newLocation;
        }
#if UNITY_EDITOR
        public void OnDestroy()
        {
            if (!Application.isPlaying && ! EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
            {
                //We only must perform this if the runtime object already exists, otherwise we may cause errors e.g. during a build
                if (GaiaUtils.GetRuntimeSceneObject(false) != null)
                {
                    //remove all references from this loader object when it gets destroyed, so terrains can unload if this was the only object holding a reference to it
                    TerrainLoaderManager.Instance.RemoveAllReferencesOfGameObject(gameObject);
                }
            }
        }
#endif

    }
}
