using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        [SerializeField] private bool _showMenuIconSettings;
        private void DrawMenuIconSettings(ExpressionSet set)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || !_avatar))
                    if (GUILayout.Button("MAメニューを更新してPrefabへ保存"))
                    {
                        try { EditorGUIUtility.PingObject(EditableMenuPrefab.Save(_avatar)); }
                        catch (Exception e) { EditorUtility.DisplayDialog("MAメニューのPrefab保存", e.Message, "OK"); }
                    }
                using (new EditorGUI.DisabledScope(_avatar == null || !_avatar.editableMenu))
                    if (GUILayout.Button("メニューを選択")) Selection.activeGameObject = _avatar.editableMenu.gameObject;
            }
            EditorGUILayout.LabelField("最後に更新した写真を同じFEE Prefabの改変すべてで共有します。メニューだけをPrefabへ保存します。", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("「メニューを選択」からMA Menu ItemのIconを変更できます。構成変更後は更新・保存してください。差し替えた画像は保持します。", EditorStyles.wordWrappedMiniLabel);
            _showMenuIconSettings = EditorGUILayout.Foldout(_showMenuIconSettings, "ゲーム内メニューのアイコン", true);
            if (!_showMenuIconSettings) return;
            EditorGUILayout.LabelField("操作・フォルダは白い記号、表情・パーツは顔写真を使います。画像欄を空にすると標準へ戻ります。同じ役割の項目に共通で反映します。", EditorStyles.wordWrappedMiniLabel);
            foreach (MenuIconKind kind in Enum.GetValues(typeof(MenuIconKind)))
            {
                var old = set.menuIconOverrides?.Find(i => i != null && i.kind == kind)?.texture;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32));
                    EditorGUI.DrawRect(rect, new Color(.16f, .16f, .19f));
                    GUI.DrawTexture(rect, MenuSymbolIcons.Get(set, kind), ScaleMode.ScaleToFit, true);
                    var texture = (Texture2D)EditorGUILayout.ObjectField(MenuSymbolIcons.Labels[(int)kind], old, typeof(Texture2D), false);
                    if (texture == old) continue;
                    Modify(set, "メニューのアイコンを変更", () =>
                    {
                        if (set.menuIconOverrides == null) set.menuIconOverrides = new List<MenuIconOverride>();
                        set.menuIconOverrides.RemoveAll(i => i == null || i.kind == kind);
                        if (texture) set.menuIconOverrides.Add(new MenuIconOverride { kind = kind, texture = texture });
                    });
                }
            }
            EditorGUILayout.Space(8);
        }
    }
}
