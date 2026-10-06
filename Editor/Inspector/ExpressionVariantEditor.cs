using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    [CustomEditor(typeof(ExpressionVariant))]
    internal class ExpressionVariantEditor : UnityEditor.Editor
    {
        private int _partsLayerIndex = -1;

        public override void OnInspectorGUI()
        {
            var variant = (ExpressionVariant)target;

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(ExpressionVariant.expressionSet)),
                new GUIContent("表情セット"));
            serializedObject.ApplyModifiedProperties();

            var descriptor = variant.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                EditorGUILayout.HelpBox("アバターのルート（VRC Avatar Descriptorがあるオブジェクト）に付けてください。", MessageType.Warning);
                return;
            }

            // FXが無い・ジェスチャーやパーツが無いアバターでも、空の表情セットから組み立てられるようにする。
            var fx = FxImporter.GetFx(descriptor);

            EditorGUILayout.Space();
            var set = variant.expressionSet;
            if (set == null)
            {
                using (new EditorGUI.DisabledScope(fx == null))
                {
                    if (GUILayout.Button("表情セットを作成して、元FXから取り込む", GUILayout.Height(28)))
                    {
                        CreateSet(variant, descriptor, true);
                    }
                }
                if (GUILayout.Button("空の表情セットを作成", GUILayout.Height(28)))
                {
                    CreateSet(variant, descriptor, false);
                }
                return;
            }

            EditorGUILayout.LabelField("表情セット", $"{set.gestureSets.Count} 組");
            EditorGUILayout.LabelField("表情", $"{set.expressions.Count} 件");
            EditorGUILayout.LabelField("パーツ", $"{set.parts.Count} 件");
            var replaced = set.originalGestureLayers.Concat(set.originalPartLayers).ToList();
            EditorGUILayout.LabelField("置き換える元FXレイヤー", replaced.Count > 0 ? string.Join(", ", replaced) : "（なし）");

            if (fx != null)
            {
                EditorGUILayout.Space();
                if (GUILayout.Button("元FXからジェスチャーを取り込み直す"))
                {
                    if (EditorUtility.DisplayDialog("取り込み直し",
                            "表情セット（ジェスチャーの割り当て）と、置き換える元FXレイヤーを元FXの内容で設定し直します。\n" +
                            "表情ごとの設定は同じクリップなら残り、同じ名前の表情セットは使い方の設定が残ります。", "取り込む", "キャンセル"))
                    {
                        ShowResult(FxImporter.ImportGestures(descriptor, set));
                    }
                }

                DrawPartsImport(descriptor, set, fx.layers.Select(l => l.name).ToArray());
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("表情セットを編集"))
            {
                Selection.activeObject = set;
            }
        }

        private void DrawPartsImport(VRCAvatarDescriptor descriptor, ExpressionSet set, string[] layerNames)
        {
            var candidates = layerNames.Where(n => !set.originalGestureLayers.Contains(n)).ToArray();
            if (candidates.Length == 0) return;

            if (_partsLayerIndex < 0 || _partsLayerIndex >= candidates.Length)
            {
                _partsLayerIndex = Math.Max(0, Array.FindIndex(candidates,
                    n => n.IndexOf("part", StringComparison.OrdinalIgnoreCase) >= 0));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _partsLayerIndex = EditorGUILayout.Popup("パーツの取り込み元", _partsLayerIndex, candidates);
                if (GUILayout.Button("取り込む", GUILayout.Width(80)))
                {
                    var added = FxImporter.ImportPartsFromLayer(descriptor, set, candidates[_partsLayerIndex]);
                    EditorUtility.DisplayDialog("パーツの取り込み", $"パーツを {added} 件追加しました。", "OK");
                }
            }
        }

        private static void CreateSet(ExpressionVariant variant, VRCAvatarDescriptor descriptor, bool importFromFx)
        {
            var path = EditorUtility.SaveFilePanelInProject("表情セットの保存先", descriptor.gameObject.name + "_表情セット",
                "asset", "表情セットを保存する場所を選んでください。", "Assets");
            if (string.IsNullOrEmpty(path)) return;

            var set = CreateInstance<ExpressionSet>();
            FxImporter.Result result = null;
            if (importFromFx)
            {
                AssetDatabase.CreateAsset(set, path);
                result = FxImporter.ImportGestures(descriptor, set);
            }
            else
            {
                set.gestureSets.Add(new GestureSet { name = "セット1" });
                AssetDatabase.CreateAsset(set, path);
            }
            AssetDatabase.SaveAssets();

            Undo.RecordObject(variant, "表情セットを設定");
            variant.expressionSet = set;
            EditorUtility.SetDirty(variant);

            if (result != null) ShowResult(result);
        }

        private static void ShowResult(FxImporter.Result result)
        {
            var layers = result.GestureLayers.Count > 0 ? string.Join(", ", result.GestureLayers) : "（見つかりませんでした）";
            EditorUtility.DisplayDialog("元FXから取り込み",
                $"表情セット：{string.Join(", ", result.GestureSets)}\n" +
                $"新しく追加した表情：{result.AddedExpressions} 件\n" +
                $"ライブラリに追加したフォルダ：{result.AddedFolders} 件\n" +
                $"置き換える元FXレイヤー：{layers}", "OK");
        }
    }
}
