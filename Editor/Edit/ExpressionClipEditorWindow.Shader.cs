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
        private const int PropertySearchLimit = 40;
        private static readonly string[] ColorComponents = { "r", "g", "b", "a" };
        private static readonly string[] VectorComponents = { "x", "y", "z", "w" };

        [SerializeField] private string _shaderRendererPath;
        [SerializeField] private string _propertySearch = "";
        private Vector2 _shaderScroll;

        private void DrawShaderProperties()
        {
            _shaderScroll = EditorGUILayout.BeginScrollView(_shaderScroll);
            EditorGUILayout.LabelField("シェーダーのパラメータ", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("頬染めなど、表情の間だけマテリアルのパラメータを変えます（lilToon の 2nd メインカラーの不透明度など）。",
                EditorStyles.wordWrappedMiniLabel);

            DrawCurrentProperties();
            EditorGUILayout.Space(12);
            DrawAddProperty();
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// クリップで変えているパラメータ。成分ごとのカーブを、色・ベクトル・数値にまとめて出す。
        /// </summary>
        private void DrawCurrentProperties()
        {
            var clip = TargetClip;
            var values = Values();
            var groups = values.Keys
                .Where(b => b.propertyName.StartsWith(ClipEditing.MaterialPrefix))
                .GroupBy(b => (b.path, b.type, name: BaseProperty(b.propertyName)))
                .ToList();

            if (groups.Count == 0)
            {
                EditorGUILayout.LabelField("変えているパラメータはありません。下から追加できます。", EditorStyles.miniLabel);
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
        private void DrawAddProperty()
        {
            EditorGUILayout.LabelField("パラメータを追加", EditorStyles.boldLabel);

            var renderers = Root.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is SkinnedMeshRenderer || r is MeshRenderer) && r.sharedMaterials.Any(m => m != null))
                .ToList();
            if (renderers.Count == 0) return;

            var paths = renderers.Select(r => PathOf(r.transform, Root)).ToList();
            var index = paths.IndexOf(_shaderRendererPath);
            if (index < 0)
            {
                var viseme = Descriptor != null ? Descriptor.VisemeSkinnedMesh : null;
                index = Mathf.Max(0, renderers.IndexOf(viseme));
            }
            index = EditorGUILayout.Popup("メッシュ", index, paths.ToArray());
            _shaderRendererPath = paths[index];
            var renderer = renderers[index];

            _propertySearch = EditorGUILayout.TextField("パラメータを検索", _propertySearch);
            if (string.IsNullOrWhiteSpace(_propertySearch))
            {
                EditorGUILayout.LabelField("名前か説明で検索します（例：2nd、Color、Emission）。", EditorStyles.miniLabel);
                return;
            }

            var search = _propertySearch.Trim();
            var shown = 0;
            foreach (var (material, shader, i) in ShaderProperties(renderer))
            {
                var name = shader.GetPropertyName(i);
                var description = shader.GetPropertyDescription(i);
                if (name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    description.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++shown > PropertySearchLimit) break;

                var type = shader.GetPropertyType(i);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent(name, description), GUILayout.Width(200));
                    EditorGUILayout.LabelField($"{description}（{type}）", EditorStyles.miniLabel);
                    if (GUILayout.Button("追加", EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        var path = PathOf(renderer.transform, Root);
                        Modify("パラメータを追加", c => AddProperty(c, path, renderer.GetType(), material, name, type));
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (shown == 0) EditorGUILayout.LabelField("見つかりません。", EditorStyles.miniLabel);
            else if (shown > PropertySearchLimit) EditorGUILayout.LabelField($"先頭の {PropertySearchLimit} 件だけ出しています。", EditorStyles.miniLabel);
        }

        private static void AddProperty(AnimationClip clip, string path, System.Type rendererType, Material material, string name, ShaderPropertyType type)
        {
            EditorCurveBinding Binding(string suffix) =>
                EditorCurveBinding.FloatCurve(path, rendererType, $"{ClipEditing.MaterialPrefix}{name}{suffix}");

            switch (type)
            {
                case ShaderPropertyType.Color:
                    var color = material.GetColor(name);
                    for (var i = 0; i < 4; i++) ClipEditing.SetFloat(clip, Binding("." + ColorComponents[i]), color[i]);
                    break;
                case ShaderPropertyType.Vector:
                    var vector = material.GetVector(name);
                    for (var i = 0; i < 4; i++) ClipEditing.SetFloat(clip, Binding("." + VectorComponents[i]), vector[i]);
                    break;
                default:
                    ClipEditing.SetFloat(clip, Binding(""), material.GetFloat(name));
                    break;
            }
        }

        /// <summary>
        /// メッシュのマテリアルのシェーダーのパラメータ（テクスチャとインスペクタで隠しているものを除く。同じ名前は1つ）。
        /// </summary>
        private static IEnumerable<(Material material, Shader shader, int index)> ShaderProperties(Renderer renderer)
        {
            var seen = new HashSet<string>();
            foreach (var material in renderer.sharedMaterials.Where(m => m != null && m.shader != null))
            {
                var shader = material.shader;
                for (var i = 0; i < shader.GetPropertyCount(); i++)
                {
                    var type = shader.GetPropertyType(i);
                    if (type == ShaderPropertyType.Texture) continue;
                    if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0) continue;
                    if (!seen.Add(shader.GetPropertyName(i))) continue;
                    yield return (material, shader, i);
                }
            }
        }

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
