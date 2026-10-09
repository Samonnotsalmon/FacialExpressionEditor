using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情の編集ウィンドウで「AFK」を開いたとき。元FXのAFKのクリップを、アニメーションウィンドウのようなタイムラインで見る。
    /// - 上に「秒:フレーム」の目盛り。クリック・ドラッグで時間を変える。ホイールで拡大・縮小、中ボタン（Alt+ドラッグ）で移動
    /// - 元のアニメーションの顔のシェイプキーは、キー（◆）と値の動きを灰色で出す（変えられない）
    /// - 青い行（ベース顔とその左右別）は、今の時間にキーを打つと「この顔だけの動き」になる（目を閉じている間はジト目を0、など）。
    ///   最初のキーは、今見えている動き（ベース顔を適用したもの）を元にする。元のクリップは変えない
    /// </summary>
    internal partial class ExpressionClipEditorWindow
    {
        private const float AfkRowHeight = 18f;
        private const float AfkRulerHeight = 20f;
        private const float AfkNameWidth = 150f;
        private const float AfkValueWidth = 52f;
        private const float AfkButtonWidth = 20f;
        private const float AfkColumnsWidth = AfkNameWidth + AfkValueWidth + AfkButtonWidth * 2 + 6;
        private const float AfkPadding = 8f;
        private const float AfkKeyHitRadius = 6f;
        private const float AfkScrollbarWidth = 15f;
        private const float AfkMajorTickPixels = 60f;
        private const float AfkMinorTickPixels = 7f;

        private static readonly Color AfkBlue = new Color(0.3f, 0.6f, 1f);
        // 灰色のキーと値の動き。ライトとダークのどちらのスキンでも見えるように。
        private static Color AfkKeyColor => EditorGUIUtility.isProSkin ? new Color(0.78f, 0.78f, 0.78f) : new Color(0.3f, 0.3f, 0.3f);
        private static Color AfkCurveFill => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.1f) : new Color(0f, 0f, 0f, 0.12f);
        private static readonly Color AfkBlueFill = new Color(0.3f, 0.6f, 1f, 0.3f);
        private static readonly Color AfkPlayheadColor = new Color(1f, 0.35f, 0.3f);
        private static Color AfkGridColor => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.06f);

        [SerializeField] private AnimationClip _afkClip;
        [SerializeField] private float _afkSeconds;
        [SerializeField] private float _afkViewStart;
        [SerializeField] private float _afkViewLength;
        [SerializeField] private Vector2 _afkScroll;

        private bool _afkPlaying;
        private double _afkPlayLast;

        private enum AfkDrag { None, Scrub, Pan, MoveKey }
        private AfkDrag _afkDrag;
        private AfkRow _afkDragRow;
        private int _afkDragKey;
        private Rect _afkArea;

        // 開いたときの、この顔だけのAFKの動き（「開いたときの状態に戻す」用）。
        private List<AfkClipCurves> _afkCurvesSnapshot;

        private bool IsAfk => _expressionId == FaceVariant.AfkId;

        public static void OpenAfk(FacialExpressionAvatar avatar)
        {
            var window = GetWindow<ExpressionClipEditorWindow>();
            window.titleContent = new GUIContent("表情の編集");
            window.minSize = new Vector2(900, 560);
            window.SetTarget(avatar, FaceVariant.AfkId);
            window.Show();
        }

        // ---- 元FXのAFK ----

        private OriginalFxAnalysis _afkAnalysis;
        private FacialExpressionAvatar _afkAnalysisFor;
        private Expression _afkExpression;

        // 元FXのAFKのレイヤーとクリップ。元FXを調べるので、アバターが変わったときだけ調べ直す。
        private OriginalFxAnalysis AfkAnalysis
        {
            get
            {
                if (_afkAnalysisFor == _avatar && _afkAnalysis != null) return _afkAnalysis;
                _afkAnalysisFor = _avatar;
                if (_avatar == null || Set == null) return _afkAnalysis = null;

                var defaults = AvatarFaceDefaults.Find(Descriptor, Set);
                var handled = defaults.MouthCancelerLayers.ToList();
                if (defaults.BlinkLayer != null) handled.Add(defaults.BlinkLayer);
                return _afkAnalysis = OriginalFxAnalysis.Analyze(Descriptor, Set, Variant, handled);
            }
        }

        // 見ているAFKのクリップ。選んでいなければ、いちばん長いもの（ループ）。
        private AnimationClip AfkClip
        {
            get
            {
                var analysis = AfkAnalysis;
                if (analysis == null) return null;
                return analysis.AfkClips.Contains(_afkClip) ? _afkClip : analysis.AfkPreviewClip;
            }
        }

        /// <summary>
        /// AFK を表情の代わりに扱うためのもの。元FXに顔を動かすAFKが無いか、顔バリアントが無ければ null。
        /// </summary>
        private Expression AfkExpression
        {
            get
            {
                var clip = Variant != null ? AfkClip : null;
                if (clip == null) return null;
                _afkExpression ??= new Expression { id = FaceVariant.AfkId, name = "AFK" };
                _afkExpression.clip = clip;
                return _afkExpression;
            }
        }

        private float AfkLength => AfkClip != null ? AfkClip.length : 0;
        private int AfkFps => AfkClip != null ? Mathf.Max(1, Mathf.RoundToInt(AfkClip.frameRate)) : 60;

        private float SnapAfk(float seconds)
        {
            return Mathf.Clamp(Mathf.Round(seconds * AfkFps) / AfkFps, 0, AfkLength);
        }

        // アニメーションウィンドウと同じ「秒:フレーム」。
        private static string FormatFrame(int frame, int fps)
        {
            return $"{frame / fps}:{frame % fps:00}";
        }

        private string FormatAfkTime(float seconds) => FormatFrame(Mathf.RoundToInt(seconds * AfkFps), AfkFps);

        // ---- 表示する値（ベース顔とこの顔だけの動きを適用したもの） ----

        // 行。見出し（Shape が無い）か、シェイプキー。
        private sealed class AfkRow
        {
            public string Label;
            public string Path;
            public string BlendShape;
            public bool Summary;
            public bool Editable;
            // 元のクリップのカーブ（キーの表示用）。元のクリップで動かしていなければ null。
            public AnimationCurve Original;

            public bool IsShape => BlendShape != null;
            public string Key => $"{Path}|{BlendShape}";
        }

        private AnimationClip _afkProcessed;
        private (FacialExpressionAvatar, AnimationClip, int) _afkProcessedOf;
        private readonly Dictionary<string, AnimationCurve> _afkShownCurves = new Dictionary<string, AnimationCurve>();
        private List<AfkRow> _afkRows;
        private List<float> _afkAllKeyTimes = new List<float>();

        private void InvalidateAfk()
        {
            _afkProcessedOf = default;
        }

        private void ReleaseAfk()
        {
            PreviewClips.Release(_afkProcessed);
            _afkProcessed = null;
            _afkShownCurves.Clear();
            _afkRows = null;
            _afkAllKeyTimes.Clear();
        }

        /// <summary>
        /// プレビューと行に使う、ベース顔とこの顔だけの動きを適用したクリップ。クリップか顔バリアントが変わったときだけ作り直す。
        /// </summary>
        private AnimationClip AfkProcessedClip()
        {
            var clip = AfkClip;
            var variant = Variant;
            var key = (_avatar, clip, (variant != null ? EditorUtility.GetDirtyCount(variant) : 0) + (Set != null ? EditorUtility.GetDirtyCount(Set) : 0));
            if (_afkProcessed != null && _afkProcessedOf.Equals(key)) return _afkProcessed;

            ReleaseAfk();
            _afkProcessedOf = key;
            if (clip == null || variant == null) return null;

            _afkProcessed = PreviewClips.ForAfk(clip, variant, AvatarRoot, Set);
            foreach (var binding in AnimationUtility.GetCurveBindings(_afkProcessed))
            {
                var blendShape = ClipEditing.BlendShapeName(binding);
                if (blendShape != null) _afkShownCurves[$"{binding.path}|{blendShape}"] = AnimationUtility.GetEditorCurve(_afkProcessed, binding);
            }
            _afkRows = BuildAfkRows(clip, variant);

            var fps = AfkFps;
            _afkAllKeyTimes = _afkRows.Where(r => r.IsShape)
                .SelectMany(r => (StoredAfkCurve(r) ?? r.Original)?.keys ?? new Keyframe[0])
                .Select(k => Mathf.RoundToInt(k.time * fps)).Distinct().OrderBy(f => f)
                .Select(f => (float)f / fps).ToList();
            return _afkProcessed;
        }

        private List<AfkRow> BuildAfkRows(AnimationClip clip, FaceVariant variant)
        {
            var blue = new List<AfkRow>();
            void AddBlue(string path, string blendShape)
            {
                if (blue.Any(r => r.Path == path && r.BlendShape == blendShape)) return;
                blue.Add(new AfkRow { Path = path, BlendShape = blendShape, Editable = true });
            }

            foreach (var key in variant.baseFace.Where(k => k.enabled))
            {
                var mesh = RendererAt(key.path)?.sharedMesh;
                if (mesh == null || mesh.GetBlendShapeIndex(key.blendShape) < 0) continue;
                AddBlue(key.path, key.blendShape);
                foreach (var side in BlendShapeList.SideVariantNames(mesh, key.blendShape)) AddBlue(key.path, side);
            }
            // ベース顔から外したシェイプキーでも、キーがあればビルドで使うので出す。
            var stored = variant.FindAfkCurves(clip);
            if (stored != null)
            {
                foreach (var curve in stored.curves) AddBlue(curve.path, curve.blendShape);
            }

            var analysis = AfkAnalysis;
            var blueKeys = new HashSet<string>(blue.Select(r => r.Key));
            var original = AnimationUtility.GetCurveBindings(clip)
                .Where(b => analysis.IsFaceCurve(b))
                .Select(b => new AfkRow { Path = b.path, BlendShape = ClipEditing.BlendShapeName(b), Original = AnimationUtility.GetEditorCurve(clip, b) })
                .Where(r => !blueKeys.Contains(r.Key))
                .ToList();
            foreach (var row in blue)
            {
                row.Original = AnimationUtility.GetEditorCurve(clip, ClipEditing.BlendShape(row.Path, row.BlendShape));
            }

            // 顔のメッシュ以外のシェイプキーは、メッシュのパスも出す。
            var facePath = Descriptor != null && Descriptor.VisemeSkinnedMesh != null
                ? PathOf(Descriptor.VisemeSkinnedMesh.transform, Root)
                : blue.Select(r => r.Path).FirstOrDefault();
            foreach (var row in blue.Concat(original)) row.Label = row.Path == facePath ? row.BlendShape : $"{row.Path}/{row.BlendShape}";

            var rows = new List<AfkRow> { new AfkRow { Label = "すべてのキー", Summary = true } };
            if (blue.Count > 0)
            {
                rows.Add(new AfkRow { Label = "ベース顔（この顔だけの動き）", Editable = true });
                rows.AddRange(blue);
            }
            if (original.Count > 0)
            {
                rows.Add(new AfkRow { Label = "元のアニメーション" });
                rows.AddRange(original);
            }
            return rows;
        }

        private SkinnedMeshRenderer RendererAt(string path)
        {
            var transform = string.IsNullOrEmpty(path) ? Root : Root.Find(path);
            return transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
        }

        private float CurrentWeight(string path, string blendShape)
        {
            var renderer = RendererAt(path);
            var index = renderer != null && renderer.sharedMesh != null ? renderer.sharedMesh.GetBlendShapeIndex(blendShape) : -1;
            return index >= 0 ? renderer.GetBlendShapeWeight(index) : 0;
        }

        private float ShownAfkValue(AfkRow row)
        {
            return _afkShownCurves.TryGetValue(row.Key, out var curve) && curve.length > 0
                ? curve.Evaluate(_afkSeconds)
                : CurrentWeight(row.Path, row.BlendShape);
        }

        // ---- 左側（プレビューの下） ----

        private void DrawAfkTargetInfo()
        {
            EditorGUILayout.HelpBox("元FXのAFKのアニメーションを見ています。青い行で今の時間にキーを打つと、この顔だけの動きになります" +
                                    "（目を閉じている間はベース顔を0にする、など）。元のアニメーションは変わりません。", MessageType.Info);
            EditorGUILayout.LabelField("タイムラインのクリック・ドラッグで時間を変えます。ホイールで拡大・縮小、中ボタン（Alt+ドラッグ）で移動。" +
                                       "「,」「.」で1フレーム、Alt+「,」「.」で前後のキー、Space で再生。青いキーはドラッグで動かせ、右クリックで削除できます。",
                EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("AFKのクリップ", AfkClip, typeof(AnimationClip), false);
            }
        }

        // ---- タイムライン ----

        private void DrawAfkTimeline()
        {
            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            var clip = AfkClip;
            AfkProcessedClip();
            if (clip == null || _afkRows == null) return;
            ClampAfkView();
            HandleAfkShortcuts();
            DrawAfkToolbar(clip);

            var area = GUILayoutUtility.GetRect(0, 100000, 0, 100000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Layout) area = _afkArea;
            else _afkArea = area;
            if (area.width <= AfkColumnsWidth + AfkPadding * 4) return;

            var rowsRect = new Rect(area.x, area.y + AfkRulerHeight, area.width, area.height - AfkRulerHeight - AfkScrollbarWidth);
            var contentHeight = _afkRows.Count * AfkRowHeight;
            var vertical = contentHeight > rowsRect.height;
            var timeline = new Rect(area.x + AfkColumnsWidth, area.y, area.width - AfkColumnsWidth - (vertical ? AfkScrollbarWidth : 0),
                AfkRulerHeight + rowsRect.height);
            // 0秒とクリップの最後のキーが端で切れないよう、左右を少し空ける。
            var x0 = timeline.x + AfkPadding;
            var width = timeline.width - AfkPadding * 2;

            HandleAfkTimelineInput(controlId, timeline, rowsRect, x0, width);
            DrawAfkRuler(new Rect(timeline.x, area.y, timeline.width, AfkRulerHeight), x0, width);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(area.x, area.y, AfkColumnsWidth, AfkRulerHeight), new Color(0, 0, 0, 0.25f));
            }
            GUI.Label(new Rect(area.x + 4, area.y + 2, AfkColumnsWidth - 8, AfkRulerHeight - 4), "秒:フレーム", EditorStyles.miniLabel);

            var contentWidth = rowsRect.width - (vertical ? AfkScrollbarWidth : 0);
            _afkScroll = GUI.BeginScrollView(rowsRect, _afkScroll, new Rect(0, 0, contentWidth, contentHeight), false, false,
                GUIStyle.none, vertical ? GUI.skin.verticalScrollbar : GUIStyle.none);
            {
                // スクロールの中の座標（左上が 0,0）。
                var cx0 = x0 - rowsRect.x;
                var ctimeline = new Rect(timeline.x - rowsRect.x, 0, timeline.width, AfkRowHeight);
                var first = Mathf.Max(0, Mathf.FloorToInt(_afkScroll.y / AfkRowHeight));
                var last = Mathf.Min(_afkRows.Count - 1, Mathf.CeilToInt((_afkScroll.y + rowsRect.height) / AfkRowHeight));
                for (var r = first; r <= last; r++)
                {
                    var rect = new Rect(0, r * AfkRowHeight, contentWidth, AfkRowHeight);
                    ctimeline.y = rect.y;
                    DrawAfkRow(rect, ctimeline, _afkRows[r], cx0, width);
                }

                if (Event.current.type == EventType.Repaint)
                {
                    var visible = new Rect(ctimeline.x, _afkScroll.y, ctimeline.width, rowsRect.height);
                    foreach (var (x, _, major) in AfkTicks(cx0, width))
                    {
                        if (major && x >= visible.x && x <= visible.xMax) EditorGUI.DrawRect(new Rect(x, visible.y, 1, visible.height), AfkGridColor);
                    }
                    var playhead = AfkTimeToX(_afkSeconds, cx0, width);
                    if (playhead >= visible.x && playhead <= visible.xMax)
                    {
                        EditorGUI.DrawRect(new Rect(playhead, visible.y, 1, visible.height), AfkPlayheadColor);
                    }
                }
            }
            GUI.EndScrollView();

            EditorGUI.BeginChangeCheck();
            var start = GUI.HorizontalScrollbar(new Rect(timeline.x, rowsRect.yMax, timeline.width, AfkScrollbarWidth),
                _afkViewStart, _afkViewLength, 0, AfkLength);
            if (EditorGUI.EndChangeCheck())
            {
                _afkViewStart = start;
                ClampAfkView();
            }
        }

        // アニメーションウィンドウと同じアイコン（初めて描くときに読み込む）。
        private static class AfkIcons
        {
            public static readonly GUIContent First = EditorGUIUtility.TrIconContent("Animation.FirstKey", "先頭へ");
            public static readonly GUIContent PrevKey = EditorGUIUtility.TrIconContent("Animation.PrevKey", "前のキーへ（Alt+,）");
            public static readonly GUIContent Play = EditorGUIUtility.TrIconContent("Animation.Play", "再生（Space）");
            public static readonly GUIContent NextKey = EditorGUIUtility.TrIconContent("Animation.NextKey", "次のキーへ（Alt+.）");
            public static readonly GUIContent Last = EditorGUIUtility.TrIconContent("Animation.LastKey", "最後へ");
        }

        private void DrawAfkToolbar(AnimationClip clip)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var clips = AfkAnalysis.AfkClips;
                var index = clips.IndexOf(clip);
                var next = EditorGUILayout.Popup(index, clips.Select(c => c.name).ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(200));
                if (next != index && next >= 0) SelectAfkClip(clips[next]);

                GUILayout.Space(4);
                if (GUILayout.Button(AfkIcons.First, EditorStyles.toolbarButton)) SetAfkTime(0);
                if (GUILayout.Button(AfkIcons.PrevKey, EditorStyles.toolbarButton)) JumpAfkKey(-1);
                var playing = GUILayout.Toggle(_afkPlaying, AfkIcons.Play, EditorStyles.toolbarButton);
                if (playing != _afkPlaying) ToggleAfkPlay();
                if (GUILayout.Button(AfkIcons.NextKey, EditorStyles.toolbarButton)) JumpAfkKey(1);
                if (GUILayout.Button(AfkIcons.Last, EditorStyles.toolbarButton)) SetAfkTime(AfkLength);

                // アニメーションウィンドウと同じく、フレーム番号で指定する。
                var fps = AfkFps;
                EditorGUI.BeginChangeCheck();
                var frame = EditorGUILayout.IntField(Mathf.RoundToInt(_afkSeconds * fps), EditorStyles.toolbarTextField, GUILayout.Width(48));
                if (EditorGUI.EndChangeCheck()) SetAfkTime((float)frame / fps);
                GUILayout.Label($"{FormatAfkTime(_afkSeconds)} / {FormatAfkTime(AfkLength)}（{fps}fps）", EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("全体を表示", EditorStyles.toolbarButton))
                {
                    _afkViewStart = 0;
                    _afkViewLength = AfkLength;
                }
            }
        }

        private void DrawAfkRow(Rect rect, Rect timeline, AfkRow row, float x0, float width)
        {
            var repaint = Event.current.type == EventType.Repaint;
            if (!row.IsShape && !row.Summary)
            {
                if (repaint)
                {
                    EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.2f));
                    if (row.Editable) EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4, rect.height), AfkBlue);
                }
                GUI.Label(new Rect(rect.x + 8, rect.y, AfkColumnsWidth - 8, rect.height), row.Label, EditorStyles.miniBoldLabel);
                return;
            }

            if (row.Summary)
            {
                if (repaint) EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.12f));
                GUI.Label(new Rect(rect.x + 8, rect.y, AfkColumnsWidth - 8, rect.height), row.Label, EditorStyles.miniLabel);
                if (repaint) DrawAfkKeys(timeline, _afkAllKeyTimes, AfkKeyColor, x0, width);
                return;
            }

            var stored = StoredAfkCurve(row);
            var value = ShownAfkValue(row);
            if (repaint && row.Editable) EditorGUI.DrawRect(rect, stored != null ? FaceValueColor : BaseFaceColor);

            var tooltip = $"{row.Path}/{row.BlendShape}";
            if (row.Editable)
            {
                // 名前を左右にドラッグしても値を変えられる（インスペクターと同じ）。
                var labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = AfkNameWidth - 8;
                EditorGUI.BeginChangeCheck();
                var next = EditorGUI.FloatField(new Rect(rect.x + 8, rect.y + 1, AfkNameWidth + AfkValueWidth - 8, rect.height - 2),
                    new GUIContent(row.Label, tooltip), value);
                EditorGUIUtility.labelWidth = labelWidth;
                if (EditorGUI.EndChangeCheck()) SetAfkKey(row, Mathf.Clamp(next, 0, 100));

                var x = rect.x + AfkNameWidth + AfkValueWidth + 2;
                var keyIndex = stored != null ? FindAfkKey(stored, _afkSeconds) : -1;
                var keyRect = new Rect(x, rect.y + 1, AfkButtonWidth, rect.height - 2);
                var keyTooltip = new GUIContent("", keyIndex >= 0 ? "この時間のキーを削除" : "この時間にキーを打つ（今の値で）");
                if (GUI.Button(keyRect, keyTooltip, EditorStyles.miniButton))
                {
                    if (keyIndex >= 0) RemoveAfkKey(row, keyIndex);
                    else SetAfkKey(row, value);
                }
                if (repaint) DrawDiamond(keyRect.center, keyIndex >= 0 ? Diamond : DiamondOutline, keyIndex >= 0 ? AfkBlue : AfkKeyColor);
                if (stored != null &&
                    GUI.Button(new Rect(x + AfkButtonWidth + 2, rect.y + 1, AfkButtonWidth, rect.height - 2),
                        new GUIContent("×", "このクリップのキーをすべて削除（ベース顔に戻す）"), EditorStyles.miniButton))
                {
                    RemoveAfkCurve(row);
                }
            }
            else
            {
                GUI.Label(new Rect(rect.x + 8, rect.y, AfkNameWidth - 8, rect.height), new GUIContent(row.Label, tooltip), EditorStyles.miniLabel);
                GUI.Label(new Rect(rect.x + AfkNameWidth, rect.y, AfkValueWidth - 4, rect.height), value.ToString("0.#"), RightMiniLabel);
            }

            if (!repaint) return;
            if (_afkShownCurves.TryGetValue(row.Key, out var shown) && shown.length > 0)
            {
                DrawAfkCurveFill(timeline, shown, row.Editable ? AfkBlueFill : AfkCurveFill, x0, width);
            }
            var keys = stored ?? row.Original;
            if (keys != null) DrawAfkKeys(timeline, keys.keys.Select(k => k.time), stored != null ? AfkBlue : AfkKeyColor, x0, width);
        }

        private static GUIStyle _rightMiniLabel;
        private static GUIStyle RightMiniLabel => _rightMiniLabel ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };

        // 値の動きを、行の高さを100として塗る。
        private void DrawAfkCurveFill(Rect timeline, AnimationCurve curve, Color color, float x0, float width)
        {
            const float step = 3f;
            var from = Mathf.Max(timeline.x, AfkTimeToX(0, x0, width));
            var to = Mathf.Min(timeline.xMax, AfkTimeToX(AfkLength, x0, width));
            for (var x = from; x < to; x += step)
            {
                var value = Mathf.Clamp01(curve.Evaluate(AfkXToTime(x + step * 0.5f, x0, width)) / 100f);
                if (value < 0.01f) continue;
                var height = (timeline.height - 2) * value;
                EditorGUI.DrawRect(new Rect(x, timeline.yMax - 1 - height, Mathf.Min(step, to - x), height), color);
            }
        }

        private void DrawAfkKeys(Rect timeline, IEnumerable<float> times, Color color, float x0, float width)
        {
            foreach (var time in times)
            {
                var x = AfkTimeToX(time, x0, width);
                if (x >= timeline.x && x <= timeline.xMax) DrawDiamond(new Vector2(x, timeline.center.y), Diamond, color);
            }
        }

        private static void DrawDiamond(Vector2 center, Texture2D texture, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - 5.5f, center.y - 5.5f, 11, 11), texture);
            GUI.color = previous;
        }

        // キーの印（塗りつぶしと、枠だけ）。
        private static Texture2D _diamond;
        private static Texture2D _diamondOutline;
        private static Texture2D Diamond => _diamond != null ? _diamond : _diamond = CreateDiamond(false);
        private static Texture2D DiamondOutline => _diamondOutline != null ? _diamondOutline : _diamondOutline = CreateDiamond(true);

        private static Texture2D CreateDiamond(bool outline)
        {
            const int size = 11;
            const float center = (size - 1) / 2f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Mathf.Abs(x - center) + Mathf.Abs(y - center);
                    var alpha = Mathf.Clamp01(center + 0.5f - distance);
                    if (outline) alpha *= Mathf.Clamp01(distance - (center - 2.5f));
                    var shade = !outline && distance > center - 1.5f ? 0.55f : 1f;
                    texture.SetPixel(x, y, new Color(shade, shade, shade, alpha));
                }
            }
            texture.Apply();
            return texture;
        }

        private void DrawAfkRuler(Rect rect, float x0, float width)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.25f));
            foreach (var (x, frame, major) in AfkTicks(x0, width))
            {
                if (x < rect.x || x > rect.xMax) continue;
                var height = major ? rect.height * 0.5f : rect.height * 0.25f;
                EditorGUI.DrawRect(new Rect(x, rect.yMax - height, 1, height), new Color(1, 1, 1, major ? 0.5f : 0.25f));
                if (major) GUI.Label(new Rect(x + 2, rect.y, 60, rect.height * 0.6f), FormatFrame(frame, AfkFps), EditorStyles.miniLabel);
            }

            var playhead = AfkTimeToX(_afkSeconds, x0, width);
            if (playhead >= rect.x && playhead <= rect.xMax)
            {
                EditorGUI.DrawRect(new Rect(playhead, rect.y, 1, rect.height), AfkPlayheadColor);
                EditorGUI.DrawRect(new Rect(playhead - 3, rect.y, 7, 4), AfkPlayheadColor);
            }
        }

        /// <summary>
        /// 目盛り（x、フレーム、数字を出すか）。数字は約60ピクセルごと、細かい目盛りは約7ピクセルごと。
        /// </summary>
        private IEnumerable<(float x, int frame, bool major)> AfkTicks(float x0, float width)
        {
            var fps = AfkFps;
            var pixelsPerFrame = width / Mathf.Max(0.0001f, _afkViewLength * fps);
            var steps = new[] { 1, 2, 5, 10, fps % 2 == 0 ? fps / 2 : fps, fps, fps * 2, fps * 5, fps * 10, fps * 30, fps * 60 }
                .Distinct().OrderBy(s => s).ToList();
            var major = steps.FirstOrDefault(s => s * pixelsPerFrame >= AfkMajorTickPixels);
            if (major == 0) major = steps[steps.Count - 1];
            var minor = steps.FirstOrDefault(s => major % s == 0 && s * pixelsPerFrame >= AfkMinorTickPixels);
            if (minor == 0) minor = major;

            var first = Mathf.FloorToInt(_afkViewStart * fps / minor) * minor;
            var last = Mathf.CeilToInt((_afkViewStart + _afkViewLength) * fps);
            for (var frame = Mathf.Max(0, first); frame <= last; frame += minor)
            {
                yield return (AfkTimeToX((float)frame / fps, x0, width), frame, frame % major == 0);
            }
        }

        private float AfkTimeToX(float seconds, float x0, float width)
        {
            return x0 + (seconds - _afkViewStart) / Mathf.Max(0.0001f, _afkViewLength) * width;
        }

        private float AfkXToTime(float x, float x0, float width)
        {
            return _afkViewStart + (x - x0) / Mathf.Max(1f, width) * _afkViewLength;
        }

        private void ClampAfkView()
        {
            var length = AfkLength;
            if (_afkViewLength <= 0 || _afkViewLength > length) _afkViewLength = length;
            _afkViewLength = Mathf.Clamp(_afkViewLength, Mathf.Min(length, 10f / AfkFps), length);
            _afkViewStart = Mathf.Clamp(_afkViewStart, 0, length - _afkViewLength);
        }

        // ---- 操作 ----

        private void HandleAfkTimelineInput(int controlId, Rect timeline, Rect rowsRect, float x0, float width)
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.ScrollWheel when timeline.Contains(e.mousePosition):
                {
                    // マウスの下の時間を動かさずに拡大・縮小する。
                    var anchor = AfkXToTime(e.mousePosition.x, x0, width);
                    var ratio = (e.mousePosition.x - x0) / width;
                    _afkViewLength *= 1f + Mathf.Clamp(e.delta.y, -3, 3) * 0.08f;
                    ClampAfkView();
                    _afkViewStart = anchor - ratio * _afkViewLength;
                    ClampAfkView();
                    e.Use();
                    break;
                }
                case EventType.MouseDown when timeline.Contains(e.mousePosition):
                {
                    GUIUtility.keyboardControl = 0;
                    var row = e.mousePosition.y >= rowsRect.y ? AfkRowAt(e.mousePosition.y - rowsRect.y + _afkScroll.y) : null;
                    var keyIndex = row != null && row.Editable ? AfkKeyAt(StoredAfkCurve(row), e.mousePosition.x, x0, width) : -1;

                    if (e.button == 2 || (e.button == 0 && e.alt))
                    {
                        _afkDrag = AfkDrag.Pan;
                    }
                    else if (e.button == 1)
                    {
                        if (row != null && row.Editable && row.IsShape) ShowAfkKeyMenu(row, keyIndex, AfkXToTime(e.mousePosition.x, x0, width));
                        e.Use();
                        break;
                    }
                    else if (keyIndex >= 0)
                    {
                        _afkDrag = AfkDrag.MoveKey;
                        _afkDragRow = row;
                        _afkDragKey = keyIndex;
                        SetAfkTime(StoredAfkCurve(row)[keyIndex].time);
                    }
                    else
                    {
                        _afkDrag = AfkDrag.Scrub;
                        // 灰色のキーの近くをクリックしたら、そのキーの時間に合わせる。
                        var original = row?.Original;
                        var near = row != null && row.Summary
                            ? _afkAllKeyTimes.Where(t => Mathf.Abs(AfkTimeToX(t, x0, width) - e.mousePosition.x) <= AfkKeyHitRadius).ToList()
                            : original != null && !row.Editable
                                ? original.keys.Select(k => k.time).Where(t => Mathf.Abs(AfkTimeToX(t, x0, width) - e.mousePosition.x) <= AfkKeyHitRadius).ToList()
                                : new List<float>();
                        SetAfkTime(near.Count > 0 ? near[0] : AfkXToTime(e.mousePosition.x, x0, width));
                    }
                    GUIUtility.hotControl = controlId;
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                    switch (_afkDrag)
                    {
                        case AfkDrag.Pan:
                            _afkViewStart -= e.delta.x / width * _afkViewLength;
                            ClampAfkView();
                            break;
                        case AfkDrag.Scrub:
                            SetAfkTime(AfkXToTime(e.mousePosition.x, x0, width));
                            break;
                        case AfkDrag.MoveKey:
                            MoveAfkKey(_afkDragRow, AfkXToTime(e.mousePosition.x, x0, width));
                            break;
                    }
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    _afkDrag = AfkDrag.None;
                    _afkDragRow = null;
                    e.Use();
                    break;
            }
        }

        private AfkRow AfkRowAt(float contentY)
        {
            var index = Mathf.FloorToInt(contentY / AfkRowHeight);
            return index >= 0 && index < _afkRows.Count ? _afkRows[index] : null;
        }

        private int AfkKeyAt(AnimationCurve curve, float x, float x0, float width)
        {
            if (curve == null) return -1;
            for (var i = 0; i < curve.length; i++)
            {
                if (Mathf.Abs(AfkTimeToX(curve[i].time, x0, width) - x) <= AfkKeyHitRadius) return i;
            }
            return -1;
        }

        private void ShowAfkKeyMenu(AfkRow row, int keyIndex, float seconds)
        {
            var menu = new GenericMenu();
            if (keyIndex >= 0)
            {
                menu.AddItem(new GUIContent("キーを削除"), false, () => RemoveAfkKey(row, keyIndex));
            }
            else
            {
                var time = SnapAfk(seconds);
                menu.AddItem(new GUIContent($"ここ（{FormatAfkTime(time)}）にキーを打つ"), false, () =>
                {
                    SetAfkTime(time);
                    SetAfkKey(row, ShownAfkValue(row));
                });
            }
            if (StoredAfkCurve(row) != null)
            {
                menu.AddItem(new GUIContent("このクリップのキーをすべて削除"), false, () => RemoveAfkCurve(row));
            }
            menu.ShowAsContext();
        }

        private void HandleAfkShortcuts()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0) return;
            switch (e.keyCode)
            {
                case KeyCode.Comma:
                    if (e.alt) JumpAfkKey(-1);
                    else SetAfkTime(_afkSeconds - 1f / AfkFps);
                    e.Use();
                    break;
                case KeyCode.Period:
                    if (e.alt) JumpAfkKey(1);
                    else SetAfkTime(_afkSeconds + 1f / AfkFps);
                    e.Use();
                    break;
                case KeyCode.Space:
                    ToggleAfkPlay();
                    e.Use();
                    break;
            }
        }

        private void SelectAfkClip(AnimationClip clip)
        {
            _afkClip = clip;
            _afkViewLength = 0;
            _afkPlaying = false;
            SetAfkTime(_afkSeconds);
            InvalidateAfk();
        }

        /// <summary>
        /// 時間を変える（フレームに合わせる）。見えている範囲の外なら、範囲を動かす。
        /// </summary>
        private void SetAfkTime(float seconds)
        {
            _afkSeconds = SnapAfk(seconds);
            _afkPlaying = false;
            if (_afkSeconds < _afkViewStart) _afkViewStart = _afkSeconds;
            else if (_afkSeconds > _afkViewStart + _afkViewLength) _afkViewStart = _afkSeconds - _afkViewLength;
            ClampAfkView();
            _previewDirty = true;
            Repaint();
        }

        private void JumpAfkKey(int direction)
        {
            var epsilon = 0.5f / AfkFps;
            var target = direction < 0
                ? _afkAllKeyTimes.Where(t => t < _afkSeconds - epsilon).DefaultIfEmpty(0).Max()
                : _afkAllKeyTimes.Where(t => t > _afkSeconds + epsilon).DefaultIfEmpty(AfkLength).Min();
            SetAfkTime(target);
        }

        private void ToggleAfkPlay()
        {
            var play = !_afkPlaying;
            if (play && _afkSeconds >= AfkLength) _afkSeconds = 0;
            GUIUtility.keyboardControl = 0;
            _afkPlaying = play;
            _afkPlayLast = EditorApplication.timeSinceStartup;
        }

        // 再生中だけ、プレビューを描き直す（30fps 程度）。
        private void Update()
        {
            if (!_afkPlaying) return;
            if (!IsAfk || AfkClip == null)
            {
                _afkPlaying = false;
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var delta = (float)(now - _afkPlayLast);
            if (delta < 1f / 30) return;
            _afkPlayLast = now;
            _afkSeconds = AfkLength > 0 ? Mathf.Repeat(_afkSeconds + delta, AfkLength) : 0;
            _previewDirty = true;
            Repaint();
        }

        // ---- キーの編集（顔バリアントに書き込む） ----

        private AnimationCurve StoredAfkCurve(AfkRow row)
        {
            var variant = Variant;
            return variant != null && row != null && row.IsShape ? variant.FindAfkCurves(AfkClip)?.Find(row.Path, row.BlendShape)?.curve : null;
        }

        private int FindAfkKey(AnimationCurve curve, float seconds)
        {
            var tolerance = 0.5f / AfkFps;
            for (var i = 0; i < curve.length; i++)
            {
                if (Mathf.Abs(curve[i].time - seconds) < tolerance) return i;
            }
            return -1;
        }

        /// <summary>
        /// 今の時間にキーを打つ（あれば値を変える）。そのシェイプキーの最初のキーは、今見えている動き（ベース顔を適用したもの）を元にする
        /// （アニメーションウィンドウで、動かしていなかったものを0秒以外で記録したときと同じ）。
        /// </summary>
        private void SetAfkKey(AfkRow row, float value)
        {
            var variant = Variant;
            var clip = AfkClip;
            if (variant == null || clip == null) return;

            Undo.RecordObject(variant, "AFKのキーを打つ");
            var entry = variant.FindAfkCurves(clip);
            if (entry == null)
            {
                entry = new AfkClipCurves { clip = clip };
                variant.afkCurves.Add(entry);
            }
            var shape = entry.Find(row.Path, row.BlendShape);
            if (shape == null)
            {
                var seed = _afkShownCurves.TryGetValue(row.Key, out var shown) && shown.length > 0
                    ? new AnimationCurve(shown.keys)
                    : new AnimationCurve(new Keyframe(0, CurrentWeight(row.Path, row.BlendShape)));
                shape = new BlendShapeCurve { path = row.Path, blendShape = row.BlendShape, curve = seed };
                entry.curves.Add(shape);
            }

            var curve = shape.curve;
            var time = SnapAfk(_afkSeconds);
            var index = FindAfkKey(curve, time);
            if (index >= 0)
            {
                var key = curve[index];
                key.value = value;
                curve.MoveKey(index, key);
                UpdateAfkTangents(curve, index, AnimationUtility.GetKeyLeftTangentMode(curve, index), AnimationUtility.GetKeyRightTangentMode(curve, index));
            }
            else
            {
                index = curve.AddKey(new Keyframe(time, value));
                UpdateAfkTangents(curve, index, AnimationUtility.TangentMode.ClampedAuto, AnimationUtility.TangentMode.ClampedAuto);
            }
            shape.curve = curve;
            EditorUtility.SetDirty(variant);
            AfterEdit();
        }

        private void MoveAfkKey(AfkRow row, float seconds)
        {
            var variant = Variant;
            var curve = StoredAfkCurve(row);
            if (curve == null || _afkDragKey < 0 || _afkDragKey >= curve.length) return;

            var time = SnapAfk(seconds);
            var key = curve[_afkDragKey];
            if (Mathf.Approximately(key.time, time)) return;
            var other = FindAfkKey(curve, time);
            if (other >= 0 && other != _afkDragKey) return;

            Undo.RecordObject(variant, "AFKのキーを動かす");
            key.time = time;
            _afkDragKey = curve.MoveKey(_afkDragKey, key);
            UpdateAfkTangents(curve, _afkDragKey, AnimationUtility.GetKeyLeftTangentMode(curve, _afkDragKey), AnimationUtility.GetKeyRightTangentMode(curve, _afkDragKey));
            variant.FindAfkCurves(AfkClip).Find(row.Path, row.BlendShape).curve = curve;
            EditorUtility.SetDirty(variant);
            _afkSeconds = time;
            AfterEdit();
        }

        private void RemoveAfkKey(AfkRow row, int index)
        {
            var variant = Variant;
            var entry = variant.FindAfkCurves(AfkClip);
            var shape = entry?.Find(row.Path, row.BlendShape);
            if (shape == null || index < 0 || index >= shape.curve.length) return;

            Undo.RecordObject(variant, "AFKのキーを削除");
            var curve = shape.curve;
            curve.RemoveKey(index);
            if (curve.length == 0)
            {
                RemoveAfkShape(variant, entry, shape);
            }
            else
            {
                // 消したキーの前後の自動の接線を付け直す。
                foreach (var i in new[] { index - 1, index }.Where(i => i >= 0 && i < curve.length))
                {
                    UpdateAfkTangents(curve, i, AnimationUtility.GetKeyLeftTangentMode(curve, i), AnimationUtility.GetKeyRightTangentMode(curve, i));
                }
                shape.curve = curve;
            }
            EditorUtility.SetDirty(variant);
            AfterEdit();
        }

        private void RemoveAfkCurve(AfkRow row)
        {
            var variant = Variant;
            var entry = variant.FindAfkCurves(AfkClip);
            var shape = entry?.Find(row.Path, row.BlendShape);
            if (shape == null) return;

            Undo.RecordObject(variant, "AFKのキーをすべて削除");
            RemoveAfkShape(variant, entry, shape);
            EditorUtility.SetDirty(variant);
            AfterEdit();
        }

        private static void RemoveAfkShape(FaceVariant variant, AfkClipCurves entry, BlendShapeCurve shape)
        {
            entry.curves.Remove(shape);
            if (entry.curves.Count == 0) variant.afkCurves.Remove(entry);
        }

        // 接線の種類を付け直す（自動の接線は前後のキーから計算し直される）。
        private static void UpdateAfkTangents(AnimationCurve curve, int index, AnimationUtility.TangentMode left, AnimationUtility.TangentMode right)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, index, left);
            AnimationUtility.SetKeyRightTangentMode(curve, index, right);
        }

        // ---- 開いたときの状態 ----

        private void TakeAfkSnapshot()
        {
            _afkCurvesSnapshot = Variant != null ? CopyAfkCurves(Variant.afkCurves) : null;
        }

        private void RestoreAfkCurves()
        {
            var variant = Variant;
            if (variant == null || _afkCurvesSnapshot == null) return;

            Undo.RecordObject(variant, "開いたときの状態に戻す");
            variant.afkCurves = CopyAfkCurves(_afkCurvesSnapshot);
            EditorUtility.SetDirty(variant);
        }

        private static List<AfkClipCurves> CopyAfkCurves(List<AfkClipCurves> source)
        {
            return source.Select(e => new AfkClipCurves
            {
                clip = e.clip,
                curves = e.curves.Select(c => new BlendShapeCurve { path = c.path, blendShape = c.blendShape, curve = new AnimationCurve(c.curve.keys) }).ToList(),
            }).ToList();
        }
    }
}
