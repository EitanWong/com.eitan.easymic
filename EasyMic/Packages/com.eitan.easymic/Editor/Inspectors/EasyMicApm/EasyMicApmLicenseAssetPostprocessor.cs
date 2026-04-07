#if EASYMIC_APM_INTEGRATION
namespace Eitan.EasyMic.Editor.Inspectors
{
    using System;
    using UnityEditor;

    internal sealed class EasyMicApmLicenseAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!TouchesPotentialProvider(importedAssets) &&
                !TouchesPotentialProvider(deletedAssets) &&
                !TouchesPotentialProvider(movedAssets) &&
                !TouchesPotentialProvider(movedFromAssetPaths))
            {
                return;
            }

            EasyMicApmLicenseEditorUtility.NotifyProviderAssetsChanged();
        }

        private static bool TouchesPotentialProvider(string[] assetPaths)
        {
            if (assetPaths == null)
            {
                return false;
            }

            for (int i = 0; i < assetPaths.Length; i++)
            {
                string path = assetPaths[i];
                if (string.IsNullOrWhiteSpace(path) ||
                    !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
#endif
