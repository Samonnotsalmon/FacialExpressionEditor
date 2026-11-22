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
            if (descriptor == null) return false;
            var name = binding.propertyName.Substring(11);
            var renderer = root.transform.Find(binding.path)?.GetComponent<SkinnedMeshRenderer>();
            if (string.IsNullOrEmpty(binding.path)) renderer = root.GetComponent<SkinnedMeshRenderer>();
            // Integrated routing retains source names and creates separate audio-only aliases.
            var config = LipSyncProfileUtility.Resolve(root.GetComponentInChildren<FacialExpressionAvatar>(true));
            if (config?.enabled == true && renderer != null)
            {
                if (config.schemaVersion >= 1)
                {
                    if (config.targets.Any(t => t.mesh == renderer && LipSyncSetup.ResolveNames(t.visemes, renderer).Contains(name))) return true;
                }
                else
                {
                    var body = LipSyncSetup.ResolveAvatar(config, descriptor);
                    if (renderer == body && LipSyncSetup.ResolveNames(config.avatarVisemes, body).Contains(name)) return true;
                    if (renderer == config.mascotMesh && LipSyncSetup.ResolveNames(config.mascotVisemes, renderer).Contains(name)) return true;
                }
            }
            if (descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShapeFaceTracking &&
                renderer != null && descriptor.faceTrackingMeshes != null && descriptor.faceTrackingMeshes.Contains(renderer) &&
                descriptor.faceTrackingBlendshapesSetList != null && descriptor.faceTrackingBlendshapesSetList.Any(s =>
                    s.visemeBlendshapeNames != null && s.visemeBlendshapeNames.Contains(name))) return true;
            if (descriptor.VisemeSkinnedMesh == null ||
                !descriptor.VisemeSkinnedMesh.transform.IsChildOf(root.transform) ||
                binding.path != AnimationUtility.CalculateTransformPath(descriptor.VisemeSkinnedMesh.transform, root.transform)) return false;
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
