using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// プレビュー用に、クリップのカーブをアバターへ直接書き込む。表情で使うプロパティだけを扱う
    /// （シェイプキー、オブジェクトのオン・オフ、Rendererの有効・無効、マテリアルの差し替え、シェーダーのパラメータ）。
    /// </summary>
    internal static class ClipApplier
    {
        private const string BlendShapePrefix = "blendShape.";
        private const string MaterialPrefix = "material.";
        private static readonly Regex MaterialSlot = new Regex(@"^m_Materials\.Array\.data\[(\d+)\]$");

        /// <summary>
        /// touchedRenderers / touchedObjects には、変更したレンダラーとオブジェクトを追加する（元に戻すときに使う）。
        /// </summary>
        public static void Apply(GameObject root, AnimationClip clip, float time,
            ICollection<Renderer> touchedRenderers = null, ICollection<GameObject> touchedObjects = null)
        {
            if (clip == null) return;

            var blocks = new Dictionary<Renderer, MaterialPropertyBlock>();

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var target = Find(root, binding.path);
                if (target == null) continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;
                var value = curve.Evaluate(time);

                if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive")
                {
                    target.gameObject.SetActive(value > 0.5f);
                    touchedObjects?.Add(target.gameObject);
                    continue;
                }

                if (!typeof(Renderer).IsAssignableFrom(binding.type)) continue;
                var renderer = target.GetComponent(binding.type) as Renderer;
                if (renderer == null) continue;
                touchedRenderers?.Add(renderer);

                if (binding.propertyName == "m_Enabled")
                {
                    renderer.enabled = value > 0.5f;
                }
                else if (renderer is SkinnedMeshRenderer skinned && binding.propertyName.StartsWith(BlendShapePrefix))
                {
                    var index = skinned.sharedMesh != null
                        ? skinned.sharedMesh.GetBlendShapeIndex(binding.propertyName.Substring(BlendShapePrefix.Length))
                        : -1;
                    if (index >= 0) skinned.SetBlendShapeWeight(index, value);
                }
                else if (binding.propertyName.StartsWith(MaterialPrefix))
                {
                    if (!blocks.TryGetValue(renderer, out var block))
                    {
                        block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block);
                        blocks[renderer] = block;
                    }
                    SetMaterialProperty(renderer, block, binding.propertyName.Substring(MaterialPrefix.Length), value);
                }
            }

            foreach (var pair in blocks) pair.Key.SetPropertyBlock(pair.Value);

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var match = MaterialSlot.Match(binding.propertyName);
                if (!match.Success) continue;

                var target = Find(root, binding.path);
                var renderer = target != null ? target.GetComponent(binding.type) as Renderer : null;
                if (renderer == null) continue;

                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                var material = ValueAt(keys, time) as Material;
                var slot = int.Parse(match.Groups[1].Value);
                var materials = renderer.sharedMaterials;
                if (material == null || slot >= materials.Length) continue;

                materials[slot] = material;
                renderer.sharedMaterials = materials;
                touchedRenderers?.Add(renderer);
            }
        }

        /// <summary>
        /// "_Color2nd.a" のような成分指定にも対応して、マテリアルのパラメータを書き込む。
        /// </summary>
        private static void SetMaterialProperty(Renderer renderer, MaterialPropertyBlock block, string property, float value)
        {
            var dot = property.LastIndexOf('.');
            if (dot < 0)
            {
                block.SetFloat(property, value);
                return;
            }

            var name = property.Substring(0, dot);
            var component = property.Substring(dot + 1);
            var material = renderer.sharedMaterial;
            Vector4 vector = block.HasVector(name) ? block.GetVector(name)
                : material != null && material.HasProperty(name) ? material.GetVector(name)
                : Vector4.zero;

            switch (component)
            {
                case "r": case "x": vector.x = value; break;
                case "g": case "y": vector.y = value; break;
                case "b": case "z": vector.z = value; break;
                case "a": case "w": vector.w = value; break;
                default: return;
            }
            block.SetVector(name, vector);
        }

        private static Object ValueAt(ObjectReferenceKeyframe[] keys, float time)
        {
            Object value = null;
            foreach (var key in keys)
            {
                if (key.time > time + 0.0001f) break;
                value = key.value;
            }
            return value ?? (keys.Length > 0 ? keys[0].value : null);
        }

        private static Transform Find(GameObject root, string path)
        {
            return string.IsNullOrEmpty(path) ? root.transform : root.transform.Find(path);
        }
    }
}
