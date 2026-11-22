using System.Collections.Generic;
using System.Linq;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class PartCategoryOrder
    {
        public static string Key(FacialPart part) => !string.IsNullOrEmpty(part.exclusiveGroup)
            ? "exclusive:" + part.exclusiveGroup : "folder:" + (part.folder ?? "");

        public static List<string> Keys(ExpressionSet set)
        {
            var available = set.partFolders.Concat(set.parts.Select(p => p.folder))
                .Where(f => !string.IsNullOrEmpty(f)).Distinct().Select(f => "folder:" + f)
                .Concat(set.parts.Select(p => p.exclusiveGroup).Where(g => !string.IsNullOrEmpty(g))
                    .Distinct().Select(g => "exclusive:" + g)).ToList();
            return (set.partCategoryOrder ?? new List<string>()).Where(available.Contains)
                .Concat(available).Distinct().ToList();
        }

        // Change presentation only; BuildPlan still assigns parameters from the original part order.
        public static IEnumerable<BuildPlan.PartEntry> MenuParts(ExpressionSet set, BuildPlan plan)
        {
            if (set.partCategoryOrder == null || set.partCategoryOrder.Count == 0) return plan.Parts;
            var keys = Keys(set);
            return plan.Parts.OrderBy(p => Key(p.Part) == "folder:" ? -1 : keys.IndexOf(Key(p.Part)));
        }
    }
}
