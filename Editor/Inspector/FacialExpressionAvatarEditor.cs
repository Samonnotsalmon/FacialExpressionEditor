using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    [CustomEditor(typeof(FacialExpressionAvatar))]
    internal class FacialExpressionAvatarEditor : UnityEditor.Editor
    {
        private static readonly string[] SourceLabels = { "今の顔から", "既存の表情をコピー", "空から" };

        private int _partsLayerIndex = -1;
        private string _newVariantName;
        private string _newExpressionName = "新しい表情";
        private ExpressionSetUtility.NewExpressionSource _newExpressionSource;
        private int _copyFromIndex;
        private bool _showNewExpression;
        private bool _showBaseFace = true;
        private bool _showInactiveBaseFace;
        private bool _showExpressions = true;

        public override void OnInspectorGUI()
        {
            var avatar = (FacialExpressionAvatar)target;

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(FacialExpressionAvatar.expressionSet)),
                new GUIContent("表情データ"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(FacialExpressionAvatar.faceVariant)),
                new GUIContent("顔バリアント"));
            serializedObject.ApplyModifiedProperties();

            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                EditorGUILayout.HelpBox("アバターのルート（VRC Avatar Descriptorがあるオブジェクト）に付けてください。", MessageType.Warning);
                return;
            }

            // FXが無い・ジェスチャーやパーツが無いアバターでも、空の表情データから組み立てられるようにする。
            var fx = FxImporter.GetFx(descriptor);
            var set = avatar.expressionSet;

            EditorGUILayout.Space();
            if (set == null)
            {
                using (new EditorGUI.DisabledScope(fx == null))
                {
                    if (GUILayout.Button("表情データを作成して、元FXから取り込む", GUILayout.Height(28)))
                    {
                        CreateSet(avatar, descriptor, true);
                    }
                }
                if (GUILayout.Button("空の表情データを作成", GUILayout.Height(28)))
                {
                    CreateSet(avatar, descriptor, false);
                }
                return;
            }

            if (GUILayout.Button("表情エディタを開く", GUILayout.Height(28)))
            {
                ExpressionEditorWindow.Open(avatar);
            }

            DrawExpressionSet(avatar, descriptor, set, fx);
            DrawNewExpression(avatar, set);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("顔バリアント", EditorStyles.boldLabel);
            if (avatar.faceVariant == null)
            {
                DrawCreateVariant(avatar, set);
            }
            else
            {
                DrawVariant(avatar, set, avatar.faceVariant);
            }
        }

        private void DrawExpressionSet(FacialExpressionAvatar avatar, VRCAvatarDescriptor descriptor, ExpressionSet set,
            UnityEditor.Animations.AnimatorController fx)
        {
            EditorGUILayout.LabelField("表情データ", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("表情セット", $"{set.gestureSets.Count} 組");
            EditorGUILayout.LabelField("表情", $"{set.expressions.Count} 件");
            EditorGUILayout.LabelField("パーツ", $"{set.parts.Count} 件");
            var replaced = set.originalGestureLayers.Concat(set.originalPartLayers).ToList();
            EditorGUILayout.LabelField("置き換える元FXレイヤー", replaced.Count > 0 ? string.Join(", ", replaced) : "（なし）");

            if (fx != null)
            {
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

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("表情データを編集"))
                {
                    Selection.activeObject = set;
                }
                if (GUILayout.Button("複製してこのアバター専用にする"))
                {
                    DuplicateSet(avatar, set);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawNewExpression(FacialExpressionAvatar avatar, ExpressionSet set)
        {
            _showNewExpression = EditorGUILayout.Foldout(_showNewExpression, "新しい表情を作成", true);
            if (!_showNewExpression) return;

            EditorGUI.indentLevel++;
            _newExpressionName = EditorGUILayout.TextField("名前", _newExpressionName);
            _newExpressionSource = (ExpressionSetUtility.NewExpressionSource)EditorGUILayout.Popup("作り方",
                (int)_newExpressionSource, SourceLabels);

            Expression copyFrom = null;
            if (_newExpressionSource == ExpressionSetUtility.NewExpressionSource.CopyExpression)
            {
                var names = set.expressions.Select(e => e.name).ToArray();
                if (names.Length == 0)
                {
                    EditorGUILayout.LabelField("コピーできる表情がありません。", EditorStyles.miniLabel);
                }
                else
                {
                    _copyFromIndex = Mathf.Clamp(EditorGUILayout.Popup("コピー元", _copyFromIndex, names), 0, names.Length - 1);
                    copyFrom = set.expressions[_copyFromIndex];
                }
            }

            EditorGUILayout.LabelField($"保存先：{AssetPathUtility.FolderOf(set)}/Expressions/", EditorStyles.miniLabel);
            EditorGUI.indentLevel--;

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newExpressionName) ||
                                               _newExpressionSource == ExpressionSetUtility.NewExpressionSource.CopyExpression && copyFrom == null))
            {
                if (GUILayout.Button("作成"))
                {
                    var expression = ExpressionSetUtility.CreateExpression(set, _newExpressionName.Trim(),
                        _newExpressionSource, copyFrom, avatar.gameObject, avatar.faceVariant);
                    if (expression != null) EditorGUIUtility.PingObject(expression.clip);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawCreateVariant(FacialExpressionAvatar avatar, ExpressionSet set)
        {
            EditorGUILayout.HelpBox(
                "ベース顔を変えたアバターでは、顔バリアントを作ると、表情中のベース顔の扱いと表情の差し替えを設定できます。" +
                "同じ顔の別のアバターは、作った顔バリアントを指定するだけで共有できます。", MessageType.None);

            if (_newVariantName == null) _newVariantName = avatar.gameObject.name;
            _newVariantName = EditorGUILayout.TextField("名前", _newVariantName);

            if (GUILayout.Button("このアバターの顔で顔バリアントを作成", GUILayout.Height(24)))
            {
                var variant = FaceVariantUtility.Create(set, avatar.gameObject, _newVariantName);
                Undo.RecordObject(avatar, "顔バリアントを設定");
                avatar.faceVariant = variant;
                EditorUtility.SetDirty(avatar);
            }
        }

        private void DrawVariant(FacialExpressionAvatar avatar, ExpressionSet set, FaceVariant variant)
        {
            var mismatched = FaceVariantUtility.Mismatched(variant, avatar.gameObject);
            if (mismatched.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"このアバターのベース顔が、顔バリアントを検出したときと {mismatched.Count} 箇所違います" +
                    $"（{string.Join(", ", mismatched.Take(3).Select(k => k.blendShape))}など）。\n" +
                    "別の顔のアバターなら別の顔バリアントを、同じ顔なら「ベース顔を検出し直す」を使ってください。",
                    MessageType.Warning);
            }

            var keep = EditorGUILayout.Popup("表情中のベース顔（標準）", variant.keepBaseFaceByDefault ? 0 : 1,
                new[] { "残す（差分を表情に上乗せ）", "リセット（元の表情そのまま）" }) == 0;
            if (keep != variant.keepBaseFaceByDefault)
            {
                Undo.RecordObject(variant, "ベース顔の標準を変更");
                variant.keepBaseFaceByDefault = keep;
                EditorUtility.SetDirty(variant);
            }

            if (GUILayout.Button("ベース顔を検出し直す"))
            {
                var missing = FaceVariantUtility.DetectBaseFace(variant, set, avatar.gameObject);
                if (missing.Count > 0)
                {
                    EditorUtility.DisplayDialog("ベース顔の検出",
                        $"元Prefabが見つからないため、次のメッシュは検出できませんでした。\n{string.Join("\n", missing)}", "OK");
                }
            }

            DrawBaseFace(variant);
            DrawUnbakedOverrides(variant);
            DrawExpressions(set, variant);
        }

        private static void DrawUnbakedOverrides(FaceVariant variant)
        {
            var unbaked = variant.overrides.Where(o => o.clip != null && !o.baseFaceBaked).ToList();
            if (unbaked.Count == 0) return;

            EditorGUILayout.HelpBox(
                $"ベース顔をまだ反映していない差し替えクリップが {unbaked.Count} 件あります。\n" +
                "反映すると、「残す」になっているシェイプキーの差分をクリップに書き込みます（1回だけ）。", MessageType.Info);
            if (GUILayout.Button($"差し替えクリップにベース顔を反映（{unbaked.Count} 件）"))
            {
                foreach (var entry in unbaked) FaceVariantUtility.BakeBaseFace(variant, entry);
                AssetDatabase.SaveAssets();
                GUIUtility.ExitGUI();
            }
        }

        private void DrawBaseFace(FaceVariant variant)
        {
            var active = variant.baseFace.Where(k => k.enabled).ToList();
            var inactive = variant.baseFace.Where(k => !k.enabled).ToList();

            _showBaseFace = EditorGUILayout.Foldout(_showBaseFace, $"ベース顔（{active.Count}）", true);
            if (_showBaseFace)
            {
                EditorGUI.indentLevel++;
                if (active.Count == 0) EditorGUILayout.LabelField("（元Prefabとの違いはありません）", EditorStyles.miniLabel);
                foreach (var key in active) BaseFaceRow(variant, key);
                EditorGUI.indentLevel--;
            }

            if (inactive.Count == 0) return;

            EditorGUI.indentLevel++;
            _showInactiveBaseFace = EditorGUILayout.Foldout(_showInactiveBaseFace, $"表情で動かないシェイプキーの違い（{inactive.Count}）", true);
            if (_showInactiveBaseFace)
            {
                foreach (var key in inactive) BaseFaceRow(variant, key);
            }
            EditorGUI.indentLevel--;
        }

        private static void BaseFaceRow(FaceVariant variant, BaseFaceKey key)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var label = $"{key.blendShape}   {key.referenceValue:0.#} → {key.variantValue:0.#}";
                var enabled = EditorGUILayout.ToggleLeft(label, key.enabled);
                bool alwaysKeep;
                using (new EditorGUI.DisabledScope(!key.enabled))
                {
                    alwaysKeep = EditorGUILayout.ToggleLeft("常に残す", key.alwaysKeep, GUILayout.Width(80));
                }
                if (enabled == key.enabled && alwaysKeep == key.alwaysKeep) return;

                Undo.RecordObject(variant, "ベース顔を変更");
                key.enabled = enabled;
                key.alwaysKeep = alwaysKeep;
                EditorUtility.SetDirty(variant);
            }
        }

        /// <summary>
        /// 表情ごとのベース顔の扱い（標準／残す／リセット）と、このバリアント用の差し替え。
        /// </summary>
        private void DrawExpressions(ExpressionSet set, FaceVariant variant)
        {
            _showExpressions = EditorGUILayout.Foldout(_showExpressions,
                $"表情の差し替え（{variant.overrides.Count(o => o.clip != null)} / {set.expressions.Count}）", true);
            if (!_showExpressions) return;

            EditorGUI.indentLevel++;
            foreach (var expression in set.expressions)
            {
                var entry = variant.FindOverride(expression.id);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(expression.name, GUILayout.Width(EditorGUIUtility.labelWidth));

                    if (entry == null)
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.ObjectField(expression.clip, typeof(AnimationClip), false);
                        }
                        using (new EditorGUI.DisabledScope(expression.clip == null))
                        {
                            if (GUILayout.Button("複製して差し替え", GUILayout.Width(110)))
                            {
                                FaceVariantUtility.DuplicateForVariant(variant, expression);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                    else
                    {
                        var clip = (AnimationClip)EditorGUILayout.ObjectField(entry.clip, typeof(AnimationClip), false);
                        if (clip != entry.clip && clip != null)
                        {
                            Undo.RecordObject(variant, "表情を差し替え");
                            entry.clip = clip;
                            EditorUtility.SetDirty(variant);
                        }
                        if (GUILayout.Button("共有に戻す", GUILayout.Width(110)))
                        {
                            FaceVariantUtility.RevertOverride(variant, expression.id);
                            GUIUtility.ExitGUI();
                        }
                    }
                }

                if (entry != null && FaceVariantUtility.IsSourceUpdated(entry, expression))
                {
                    EditorGUILayout.HelpBox("複製した後に、共有の表情が更新されています。", MessageType.Info);
                }
            }
            EditorGUI.indentLevel--;
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

        private static void DuplicateSet(FacialExpressionAvatar avatar, ExpressionSet set)
        {
            var path = EditorUtility.SaveFilePanelInProject("複製した表情データの保存先", avatar.gameObject.name + "_表情データ",
                "asset", "このアバター専用の表情データを保存する場所を選んでください。", AssetPathUtility.FolderOf(set));
            if (string.IsNullOrEmpty(path)) return;

            var copy = ExpressionSetUtility.Duplicate(set, path);
            Undo.RecordObject(avatar, "表情データを複製");
            avatar.expressionSet = copy;
            EditorUtility.SetDirty(avatar);
        }

        private static void CreateSet(FacialExpressionAvatar avatar, VRCAvatarDescriptor descriptor, bool importFromFx)
        {
            var path = EditorUtility.SaveFilePanelInProject("表情データの保存先", descriptor.gameObject.name + "_表情データ",
                "asset", "表情データを保存する場所を選んでください。", "Assets");
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
                set.faceControlImported = true;
                AssetDatabase.CreateAsset(set, path);
            }
            AssetDatabase.SaveAssets();

            Undo.RecordObject(avatar, "表情データを設定");
            avatar.expressionSet = set;
            EditorUtility.SetDirty(avatar);

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
