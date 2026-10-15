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
        private bool _showStartReplacement;
        [SerializeField] private bool _showSetupCamera;

        private void DrawStartTargets()
        {
            if (_targetsAvatar != _startAvatar)
            {
                _targetsAvatar = _startAvatar;
                _startMeshPaths.Clear(); _startObjectPaths.Clear(); _startLayerNames.Clear();
                var face = AvatarSetup.FaceRenderer(_startAvatar);
                if (face != null) _startMeshPaths.Add(AnimationUtility.CalculateTransformPath(face.transform, _startAvatar.transform));
                var d = _startAvatar.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                _startLayerNames.AddRange(FxImporter.PeekGestures(d).layers);
                _startPreviewHeight = 0.03f; _startPreviewZoom = 1f;
            }
            DrawSetupCamera(_startAvatar, ref _startPreviewHeight, ref _startPreviewZoom);
            DrawTargets(_startAvatar, _startMeshPaths, _startObjectPaths);
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

        }

        private static void DrawTargets(GameObject root, List<string> meshes, List<string> objects)
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
            var meshes = set.faceMeshPaths.ToList(); var objects = set.linkedObjectPaths.ToList();
            DrawTargets(AvatarRoot, meshes, objects);
            if (!meshes.SequenceEqual(set.faceMeshPaths) || !objects.SequenceEqual(set.linkedObjectPaths))
                Modify(set, "アバターの編集対象", () => { set.faceMeshPaths = meshes; set.linkedObjectPaths = objects; });
            EditorGUILayout.Space();
            DrawReplacementLayers(set);
            EditorGUILayout.LabelField("元メニューの編集はModular Avatarで行ってください。", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
        }

        private bool _showReplacementLayers;
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
