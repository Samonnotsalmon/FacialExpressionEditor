using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 「シェイプキー」タブ。検索、変更したものだけの表示、グループごとの折りたたみ。
    /// 顔バリアントのアバターでは、ベース顔のシェイプキーとその左右別のもの（eye_zitome1 と eye_zitome1_L / _R）を
    /// 先頭の「ベース顔」にまとめて青で出す。青い行は、どこに出ていても「この顔のこの表情だけの値」を編集する
    /// （ベース顔はすべての表情に適用し、ウインクで閉じている目はジト目にしない、のような調整をここでする）。
    /// シェイプキーが数百あっても重くならないよう、見えている行だけを描く。
    /// </summary>
    internal partial class ExpressionClipEditorWindow
    {
        private const float RowHeight = 20f;
        private const float ChangedEpsilon = 0.01f;
        private const int BaseFaceGroup = -1;
        private const string BaseFaceGroupName = "ベース顔";

        private static readonly Color ChangedColor = new Color(0.95f, 0.65f, 0.2f, 0.15f);
        private static readonly Color BaseFaceColor = new Color(0.3f, 0.6f, 1f, 0.18f);
        private static readonly Color FaceValueColor = new Color(0.3f, 0.6f, 1f, 0.38f);

        [SerializeField] private string _meshPath;
        [SerializeField] private string _search = "";
        [SerializeField] private bool _changedOnly;
        [SerializeField] private List<string> _collapsedGroups = new List<string>();

        private BlendShapeList _shapes;
        private Vector2 _shapeScroll;
        private Rect _shapeArea;

        // 見出し（Shape が -1）またはシェイプキーの行。Group が BaseFaceGroup なら先頭の「ベース顔」。
        private struct Row
        {
            public int Group;
            public int Shape;
        }

        // 描くときに使う、このメッシュの状態。
        private sealed class ShapeState
        {
            public Dictionary<EditorCurveBinding, float> Values;
            public float[] Neutral;
            public bool[] Changed;
            // 青い行（ベース顔とその左右別）。
            public bool[] FaceRelated;
            public List<int> BaseFaceShapes;
            // この表情だけの値（シェイプキーの名前 → 値）と、プレビューで見える値（青い行に出す）。
            public Dictionary<string, float> FaceValues;
            public Dictionary<EditorCurveBinding, float> Shown;
        }

        private void DrawBlendShapes(Expression expression)
        {
            var meshes = Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0)
                .ToList();
            if (meshes.Count == 0)
            {
                EditorGUILayout.LabelField("シェイプキーのあるメッシュがありません。", EditorStyles.miniLabel);
                return;
            }

            var paths = meshes.Select(m => PathOf(m.transform, Root)).ToList();
            var index = paths.IndexOf(_meshPath);
            if (index < 0) index = meshes.IndexOf(DefaultMesh(meshes));

            using (new EditorGUILayout.HorizontalScope())
            {
                var labels = meshes.Select((m, i) => $"{paths[i]}（{m.sharedMesh.blendShapeCount}）").ToArray();
                index = EditorGUILayout.Popup(index, labels, GUILayout.Width(200));
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                _changedOnly = GUILayout.Toggle(_changedOnly, "変更したものだけ", EditorStyles.miniButton, GUILayout.Width(110));
            }
            _meshPath = paths[index];
            if (_shapes == null || _shapes.Renderer != meshes[index]) _shapes = new BlendShapeList(meshes[index], Root);

            var state = BuildState(expression);
            DrawRows(BuildRows(state), state);
        }

        private SkinnedMeshRenderer DefaultMesh(List<SkinnedMeshRenderer> meshes)
        {
            var viseme = Descriptor != null ? Descriptor.VisemeSkinnedMesh : null;
            if (viseme != null && meshes.Contains(viseme)) return viseme;

            // クリップでいちばん多く動かしているメッシュ。
            var counts = Values().Keys.GroupBy(b => b.path).ToDictionary(g => g.Key, g => g.Count());
            return meshes.OrderByDescending(m => counts.TryGetValue(PathOf(m.transform, Root), out var c) ? c : 0).First();
        }

        private ShapeState BuildState(Expression expression)
        {
            var count = _shapes.Names.Length;
            var state = new ShapeState
            {
                Values = Values(),
                Neutral = new float[count],
                Changed = new bool[count],
                FaceRelated = new bool[count],
                BaseFaceShapes = new List<int>(),
                FaceValues = new Dictionary<string, float>(),
                Shown = ShownValues(expression),
            };

            var variant = Variant;
            if (variant != null)
            {
                foreach (var key in variant.baseFace.Where(k => k.enabled && k.path == _shapes.Path))
                {
                    var i = _shapes.IndexOf(key.blendShape);
                    if (i < 0) continue;
                    foreach (var j in new[] { i }.Concat(_shapes.SideVariants(key.blendShape)))
                    {
                        if (state.FaceRelated[j]) continue;
                        state.FaceRelated[j] = true;
                        state.BaseFaceShapes.Add(j);
                    }
                }

                var faceValues = variant.FindFaceValues(_expressionId);
                if (faceValues != null)
                {
                    foreach (var value in faceValues.values.Where(v => v.path == _shapes.Path)) state.FaceValues[value.blendShape] = value.value;
                }
            }

            // 「変更していない」とみなす値：元Prefabの顔（共有の表情）か、この顔（差し替え）。青い行はこの表情だけの値があれば「変更」。
            for (var i = 0; i < count; i++) state.Neutral[i] = _shapes.Renderer.GetBlendShapeWeight(i);
            if (EditsShared && variant != null)
            {
                foreach (var key in variant.baseFace.Where(k => k.enabled && k.path == _shapes.Path))
                {
                    var i = _shapes.IndexOf(key.blendShape);
                    if (i >= 0) state.Neutral[i] = key.referenceValue;
                }
            }
            for (var i = 0; i < count; i++)
            {
                state.Changed[i] = state.FaceRelated[i]
                    ? state.FaceValues.ContainsKey(_shapes.Names[i])
                    : state.Values.TryGetValue(ClipEditing.BlendShape(_shapes.Path, _shapes.Names[i]), out var value) &&
                      Mathf.Abs(value - state.Neutral[i]) > ChangedEpsilon;
            }
            return state;
        }

        // プレビューで見える値（ベース顔とこの表情だけの値を適用した後）。クリップと顔バリアントが変わったときだけ作り直す。
        private Dictionary<EditorCurveBinding, float> _shown;
        private (Expression, AnimationClip, int, int) _shownOf;

        private Dictionary<EditorCurveBinding, float> ShownValues(Expression expression)
        {
            var variant = Variant;
            var clip = TargetClip;
            var key = (expression, clip, clip != null ? EditorUtility.GetDirtyCount(clip) : 0,
                variant != null ? EditorUtility.GetDirtyCount(variant) : 0);
            if (_shown != null && _shownOf.Equals(key)) return _shown;

            _shown = new Dictionary<EditorCurveBinding, float>();
            _shownOf = key;
            var preview = PreviewClips.ForExpression(expression, variant, AvatarRoot);
            foreach (var binding in AnimationUtility.GetCurveBindings(preview))
            {
                var curve = AnimationUtility.GetEditorCurve(preview, binding);
                if (curve == null || curve.length == 0) continue;
                // 表情は最後まで再生した形。
                _shown[binding] = curve.keys[curve.length - 1].value;
            }
            PreviewClips.Release(preview);
            return _shown;
        }

        private bool MatchesSearch(int shape)
        {
            return string.IsNullOrWhiteSpace(_search) ||
                   _shapes.Names[shape].IndexOf(_search.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private List<Row> BuildRows(ShapeState state)
        {
            var searching = !string.IsNullOrWhiteSpace(_search);
            var rows = new List<Row>();

            // 先頭の「ベース顔」。「変更したものだけ」でも出す。
            var baseFace = state.BaseFaceShapes.Where(MatchesSearch).ToList();
            if (baseFace.Count > 0)
            {
                rows.Add(new Row { Group = BaseFaceGroup, Shape = -1 });
                if (searching || !_collapsedGroups.Contains(BaseFaceGroupName))
                {
                    rows.AddRange(baseFace.Select(i => new Row { Group = BaseFaceGroup, Shape = i }));
                }
            }

            for (var g = 0; g < _shapes.Groups.Count; g++)
            {
                var group = _shapes.Groups[g];
                var members = group.Indices.Where(MatchesSearch).Where(i => !_changedOnly || state.Changed[i]).ToList();
                if (members.Count == 0) continue;

                rows.Add(new Row { Group = g, Shape = -1 });
                if (!searching && !_changedOnly && _collapsedGroups.Contains(group.Name)) continue;
                rows.AddRange(members.Select(i => new Row { Group = g, Shape = i }));
            }
            return rows;
        }

        private void DrawRows(List<Row> rows, ShapeState state)
        {
            var area = GUILayoutUtility.GetRect(0, 100000, 0, 100000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Layout) area = _shapeArea;
            else _shapeArea = area;

            var content = new Rect(0, 0, Mathf.Max(0, area.width - 16), rows.Count * RowHeight);
            _shapeScroll = GUI.BeginScrollView(area, _shapeScroll, content);

            var first = Mathf.Max(0, Mathf.FloorToInt(_shapeScroll.y / RowHeight));
            var last = Mathf.Min(rows.Count - 1, Mathf.CeilToInt((_shapeScroll.y + area.height) / RowHeight));
            for (var r = first; r <= last; r++)
            {
                var rect = new Rect(0, r * RowHeight, content.width, RowHeight);
                if (rows[r].Shape >= 0) DrawShapeRow(rect, rows[r].Shape, state);
                else if (rows[r].Group == BaseFaceGroup) DrawBaseFaceHeader(rect);
                else DrawGroupHeader(rect, rows[r].Group, state);
            }

            GUI.EndScrollView();
        }

        private void DrawGroupHeader(Rect rect, int groupIndex, ShapeState state)
        {
            var group = _shapes.Groups[groupIndex];
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.2f));

            var collapsed = _collapsedGroups.Contains(group.Name);
            var changedCount = group.Indices.Count(i => state.Changed[i]);
            var label = $"{(collapsed ? "▶" : "▼")} {group.Name}（{group.Indices.Count}{(changedCount > 0 ? $"、変更 {changedCount}" : "")}）";
            if (GUI.Button(rect, label, EditorStyles.boldLabel)) ToggleCollapsed(group.Name);
        }

        private void DrawBaseFaceHeader(Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.2f));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4, rect.height), new Color(0.3f, 0.6f, 1f));
            }

            var collapsed = _collapsedGroups.Contains(BaseFaceGroupName);
            var label = $"{(collapsed ? "▶" : "▼")} {BaseFaceGroupName}（青い行は、この顔のこの表情だけの値を変えます）";
            if (GUI.Button(new Rect(rect.x + 6, rect.y, rect.width - 6, rect.height), label, EditorStyles.boldLabel))
            {
                ToggleCollapsed(BaseFaceGroupName);
            }
        }

        private void ToggleCollapsed(string name)
        {
            if (!_collapsedGroups.Remove(name)) _collapsedGroups.Add(name);
        }

        private void DrawShapeRow(Rect rect, int shape, ShapeState state)
        {
            var name = _shapes.Names[shape];
            var binding = ClipEditing.BlendShape(_shapes.Path, name);
            var faceRelated = state.FaceRelated[shape];
            var hasFaceValue = state.FaceValues.TryGetValue(name, out var faceValue);
            var inClip = state.Values.TryGetValue(binding, out var clipValue);

            if (Event.current.type == EventType.Repaint)
            {
                if (faceRelated) EditorGUI.DrawRect(rect, hasFaceValue ? FaceValueColor : BaseFaceColor);
                else if (state.Changed[shape]) EditorGUI.DrawRect(rect, ChangedColor);
            }

            var nameWidth = Mathf.Min(220, rect.width * 0.4f);
            var nameStyle = state.Changed[shape] ? EditorStyles.boldLabel : faceRelated || inClip ? EditorStyles.label : DimLabel;
            GUI.Label(new Rect(rect.x + 12, rect.y + 1, nameWidth - 12, rect.height), name, nameStyle);

            var x = rect.x + nameWidth;
            if (hasFaceValue) GUI.Label(new Rect(x, rect.y + 1, 80, rect.height), "この表情だけ", EditorStyles.miniLabel);
            x += 80;

            // 青い行はこの表情だけの値（見える値）、それ以外はクリップの値。
            var shownValue = state.Shown.TryGetValue(binding, out var shown) ? shown : _shapes.Renderer.GetBlendShapeWeight(shape);
            var value = faceRelated ? hasFaceValue ? faceValue : shownValue : inClip ? clipValue : state.Neutral[shape];

            var sliderRect = new Rect(x, rect.y + 1, rect.xMax - x - 28, rect.height - 2);
            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.Slider(sliderRect, value, 0, 100);
            if (EditorGUI.EndChangeCheck())
            {
                if (faceRelated) SetFaceValue(name, next);
                else Modify("シェイプキーを変更", clip => ClipEditing.SetFloat(clip, binding, next));
            }

            var canRemove = faceRelated ? hasFaceValue : inClip;
            var tooltip = faceRelated ? "この表情だけの値をやめる（ベース顔を適用した値に戻す）" : "この表情で動かさない（クリップから外す）";
            if (canRemove && GUI.Button(new Rect(rect.xMax - 24, rect.y + 1, 22, rect.height - 2), new GUIContent("×", tooltip), EditorStyles.miniButton))
            {
                if (faceRelated) SetFaceValue(name, null);
                else Modify("シェイプキーを外す", clip => ClipEditing.RemoveFloat(clip, binding));
            }
        }

        private void SetFaceValue(string blendShape, float? value)
        {
            FaceVariantUtility.SetFaceValue(Variant, _expressionId, _shapes.Path, blendShape, value);
            AfterEdit();
        }

        private static GUIStyle _dimLabel;
        private static GUIStyle DimLabel => _dimLabel ??= new GUIStyle(EditorStyles.label)
        {
            normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
        };
    }
}
