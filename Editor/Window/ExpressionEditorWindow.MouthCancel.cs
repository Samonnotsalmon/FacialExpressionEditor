using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        private SkinnedMeshRenderer _mouthMesh;
        private GameObject _mouthRoot;
        private ExpressionSet _mouthSet;
        private string _mouthSearch = "";
        private Vector2 _mouthScroll;
        private readonly HashSet<int> _mouthSelection = new HashSet<int>();
        private int _mouthAnchor = -1;

        private void ClearMouthSelection()
        {
            _mouthSelection.Clear(); _mouthAnchor = -1; _mouthScroll = Vector2.zero;
        }

        private void DrawMouthCancelRows(ExpressionSet set)
        {
            var root = AvatarRoot;
            if (root == null) return;
            var meshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToList();
            if (meshes.Count == 0) return;
            if (_mouthRoot != root || _mouthSet != set || !meshes.Contains(_mouthMesh))
            {
                _mouthRoot = root; _mouthSet = set;
                _mouthMesh = meshes.Contains(AvatarSetup.FaceRenderer(root)) ? AvatarSetup.FaceRenderer(root) : meshes[0];
                ClearMouthSelection();
            }
            var labels = meshes.Select(r => AnimationUtility.CalculateTransformPath(r.transform, root.transform)).ToArray();
            var nextMesh = meshes[EditorGUILayout.Popup("メッシュ", meshes.IndexOf(_mouthMesh), labels)];
            if (nextMesh != _mouthMesh) { _mouthMesh = nextMesh; ClearMouthSelection(); }
            var search = EditorGUILayout.TextField("検索", _mouthSearch);
            if (search != _mouthSearch) { _mouthSearch = search; ClearMouthSelection(); }
            var path = AnimationUtility.CalculateTransformPath(_mouthMesh.transform, root.transform);
            var names = Enumerable.Range(0, _mouthMesh.sharedMesh.blendShapeCount).Select(_mouthMesh.sharedMesh.GetBlendShapeName)
                .Where(n => !BlendShapeList.IsSeparator(n) &&
                    n.IndexOf(_mouthSearch, System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !LipSyncProtection.IsProtected(ClipEditing.BlendShape(path, n), root, set)).ToArray();
            _mouthSelection.RemoveWhere(i => i >= names.Length);
            EditorGUILayout.LabelField("青・●：ベース顔で変更　✓：クリップに登録済み", EditorStyles.wordWrappedMiniLabel);
            const float rowHeight = 22;
            var area = GUILayoutUtility.GetRect(0, 220, GUILayout.ExpandWidth(true));
            var content = new Rect(0, 0, Mathf.Max(0, area.width - 16), names.Length * rowHeight);
            _mouthScroll = GUI.BeginScrollView(area, _mouthScroll, content);
            var first = Mathf.Max(0, Mathf.FloorToInt(_mouthScroll.y / rowHeight));
            var last = Mathf.Min(names.Length, Mathf.CeilToInt((_mouthScroll.y + area.height) / rowHeight));
            for (var i = first; i < last; i++)
            {
                var row = new Rect(0, i * rowHeight, content.width, rowHeight);
                var hint = MouthCancelEditing.BaseFaceHighlight(Variant, path, names[i]);
                var selected = _mouthSelection.Contains(i);
                if (Event.current.type == EventType.Repaint)
                {
                    if (hint != null) EditorGUI.DrawRect(row, new Color(0.3f, 0.6f, 1f, 0.22f));
                    if (selected) EditorGUI.DrawRect(row, new Color(1f, 0.65f, 0.15f, 0.3f));
                }
                var binding = ClipEditing.BlendShape(path, names[i]);
                var curve = set.mouthCancelAnimation != null ? AnimationUtility.GetEditorCurve(set.mouthCancelAnimation, binding) : null;
                GUI.Label(new Rect(4, row.y, Mathf.Max(0, row.width - 80), rowHeight),
                    new GUIContent((hint != null ? "● " : "") + names[i], hint ?? path));
                GUI.Label(new Rect(Mathf.Max(0, row.width - 76), row.y, 76, rowHeight), curve != null ? $"✓ {curve.Evaluate(0):0.##}" : "—");
                var e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
                {
                    SelectMouthRange(_mouthSelection, ref _mouthAnchor, i, e.shift, e.control || e.command);
                    GUI.FocusControl(null); e.Use(); Repaint();
                }
            }
            GUI.EndScrollView();
            if (names.Length == 0) EditorGUILayout.LabelField("該当するシェイプキーはありません。", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"選択中：{_mouthSelection.Count} 件", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(_mouthSelection.Count == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("選択した項目を0にする")) ApplyMouthSelection(set, root, path, names, false);
                using (new EditorGUI.DisabledScope(set.mouthCancelAnimation == null))
                    if (GUILayout.Button("選択した項目を対象から外す")) ApplyMouthSelection(set, root, path, names, true);
            }
        }

        internal static void SelectMouthRange(HashSet<int> selection, ref int anchor, int index, bool shift, bool additive)
        {
            if (shift && anchor >= 0)
            {
                if (!additive) selection.Clear();
                for (var i = Mathf.Min(anchor, index); i <= Mathf.Max(anchor, index); i++) selection.Add(i);
            }
            else
            {
                if (!additive) selection.Clear();
                if (!additive || !selection.Remove(index)) selection.Add(index);
                anchor = index;
            }
        }

        private void ApplyMouthSelection(ExpressionSet set, GameObject root, string path, string[] names, bool remove)
        {
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(remove ? "口キャンセルの対象を外す" : "口キャンセルを0に設定");
            var clip = ExpressionSetUtility.CreateMouthCancelClip(set);
            foreach (var i in _mouthSelection.OrderBy(i => i))
            {
                var binding = ClipEditing.BlendShape(path, names[i]);
                if (remove) MouthCancelEditing.SetShapeSelected(set, root, binding, false);
                else MouthCancelEditing.SetValue(clip, binding, 0);
            }
            AssetDatabase.SaveAssetIfDirty(clip);
            Undo.CollapseUndoOperations(group);
            Repaint();
        }
    }
}
