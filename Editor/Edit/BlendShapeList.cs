using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 1つのメッシュのシェイプキーの一覧とグループ分け。
    /// 区切り用のシェイプキー（=====EYE===== など）があればそれで分け、無ければ名前の頭（最初の _ より前）で分ける。
    /// </summary>
    internal sealed class BlendShapeList
    {
        public sealed class Group
        {
            public string Name;
            public readonly List<int> Indices = new List<int>();
        }

        private const string SeparatorChars = @"=\-_#\*~\+■□◆◇●○★☆・";
        private static readonly Regex Separator = new Regex($@"^\s*([{SeparatorChars}]{{2,}}.*|【.*】)\s*$");
        private static readonly Regex SeparatorTrim = new Regex($@"^[{SeparatorChars}\s【]+|[{SeparatorChars}\s】]+$");
        private static readonly string[] SideSuffixes =
            { "_L", "_R", ".L", ".R", "_l", "_r", ".l", ".r", "_Left", "_Right", "Left", "Right", "_左", "_右", "左", "右" };

        public readonly SkinnedMeshRenderer Renderer;
        public readonly string Path;
        public readonly string[] Names;
        public readonly List<Group> Groups = new List<Group>();
        private readonly Dictionary<string, int> _indexOf = new Dictionary<string, int>();

        public BlendShapeList(SkinnedMeshRenderer renderer, Transform root)
        {
            Renderer = renderer;
            Path = AnimationUtility.CalculateTransformPath(renderer.transform, root);
            var mesh = renderer.sharedMesh;
            Names = mesh != null ? Enumerable.Range(0, mesh.blendShapeCount).Select(i => mesh.GetBlendShapeName(i)).ToArray() : new string[0];

            for (var i = 0; i < Names.Length; i++) _indexOf[Names[i]] = i;

            if (Names.Any(IsSeparator)) GroupBySeparators();
            else GroupByPrefix();
        }

        public int IndexOf(string name) => _indexOf.TryGetValue(name, out var i) ? i : -1;

        /// <summary>
        /// 名前の後ろに左右を付けたシェイプキー（eye_zitome1 なら eye_zitome1_L と eye_zitome1_R）。
        /// </summary>
        public IEnumerable<int> SideVariants(string name)
        {
            return SideSuffixes.Select(s => IndexOf(name + s)).Where(i => i >= 0);
        }

        public static IEnumerable<string> SideVariantNames(Mesh mesh, string name)
        {
            return SideSuffixes.Select(s => name + s).Where(n => mesh.GetBlendShapeIndex(n) >= 0);
        }

        public static bool IsSeparator(string name) => Separator.IsMatch(name);

        private void GroupBySeparators()
        {
            var current = new Group { Name = "（先頭）" };
            for (var i = 0; i < Names.Length; i++)
            {
                if (IsSeparator(Names[i]))
                {
                    if (current.Indices.Count > 0) Groups.Add(current);
                    var name = SeparatorTrim.Replace(Names[i], "");
                    current = new Group { Name = name.Length > 0 ? name : "（区切り）" };
                    continue;
                }
                current.Indices.Add(i);
            }
            if (current.Indices.Count > 0) Groups.Add(current);
        }

        private void GroupByPrefix()
        {
            var byPrefix = new Dictionary<string, Group>();
            var order = new List<Group>();
            for (var i = 0; i < Names.Length; i++)
            {
                var prefix = Prefix(Names[i]);
                if (!byPrefix.TryGetValue(prefix, out var group))
                {
                    group = new Group { Name = prefix };
                    byPrefix[prefix] = group;
                    order.Add(group);
                }
                group.Indices.Add(i);
            }

            // 1つだけのグループは「その他」にまとめる。
            var others = new Group { Name = "その他" };
            foreach (var group in order)
            {
                if (group.Indices.Count > 1) Groups.Add(group);
                else others.Indices.AddRange(group.Indices);
            }
            if (others.Indices.Count > 0) Groups.Add(others);
        }

        private static string Prefix(string name)
        {
            var end = name.IndexOfAny(new[] { '_', '.', ' ', '-' });
            return end > 0 ? name.Substring(0, end) : name;
        }
    }
}
