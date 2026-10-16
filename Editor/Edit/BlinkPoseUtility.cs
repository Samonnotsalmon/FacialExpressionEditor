using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class BlinkPoseUtility
    {
        internal static List<BlinkShape> Shapes(AnimationClip pose, GameObject root, ExpressionSet set)
        {
            return AnimationUtility.GetCurveBindings(pose)
                .Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape.") &&
                    !LipSyncProtection.IsProtected(b, root, set) && AnimationUtility.GetFloatValue(root, b, out _))
                .Select(b => new BlinkShape { path = b.path, blendShape = b.propertyName.Substring(11), closedValue = AnimationUtility.GetEditorCurve(pose, b).Evaluate(0) }).ToList();
        }

        internal static AnimationClip Editable(ExpressionSet set, GameObject root)
        {
            if (set.blinkPose != null && set.blinkPose == set.editableBlinkPose) return set.blinkPose;
            var source = set.blinkPose;
            var pose = new AnimationClip { name = "BlinkPose" };
            if (source != null)
            {
                var bindings = AnimationUtility.GetCurveBindings(source)
                    .Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape.") &&
                        !LipSyncProtection.IsProtected(b, root, set) && AnimationUtility.GetFloatValue(root, b, out _)).ToArray();
                foreach (var binding in bindings)
                    AnimationUtility.SetEditorCurve(pose, binding, AnimationCurve.Constant(0, 0, AnimationUtility.GetEditorCurve(source, binding).Evaluate(0)));
            }
            else
            {
                var shapes = set.customBlink ? set.blinkShapes : AvatarFaceDefaults.DescriptorBlinkShapes(root.GetComponent<VRCAvatarDescriptor>());
                foreach (var shape in shapes)
                {
                    var binding = ClipEditing.BlendShape(shape.path, shape.blendShape);
                    if (!LipSyncProtection.IsProtected(binding, root, set) && AnimationUtility.GetFloatValue(root, binding, out _))
                        AnimationUtility.SetEditorCurve(pose, binding, AnimationCurve.Constant(0, 0, shape.closedValue));
                }
            }
            var folder = AssetPathUtility.EnsureFolder(AssetPathUtility.FolderOf(set), "Blink");
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/BlinkPose.anim");
            pose.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(pose, path);
            Undo.RecordObject(set, "専用の目閉じポーズを作成");
            set.blinkPose = pose; set.editableBlinkPose = pose;
            EditorUtility.SetDirty(set); AssetDatabase.SaveAssets();
            return pose;
        }

        internal static AnimationClip Loop(AnimationClip pose, GameObject root, ExpressionSet set)
        {
            var loop = new AnimationClip { name = "Blink Preview", hideFlags = HideFlags.HideAndDontSave };
            foreach (var shape in Shapes(pose, root, set))
            {
                var binding = ClipEditing.BlendShape(shape.path, shape.blendShape);
                AnimationUtility.GetFloatValue(root, binding, out var open);
                AnimationUtility.SetEditorCurve(loop, binding, GenerateFxPass.BlinkCurve(open, shape.closedValue));
            }
            var settings = AnimationUtility.GetAnimationClipSettings(loop); settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(loop, settings);
            return loop;
        }
    }
}
