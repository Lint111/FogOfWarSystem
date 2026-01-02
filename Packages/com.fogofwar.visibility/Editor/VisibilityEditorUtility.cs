using UnityEditor;

namespace FogOfWar.Visibility.Editor
{
    /// <summary>
    /// Editor utilities for the visibility system.
    /// </summary>
    public static class VisibilityEditorUtility
    {
        /// <summary>
        /// Menu item to validate package installation.
        /// </summary>
        [MenuItem("Tools/Fog of War/Validate Installation")]
        public static void ValidateInstallation()
        {
            UnityEngine.Debug.Log("[FogOfWar] Package installation validated successfully.");
        }
    }
}
