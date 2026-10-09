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
        [SerializeField] private bool _showSetupCamera;

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
            EditorGUILayout.LabelField("別のベース顔で作成", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("新規作成…", "現在のアバターの顔から、空の表情設定を作成します。")))
                {
                    BeginStart(AvatarRoot);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(new GUIContent("今の設定を複製…", "割り当てと編集済みクリップを引き継ぎ、現在のアバターの顔で別設定を作成します。"))) DuplicateFaceSetup(set);
            }
            _showSetupCamera = EditorGUILayout.Foldout(_showSetupCamera, "プレビューカメラ", true);
            if (_showSetupCamera)
            {
                var height = set.previewHeight; var zoom = set.previewZoom;
                DrawSetupCamera(AvatarRoot, ref height, ref zoom);
                if (height != set.previewHeight || zoom != set.previewZoom)
                {
                    Modify(set, "プレビューカメラの位置", () => { set.previewHeight = height; set.previewZoom = zoom; });
                    DisposePreview(); _thumbnails.Clear();
                }
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
            EditorGUILayout.Space();
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

    }
}
