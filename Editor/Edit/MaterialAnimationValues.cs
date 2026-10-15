using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class MaterialAnimationValues
    {
        public static Dictionary<string, float> Read(Material material)
        {
            var result = new Dictionary<string, float>();
            if (material == null || material.shader == null) return result;
            void Vector(string name, Vector4 value, string[] components)
            { for (var c = 0; c < 4; c++) result[name + "." + components[c]] = value[c]; }
            for (var i = 0; i < material.shader.GetPropertyCount(); i++)
            {
                var name = material.shader.GetPropertyName(i);
                switch (material.shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: Vector(name, material.GetColor(name), new[]{"r","g","b","a"}); break;
                    case ShaderPropertyType.Vector: Vector(name, material.GetVector(name), new[]{"x","y","z","w"}); break;
                    case ShaderPropertyType.Texture:
                        var scale = material.GetTextureScale(name); var offset = material.GetTextureOffset(name);
                        Vector(name + "_ST", new Vector4(scale.x,scale.y,offset.x,offset.y), new[]{"x","y","z","w"}); break;
                    case ShaderPropertyType.Int: result[name] = material.GetInteger(name); break;
                    default: result[name] = material.GetFloat(name); break;
                }
            }
            return result;
        }

        public static void Apply(Material material, string property, float value)
        {
            var dot = property.LastIndexOf('.');
            var name = dot < 0 ? property : property.Substring(0, dot);
            if (dot < 0)
            {
                if (!material.HasProperty(name)) return;
                var index = material.shader.FindPropertyIndex(name);
                if (index >= 0 && material.shader.GetPropertyType(index) == ShaderPropertyType.Int) material.SetInteger(name, Mathf.RoundToInt(value));
                else material.SetFloat(name, value);
                return;
            }
            var texture = name.EndsWith("_ST") ? name.Substring(0, name.Length - 3) : null;
            var textureIndex = texture != null ? material.shader.FindPropertyIndex(texture) : -1;
            var isTexture = textureIndex >= 0 && material.shader.GetPropertyType(textureIndex) == ShaderPropertyType.Texture;
            if (!isTexture && !material.HasProperty(name)) return;
            var component = property.Substring(dot + 1);
            if (component.Length != 1) return;
            var indexOfComponent = "rgba".IndexOf(component);
            var color = indexOfComponent >= 0;
            if (!color) indexOfComponent = "xyzw".IndexOf(component);
            if (indexOfComponent < 0) return;
            var scale = isTexture ? material.GetTextureScale(texture) : Vector2.zero;
            var offset = isTexture ? material.GetTextureOffset(texture) : Vector2.zero;
            Vector4 vector = isTexture ? new Vector4(scale.x,scale.y,offset.x,offset.y) : color ? (Vector4)material.GetColor(name) : material.GetVector(name);
            vector[indexOfComponent] = value;
            if (isTexture) { material.SetTextureScale(texture,new Vector2(vector.x,vector.y)); material.SetTextureOffset(texture,new Vector2(vector.z,vector.w)); }
            else if (color) material.SetColor(name,vector);
            else material.SetVector(name,vector);
        }
    }
}
