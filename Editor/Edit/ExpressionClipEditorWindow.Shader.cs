using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 「シェーダーのパラメータ」タブ。頬染めなど、表情の間だけマテリアルのパラメータを変える（表情が終わると元に戻る）。
    /// クリップには material.&lt;名前&gt;（色やベクトルは .r .g .b .a / .x .y .z .w の成分ごと）のカーブとして書き込む。
    /// </summary>
    internal partial class ExpressionClipEditorWindow
    {
        private static readonly string[] ColorComponents = { "r", "g", "b", "a" };
        private static readonly string[] VectorComponents = { "x", "y", "z", "w" };

        [SerializeField] private string _shaderRendererPath;
        [SerializeField] private bool _showRecordedMaterialProperties;
        private Vector2 _shaderScroll;
        private Rect _shaderViewport;

        private void DrawShaderProperties()
        {
            _shaderScroll = EditorGUILayout.BeginScrollView(_shaderScroll, GUILayout.ExpandHeight(true));
            DrawMaterialInspector();
            EditorGUILayout.Space(12);
            var count = Values().Keys.Where(b => b.propertyName.StartsWith(ClipEditing.MaterialPrefix))
                .Select(b => (b.path, b.type, name: BaseProperty(b.propertyName))).Distinct().Count();
            _showRecordedMaterialProperties = EditorGUILayout.Foldout(_showRecordedMaterialProperties,
                $"アニメーションに記録済みの項目（{count}）", true);
            if (_showRecordedMaterialProperties) DrawCurrentProperties();
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) _shaderViewport = GUILayoutUtility.GetLastRect();
        }

        private Material _inspectorMaterial;
        private Material _inspectorSource;
        private MaterialEditor _materialEditor;
        private int _materialSlot;
        private string _materialInspectorContext;
        private Dictionary<string, float> _materialInspectorBaseline;

        private void DisposeMaterialInspector()
        {
            if (_materialEditor != null) DestroyImmediate(_materialEditor);
            if (_inspectorMaterial != null) DestroyImmediate(_inspectorMaterial);
            _materialEditor = null;
            _inspectorMaterial = null;
            _inspectorSource = null;
            _materialInspectorContext = null;
            _materialInspectorBaseline = null;
        }

        private void DrawMaterialInspector()
        {
            EditorGUILayout.LabelField("マテリアルのインスペクター", EditorStyles.boldLabel);
            var renderers = Root.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && IsSelectedTarget(r.transform) && r.sharedMaterials.Any(m => m != null)).ToList();
            if (renderers.Count == 0)
            {
                EditorGUILayout.HelpBox("アバター設定で、編集するマテリアルを持つメッシュを選択してください。", MessageType.Info);
                return;
            }
            var paths = renderers.Select(r => PathOf(r.transform, Root)).ToList();
            var index = Mathf.Max(0, paths.IndexOf(_shaderRendererPath));
            index = EditorGUILayout.Popup("メッシュ", index, paths.ToArray());
            _shaderRendererPath = paths[index];
            var renderer = renderers[index];
            var materials = renderer.sharedMaterials;
            _materialSlot = EditorGUILayout.Popup("マテリアル", Mathf.Clamp(_materialSlot, 0, materials.Length - 1),
                materials.Select((m, i) => $"{i}: {(m != null ? m.name : "なし")}").ToArray());
            var source = materials[_materialSlot];
            if (source == null || source.shader == null) return;
            if (_inspectorSource != source || _inspectorMaterial == null || _inspectorMaterial.shader != source.shader)
            {
                DisposeMaterialInspector();
                _inspectorSource = source;
                _inspectorMaterial = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
                _materialEditor = (MaterialEditor)UnityEditor.Editor.CreateEditor(_inspectorMaterial);
            }
            // Keep the editor instance and temporary values between GUI events (color pickers
            // and shader foldouts also update outside the initiating mouse event).
            var clip = TargetClip;
            var context = $"{_expressionId}|{_shaderRendererPath}|{_materialSlot}|{source.GetInstanceID()}|{EditorUtility.GetDirtyCount(source)}|{(clip != null ? clip.GetInstanceID() : 0)}|{(clip != null ? EditorUtility.GetDirtyCount(clip) : 0)}";
            if (_materialInspectorContext != context)
            {
                _inspectorMaterial.CopyPropertiesFromMaterial(source);
                foreach (var pair in Values().Where(p => p.Key.path == _shaderRendererPath && p.Key.type == renderer.GetType() && p.Key.propertyName.StartsWith(ClipEditing.MaterialPrefix)))
                    MaterialAnimationValues.Apply(_inspectorMaterial, pair.Key.propertyName.Substring(ClipEditing.MaterialPrefix.Length), pair.Value);
                _materialInspectorBaseline = MaterialAnimationValues.Read(_inspectorMaterial);
                _materialInspectorContext = context;
            }
            UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(_inspectorMaterial, true);
            EditorGUILayout.HelpBox("下のインスペクターで変更した数値・色・位置やサイズを、編集中のアニメーションへ自動記録します。元のマテリアルは変更しません。", MessageType.Info);
            EditorGUILayout.LabelField("画像の差し替え・シェーダー変更は記録対象外。同じメッシュの同名パラメータには共通で適用されます。", EditorStyles.wordWrappedMiniLabel);
            EditorGUI.BeginChangeCheck();
            var labelWidth = EditorGUIUtility.labelWidth;
            var fieldWidth = EditorGUIUtility.fieldWidth;
            try
            {
                // MaterialEditor.PropertiesGUI temporarily ends/restarts its parent vertical
                // group for the version-control bar. Give it a dedicated group so it cannot
                // close our scroll-view content and collapse the viewport.
                using (new EditorGUILayout.VerticalScope())
                {
                    _materialEditor.SetDefaultGUIWidths();
                    _materialEditor.OnInspectorGUI();
                }
            }
            finally
            {
                EditorGUIUtility.labelWidth = labelWidth;
                EditorGUIUtility.fieldWidth = fieldWidth;
            }
            if (EditorGUI.EndChangeCheck()) RecordMaterialInspectorChanges(renderer);
        }

        private void RecordMaterialInspectorChanges(Renderer renderer)
        {
            var changed = MaterialAnimationValues.Read(_inspectorMaterial)
                .Where(p => _materialInspectorBaseline.TryGetValue(p.Key, out var old) && !Mathf.Approximately(old, p.Value)).ToList();
            if (changed.Count == 0) return;
            Modify("マテリアルのパラメータを変更", targetClip =>
            {
                foreach (var pair in changed)
                    ClipEditing.SetFloat(targetClip, EditorCurveBinding.FloatCurve(_shaderRendererPath, renderer.GetType(), ClipEditing.MaterialPrefix + pair.Key), pair.Value);
            });
        }

        /// <summary>
        /// クリップで変えているパラメータ。成分ごとのカーブを、色・ベクトル・数値にまとめて出す。
        /// </summary>
        private void DrawCurrentProperties()
        {
            var values = Values();
            var groups = values.Keys
                .Where(b => b.propertyName.StartsWith(ClipEditing.MaterialPrefix))
                .GroupBy(b => (b.path, b.type, name: BaseProperty(b.propertyName)))
                .ToList();

            if (groups.Count == 0)
            {
                EditorGUILayout.LabelField("記録済みの項目はありません。インスペクターで値を変更すると追加されます。", EditorStyles.miniLabel);
                return;
            }

            foreach (var group in groups)
            {
                var (path, type, property) = group.Key;
                var renderer = Root.Find(path)?.GetComponent(type) as Renderer;
                var components = group.ToDictionary(b => Component(b.propertyName), b => b);

                using (new EditorGUILayout.HorizontalScope())
                {
                    var label = new GUIContent($"{path}：{property}", Description(renderer, property));
                    EditorGUILayout.LabelField(label, GUILayout.Width(240));

                    if (ColorComponents.All(components.ContainsKey))
                    {
                        var color = new Color(values[components["r"]], values[components["g"]], values[components["b"]], values[components["a"]]);
                        EditorGUI.BeginChangeCheck();
                        var next = EditorGUILayout.ColorField(GUIContent.none, color, true, true, true);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Modify("パラメータを変更", c =>
                            {
                                for (var i = 0; i < 4; i++) ClipEditing.SetFloat(c, components[ColorComponents[i]], next[i]);
                            });
                        }
                    }
                    else if (VectorComponents.All(components.ContainsKey))
                    {
                        var vector = new Vector4(values[components["x"]], values[components["y"]], values[components["z"]], values[components["w"]]);
                        EditorGUI.BeginChangeCheck();
                        var next = EditorGUILayout.Vector4Field(GUIContent.none, vector);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Modify("パラメータを変更", c =>
                            {
                                for (var i = 0; i < 4; i++) ClipEditing.SetFloat(c, components[VectorComponents[i]], next[i]);
                            });
                        }
                    }
                    else
                    {
                        // 成分ごと（色の a だけなど）、または数値1つ。
                        foreach (var pair in components)
                        {
                            var binding = pair.Value;
                            var limits = RangeLimits(renderer, property);
                            EditorGUI.BeginChangeCheck();
                            var label2 = string.IsNullOrEmpty(pair.Key) ? GUIContent.none : new GUIContent(pair.Key);
                            var next = limits != null
                                ? EditorGUILayout.Slider(label2, values[binding], limits.Value.x, limits.Value.y)
                                : EditorGUILayout.FloatField(label2, values[binding]);
                            if (EditorGUI.EndChangeCheck()) Modify("パラメータを変更", c => ClipEditing.SetFloat(c, binding, next));
                        }
                    }

                    if (GUILayout.Button(new GUIContent("×", "この表情で変えない"), EditorStyles.miniButton, GUILayout.Width(24)))
                    {
                        Modify("パラメータを外す", c =>
                        {
                            foreach (var binding in group) ClipEditing.RemoveFloat(c, binding);
                        });
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        /// <summary>
        /// メッシュを選び、そのマテリアルのシェーダーのパラメータを検索して追加する。追加するときの値は、今のマテリアルの値。
        /// </summary>
        private static string BaseProperty(string propertyName)
        {
            var name = propertyName.Substring(ClipEditing.MaterialPrefix.Length);
            var dot = name.LastIndexOf('.');
            return dot >= 0 ? name.Substring(0, dot) : name;
        }

        private static string Component(string propertyName)
        {
            var name = propertyName.Substring(ClipEditing.MaterialPrefix.Length);
            var dot = name.LastIndexOf('.');
            return dot >= 0 ? name.Substring(dot + 1) : "";
        }

        private static string Description(Renderer renderer, string property)
        {
            var (shader, index) = FindProperty(renderer, property);
            return shader != null ? shader.GetPropertyDescription(index) : "";
        }

        /// <summary>
        /// スライダーで出す範囲。Range はシェーダーの範囲、色の成分は 0〜1。それ以外は数値の入力欄にする（null）。
        /// </summary>
        private static Vector2? RangeLimits(Renderer renderer, string property)
        {
            var (shader, index) = FindProperty(renderer, property);
            if (shader == null) return null;
            switch (shader.GetPropertyType(index))
            {
                case ShaderPropertyType.Range: return shader.GetPropertyRangeLimits(index);
                case ShaderPropertyType.Color: return new Vector2(0, 1);
                default: return null;
            }
        }

        private static (Shader shader, int index) FindProperty(Renderer renderer, string property)
        {
            if (renderer == null) return (null, -1);
            foreach (var material in renderer.sharedMaterials.Where(m => m != null && m.shader != null))
            {
                var index = material.shader.FindPropertyIndex(property);
                if (index >= 0) return (material.shader, index);
            }
            return (null, -1);
        }
    }
}
