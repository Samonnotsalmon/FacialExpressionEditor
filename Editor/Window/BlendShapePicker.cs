using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// シェイプキーを一覧からチェックで選ぶドロップダウン（FaceEmo と同じ使い方）。チェックを付けた・外したものは、すぐに反映する。
    /// メッシュの選択、名前の検索、選んだものだけの表示、区切りのシェイプキーでのグループ分け。数百あっても見えている行だけを描く。
    /// </summary>
    internal sealed class BlendShapePicker : PopupWindowContent
    {
        private const float RowHeight = 18f;
        private const string SearchControl = "BlendShapePickerSearch";

        private readonly GameObject _avatarRoot;
        private readonly Func<string, string, bool> _isOn;
        private readonly Action<string, string, bool> _set;
        private readonly List<SkinnedMeshRenderer> _meshes;

        private int _meshIndex;
        private BlendShapeList _shapes;
        private string _search = "";
        private bool _selectedOnly;
        private bool _focused;
        private Vector2 _scroll;

        /// <param name="isOn">シェイプキー（メッシュのパスと名前）を選んでいるか。</param>
        /// <param name="set">チェックを付けた（true）・外した（false）ときに呼ぶ。</param>
        public BlendShapePicker(GameObject avatarRoot, Func<string, string, bool> isOn, Action<string, string, bool> set)
        {
            _avatarRoot = avatarRoot;
            _isOn = isOn;
            _set = set;
            _meshes = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0)
                .ToList();
            _meshIndex = Mathf.Max(0, _meshes.IndexOf(AvatarSetup.FaceRenderer(avatarRoot)));
        }

        public override Vector2 GetWindowSize() => new Vector2(340, 460);

        public override void OnGUI(Rect rect)
        {
            if (_meshes.Count == 0)
            {
                EditorGUILayout.LabelField("シェイプキーのあるメッシュがありません。", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            var labels = _meshes.Select(m => $"{m.name}（{m.sharedMesh.blendShapeCount}）").ToArray();
            _meshIndex = EditorGUILayout.Popup(_meshIndex, labels);
            var renderer = _meshes[_meshIndex];
            if (_shapes == null || _shapes.Renderer != renderer) _shapes = new BlendShapeList(renderer, _avatarRoot.transform);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.SetNextControlName(SearchControl);
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                _selectedOnly = GUILayout.Toggle(_selectedOnly, "選んだものだけ", EditorStyles.miniButton, GUILayout.Width(90));
            }
            if (!_focused)
            {
                _focused = true;
                EditorGUI.FocusTextInControl(SearchControl);
            }

            var rows = Rows();
            var area = GUILayoutUtility.GetRect(0, 100000, 0, 100000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var content = new Rect(0, 0, area.width - 16, rows.Count * RowHeight);
            _scroll = GUI.BeginScrollView(area, _scroll, content);
            var first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / RowHeight));
            var last = Mathf.Min(rows.Count - 1, Mathf.CeilToInt((_scroll.y + area.height) / RowHeight));
            for (var r = first; r <= last; r++)
            {
                var row = new Rect(0, r * RowHeight, content.width, RowHeight);
                var (group, shape) = rows[r];
                if (shape < 0)
                {
                    if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(row, new Color(0, 0, 0, 0.2f));
                    GUI.Label(new Rect(row.x + 4, row.y, row.width - 4, row.height), _shapes.Groups[group].Name, EditorStyles.miniBoldLabel);
                    continue;
                }

                var name = _shapes.Names[shape];
                var on = _isOn(_shapes.Path, name);
                var next = GUI.Toggle(new Rect(row.x + 6, row.y, row.width - 6, row.height), on, name);
                if (next != on)
                {
                    _set(_shapes.Path, name, next);
                    editorWindow.Repaint();
                }
            }
            GUI.EndScrollView();

            var count = _shapes.Names.Count(n => _isOn(_shapes.Path, n));
            EditorGUILayout.LabelField($"このメッシュで選んでいるもの：{count} 件", EditorStyles.miniLabel);
        }

        // 見出し（shape が -1）とシェイプキーの行。検索中・選んだものだけのときは、当てはまるものがあるグループだけ出す。
        private List<(int group, int shape)> Rows()
        {
            var search = _search.Trim();
            var rows = new List<(int, int)>();
            for (var g = 0; g < _shapes.Groups.Count; g++)
            {
                var members = _shapes.Groups[g].Indices
                    .Where(i => search.Length == 0 || _shapes.Names[i].IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Where(i => !_selectedOnly || _isOn(_shapes.Path, _shapes.Names[i]))
                    .ToList();
                if (members.Count == 0) continue;
                rows.Add((g, -1));
                rows.AddRange(members.Select(i => (g, i)));
            }
            return rows;
        }
    }
}
