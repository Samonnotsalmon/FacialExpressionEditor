using System.Collections.Generic;
using System.Linq;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情セットから、生成するパラメータとその値の割り当てを決める。
    /// メニュー生成とFX生成の両方のパスが同じ結果を使えるよう、表情セットだけから決定的に作る。
    /// </summary>
    internal class BuildPlan
    {
        public const string Prefix = "FEE/";
        public const string SetParameter = Prefix + "Set";
        public const string FixedParameter = Prefix + "Fixed";

        public class FixedMenu
        {
            public GestureSet Set;
            public List<Expression> Expressions = new List<Expression>();
        }

        public class PartEntry
        {
            public FacialPart Part;
            public string Parameter;
            public bool IsGrouped;
            // グループなら1始まりのInt値、単独ならBoolなので常に1。
            public int Value;
        }

        public class PartGroup
        {
            public string Name;
            public string Parameter;
            public List<PartEntry> Parts = new List<PartEntry>();
        }

        // 添字が FEE/Set の値になる。
        public readonly List<GestureSet> GestureSets = new List<GestureSet>();
        public readonly List<FixedMenu> FixedMenus = new List<FixedMenu>();
        // Expression.id → FEE/Fixed の値（1始まり）。
        public readonly Dictionary<string, int> FixedValues = new Dictionary<string, int>();
        public readonly List<PartEntry> Parts = new List<PartEntry>();
        public readonly List<PartGroup> PartGroups = new List<PartGroup>();

        public bool UsesSetParameter => GestureSets.Count > 1;
        public bool UsesFixedParameter => FixedValues.Count > 0;

        public static BuildPlan Create(ExpressionSet set)
        {
            var plan = new BuildPlan();

            foreach (var gestureSet in set.gestureSets)
            {
                gestureSet.mapping.EnsureSize();
                if (gestureSet.useForGesture) plan.GestureSets.Add(gestureSet);

                if (!gestureSet.showInFixedMenu) continue;
                var menu = new FixedMenu { Set = gestureSet };
                foreach (var id in gestureSet.mapping.left.Concat(gestureSet.mapping.right))
                {
                    var expression = set.FindExpression(id);
                    if (expression == null || menu.Expressions.Contains(expression)) continue;
                    menu.Expressions.Add(expression);
                    if (!plan.FixedValues.ContainsKey(expression.id))
                    {
                        plan.FixedValues[expression.id] = plan.FixedValues.Count + 1;
                    }
                }
                if (menu.Expressions.Count > 0) plan.FixedMenus.Add(menu);
            }

            for (var i = 0; i < set.parts.Count; i++)
            {
                var part = set.parts[i];
                if (part.clip == null || part.properties.Count == 0) continue;

                if (string.IsNullOrEmpty(part.exclusiveGroup))
                {
                    plan.Parts.Add(new PartEntry { Part = part, Parameter = $"{Prefix}Part/{i}", Value = 1 });
                    continue;
                }

                var group = plan.PartGroups.Find(g => g.Name == part.exclusiveGroup);
                if (group == null)
                {
                    group = new PartGroup { Name = part.exclusiveGroup, Parameter = $"{Prefix}PartGroup/{plan.PartGroups.Count}" };
                    plan.PartGroups.Add(group);
                }

                var entry = new PartEntry
                {
                    Part = part,
                    Parameter = group.Parameter,
                    IsGrouped = true,
                    Value = group.Parts.Count + 1,
                };
                group.Parts.Add(entry);
                plan.Parts.Add(entry);
            }

            return plan;
        }
    }
}
