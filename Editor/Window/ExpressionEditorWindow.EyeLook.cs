using System;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        private void DrawManualEyeLook(ExpressionSet set)
        {
            EditorGUILayout.Space(12);
            var enabled = EditorGUILayout.ToggleLeft("視線操作メニューを追加", set.manualEyeLook);
            if (enabled != set.manualEyeLook) Modify(set, "視線操作メニュー", () => set.manualEyeLook = enabled);
            if (!enabled) return;
            EditorGUILayout.HelpBox("ONの間は手動の目線を優先し、OFFで元の視線制御へ戻ります。各クリップの先頭の姿勢を使います。", MessageType.Info);
            var mode = (EyeLookSetupMode)EditorGUILayout.Popup("設定方法", (int)set.eyeLookSetupMode,
                new[] { "クリップを割り当てる", "Eye Lookから生成する" });
            if (mode != set.eyeLookSetupMode) Modify(set, "視線の設定方法を変更", () => set.eyeLookSetupMode = mode);
            var generate = mode == EyeLookSetupMode.GenerateFromDescriptor;
            if (generate)
            {
                EditorGUILayout.LabelField("Avatar Descriptorの目のボーンと5方向の回転設定から生成します。生成ボタンを押すまで現在の割り当ては保持されます。", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Avatar DescriptorのEye Lookから生成"))
                {
                    try { EyeLookPoseUtility.Generate(set, Descriptor); }
                    catch (InvalidOperationException e) { EditorUtility.DisplayDialog("視線クリップを生成", e.Message, "OK"); }
                }
                EditorGUILayout.LabelField("生成済みのクリップは上書きしません。", EditorStyles.wordWrappedMiniLabel);
            }
            else EditorGUILayout.LabelField("視線だけを記録した正面・上下左右のクリップを指定してください。", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(generate))
            {
                for (var i = 0; i < 5; i++)
                {
                    var old = set.eyeLookPoses != null && i < set.eyeLookPoses.Length ? set.eyeLookPoses[i] : null;
                    var clip = (AnimationClip)EditorGUILayout.ObjectField(ManualEyeLook.Labels[i], old, typeof(AnimationClip), false);
                    if (clip == old) continue;
                    var index = i;
                    Modify(set, "視線クリップを変更", () => { Array.Resize(ref set.eyeLookPoses, 5); set.eyeLookPoses[index] = clip; });
                }
            }
            try { ManualEyeLook.Validate(set); }
            catch (InvalidOperationException e) { EditorGUILayout.HelpBox(e.Message, MessageType.Warning); }
        }
    }
}
