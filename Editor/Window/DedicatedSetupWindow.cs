using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal sealed class DedicatedSetupWindow : EditorWindow
    {
        [SerializeField] private FacialExpressionAvatar _source;
        [SerializeField] private string _name;
        private bool _focusName = true;

        public static void Open(FacialExpressionAvatar source)
        {
            var window = CreateInstance<DedicatedSetupWindow>();
            window._source = source;
            window._name = AvatarSetup.DedicatedSetupName(source);
            window.titleContent = new GUIContent("専用設定を複製");
            window.minSize = new Vector2(460, 210);
            window.maxSize = new Vector2(700, 260);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            if (_source == null || _source.expressionSet == null)
            {
                EditorGUILayout.HelpBox("複製元の設定が見つかりません。", MessageType.Warning);
                if (GUILayout.Button("閉じる")) Close();
                return;
            }
            GUILayout.Space(8);
            EditorGUILayout.LabelField("元の設定の隣に、独立した設定を作成します。", EditorStyles.wordWrappedLabel);
            GUI.SetNextControlName("SetupName");
            _name = EditorGUILayout.TextField("名前", _name);
            if (_focusName) { EditorGUI.FocusTextInControl("SetupName"); _focusName = false; }
            var error = AvatarSetup.ValidateSetupName(_name);
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Info);
            else
            {
                var folder = AvatarSetup.DedicatedSetupFolder(_source, _name);
                EditorGUILayout.LabelField("保存先", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(folder + "/", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("同名がある場合は番号を付けます。", EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("キャンセル")) { Close(); GUIUtility.ExitGUI(); }
                using (new EditorGUI.DisabledScope(error != null))
                {
                    if (GUILayout.Button("複製して適用"))
                    {
                        var applied = AvatarSetup.DuplicateBesideSource(_source, _name);
                        Selection.activeGameObject = applied.gameObject;
                        ExpressionEditorWindow.Open(applied);
                        Close();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            GUILayout.Space(8);
        }
    }
}
