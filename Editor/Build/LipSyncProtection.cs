using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Samon.FacialExpressionEditor.Editor
{
    // Expression curves must not take ownership of blendshapes driven by VRChat lip sync.
    internal static class LipSyncProtection
    {
        public static bool IsProtected(EditorCurveBinding binding, GameObject root, ExpressionSet set = null)
        {
            if (root == null || binding.type != typeof(SkinnedMeshRenderer) || !binding.propertyName.StartsWith("blendShape.")) return false;
            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null || descriptor.VisemeSkinnedMesh == null ||
                !descriptor.VisemeSkinnedMesh.transform.IsChildOf(root.transform) ||
                binding.path != AnimationUtility.CalculateTransformPath(descriptor.VisemeSkinnedMesh.transform, root.transform)) return false;
            var name = binding.propertyName.Substring(11);
            return (descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape &&
                    descriptor.VisemeBlendShapes != null && descriptor.VisemeBlendShapes.Contains(name)) ||
                (descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape &&
                    !string.IsNullOrEmpty(descriptor.MouthOpenBlendShapeName) && descriptor.MouthOpenBlendShapeName == name);
        }

        public static void Remove(AnimationClip clip, GameObject root, ExpressionSet set = null)
        {
            if (clip == null || root == null) return;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(b => IsProtected(b, root, set)))
                AnimationUtility.SetEditorCurve(clip, binding, null);
        }
    }
}
