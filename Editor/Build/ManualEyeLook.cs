using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class ManualEyeLook
    {
        public const string Enabled = "FEE/EyeLookEnabled", X = "FEE/EyeLookX", Y = "FEE/EyeLookY";
        public const string RequestedTracking = "FEE/EyeTrackingRequested";
        public static readonly string[] Labels = { "正面", "上", "下", "左", "右" };
        public static readonly Vector2[] Positions = { Vector2.zero, Vector2.up, Vector2.down, Vector2.left, Vector2.right };

        public static void Validate(ExpressionSet set)
        {
            if (set.eyeLookPoses == null || set.eyeLookPoses.Length != 5 || set.eyeLookPoses.Any(c => !c))
                throw new InvalidOperationException("視線操作には正面・上下左右の5つのクリップを指定してください。");
            var bindings = AnimationUtility.GetCurveBindings(set.eyeLookPoses[0]);
            if (bindings.Length == 0) throw new InvalidOperationException("正面の視線クリップにカーブがありません。");
            foreach (var clip in set.eyeLookPoses)
            {
                var actual = AnimationUtility.GetCurveBindings(clip);
                if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0 ||
                    actual.Except(bindings).Any())
                    throw new InvalidOperationException("正面の視線クリップには全方向で使う項目の初期値を記録してください。画像やマテリアル差し替えは使用できません。");
            }
        }

    }
}
