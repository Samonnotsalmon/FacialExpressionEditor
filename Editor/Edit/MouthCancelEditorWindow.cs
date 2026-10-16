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
            EditorGUILayout.HelpBox("この表情設定専用の口キャンセルを編集します。話している間に使う0秒時点の値を変更します。リップシンク用シェイプキーは対象外です。", MessageType.Info);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("クリップ", clip, typeof(AnimationClip), false);
            if (GUILayout.Button("表示を更新")) Refresh();
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
                    if (bindings.Length == 0) EditorGUILayout.HelpBox("このアバターで使用できる口のシェイプキーがクリップにありません。", MessageType.Info);
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
