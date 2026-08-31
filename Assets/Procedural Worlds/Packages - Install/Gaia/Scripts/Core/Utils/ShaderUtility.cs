using System;

namespace Gaia.ShaderUtilities
{
    public enum ShaderIDs
    {
        Null,
        PW_General_Forward,
        PW_General_Deferred
    }
    //Domain Reload warning disabled
    //Reason: only read access 
#pragma warning disable UDR0001
    public static class PWShaderNameUtility
    { 
        public static String[] ShaderName =
       {
           "",
           "PWS/PW_General_Forward",
           "PWS/PW_General_Deferred"
       };
    }
#pragma warning restore UDR0001
}


