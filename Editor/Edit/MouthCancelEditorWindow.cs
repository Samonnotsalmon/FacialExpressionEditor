using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal sealed class MouthCancelEditorWindow : EditorWindow
    {
        [SerializeField] private FacialExpressionAvatar _avatar;
        private Vector2 _scroll;
        private FacePreview _preview;
        private Texture2D _texture;
        private AnimationClip _shownClip;
        private int _shownDirty = -1;
        private ExpressionSet Set => _avatar != null ? _avatar.expressionSet : null;
        private GameObject Root => AvatarSetup.AvatarRootOf(_avatar);

        public static void Open(FacialExpressionAvatar avatar)
        {
            if (avatar == null || avatar.expressionSet == null || avatar.expressionSet.mouthCancelAnimation == null) return;
            ExpressionSetUtility.MakeMouthCancelEditable(avatar.expressionSet);
            var window = GetWindow<MouthCancelEditorWindow>();
            window.DisposePreview();
            window._avatar = avatar;
            window.titleContent = new GUIContent("口キャンセルの編集");
            window.minSize = new Vector2(660, 360);
            window.Show();
        }

        private void OnEnable() => Undo.undoRedoPerformed += Refresh;
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Refresh;
            DisposePreview();
        }
        private void OnLostFocus()
        {
            if (Set != null && Set.mouthCancelAnimation != null) AssetDatabase.SaveAssetIfDirty(Set.mouthCancelAnimation);
        }
        private void Refresh() { DisposePreview(); Repaint(); }
        private void DisposePreview()
        {
            _preview?.Dispose(); _preview = null;
            if (_texture != null) DestroyImmediate(_texture);
            _texture = null;
            _shownClip = null;
        }

        private void OnGUI()
        {
            if (Set == null || Set.mouthCancelAnimation == null || Root == null)
            {
                EditorGUILayout.HelpBox("アバターと口キャンセルのクリップを指定してください。", MessageType.Info);
                return;
            }
            var clip = Set.mouthCancelAnimation;
            EditorGUILayout.HelpBox("この表情設定専用の口キャンセルを編集します。話している間に使う0秒時点の値を変更します。複数の項目をチェックすると、それぞれ0で追加されます。既存項目の調整値は保持します。リップシンク用シェイプキーは対象外です。", MessageType.Info);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("クリップ", clip, typeof(AnimationClip), false);
            if (GUILayout.Button("表示を更新")) Refresh();
            var pickerRect = GUILayoutUtility.GetRect(new GUIContent("0にするシェイプキーを選ぶ…"), GUI.skin.button);
            if (GUI.Button(pickerRect, "0にするシェイプキーを選ぶ…"))
            {
                PopupWindow.Show(pickerRect, new BlendShapePicker(Root,
                    (path, name) => Set != null && Set.mouthCancelAnimation != null &&
                        AnimationUtility.GetEditorCurve(Set.mouthCancelAnimation, ClipEditing.BlendShape(path, name)) != null,
                    (path, name, on) =>
                    {
                        if (Set == null || Root == null || Set.mouthCancelAnimation == null) return;
                        SetShapeSelected(Set, Root, ClipEditing.BlendShape(path, name), on);
                        Refresh();
                    },
                    (path, name) => Root != null && !LipSyncProtection.IsProtected(ClipEditing.BlendShape(path, name), Root, Set),
                    (path, name) => BaseFaceHighlight(_avatar != null ? _avatar.faceVariant : null, path, name)));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var rect = GUILayoutUtility.GetRect(240, 240, GUILayout.Width(240), GUILayout.Height(240));
                if (Event.current.type == EventType.Repaint)
                {
                    if (_preview == null || _preview.Source != Root) { DisposePreview(); _preview = new FacePreview(Root, Set); }
                    if (_texture == null || _shownClip != clip || _shownDirty != EditorUtility.GetDirtyCount(clip))
                    {
                        // Apply only bindings actually used by the runtime mouth canceler.
                        var sampled = new AnimationClip();
                        try
                        {
                            foreach (var binding in Bindings(clip))
                                AnimationUtility.SetEditorCurve(sampled, binding, AnimationCurve.Constant(0, 0, AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0)));
                            _preview.Apply(sampled, 0);
                            if (_texture != null) DestroyImmediate(_texture);
                            _texture = _preview.RenderStatic(256);
                            _shownClip = clip; _shownDirty = EditorUtility.GetDirtyCount(clip);
                        }
                        finally { DestroyImmediate(sampled); }
                    }
                    GUI.DrawTexture(rect, _texture, ScaleMode.ScaleToFit);
                }
                using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
                {
                    _scroll = scroll.scrollPosition;
                    var bindings = Bindings(clip);
                    if (bindings.Length == 0) EditorGUILayout.HelpBox("シェイプキー未選択です。「0にするシェイプキーを選ぶ…」から戻したい口の項目を追加してください。", MessageType.Info);
                    foreach (var binding in bindings)
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        var value = curve.Evaluate(0);
                        EditorGUILayout.LabelField(new GUIContent(binding.propertyName.Substring("blendShape.".Length), binding.path));
                        EditorGUI.BeginChangeCheck();
                        var next = EditorGUILayout.Slider(value, 0, 100);
                        if (!EditorGUI.EndChangeCheck()) continue;
                        clip = ExpressionSetUtility.MakeMouthCancelEditable(Set);
                        SetValue(clip, binding, next);
                        Repaint();
                    }
                }
            }
        }

        internal static string BaseFaceHighlight(FaceVariant variant, string path, string name)
        {
            var key = variant != null ? variant.baseFace.Find(k => k.enabled && k.path == path && k.blendShape == name) : null;
            if (key == null || Mathf.Abs(key.variantValue - key.referenceValue) < 0.01f) return null;
            return $"ベース顔で変更：{key.referenceValue:0.##} → {key.variantValue:0.##}\n{path}";
        }

        internal static void SetShapeSelected(ExpressionSet set, GameObject root, EditorCurveBinding binding, bool selected)
        {
            if (LipSyncProtection.IsProtected(binding, root, set) || !AnimationUtility.GetFloatValue(root, binding, out _)) return;
            var clip = ExpressionSetUtility.MakeMouthCancelEditable(set);
            if (clip == null) return;
            if (selected)
            {
                if (AnimationUtility.GetEditorCurve(clip, binding) == null) SetValue(clip, binding, 0);
            }
            else
            {
                Undo.RecordObject(clip, "口キャンセルの対象を外す");
                AnimationUtility.SetEditorCurve(clip, binding, null);
                EditorUtility.SetDirty(clip);
            }
            AssetDatabase.SaveAssetIfDirty(clip);
        }

        internal static void SetValue(AnimationClip clip, EditorCurveBinding binding, float value)
        {
            Undo.RecordObject(clip, "口キャンセルの値を変更");
            var curve = AnimationUtility.GetEditorCurve(clip, binding) ?? new AnimationCurve();
            // Keep the source animation's later keys; runtime samples only time zero.
            var index = System.Array.FindIndex(curve.keys, k => k.time == 0);
            if (index >= 0) { var key = curve.keys[index]; key.value = value; curve.MoveKey(index, key); }
            else curve.AddKey(new Keyframe(0, value));
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            EditorUtility.SetDirty(clip);
        }

        private EditorCurveBinding[] Bindings(AnimationClip clip) => AnimationUtility.GetCurveBindings(clip)
            .Where(b => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape.") &&
                !LipSyncProtection.IsProtected(b, Root, Set) && AnimationUtility.GetFloatValue(Root, b, out _)).ToArray();
    }
}
