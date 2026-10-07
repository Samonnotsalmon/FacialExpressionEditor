using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 「オブジェクト・マテリアル」タブ。表情の間だけオブジェクトを出し入れし、マテリアルを差し替える（表情が終わると元に戻る）。
    /// </summary>
    internal partial class ExpressionClipEditorWindow
    {
        [SerializeField] private bool _showAllMaterials;
        private Vector2 _objectsScroll;

        private void DrawObjectsAndMaterials()
        {
            _objectsScroll = EditorGUILayout.BeginScrollView(_objectsScroll);
            DrawObjectToggles();
            EditorGUILayout.Space(12);
            DrawMaterialSwaps();
            EditorGUILayout.EndScrollView();
        }

        private void DrawObjectToggles()
        {
            EditorGUILayout.LabelField("オブジェクトのオン・オフ", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("表情の間だけ、オブジェクトを出したり消したりします。", EditorStyles.wordWrappedMiniLabel);

            var clip = TargetClip;
            var bindings = clip == null
                ? new EditorCurveBinding[0]
                : AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(GameObject) && b.propertyName == ClipEditing.IsActive).ToArray();
            var values = Values();

            foreach (var binding in bindings)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var exists = Root.Find(binding.path) != null;
                    EditorGUILayout.LabelField(new GUIContent(exists ? binding.path : $"{binding.path}（見つかりません）", binding.path));
                    values.TryGetValue(binding, out var value);
                    EditorGUI.BeginChangeCheck();
                    var on = EditorGUILayout.ToggleLeft("出す", value > 0.5f, GUILayout.Width(60));
                    if (EditorGUI.EndChangeCheck()) Modify("オブジェクトのオン・オフを変更", c => ClipEditing.SetFloat(c, binding, on ? 1 : 0));
                    if (GUILayout.Button(new GUIContent("×", "この表情で動かさない"), EditorStyles.miniButton, GUILayout.Width(24)))
                    {
                        Modify("オブジェクトのオン・オフを外す", c => ClipEditing.RemoveFloat(c, binding));
                        GUIUtility.ExitGUI();
                    }
                }
            }

            var added = (GameObject)EditorGUILayout.ObjectField(new GUIContent("オブジェクトを追加", "ヒエラルキーからアバターの中のオブジェクトをドロップします"),
                null, typeof(GameObject), true);
            if (added == null) return;

            if (added.transform == Root || !added.transform.IsChildOf(Root))
            {
                ShowNotification(new GUIContent("このアバターの中のオブジェクトを選んでください"), 2);
                return;
            }
            // 今と逆の状態（今出ていれば消す、消えていれば出す）で追加する。
            var path = PathOf(added.transform, Root);
            Modify("オブジェクトのオン・オフを追加", c => ClipEditing.SetFloat(c, ClipEditing.Active(path), added.activeSelf ? 0 : 1));
        }

        private void DrawMaterialSwaps()
        {
            EditorGUILayout.LabelField("マテリアルの差し替え", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("表情の間だけ、マテリアルを差し替えます。", EditorStyles.wordWrappedMiniLabel);
            _showAllMaterials = EditorGUILayout.ToggleLeft("差し替えていないものも表示する", _showAllMaterials);

            var clip = TargetClip;
            var shown = 0;
            foreach (var renderer in Root.GetComponentsInChildren<Renderer>(true).Where(r => r is SkinnedMeshRenderer || r is MeshRenderer))
            {
                var path = PathOf(renderer.transform, Root);
                var materials = renderer.sharedMaterials;
                for (var slot = 0; slot < materials.Length; slot++)
                {
                    var binding = ClipEditing.MaterialSlot(path, renderer.GetType(), slot);
                    var current = clip != null ? ClipEditing.GetObject(clip, binding) as Material : null;
                    if (!_showAllMaterials && current == null) continue;
                    shown++;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var original = materials[slot] != null ? materials[slot].name : "なし";
                        EditorGUILayout.LabelField(new GUIContent($"{path} [{slot}]", $"元：{original}"), GUILayout.Width(200));
                        EditorGUI.BeginChangeCheck();
                        var next = (Material)EditorGUILayout.ObjectField(current, typeof(Material), false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            var slotBinding = binding;
                            Modify("マテリアルの差し替えを変更", c =>
                            {
                                if (next == null) ClipEditing.RemoveObject(c, slotBinding);
                                else ClipEditing.SetObject(c, slotBinding, next);
                            });
                        }
                        EditorGUILayout.LabelField($"元：{original}", EditorStyles.miniLabel, GUILayout.Width(140));
                    }
                }
            }
            if (shown == 0)
            {
                EditorGUILayout.LabelField("差し替えているマテリアルはありません。「差し替えていないものも表示する」から選べます。", EditorStyles.wordWrappedMiniLabel);
            }
        }
    }
}
