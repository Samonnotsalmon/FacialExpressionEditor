using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    // 合成結果はこのベース顔専用の独立クリップとして保存する。素材は変更しない。
    internal sealed class ExpressionComposerWindow : EditorWindow
    {
        private ExpressionSet _set;
        private readonly List<AnimationClip> _clips = new List<AnimationClip>();
        private readonly List<HashSet<string>> _excluded = new List<HashSet<string>>();
        private readonly List<bool> _expanded = new List<bool>();
        private string _name = "新しい合成表情";
        private Vector2 _scroll;

        public static void Open(ExpressionSet set, AnimationClip first)
        {
            var w = CreateInstance<ExpressionComposerWindow>();
            w._set = set;
            w.Add(first); w.Add(null);
            w.titleContent = new GUIContent("表情を合成");
            w.minSize = new Vector2(480, 360); w.Show();
        }
        private void Add(AnimationClip clip) { _clips.Add(clip); _excluded.Add(new HashSet<string>()); _expanded.Add(false); }

        private void OnGUI()
        {
            if (_set == null) { EditorGUILayout.HelpBox("表情データを選び直してください。", MessageType.Info); return; }
            _name = EditorGUILayout.TextField("新しい表情の名前", _name);
            EditorGUILayout.HelpBox("目元・口元などをD&Dしてください。同じ項目は下の素材を優先します。不要な項目はチェックを外せます。動く素材は元の時間軸を保持します。", MessageType.Info);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < _clips.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var next = (AnimationClip)EditorGUILayout.ObjectField($"素材 {i + 1}", _clips[i], typeof(AnimationClip), false);
                    if (next != _clips[i]) { _clips[i] = next; _excluded[i].Clear(); }
                    if (GUILayout.Button("削除", GUILayout.Width(48)))
                    { _clips.RemoveAt(i); _excluded.RemoveAt(i); _expanded.RemoveAt(i); GUIUtility.ExitGUI(); }
                }
                var clip = _clips[i]; if (clip == null) continue;
                _expanded[i] = EditorGUILayout.Foldout(_expanded[i], "使用するプロパティ", true);
                if (!_expanded[i]) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                {
                    var key = ExpressionClipBuilder.Key(b);
                    var on = EditorGUILayout.ToggleLeft($"{b.path} / {b.propertyName}", !_excluded[i].Contains(key));
                    if (on) _excluded[i].Remove(key); else _excluded[i].Add(key);
                }
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("＋ 素材を追加")) Add(null);
            var moving = _clips.Where(c => c != null && PreviewClips.IsTimeVarying(c)).ToList();
            var incompatible = moving.Count > 1 && moving.Any(c => Mathf.Abs(c.length - moving[0].length) > .001f || c.isLooping != moving[0].isLooping);
            if (incompatible) EditorGUILayout.HelpBox("動く素材の長さ／ループ設定が異なります。時間を引き伸ばさず合成するため、外部エディタで揃えてください。", MessageType.Warning);
            using (new EditorGUI.DisabledScope(incompatible || !_clips.Any(c => c != null) || string.IsNullOrWhiteSpace(_name)))
            if (GUILayout.Button("合成してライブラリへ追加", GUILayout.Height(30)))
            {
                var clip = Compose(_clips, _excluded);
                clip.name = _name.Trim();
                var folder = AssetPathUtility.EnsureFolder(AssetPathUtility.FolderOf(_set), "Expressions");
                AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{AssetPathUtility.SafeFileName(clip.name)}.anim"));
                Undo.RecordObject(_set, "合成表情を追加");
                _set.expressions.Add(new Expression { name = clip.name, clip = clip });
                EditorUtility.SetDirty(_set); AssetDatabase.SaveAssets(); Close();
            }
        }

        internal static AnimationClip Compose(IList<AnimationClip> clips, IList<HashSet<string>> excluded)
        {
            var result = new AnimationClip();
            var master = clips.FirstOrDefault(c => c != null && PreviewClips.IsTimeVarying(c)) ?? clips.First(c => c != null);
            for (var i = 0; i < clips.Count; i++)
            {
                if (clips[i] == null) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(clips[i]))
                    if (!excluded[i].Contains(ExpressionClipBuilder.Key(b))) AnimationUtility.SetEditorCurve(result, b, AnimationUtility.GetEditorCurve(clips[i], b));
                foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clips[i]))
                    if (!excluded[i].Contains(ExpressionClipBuilder.Key(b))) AnimationUtility.SetObjectReferenceCurve(result, b, AnimationUtility.GetObjectReferenceCurve(clips[i], b));
            }
            result.frameRate = master.frameRate;
            AnimationUtility.SetAnimationClipSettings(result, AnimationUtility.GetAnimationClipSettings(master));
            return result;
        }
    }
}
