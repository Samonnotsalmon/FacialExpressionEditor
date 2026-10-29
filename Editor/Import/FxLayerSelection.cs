using System.Linq;

namespace Samon.FacialExpressionEditor.Editor
{
    // Keep the checkbox consistent with every saved setting that removes a layer.
    internal static class FxLayerSelection
    {
        public static bool IsSelected(ExpressionSet set, string name) =>
            set.originalGestureLayers.Contains(name) || set.originalPartLayers.Contains(name) ||
            set.originalLayerSettings.Any(s => s.layerName == name && s.mode == OriginalLayerMode.Disable);

        public static void SetSelected(ExpressionSet set, string name, bool selected)
        {
            set.originalLayerSettings.RemoveAll(s => s.layerName == name);
            if (selected)
            {
                if (!set.originalPartLayers.Contains(name) && !set.originalGestureLayers.Contains(name))
                    set.originalGestureLayers.Add(name);
            }
            else
            {
                set.originalGestureLayers.RemoveAll(n => n == name);
                set.originalPartLayers.RemoveAll(n => n == name);
            }
            SyncImportSelection(set);
        }

        public static void SyncImportSelection(ExpressionSet set)
        {
            set.explicitLayerSelection = true;
            set.importLayerNames = set.originalGestureLayers.Concat(set.originalPartLayers).Distinct().ToList();
        }
    }
}
