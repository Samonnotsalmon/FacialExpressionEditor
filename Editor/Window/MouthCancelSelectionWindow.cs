using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal sealed class MouthCancelSelectionWindow : EditorWindow
    {
        [SerializeField] private FacialExpressionAvatar _avatar;
        private GameObject AvatarRoot => AvatarSetup.AvatarRootOf(_avatar);
        private FaceVariant Variant => _avatar != null ? _avatar.faceVariant : null;

        public static void Open(FacialExpressionAvatar avatar, Rect buttonRect)
        {
            var window = CreateInstance<MouthCancelSelectionWindow>();
            window._avatar = avatar;
            window.ClearMouthSelection();
            window.titleContent = new GUIContent("口キャンセルのシェイプキー");
            window.ShowAsDropDown(GUIUtility.GUIToScreenRect(buttonRect), new Vector2(420, 440));
        }

        private void OnEnable() => Undo.undoRedoPerformed += Repaint;
        private void OnDisable() => Undo.undoRedoPerformed -= Repaint;

        private void OnGUI()
        {
            var set = _avatar != null ? _avatar.expressionSet : null;
            if (set == null || AvatarRoot == null)
            {
                EditorGUILayout.HelpBox("編集対象のアバターがありません。表情エディタから開き直してください。", MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField("Shift：範囲選択　Ctrl：個別追加・解除", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("記録先クリップ", set.mouthCancelAnimation, typeof(AnimationClip), false);
            DrawMouthCancelRows(set);
        }

        private SkinnedMeshRenderer _mouthMesh;
        private GameObject _mouthRoot;
        private ExpressionSet _mouthSet;
        private string _mouthSearch = "";
        private Vector2 _mouthScroll;
        private readonly HashSet<int> _mouthSelection = new HashSet<int>();
        private int _mouthAnchor = -1;
        private int _mouthFocus = -1;

        private void ClearMouthSelection()
        {
            _mouthSelection.Clear(); _mouthAnchor = -1; _mouthFocus = -1; _mouthScroll = Vector2.zero;
        }

        private void DrawMouthCancelRows(ExpressionSet set)
        {
            var root = AvatarRoot;
            if (root == null) return;
            var meshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0).ToList();
            if (meshes.Count == 0) { EditorGUILayout.HelpBox("シェイプキーのあるメッシュがありません。", MessageType.Info); return; }
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
            var area = GUILayoutUtility.GetRect(0, 100000, 100, 100000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
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
                    _mouthFocus = i;
                    GUI.FocusControl(null); e.Use(); Repaint();
                }
            }
            GUI.EndScrollView();
            if (names.Length == 0) EditorGUILayout.LabelField("該当するシェイプキーはありません。", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"選択中：{_mouthSelection.Count} 件", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_mouthSelection.Count == 0))
                    if (GUILayout.Button("追加")) { ApplyMouthSelection(set, root, path, names); Close(); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("キャンセル")) { Close(); GUIUtility.ExitGUI(); }
            }
            var keyboard = Event.current;
            if (keyboard.type == EventType.KeyDown && keyboard.keyCode == KeyCode.Escape)
            { keyboard.Use(); Close(); GUIUtility.ExitGUI(); }
            if (keyboard.type == EventType.KeyDown && !EditorGUIUtility.editingTextField && names.Length > 0 &&
                (keyboard.keyCode == KeyCode.UpArrow || keyboard.keyCode == KeyCode.DownArrow))
            {
                _mouthFocus = Mathf.Clamp(_mouthFocus < 0 ? 0 : _mouthFocus + (keyboard.keyCode == KeyCode.DownArrow ? 1 : -1), 0, names.Length - 1);
                SelectMouthRange(_mouthSelection, ref _mouthAnchor, _mouthFocus, keyboard.shift, keyboard.control || keyboard.command);
                if (_mouthFocus * rowHeight < _mouthScroll.y) _mouthScroll.y = _mouthFocus * rowHeight;
                else if ((_mouthFocus + 1) * rowHeight > _mouthScroll.y + area.height) _mouthScroll.y = (_mouthFocus + 1) * rowHeight - area.height;
                keyboard.Use(); Repaint();
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

        private void ApplyMouthSelection(ExpressionSet set, GameObject root, string path, string[] names)
        {
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("口キャンセルの項目を追加");
            var clip = ExpressionSetUtility.CreateMouthCancelClip(set);
            foreach (var i in _mouthSelection.OrderBy(i => i))
            {
                var binding = ClipEditing.BlendShape(path, names[i]);
                // Existing entries keep their authored value; only new entries start at zero.
                if (AnimationUtility.GetEditorCurve(clip, binding) == null) MouthCancelEditing.SetValue(clip, binding, 0);
            }
            AssetDatabase.SaveAssetIfDirty(clip);
            Undo.CollapseUndoOperations(group);
            Repaint();
        }
    }
}
