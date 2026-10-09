using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        private GameObject _targetsAvatar;
        private List<string> _startMeshPaths = new List<string>();
        private List<string> _startObjectPaths = new List<string>();
        private List<string> _startLayerNames = new List<string>();
        private string _startLipMesh = "";
        private readonly List<OriginalMenuSelection> _startRemovedMenus = new List<OriginalMenuSelection>();
        private bool _showOriginalMenus;
        private bool _showStartReplacement;

        private void DrawStartTargets()
        {
            if (_targetsAvatar != _startAvatar)
            {
                _targetsAvatar = _startAvatar;
                _startRemovedMenus.Clear();
                _startMeshPaths.Clear(); _startObjectPaths.Clear(); _startLayerNames.Clear();
                var face = AvatarSetup.FaceRenderer(_startAvatar);
                if (face != null) _startMeshPaths.Add(AnimationUtility.CalculateTransformPath(face.transform, _startAvatar.transform));
                var d = _startAvatar.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                _startLipMesh = d.VisemeSkinnedMesh != null ? AnimationUtility.CalculateTransformPath(d.VisemeSkinnedMesh.transform, _startAvatar.transform) : "";
                _startLayerNames.AddRange(FxImporter.PeekGestures(d).layers);
                _startPreviewHeight = 0.03f; _startPreviewZoom = 1f;
            }
            DrawSetupCamera(_startAvatar, ref _startPreviewHeight, ref _startPreviewZoom);
            DrawTargets(_startAvatar, _startMeshPaths, _startObjectPaths, ref _startLipMesh);
            _showStartReplacement = EditorGUILayout.Foldout(_showStartReplacement, $"既存設定との置き換え（FX {_startLayerNames.Count} レイヤー）", true);
            if (!_showStartReplacement) return;
            var descriptor = _startAvatar.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            var fx = FxImporter.GetFx(descriptor);
            if (fx == null) return;
            foreach (var layer in fx.layers)
            {
                var next = EditorGUILayout.ToggleLeft(layer.name, _startLayerNames.Contains(layer.name));
                if (next && !_startLayerNames.Contains(layer.name)) _startLayerNames.Add(layer.name);
                if (!next) _startLayerNames.Remove(layer.name);
            }
            EditorGUILayout.HelpBox("チェックしたレイヤーを新しい表情制御に置き換えます。衣装・既存ギミック・引き継ぐパーツは残してください。", MessageType.Info);
            DrawOriginalMenuSelection(_startAvatar, _startRemovedMenus);
        }

        private static void DrawTargets(GameObject root, List<string> meshes, List<string> objects, ref string lipMesh)
        {
            EditorGUILayout.LabelField("表情を編集するメッシュ", EditorStyles.boldLabel);
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToList();
            foreach (var r in renderers)
            {
                var path = AnimationUtility.CalculateTransformPath(r.transform, root.transform);
                var on = EditorGUILayout.ToggleLeft(path == "" ? "（ルート）" : path, meshes.Contains(path));
                if (on && !meshes.Contains(path)) meshes.Add(path);
                if (!on) meshes.Remove(path);
            }
            var names = new[] { "現在の設定を維持" }.Concat(renderers.Select(r => AnimationUtility.CalculateTransformPath(r.transform, root.transform))).ToArray();
            var index = System.Array.IndexOf(names, lipMesh);
            var selected = EditorGUILayout.Popup("リップシンクのメッシュ", Mathf.Max(0, index), names);
            lipMesh = selected == 0 ? "" : names[selected];
            EditorGUILayout.LabelField("連動するオブジェクト／マテリアルの対象", EditorStyles.boldLabel);
            foreach (var path in objects.ToList())
                using (new EditorGUILayout.HorizontalScope())
                { GUILayout.Label(path); if (GUILayout.Button("外す", GUILayout.Width(45))) objects.Remove(path); }
            var added = (GameObject)EditorGUILayout.ObjectField("追加（D&D）", null, typeof(GameObject), true);
            if (added != null && added.transform.IsChildOf(root.transform))
            {
                var path = AnimationUtility.CalculateTransformPath(added.transform, root.transform);
                if (!objects.Contains(path)) objects.Add(path);
            }
        }

        private void DrawSetupTab(ExpressionSet set)
        {
            EditorGUILayout.HelpBox("この顔専用の設定です。表情クリップは取り込み時に複製し、作者の元データを保持します。", MessageType.Info);
            if (GUILayout.Button("この設定を別のベース顔用に複製…")) DuplicateFaceSetup(set);
            var height = set.previewHeight; var zoom = set.previewZoom;
            DrawSetupCamera(AvatarRoot, ref height, ref zoom);
            if (height != set.previewHeight || zoom != set.previewZoom)
            {
                Modify(set, "プレビューカメラの位置", () => { set.previewHeight = height; set.previewZoom = zoom; });
                DisposePreview(); _thumbnails.Clear();
            }
            // リストを直接変更するUIはコピーに対して描き、変更があったときだけUndoを記録する。
            var meshes = set.faceMeshPaths.ToList(); var objects = set.linkedObjectPaths.ToList(); var lip = set.lipSyncMeshPath;
            DrawTargets(AvatarRoot, meshes, objects, ref lip);
            if (!meshes.SequenceEqual(set.faceMeshPaths) || !objects.SequenceEqual(set.linkedObjectPaths) || lip != set.lipSyncMeshPath)
                Modify(set, "アバターの編集対象", () => { set.faceMeshPaths = meshes; set.linkedObjectPaths = objects; set.lipSyncMeshPath = lip; });
            EditorGUILayout.Space();
            DrawReplacementLayers(set);
            var explicitMenus = EditorGUILayout.ToggleLeft("元メニューの置換対象を個別に選ぶ", set.explicitMenuSelection);
            if (explicitMenus != set.explicitMenuSelection) Modify(set, "元メニューの扱い", () => set.explicitMenuSelection = explicitMenus);
            if (explicitMenus)
            {
                var selections = set.replacedMenuItems.ToList();
                DrawOriginalMenuSelection(AvatarRoot, selections);
                if (!selections.SequenceEqual(set.replacedMenuItems)) Modify(set, "置き換えるメニューを選択", () => set.replacedMenuItems = selections);
            }
            using (new EditorGUI.DisabledScope(!AvatarSetup.IsInScene(AvatarRoot)))
            if (GUILayout.Button("シーンの顔をベース顔として再読み込み"))
            {
                Undo.RecordObject(_avatar, "ベース顔を更新");
                if (Variant == null) _avatar.faceVariant = FaceVariantUtility.Create(set, AvatarRoot, set.name + "_ベース顔");
                else FaceVariantUtility.DetectBaseFace(Variant, set, AvatarRoot);
                Variant.useCapturedValues = true;
                EditorUtility.SetDirty(Variant);
                EditorUtility.SetDirty(_avatar); DisposePreview(); InvalidateDetailPreview();
            }
            EditorGUILayout.Space();
            DrawFistEyeProperties(set);
            DrawBatchBaseCorrections(set);
        }

        private bool _showReplacementLayers;
        private void DrawOriginalMenuSelection(GameObject root, List<OriginalMenuSelection> selected)
        {
            _showOriginalMenus = EditorGUILayout.Foldout(_showOriginalMenus, $"置き換える元メニュー（{selected.Count} 項目）", true);
            if (!_showOriginalMenus) return;
            EditorGUILayout.HelpBox("チェックした元メニュー項目をビルド時に取り除きます。未選択の項目とパラメータは保持します。", MessageType.Info);
            var roots = new List<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu>();
            var descriptor = root.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            roots.Add(descriptor.expressionsMenu);
            roots.AddRange(root.GetComponentsInChildren<nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller>(true).Select(i => i.menuToAppend));
            var visited = new HashSet<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu>();
            void Draw(VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu menu, string prefix)
            {
                if (menu == null || !visited.Add(menu)) return;
                for (var index = 0; index < menu.controls.Count; index++)
                {
                    var c = menu.controls[index];
                    var item = selected.Find(s => s.menu == menu && s.index == index && s.controlName == c.name);
                    var next = EditorGUILayout.ToggleLeft(prefix + c.name, item != null);
                    if (next && item == null) selected.Add(new OriginalMenuSelection { menu = menu, index = index, controlName = c.name });
                    if (!next && item != null) selected.Remove(item);
                    Draw(c.subMenu, prefix + c.name + " / ");
                }
            }
            foreach (var menu in roots) Draw(menu, "");
        }

        private void DrawReplacementLayers(ExpressionSet set)
        {
            _showReplacementLayers = EditorGUILayout.Foldout(_showReplacementLayers, "置き換える元FXレイヤー", true);
            if (!_showReplacementLayers) return;
            var fx = FxImporter.GetFx(Descriptor);
            if (fx == null) return;
            foreach (var layer in fx.layers)
            {
                var was = set.originalGestureLayers.Contains(layer.name);
                var next = EditorGUILayout.ToggleLeft(layer.name, was);
                if (next != was) Modify(set, "置き換えるFXを選択", () => { if (next) set.originalGestureLayers.Add(layer.name); else set.originalGestureLayers.Remove(layer.name); });
            }
            EditorGUILayout.HelpBox("選択したレイヤーだけを新しい表情制御に置換します。衣装などの既存ギミックのレイヤーは残します。", MessageType.Info);
        }

        private void DrawFistEyeProperties(ExpressionSet set)
        {
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Fistで握り具合を反映する目元", EditorStyles.boldLabel);
            var custom = EditorGUILayout.ToggleLeft("対象を個別に指定する（口元は完成表情を維持）", set.customFistEyeProperties);
            var bindings = set.expressions.Where(e => e.clip != null).SelectMany(e => AnimationUtility.GetCurveBindings(e.clip)).Distinct()
                .Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape.")).ToList();
            if (custom != set.customFistEyeProperties)
                Modify(set, "Fistの目元を設定", () => { if (custom) set.fistEyeProperties = bindings.Where(b => FistBlend.IsEyeProperty(b)).Select(ExpressionClipBuilder.Key).ToList(); set.customFistEyeProperties = custom; });
            if (!custom) { EditorGUILayout.LabelField("初期値は目・眉・blink等の名前から判定します。プレビューで確認してください。", EditorStyles.wordWrappedMiniLabel); return; }
            foreach (var binding in bindings)
            {
                var key = ExpressionClipBuilder.Key(binding); var was = set.fistEyeProperties.Contains(key);
                var next = EditorGUILayout.ToggleLeft(binding.path + " / " + binding.propertyName.Substring(11), was);
                if (next != was) Modify(set, "Fistの対象を変更", () => { if (next) set.fistEyeProperties.Add(key); else set.fistEyeProperties.Remove(key); });
            }
        }

        private void DuplicateFaceSetup(ExpressionSet set)
        {
            var path = EditorUtility.SaveFilePanelInProject("ベース顔ごとの表情設定", set.name + "_別の顔", "asset", "複製した設定の名前を指定してください。");
            if (string.IsNullOrEmpty(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var folder = AssetPathUtility.EnsureFolder(parent, name);
            var copy = ExpressionSetUtility.Duplicate(set, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".asset"));
            var baseline = Variant != null ? Instantiate(Variant) : CreateInstance<FaceVariant>();
            baseline.name = name + "_ベース顔";
            AssetDatabase.CreateAsset(baseline, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + baseline.name + ".asset"));
            foreach (var replacement in baseline.overrides.Where(o => o.clip != null))
            {
                var clip = Instantiate(replacement.clip);
                AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + AssetPathUtility.SafeFileName(clip.name) + ".anim"));
                replacement.clip = clip;
            }
            FaceVariantUtility.DetectBaseFace(baseline, copy, AvatarRoot);
            baseline.useCapturedValues = true;
            Undo.RecordObject(_avatar, "別のベース顔の表情設定に切り替え");
            _avatar.expressionSet = copy; _avatar.faceVariant = baseline;
            EditorUtility.SetDirty(_avatar); EditorUtility.SetDirty(baseline); AssetDatabase.SaveAssets();
            MarkLibraryDirty(); DisposePreview(); InvalidateDetailPreview();
        }

        private readonly HashSet<string> _batchExpressions = new HashSet<string>();
        private void DrawBatchBaseCorrections(ExpressionSet set)
        {
            if (Variant == null || Variant.baseFace.Count == 0) return;
            EditorGUILayout.Space(); EditorGUILayout.LabelField("ベース顔の一括補正", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全表情を選択")) foreach (var e in set.expressions) _batchExpressions.Add(e.id);
                if (GUILayout.Button("選択解除")) _batchExpressions.Clear();
            }
            foreach (var e in set.expressions)
            { var on = EditorGUILayout.ToggleLeft(e.name, _batchExpressions.Contains(e.id)); if (on) _batchExpressions.Add(e.id); else _batchExpressions.Remove(e.id); }
            foreach (var key in Variant.baseFace.Where(k => k.enabled))
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(key.blendShape);
                    using (new EditorGUI.DisabledScope(_batchExpressions.Count == 0))
                    {
                        if (GUILayout.Button("反映", GUILayout.Width(45))) BatchCorrection(set, key, true);
                        if (GUILayout.Button("除外", GUILayout.Width(45))) BatchCorrection(set, key, false);
                    }
                }
        }
        private void BatchCorrection(ExpressionSet set, BaseFaceKey key, bool apply)
        {
            Undo.RecordObject(Variant, "ベース顔を一括補正");
            foreach (var expression in set.expressions.Where(e => _batchExpressions.Contains(e.id)))
            {
                if (FaceVariantUtility.EffectiveOverride(Variant, expression) != null) continue;
                var exclusion = Variant.baseFaceExclusions.Find(e => e.expressionId == expression.id);
                if (exclusion == null) { exclusion = new BaseFaceExclusion { expressionId = expression.id }; Variant.baseFaceExclusions.Add(exclusion); }
                exclusion.keys.Remove(key.Key); if (!apply) exclusion.keys.Add(key.Key);
                Variant.FindFaceValues(expression.id)?.values.RemoveAll(v => v.path == key.path && v.blendShape == key.blendShape);
            }
            EditorUtility.SetDirty(Variant); InvalidateDetailPreview();
        }
    }
}
