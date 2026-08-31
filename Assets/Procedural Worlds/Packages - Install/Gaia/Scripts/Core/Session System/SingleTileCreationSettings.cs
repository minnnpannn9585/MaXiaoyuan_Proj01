using Gaia;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingleTileCreationSettings : ScriptableObject
{
    public string m_terrainName;
    public Vector3 m_position;
    public int m_terrainSize;
    public GaiaDefaults m_gaiaDefaults;
    public float m_terrainHeight;
    public bool m_createInTerrainScene;
    public TerrainLayer[] m_terrainLayers;
    [NonSerialized]
    public DetailPrototype[] m_terrainDetails;
    [NonSerialized]
    public TreePrototype[] m_terrainTrees;
}
