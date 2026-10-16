using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal sealed class BlinkPoseEditorWindow : EditorWindow
    {
        [SerializeField] private FacialExpressionAvatar _avatar;
        private FacePreview _preview;
        private AnimationClip _loop;
        private AnimationClip _source;
        private int _dirty = -1;
        private bool _playing;
        private double _started;
        private Vector2 _scroll;
        private ExpressionSet Set => _avatar != null ? _avatar.expressionSet : null;
        private GameObject Root => AvatarSetup.AvatarRootOf(_avatar);
        public static void Open(FacialExpressionAvatar avatar)
        {
            if (avatar == null || avatar.expressionSet == null || AvatarSetup.AvatarRootOf(avatar) == null) return;
            BlinkPoseUtility.Editable(avatar.expressionSet, AvatarSetup.AvatarRootOf(avatar));
            var window = GetWindow<BlinkPoseEditorWindow>(); window.Release(); window._avatar = avatar; window._playing = false;
            window.titleContent = new GUIContent("目閉じポーズの編集"); window.minSize = new Vector2(720, 440); window.Show();
        }
        private void OnEnable() { EditorApplication.update += Tick; Undo.undoRedoPerformed += Refresh; }
        private void OnDisable() { EditorApplication.update -= Tick; Undo.undoRedoPerformed -= Refresh; Release(); }
        private void Tick() { if (_playing) Repaint(); }
        private void Refresh() { Release(); Repaint(); }
        private void Release()
        {
            _preview?.Dispose(); _preview = null;
            if (_loop != null) DestroyImmediate(_loop); _loop = null; _source = null;
        }
        private void OnGUI()
        {
            if (Set == null || Set.blinkPose == null || Root == null) { EditorGUILayout.HelpBox("表情エディタから開き直してください。", MessageType.Info); return; }
            EditorGUILayout.LabelField("目を閉じた状態を編集します。指定した項目だけでループ瞬きを生成します。", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("専用クリップ", Set.blinkPose, typeof(AnimationClip), false);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_playing ? "停止して目閉じを確認" : "瞬きを再生")) { _playing = !_playing; _started = EditorApplication.timeSinceStartup - 2.3; }
                if (GUILayout.Button("表示を更新")) Refresh();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var rect = GUILayoutUtility.GetRect(280, 280, GUILayout.Width(280), GUILayout.Height(280));
                if (Event.current.type == EventType.Repaint)
                {
                    if (_preview == null || _preview.Source != Root) { Release(); _preview = new FacePreview(Root, Set); }
                    if (_loop == null || _source != Set.blinkPose || _dirty != EditorUtility.GetDirtyCount(Set.blinkPose))
                    {
                        if (_loop != null) DestroyImmediate(_loop);
                        _loop = BlinkPoseUtility.Loop(Set.blinkPose, Root, Set); _source = Set.blinkPose; _dirty = EditorUtility.GetDirtyCount(_source);
                    }
                    // The loop has exactly the same curves as the build. Sample its first closure for pose preview.
                    var time = _playing ? (float)((EditorApplication.timeSinceStartup - _started) % 10) : 2.46f;
                    _preview.Apply(_loop, _loop.length > 0 ? time / _loop.length : 0);
                    var texture = _preview.RenderStatic(384);
                    try { GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit); } finally { DestroyImmediate(texture); }
                }
                using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
                {
                    _scroll = scroll.scrollPosition;
                    var add = GUILayoutUtility.GetRect(new GUIContent("＋ シェイプキーを選ぶ"), GUI.skin.button);
                    if (GUI.Button(add, "＋ シェイプキーを選ぶ"))
                        PopupWindow.Show(add, new BlendShapePicker(Root,
                            (path, name) => Set != null && Set.blinkPose != null && AnimationUtility.GetEditorCurve(Set.blinkPose, ClipEditing.BlendShape(path, name)) != null,
                            (path, name, on) =>
                            {
                                if (Set == null || Root == null) return;
                                var binding = ClipEditing.BlendShape(path, name);
                                if (LipSyncProtection.IsProtected(binding, Root, Set)) return;
                                var clip = BlinkPoseUtility.Editable(Set, Root);
                                if (on) { if (AnimationUtility.GetEditorCurve(clip, binding) == null) MouthCancelEditing.SetValue(clip, binding, 100); }
                                else { Undo.RecordObject(clip, "目閉じの項目を外す"); AnimationUtility.SetEditorCurve(clip, binding, null); EditorUtility.SetDirty(clip); }
                                AssetDatabase.SaveAssetIfDirty(clip); Repaint();
                            }, (path, name) => Root != null && !LipSyncProtection.IsProtected(ClipEditing.BlendShape(path, name), Root, Set)));
                    var shapes = BlinkPoseUtility.Shapes(Set.blinkPose, Root, Set);
                    if (shapes.Count == 0) EditorGUILayout.HelpBox("目閉じに使うシェイプキーを選択してください。", MessageType.Info);
                    foreach (var shape in shapes)
                    {
                        EditorGUILayout.LabelField(new GUIContent(shape.blendShape, shape.path));
                        EditorGUI.BeginChangeCheck();
                        var value = EditorGUILayout.Slider(shape.closedValue, 0, 100);
                        if (EditorGUI.EndChangeCheck())
                        {
                            var clip = BlinkPoseUtility.Editable(Set, Root);
                            MouthCancelEditing.SetValue(clip, ClipEditing.BlendShape(shape.path, shape.blendShape), value);
                            AssetDatabase.SaveAssetIfDirty(clip); Repaint();
                        }
                    }
                }
            }
        }
    }
}
