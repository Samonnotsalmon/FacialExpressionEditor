using System.Collections.Generic;
using System.Linq;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情データから、生成するパラメータとその値の割り当てを決める。
    /// メニュー生成とFX生成の両方のパスが同じ結果を使えるよう、表情データだけから決定的に作る。
    /// </summary>
    internal class BuildPlan
    {
        public const string Prefix = "FEE/";
        public const string BlinkOffParameter = Prefix + "BlinkOff";
        public const string DanceEnabledParameter = Prefix + "DanceEnabled";
        public const string DanceActiveParameter = Prefix + "DanceActive";
        public const string ModeParameter = Prefix + "Mode";
        public const string EmoteParameter = Prefix + "Emote";

        /// <summary>
        /// メニューで選べるモード（表情セット）。FEE/Mode がこの値のとき有効になる。
        /// </summary>
        public class Mode
        {
            public MenuNode Node;
            public int Value;
            public GestureSet GestureSet;

            // そのジェスチャーに割り当てた表情（「表情選択」メニューで、この表情セットのフォルダに並べる順）。
            public List<Expression> Emotes = new List<Expression>();
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

        // メニューの並び順。既定のモードの値は0。
        public readonly List<Mode> Modes = new List<Mode>();

        // 「表情選択」で固定できる表情（Expression.id → FEE/Emote の値。1始まり、0は選んでいない）。
        // FaceEmoと同じく、表情セットのジェスチャーに割り当てた表情と、メニューに置いた固定だけの表情がすべてここに並ぶ。
        public readonly Dictionary<string, int> EmoteValues = new Dictionary<string, int>();

        public readonly List<PartEntry> Parts = new List<PartEntry>();
        public readonly List<PartGroup> PartGroups = new List<PartGroup>();

        public bool UsesModeParameter => Modes.Count > 1;
        public bool UsesEmoteParameter => EmoteValues.Count > 0;

        public Mode ModeOf(MenuNode node) => Modes.Find(m => m.Node == node);

        public static BuildPlan Create(ExpressionSet set)
        {
            var plan = new BuildPlan();

            foreach (var gestureSet in set.gestureSets) gestureSet.mapping.EnsureSize();

            // メニューが無い（旧形式のまま）なら、表情セットをそのままモードにする。アセットは変更しない。
            var nodes = set.menu.Count > 0
                ? ExpressionSetUtility.TreeOrder(set).Select(t => t.node).ToList()
                : set.gestureSets.Where(s => s.useForGesture)
                    .Select(s => new MenuNode { kind = MenuNodeKind.GestureSet, gestureSetId = s.id })
                    .ToList();

            var modes = nodes
                .Where(ExpressionSetUtility.IsMode)
                .Select(n => new Mode { Node = n, GestureSet = set.FindGestureSet(n.gestureSetId) })
                .Where(m => m.GestureSet != null)
                .ToList();

            var defaultNode = set.menu.Count > 0 ? ExpressionSetUtility.DefaultMode(set) : null;
            var defaultMode = modes.Find(m => m.Node == defaultNode) ?? modes.FirstOrDefault();
            if (defaultMode != null)
            {
                defaultMode.Value = 0;
                plan.Modes.Add(defaultMode);
            }
            foreach (var mode in modes.Where(m => m != defaultMode))
            {
                mode.Value = plan.Modes.Count;
                plan.Modes.Add(mode);
            }

            // 表情選択：メニューの並び順で、表情セットは優先する手の Fist から（GestureExpressions の順）、固定だけの表情はその表情を並べる。
            void AddEmote(Expression expression)
            {
                if (expression != null && !plan.EmoteValues.ContainsKey(expression.id)) plan.EmoteValues[expression.id] = plan.EmoteValues.Count + 1;
            }

            foreach (var node in nodes)
            {
                var mode = modes.Find(m => m.Node == node);
                if (mode != null)
                {
                    mode.Emotes = ExpressionSetUtility.GestureExpressions(set, mode.GestureSet);
                    mode.Emotes.ForEach(AddEmote);
                }
                else if (node.kind == MenuNodeKind.Expression)
                {
                    AddEmote(set.FindExpression(node.expressionId));
                }
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
