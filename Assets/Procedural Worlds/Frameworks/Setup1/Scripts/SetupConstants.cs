using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralWorlds.Setup
{
    public static class SetupConstants
    {
        //Disabled Domain Reload Warning
        //Reason: Readonly single string value
#pragma warning disable UDR0001
        public static string maintenanceTokenFilename = "MaintenanceToken.dat";
#pragma warning restore UDR0001
    }
}