using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情データの複製、新しい表情の作成、表情メニューの編集を行う。
    /// </summary>
    public static class ExpressionSetUtility
    {
        public static string RenameExpression(ExpressionSet set, Expression expression, string name)
        {
            name = name.Trim();
            if (string.IsNullOrEmpty(name)) return "名前を入力してください";
            if (OwnsClip(set, expression.clip))
            {
                var error = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(expression.clip), AssetPathUtility.SafeFileName(name));
                if (!string.IsNullOrEmpty(error)) return error;
            }
            Undo.RecordObject(set, "表情の名前を変更");
            expression.name = name;
            EditorUtility.SetDirty(set);
            return null;
        }
        // メニューに無い表情をまとめて置くフォルダの名前。
        public const string AddFolderName = "Add";

        public enum NewExpressionSource
        {
            // アバターの今の顔（ベース顔）をそのままクリップにする。
            CurrentFace,
            // 既存の表情のクリップを複製する。
            CopyExpression,
            // 何も動かさないクリップから始める。
            Empty,
        }

        /// <summary>
        /// クリップを使っている表情を返す（編集のために複製した表情は、元のクリップでも見つかる）。
        /// 無ければクリップ名で表情を作って追加する（Undoは呼び出し側で記録する）。
        /// </summary>
        public static Expression FindOrCreateExpression(ExpressionSet set, AnimationClip clip)
        {
            var expression = FindExpressionByClip(set, clip);
            if (expression != null) return expression;

            expression = new Expression { name = clip.name, clip = clip };
            set.expressions.Add(expression);
            if (set.independentClips && !OwnsClip(set, clip)) MakeClipEditable(set, expression);
            return expression;
        }

        public static Expression FindExpressionByClip(ExpressionSet set, AnimationClip clip)
        {
            if (clip == null) return null;
            return set.expressions.Find(e => e.clip == clip) ?? set.expressions.Find(e => e.originalClip == clip);
        }

        /// <summary>
        /// 表情データで作ったクリップ（Expressions フォルダの中）かどうか。それ以外（作者のクリップなど）は、編集する前に複製する。
        /// </summary>
        public static bool OwnsClip(ExpressionSet set, AnimationClip clip)
        {
            var path = AssetDatabase.GetAssetPath(clip);
            return !string.IsNullOrEmpty(path) && path.StartsWith($"{AssetPathUtility.FolderOf(set)}/Expressions/");
        }

        /// <summary>
        /// 表情のクリップを 表情データのフォルダ/Expressions/ に複製し、表情をその複製に付け替える。元のクリップは originalClip に覚えておく。
        /// クリップが無い表情には、空のクリップを作る。戻り値は新しいクリップ。
        /// </summary>
        public static AnimationClip MakeClipEditable(ExpressionSet set, Expression expression)
        {
            var folder = AssetPathUtility.EnsureFolder(AssetPathUtility.FolderOf(set), "Expressions");
            var source = expression.clip;
            var path = AssetDatabase.GenerateUniqueAssetPath(
                $"{folder}/{AssetPathUtility.SafeFileName(source != null ? source.name : expression.name)}.anim");

            AnimationClip clip;
            if (source != null)
            {
                clip = Object.Instantiate(source);
                clip.name = expression.name;
                AssetDatabase.CreateAsset(clip, path);
            }
            else
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }

            Undo.RecordObject(set, "表情を編集用に複製");
            if (expression.originalClip == null) expression.originalClip = source;
            expression.clip = clip;
            EditorUtility.SetDirty(set);
            return clip;
        }

        // ---- パーツ ----

        /// <summary>
        /// 表情データで作ったパーツのクリップ（Parts フォルダの中）かどうか。それ以外（作者のクリップなど）は、編集する前に複製する。
        /// </summary>
        public static bool OwnsPartClip(ExpressionSet set, AnimationClip clip)
        {
            var path = AssetDatabase.GetAssetPath(clip);
            return !string.IsNullOrEmpty(path) && path.StartsWith($"{AssetPathUtility.FolderOf(set)}/Parts/");
        }

        /// <summary>
        /// 空のクリップのパーツを 表情データのフォルダ/Parts/ に作り、表情データに追加する。
        /// </summary>
        public static FacialPart CreatePart(ExpressionSet set, string name)
        {
            var folder = AssetPathUtility.EnsureFolder(AssetPathUtility.FolderOf(set), "Parts");
            var clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{AssetPathUtility.SafeFileName(name)}.anim"));
            AssetDatabase.SaveAssets();

            var part = new FacialPart { name = name, clip = clip };
            Undo.RecordObject(set, "新しいパーツを作成");
            set.parts.Add(part);
            EditorUtility.SetDirty(set);
            return part;
        }

        /// <summary>
        /// パーツのクリップを 表情データのフォルダ/Parts/ に複製し、パーツをその複製に付け替える。戻り値は新しいクリップ。
        /// </summary>
        public static AnimationClip MakePartClipEditable(ExpressionSet set, FacialPart part)
        {
            var folder = AssetPathUtility.EnsureFolder(AssetPathUtility.FolderOf(set), "Parts");
            var source = part.clip;
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{AssetPathUtility.SafeFileName(source != null ? source.name : part.name)}.anim");

            AnimationClip clip;
            if (source != null)
            {
                clip = Object.Instantiate(source);
                clip.name = part.name;
                AssetDatabase.CreateAsset(clip, path);
            }
            else
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }

            Undo.RecordObject(set, "パーツを編集用に複製");
            part.clip = clip;
            EditorUtility.SetDirty(set);
            return clip;
        }

        /// <summary>
        /// 表情を削除し、ジェスチャー・組み合わせ・表情メニューからの参照も外す（Undoは呼び出し側で記録する）。
        /// クリップのファイルは消さない。
        /// </summary>
        public static void RemoveExpression(ExpressionSet set, Expression expression)
        {
            set.expressions.Remove(expression);
            foreach (var mapping in set.gestureSets.Select(s => s.mapping))
            {
                mapping.EnsureSize();
                for (var i = 0; i < GestureMapping.GestureCount; i++)
                {
                    if (mapping.left[i] == expression.id) mapping.left[i] = null;
                    if (mapping.right[i] == expression.id) mapping.right[i] = null;
                }
                mapping.combos.RemoveAll(c => c.expressionId == expression.id);
            }
            foreach (var node in set.menu.Where(n => n.kind == MenuNodeKind.Expression && n.expressionId == expression.id).ToList())
            {
                RemoveNode(set, node);
            }
            foreach (var trigger in set.contactTriggers.Where(t => t.expressionId == expression.id))
            {
                trigger.expressionId = null;
            }
        }

        // ---- 表情メニュー ----

        /// <summary>
        /// ゲーム内のメニューでモードとして切り替える項目（表情セット）。固定だけの表情は「表情選択」にだけ並ぶ。
        /// </summary>
        public static bool IsMode(MenuNode node) => node.kind == MenuNodeKind.GestureSet;

        public static IEnumerable<MenuNode> Children(ExpressionSet set, string parentId)
        {
            return set.menu.Where(n => (n.parentId ?? "") == (parentId ?? ""));
        }

        /// <summary>
        /// メニューを上から順に（フォルダの中身はフォルダの直後に）並べる。
        /// </summary>
        public static List<(MenuNode node, int depth)> TreeOrder(ExpressionSet set)
        {
            var result = new List<(MenuNode, int)>();
            void Walk(string parentId, int depth)
            {
                foreach (var node in Children(set, parentId))
                {
                    result.Add((node, depth));
                    if (node.kind == MenuNodeKind.Folder) Walk(node.id, depth + 1);
                }
            }
            Walk("", 0);
            return result;
        }

        /// <summary>
        /// メニューで何も選んでいないときのモード。指定が無ければ、メニューで最初の表情セット。
        /// </summary>
        public static MenuNode DefaultMode(ExpressionSet set)
        {
            var modes = TreeOrder(set).Select(t => t.node).Where(IsMode).ToList();
            return modes.Find(n => n.id == set.defaultModeId) ?? modes.FirstOrDefault();
        }

        public static string NodeName(ExpressionSet set, MenuNode node)
        {
            switch (node.kind)
            {
                case MenuNodeKind.GestureSet: return set.FindGestureSet(node.gestureSetId)?.name ?? "（表情セットが見つかりません）";
                case MenuNodeKind.Expression: return set.FindExpression(node.expressionId)?.name ?? "（表情が見つかりません）";
                default: return node.name;
            }
        }

        /// <summary>
        /// 項目とその中身を削除する（表情セットや表情そのものは消さない）。
        /// </summary>
        public static void RemoveNode(ExpressionSet set, MenuNode node)
        {
            foreach (var child in Children(set, node.id).ToList()) RemoveNode(set, child);
            set.menu.Remove(node);
        }

        /// <summary>
        /// 項目を parentId のフォルダへ移す。before があればその前に、無ければフォルダの最後に置く。
        /// </summary>
        public static void MoveNode(ExpressionSet set, MenuNode node, string parentId, MenuNode before)
        {
            if (node == before) return;
            // フォルダを自分の中には入れない。
            for (var p = parentId; !string.IsNullOrEmpty(p); p = set.menu.Find(n => n.id == p)?.parentId)
            {
                if (p == node.id) return;
            }

            set.menu.Remove(node);
            node.parentId = parentId ?? "";
            var index = before != null ? set.menu.IndexOf(before) : -1;
            if (index < 0)
            {
                var last = set.menu.FindLastIndex(n => (n.parentId ?? "") == node.parentId);
                index = last >= 0 ? last + 1 : set.menu.Count;
            }
            set.menu.Insert(index, node);
        }

        /// <summary>
        /// 表情セットのジェスチャーに割り当てた表情（重複なし）。FaceEmoと同じく、これらはゲーム内の「表情選択」メニューに自動で並び、固定できる。
        /// 並びは、優先する手の Fist → HandOpen → … → ThumbsUp → Neutral、反対の手も同じ順、最後に組み合わせ。
        /// </summary>
        public static List<Expression> GestureExpressions(ExpressionSet set, GestureSet gestureSet)
        {
            var mapping = gestureSet.mapping;
            mapping.EnsureSize();
            var dominant = mapping.dominantHand == Hand.Left ? mapping.left : mapping.right;
            var other = mapping.dominantHand == Hand.Left ? mapping.right : mapping.left;

            // Neutral（0）はふだん空なので、各手の最後に回す。
            var order = Enumerable.Range(1, GestureMapping.GestureCount - 1).Append(0).ToList();
            return order.Select(g => dominant[g])
                .Concat(order.Select(g => other[g]))
                .Concat(mapping.combos.Select(c => c.expressionId))
                .Select(set.FindExpression)
                .Where(e => e != null)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// メニューに置いた表情セットのジェスチャーに割り当てている表情か（＝「表情選択」に自動で並ぶ）。
        /// </summary>
        public static bool IsInGestureOfMenu(ExpressionSet set, string expressionId)
        {
            return set.menu
                .Where(n => n.kind == MenuNodeKind.GestureSet)
                .Select(n => set.FindGestureSet(n.gestureSetId))
                .Where(s => s != null)
                .Any(s => GestureExpressions(set, s).Any(e => e.id == expressionId));
        }

        /// <summary>
        /// 表情セットをメニューに置く。ジェスチャーに割り当てた表情は「表情選択」に自動で並ぶので、
        /// 固定だけの項目としては追加しない。既にメニューにある表情セットは作り直さない。
        /// </summary>
        public static void AddMenuFromSets(ExpressionSet set)
        {
            foreach (var gestureSet in set.gestureSets)
            {
                if (set.menu.Any(n => n.kind == MenuNodeKind.GestureSet && n.gestureSetId == gestureSet.id)) continue;
                InsertAtRootBeforeFolders(set, new MenuNode { kind = MenuNodeKind.GestureSet, gestureSetId = gestureSet.id });
            }
        }

        /// <summary>
        /// 旧形式（表情セットの「ジェスチャーで使う」と固定メニューのフォルダ）から、表情メニューを作る。
        /// メニューが空のときだけ行う。旧固定メニューのうち、ジェスチャーに割り当てていない表情は「Add」に置く。
        /// </summary>
        public static bool EnsureMenu(ExpressionSet set)
        {
            if (set.menu.Count > 0 || set.gestureSets.Count == 0 && set.fixedMenu.Count == 0) return false;

            foreach (var gestureSet in set.gestureSets.Where(s => s.useForGesture))
            {
                set.menu.Add(new MenuNode { kind = MenuNodeKind.GestureSet, gestureSetId = gestureSet.id });
            }

            var fixedOnly = set.fixedMenu
                .SelectMany(f => f.expressionIds)
                .Where(id => set.FindExpression(id) != null && !IsInGestureOfMenu(set, id))
                .Distinct()
                .ToList();
            if (fixedOnly.Count > 0)
            {
                var folder = new MenuNode { kind = MenuNodeKind.Folder, name = AddFolderName };
                set.menu.Add(folder);
                foreach (var id in fixedOnly)
                {
                    set.menu.Add(new MenuNode { kind = MenuNodeKind.Expression, expressionId = id, parentId = folder.id });
                }
            }

            set.fixedMenu.Clear();
            return true;
        }
        private static void InsertAtRootBeforeFolders(ExpressionSet set, MenuNode node)
        {
            var index = set.menu.FindIndex(n => string.IsNullOrEmpty(n.parentId) && n.kind == MenuNodeKind.Folder);
            if (index < 0) set.menu.Add(node);
            else set.menu.Insert(index, node);
        }
        /// <summary>
        /// 表情データを複製する。表情・表情セット・パーツの設定を引き継ぎ、表情のIDも同じものを使うので、
        /// 顔バリアントの差し替えは複製した表情データでもそのまま使える。
        /// </summary>
        public static ExpressionSet Duplicate(ExpressionSet source, string path)
        {
            var copy = Object.Instantiate(source);
            AssetDatabase.CreateAsset(copy, path);
            copy.independentClips = true;
            foreach (var expression in copy.expressions) MakeClipEditable(copy, expression);
            foreach (var part in copy.parts) MakePartClipEditable(copy, part);
            foreach (var auxiliary in new[] { copy.blinkAnimation, copy.mouthCancelAnimation }.Where(c => c != null).Distinct().ToList())
            {
                var clone = Object.Instantiate(auxiliary);
                AssetDatabase.CreateAsset(clone, AssetDatabase.GenerateUniqueAssetPath($"{AssetPathUtility.FolderOf(copy)}/{AssetPathUtility.SafeFileName(auxiliary.name)}.anim"));
                if (copy.blinkAnimation == auxiliary) copy.blinkAnimation = clone;
                if (copy.mouthCancelAnimation == auxiliary) copy.mouthCancelAnimation = clone;
            }
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssets();
            return copy;
        }

        /// <summary>
        /// 新しい表情クリップを 表情データのフォルダ/Expressions/ に作り、表情データに追加する。
        /// </summary>
        public static Expression CreateExpression(ExpressionSet set, string name, NewExpressionSource source,
            Expression copyFrom, GameObject avatarRoot, FaceVariant variant)
        {
            var folder = AssetPathUtility.EnsureFolder(AssetPathUtility.FolderOf(set), "Expressions");
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{AssetPathUtility.SafeFileName(name)}.anim");

            AnimationClip clip;
            if (source == NewExpressionSource.CopyExpression)
            {
                if (copyFrom == null) return null;
                var sourceClip = variant?.FindOverride(copyFrom.id)?.clip ?? copyFrom.clip;
                if (sourceClip == null || !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(sourceClip), path)) return null;
                clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            }
            else
            {
                clip = new AnimationClip();
                if (source == NewExpressionSource.CurrentFace) WriteCurrentFace(clip, set, avatarRoot, variant);
                AssetDatabase.CreateAsset(clip, path);
            }

            Undo.RecordObject(set, "新しい表情を作成");
            var expression = new Expression { name = name, clip = clip };
            set.expressions.Add(expression);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return expression;
        }

        /// <summary>
        /// 表情で動くシェイプキー（既存の表情が動かすものと、顔バリアントのベース顔）に、アバターの今の値を書き込む。
        /// ベース顔のシェイプキーは元Prefabの値にする（共有の表情なので。この顔の値はビルド時にベース顔として足される）。
        /// </summary>
        private static void WriteCurrentFace(AnimationClip clip, ExpressionSet set, GameObject avatarRoot, FaceVariant variant)
        {
            var keys = FaceVariantUtility.AnimatedBlendShapes(set);
            var baseFace = new Dictionary<(string, string), float>();
            if (variant != null)
            {
                foreach (var key in variant.baseFace.Where(k => k.enabled))
                {
                    keys.Add((key.path, key.blendShape));
                    baseFace[(key.path, key.blendShape)] = key.referenceValue;
                }
            }

            foreach (var (path, blendShape) in keys.OrderBy(k => k.path).ThenBy(k => k.blendShape))
            {
                var transform = string.IsNullOrEmpty(path) ? avatarRoot.transform : avatarRoot.transform.Find(path);
                var renderer = transform != null ? transform.GetComponent<SkinnedMeshRenderer>() : null;
                var index = renderer != null && renderer.sharedMesh != null ? renderer.sharedMesh.GetBlendShapeIndex(blendShape) : -1;
                if (index < 0) continue;

                var value = baseFace.TryGetValue((path, blendShape), out var reference) ? reference : renderer.GetBlendShapeWeight(index);
                var binding = EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + blendShape);
                AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(new Keyframe(0, value)));
            }
        }
    }
}
