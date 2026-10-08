using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情の編集ウィンドウでパーツ（汗・涙・頬染めなど）を開いたとき。パーツのクリップを、表情と同じタブで編集する。
    /// - パーツは今の表情に重ねて出すものなので、ベース顔や顔バリアントの差し替えは通さず、クリップをそのまま重ねて見せる
    /// - 作者のクリップは、最初に編集したときに 表情データのフォルダ/Parts/ に複製して編集する
    /// - 編集したプロパティは、パーツとして動かすプロパティに加える（クリップから外したものは除く）
    /// </summary>
    internal partial class ExpressionClipEditorWindow
    {
        private Expression _partExpression;

        // 開いているパーツ（パーツを開いていなければ null）。
        private FacialPart Part => !IsAfk && Set != null ? Set.parts.Find(p => p.id == _expressionId) : null;
        private bool IsPart => Part != null;

        public static void OpenPart(FacialExpressionAvatar avatar, FacialPart part)
        {
            var window = GetWindow<ExpressionClipEditorWindow>();
            window.titleContent = new GUIContent("表情の編集");
            window.minSize = new Vector2(900, 560);
            window.SetTarget(avatar, part?.id);
            window.Show();
        }

        // パーツを表情の代わりに扱うためのもの（クリップはパーツのクリップ）。
        private Expression PartExpression(FacialPart part)
        {
            _partExpression ??= new Expression();
            _partExpression.id = part.id;
            _partExpression.name = part.name;
            _partExpression.clip = part.clip;
            return _partExpression;
        }

        /// <summary>
        /// プレビューで見せるクリップ（使い終わったら PreviewClips.Release）。表情はビルドと同じく差し替えとベース顔を通し、パーツはそのまま重ねる。
        /// </summary>
        private AnimationClip PreviewClipFor(Expression expression)
        {
            if (!IsPart) return PreviewClips.ForExpression(expression, Variant, AvatarRoot);

            var clip = expression.clip != null ? Instantiate(expression.clip) : new AnimationClip();
            clip.hideFlags = HideFlags.HideAndDontSave;
            return clip;
        }

        private AnimationClip PreparePartClip(FacialPart part)
        {
            if (part.clip != null && ExpressionSetUtility.OwnsPartClip(Set, part.clip)) return part.clip;

            var original = part.clip;
            var copy = ExpressionSetUtility.MakePartClipEditable(Set, part);
            if (copy != null && original != null)
            {
                ShowNotification(new GUIContent($"「{original.name}」を複製して編集します"), 3);
            }
            return copy;
        }

        private void DrawPartTargetInfo(FacialPart part)
        {
            EditorGUILayout.HelpBox($"パーツ「{part.name}」を編集しています。ゲーム中にメニューで、今の表情に重ねて出します。", MessageType.Info);
            if (part.clip != null && !ExpressionSetUtility.OwnsPartClip(Set, part.clip))
            {
                EditorGUILayout.LabelField("作者のクリップは、最初に編集したときに複製して編集します（元のファイルは変わりません）。",
                    EditorStyles.wordWrappedMiniLabel);
            }
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("クリップ", part.clip, typeof(AnimationClip), false);
            }
        }

        // クリップのプロパティ（パーツで使うキー → 値。オブジェクト参照は null）。
        private static Dictionary<string, float?> PartPropertyValues(AnimationClip clip)
        {
            var result = new Dictionary<string, float?>();
            if (clip == null) return result;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                result[ExpressionClipBuilder.Key(binding)] = curve != null && curve.length > 0 ? curve.keys[curve.length - 1].value : (float?)null;
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                result[ExpressionClipBuilder.Key(binding)] = null;
            }
            return result;
        }

        /// <summary>
        /// 編集に合わせて、パーツとして動かすプロパティを直す。値を変えたもの・足したものは加え、クリップから外したものは除く。
        /// </summary>
        private void SyncPartProperties(FacialPart part, Dictionary<string, float?> before, AnimationClip clip)
        {
            var after = PartPropertyValues(clip);
            var changed = after
                .Where(p => !before.TryGetValue(p.Key, out var old) || old.HasValue != p.Value.HasValue ||
                            old.HasValue && Mathf.Abs(old.Value - p.Value.Value) > 0.0001f)
                .Select(p => p.Key)
                .Where(k => !part.properties.Contains(k))
                .ToList();
            var removed = part.properties.Where(k => !after.ContainsKey(k)).ToList();
            if (changed.Count == 0 && removed.Count == 0) return;

            Undo.RecordObject(Set, "パーツのプロパティを変更");
            part.properties.AddRange(changed);
            part.properties.RemoveAll(removed.Contains);
            EditorUtility.SetDirty(Set);
        }
    }
}
