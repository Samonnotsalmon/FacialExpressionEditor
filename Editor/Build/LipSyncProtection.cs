using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    // Expression curves must not take ownership of blendshapes driven by VRChat lip sync.
    internal static class LipSyncProtection
    {
        public static bool IsProtected(EditorCurveBinding binding, GameObject root, ExpressionSet set = null)
        {
            if (root == null || binding.type != typeof(SkinnedMeshRenderer) || !binding.propertyName.StartsWith("blendShape.")) return false;
            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return false;
            var name = binding.propertyName.Substring(11);
            return (descriptor.VisemeBlendShapes != null && descriptor.VisemeBlendShapes.Contains(name)) ||
                (!string.IsNullOrEmpty(descriptor.MouthOpenBlendShapeName) && descriptor.MouthOpenBlendShapeName == name);
        }

        public static void Remove(AnimationClip clip, GameObject root, ExpressionSet set = null)
        {
            if (clip == null || root == null) return;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(b => IsProtected(b, root, set)))
                AnimationUtility.SetEditorCurve(clip, binding, null);
        }
    }
}
