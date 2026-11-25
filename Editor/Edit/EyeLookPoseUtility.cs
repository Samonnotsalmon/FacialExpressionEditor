using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class EyeLookPoseUtility
    {
        public static AnimationClip[] BuildPoses(VRCAvatarDescriptor descriptor)
        {
            if (!descriptor || !descriptor.enableEyeLook)
                throw new InvalidOperationException("Avatar DescriptorのEye Lookを有効にして設定してください。");
            var settings = descriptor.customEyeLookSettings;
            var eyes = new[] { settings.leftEye, settings.rightEye };
            if (eyes.All(e => !e))
                throw new InvalidOperationException("Eye LookのLeft Eye／Right Eyeに目のボーンを指定してください。シェイプキーだけの視線は既存クリップを指定してください。");
            if (eyes[0] && eyes[0] == eyes[1])
                throw new InvalidOperationException("左右に同じ目のボーンが指定されています。");
            foreach (var eye in eyes.Where(e => e))
                if (eye == descriptor.transform || !eye.IsChildOf(descriptor.transform))
                    throw new InvalidOperationException("目のボーンは対象アバターの子階層に指定してください。");

            var rotations = new[] { settings.eyesLookingStraight, settings.eyesLookingUp, settings.eyesLookingDown,
                settings.eyesLookingLeft, settings.eyesLookingRight };
            if (rotations.Any(r => r == null))
                throw new InvalidOperationException("Eye Lookの正面・上下左右の回転をAvatar Descriptorで設定してください。");
            var poses = new AnimationClip[5];
            try
            {
                for (var direction = 0; direction < poses.Length; direction++)
                {
                    var clip = poses[direction] = new AnimationClip { name = "EyeLook_" + new[] { "Straight", "Up", "Down", "Left", "Right" }[direction] };
                    for (var side = 0; side < eyes.Length; side++)
                    {
                        if (!eyes[side]) continue;
                        var rotation = Normalize(side == 0 ? rotations[direction].left : rotations[direction].right);
                        var neutral = Normalize(side == 0 ? rotations[0].left : rotations[0].right);
                        // qと-qは同じ回転。ブレンド時の反転を避け、正面と符号を揃える。
                        if (Quaternion.Dot(rotation, neutral) < 0) rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                        var path = AnimationUtility.CalculateTransformPath(eyes[side], descriptor.transform);
                        for (var axis = 0; axis < 4; axis++)
                            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + "xyzw"[axis]),
                                AnimationCurve.Constant(0, 1, rotation[axis]));
                    }
                    clip.EnsureQuaternionContinuity();
                }
                return poses;
            }
            catch
            {
                foreach (var pose in poses.Where(p => p)) UnityEngine.Object.DestroyImmediate(pose);
                throw;
            }
        }

        private static Quaternion Normalize(Quaternion value)
        {
            var length = Mathf.Sqrt(Quaternion.Dot(value, value));
            if (float.IsNaN(length) || float.IsInfinity(length) || length < 0.00001f)
                throw new InvalidOperationException("Eye Lookの回転が未設定または不正です。正面・上下左右をAvatar Descriptorで設定してください。");
            return new Quaternion(value.x / length, value.y / length, value.z / length, value.w / length);
        }

        public static void Generate(ExpressionSet set, VRCAvatarDescriptor descriptor)
        {
            if (!set || !AssetDatabase.GetAssetPath(set).StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("先に表情設定をAssets内へ保存してください。");
            var poses = BuildPoses(descriptor);
            var paths = new System.Collections.Generic.List<string>();
            try
            {
                var folder = AssetPathUtility.EnsureFolder(AssetPathUtility.SettingsFolder(set), "EyeLook");
                foreach (var pose in poses)
                {
                    var path = AssetPathUtility.UniqueAssetPath(folder + "/" + pose.name + ".anim");
                    AssetDatabase.CreateAsset(pose, path);
                    paths.Add(path);
                }
            }
            catch
            {
                foreach (var path in paths) AssetDatabase.DeleteAsset(path);
                foreach (var pose in poses.Where(p => p && !AssetDatabase.Contains(p))) UnityEngine.Object.DestroyImmediate(pose);
                throw;
            }
            Undo.RecordObject(set, "Eye Lookから視線クリップを生成");
            set.eyeLookPoses = poses;
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
        }
    }
}
