using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// アバターの元FXから、ジェスチャーと表情の対応やパーツを表情セットへ取り込む。
    /// </summary>
    public static class FxImporter
    {
        private const string GestureLeft = "GestureLeft";
        private const string GestureRight = "GestureRight";

        public class Result
        {
            public readonly List<string> GestureLayers = new List<string>();
            public readonly List<string> GestureSets = new List<string>();
            public int AddedExpressions;
            public int AddedFolders;
        }

        public static AnimatorController GetFx(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null || !descriptor.customizeAnimationLayers) return null;
            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX && !layer.isDefault)
                {
                    return layer.animatorController as AnimatorController;
                }
            }
            return null;
        }

        /// <summary>
        /// 元FXのジェスチャーレイヤーを解析して、表情セット（ジェスチャーの組）と置き換えるレイヤーを設定し直す。
        /// ジェスチャー以外の条件（しなのの F_Set など）が違うものは、別の表情セットとして取り込む。
        /// 既存の表情は同じクリップなら再利用し、同じ名前の表情セットは使い方の設定を引き継ぐ。
        /// </summary>
        public static Result ImportGestures(VRCAvatarDescriptor descriptor, ExpressionSet set)
        {
            var result = new Result();
            var fx = GetFx(descriptor);
            if (fx == null) return result;

            var avatarRoot = descriptor.gameObject;
            var entries = new List<GestureEntry>();
            var lastLayerOfHand = new Dictionary<Hand, int>();

            for (var i = 0; i < fx.layers.Length; i++)
            {
                var layer = fx.layers[i];
                var layerEntries = CollectGestureEntries(layer.stateMachine)
                    .Where(e => !IsDummyClip(e.Clip, avatarRoot))
                    .ToList();
                if (layerEntries.Count == 0) continue;

                result.GestureLayers.Add(layer.name);
                foreach (var entry in layerEntries)
                {
                    lastLayerOfHand[entry.Hand] = i;
                    entries.Add(entry);
                }
            }

            Undo.RecordObject(set, "元FXから取り込み");

            // 後ろのレイヤーほど優先されるので、後ろにある方の手を優先する。
            var dominantHand = Hand.Right;
            if (lastLayerOfHand.TryGetValue(Hand.Left, out var leftIndex) &&
                lastLayerOfHand.TryGetValue(Hand.Right, out var rightIndex))
            {
                dominantHand = leftIndex > rightIndex ? Hand.Left : Hand.Right;
            }

            // ジェスチャー以外の条件ごとに組を分ける。条件の無いものは全部の組に入れる。
            var groups = entries
                .Where(e => e.ExtraConditions.Count > 0)
                .GroupBy(e => SignatureOf(e.ExtraConditions))
                .OrderBy(g => g.Key)
                .Select(g => g.First().ExtraConditions)
                .ToList();
            if (groups.Count == 0) groups.Add(new List<AnimatorCondition>());

            var menuNames = MenuToggleNames(descriptor.expressionsMenu);
            var previous = set.gestureSets;
            set.gestureSets = new List<GestureSet>();
            var added = new List<Expression>();

            for (var g = 0; g < groups.Count; g++)
            {
                var signature = SignatureOf(groups[g]);
                var name = NameFor(groups[g], menuNames) ?? $"セット{g + 1}";
                var old = previous.Find(s => s.name == name);
                var gestureSet = new GestureSet
                {
                    name = name,
                    useForGesture = old?.useForGesture ?? true,
                };
                gestureSet.mapping.dominantHand = dominantHand;

                foreach (var entry in entries)
                {
                    var entrySignature = SignatureOf(entry.ExtraConditions);
                    if (entrySignature != "" && entrySignature != signature) continue;

                    var table = entry.Hand == Hand.Left ? gestureSet.mapping.left : gestureSet.mapping.right;
                    var index = (int)entry.Gesture;
                    if (!string.IsNullOrEmpty(table[index])) continue;

                    var expression = ExpressionSetUtility.FindExpressionByClip(set, entry.Clip);
                    if (expression == null)
                    {
                        expression = new Expression { name = entry.Clip.name, clip = entry.Clip };
                        set.expressions.Add(expression);
                        added.Add(expression);
                        result.AddedExpressions++;
                    }

                    table[index] = expression.id;
                }

                set.gestureSets.Add(gestureSet);
                result.GestureSets.Add(name);
            }

            foreach (var entry in entries)
            {
                if (AddFolder(set, entry.Clip)) result.AddedFolders++;
            }

            // 表情メニューに表情セットを置き、各セットの表情を「表情固定」フォルダに並べる
            // （既にメニューにあるものや、同じ名前のフォルダは作り直さないので、並べ替えた状態は残る）。
            ExpressionSetUtility.AddMenuFromSets(set);

            set.originalGestureLayers = new List<string>(result.GestureLayers);

            // 新しく追加した表情のまばたき・リップシンクは、元FXの指定に合わせる（既存の表情の設定は変えない）。
            ImportFaceControl(descriptor, set, added);
            set.faceControlImported = true;

            EditorUtility.SetDirty(set);
            return result;
        }

        /// <summary>
        /// 元FXのジェスチャーレイヤーのステートにある視線・リップシンクの指定から、表情ごとのまばたき・リップシンクを設定する。
        /// 元FXで目をアニメーションにしていた表情（しなのなど）は、まばたきを止める表情として取り込む（視線は動かしたままにする）。
        /// only を指定したときは、その表情だけを設定する。戻り値は設定を変えた表情の数。
        /// </summary>
        public static int ImportFaceControl(VRCAvatarDescriptor descriptor, ExpressionSet set, IEnumerable<Expression> only = null)
        {
            var fx = GetFx(descriptor);
            if (fx == null) return 0;

            var found = new HashSet<AnimationClip>();
            var eyesOff = new HashSet<AnimationClip>();
            var mouthOff = new HashSet<AnimationClip>();
            foreach (var layer in fx.layers.Where(l => set.originalGestureLayers.Contains(l.name)))
            {
                foreach (var state in AllStates(layer.stateMachine))
                {
                    var clip = FirstClip(state.motion);
                    if (clip == null) continue;

                    found.Add(clip);
                    foreach (var tracking in state.behaviours.OfType<VRCAnimatorTrackingControl>())
                    {
                        if (tracking.trackingEyes == VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.Animation) eyesOff.Add(clip);
                        if (tracking.trackingMouth == VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.Animation) mouthOff.Add(clip);
                    }
                }
            }

            Undo.RecordObject(set, "まばたき・リップシンクの設定を取り込み");
            var changed = 0;
            foreach (var expression in only ?? set.expressions)
            {
                // 編集のために複製した表情は、元のクリップで元FXと照らし合わせる。
                var clip = expression.originalClip != null ? expression.originalClip : expression.clip;
                if (clip == null || !found.Contains(clip)) continue;

                var blink = !eyesOff.Contains(clip);
                var lipSync = !mouthOff.Contains(clip);
                if (expression.enableBlink == blink && expression.enableLipSync == lipSync) continue;

                expression.enableBlink = blink;
                expression.enableLipSync = lipSync;
                changed++;
            }
            EditorUtility.SetDirty(set);
            return changed;
        }

        /// <summary>
        /// 指定した元FXレイヤーの各ステートのクリップを、パーツとして取り込む。
        /// そのレイヤーはビルド時に取り除き、生成したパーツレイヤーに置き換える。戻り値は追加したパーツの数。
        /// </summary>
        public static int ImportPartsFromLayer(VRCAvatarDescriptor descriptor, ExpressionSet set, string layerName)
        {
            var fx = GetFx(descriptor);
            var layer = fx?.layers.FirstOrDefault(l => l.name == layerName);
            if (layer == null) return 0;

            Undo.RecordObject(set, "パーツを取り込み");

            var added = 0;
            foreach (var state in AllStates(layer.stateMachine))
            {
                if (!(state.motion is AnimationClip clip) || IsDummyClip(clip, descriptor.gameObject)) continue;
                if (set.parts.Any(p => p.clip == clip)) continue;

                set.parts.Add(new FacialPart { name = state.name, clip = clip, properties = DefaultPartProperties(clip) });
                added++;
            }

            if (!set.originalPartLayers.Contains(layerName)) set.originalPartLayers.Add(layerName);

            EditorUtility.SetDirty(set);
            return added;
        }

        /// <summary>
        /// パーツとして動かすプロパティの初期値。0以外の値を持つカーブと、オブジェクト参照のカーブ。
        /// </summary>
        public static List<string> DefaultPartProperties(AnimationClip clip)
        {
            var result = new List<string>();
            if (clip == null) return result;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null && curve.keys.Any(k => Mathf.Abs(k.value) > 0.0001f))
                {
                    result.Add(ExpressionClipBuilder.Key(binding));
                }
            }

            result.AddRange(AnimationUtility.GetObjectReferenceCurveBindings(clip).Select(ExpressionClipBuilder.Key));
            return result;
        }

        public static bool AddFolder(ExpressionSet set, Object asset)
        {
            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return false;

            var folder = AssetDatabase.IsValidFolder(path) ? path : Path.GetDirectoryName(path)?.Replace('\\', '/');
            var guid = AssetDatabase.AssetPathToGUID(folder);
            if (string.IsNullOrEmpty(guid) || set.libraryFolderGuids.Contains(guid)) return false;

            set.libraryFolderGuids.Add(guid);
            return true;
        }

        /// <summary>
        /// カーブが無い、またはアバター上に存在しないオブジェクトしか動かさないクリップ（ダミー）かどうか。
        /// </summary>
        public static bool IsDummyClip(AnimationClip clip, GameObject avatarRoot)
        {
            if (clip == null) return true;
            var bindings = AnimationUtility.GetCurveBindings(clip)
                .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip));
            return bindings.All(b => b.path != "" && avatarRoot.transform.Find(b.path) == null);
        }

        private class GestureEntry
        {
            public Hand Hand;
            public HandGesture Gesture;
            public AnimationClip Clip;
            public List<AnimatorCondition> ExtraConditions;
        }

        private static IEnumerable<GestureEntry> CollectGestureEntries(AnimatorStateMachine root)
        {
            foreach (var transition in AllTransitions(root))
            {
                if (transition.destinationState == null) continue;

                var gestureCondition = transition.conditions.FirstOrDefault(c =>
                    (c.parameter == GestureLeft || c.parameter == GestureRight) && c.mode == AnimatorConditionMode.Equals);
                if (gestureCondition.parameter == null) continue;

                var clip = FirstClip(transition.destinationState.motion);
                if (clip == null) continue;

                yield return new GestureEntry
                {
                    Hand = gestureCondition.parameter == GestureLeft ? Hand.Left : Hand.Right,
                    Gesture = (HandGesture)Mathf.RoundToInt(gestureCondition.threshold),
                    Clip = clip,
                    ExtraConditions = transition.conditions.Where(c => c.parameter != gestureCondition.parameter).ToList(),
                };
            }
        }

        private static string SignatureOf(IEnumerable<AnimatorCondition> conditions)
        {
            return string.Join(" & ", conditions
                .Select(c => $"{c.parameter} {c.mode} {c.threshold:000.###}")
                .OrderBy(s => s));
        }

        /// <summary>
        /// 条件が「パラメータ = 値」1つだけなら、同じ値にするメニューのトグル名を表情セット名に使う。
        /// </summary>
        private static string NameFor(List<AnimatorCondition> conditions,
            Dictionary<(string, int), string> menuNames)
        {
            if (conditions.Count != 1) return null;
            var c = conditions[0];
            int value;
            switch (c.mode)
            {
                case AnimatorConditionMode.Equals: value = Mathf.RoundToInt(c.threshold); break;
                case AnimatorConditionMode.If: value = 1; break;
                case AnimatorConditionMode.IfNot: value = 0; break;
                default: return null;
            }
            return menuNames.TryGetValue((c.parameter, value), out var name) ? name : null;
        }

        private static Dictionary<(string, int), string> MenuToggleNames(VRCExpressionsMenu root)
        {
            var result = new Dictionary<(string, int), string>();
            var visited = new HashSet<VRCExpressionsMenu>();

            void Walk(VRCExpressionsMenu menu)
            {
                if (menu == null || !visited.Add(menu)) return;
                foreach (var control in menu.controls)
                {
                    if (control.parameter != null && !string.IsNullOrEmpty(control.parameter.name))
                    {
                        var key = (control.parameter.name, Mathf.RoundToInt(control.value));
                        if (!result.ContainsKey(key)) result[key] = control.name;
                    }
                    Walk(control.subMenu);
                }
            }

            Walk(root);
            return result;
        }

        private static AnimationClip FirstClip(Motion motion)
        {
            switch (motion)
            {
                case AnimationClip clip:
                    return clip;
                case BlendTree tree:
                    return tree.children.Select(c => FirstClip(c.motion)).FirstOrDefault(c => c != null);
                default:
                    return null;
            }
        }

        internal static IEnumerable<AnimatorStateTransition> AllTransitions(AnimatorStateMachine stateMachine)
        {
            foreach (var t in stateMachine.anyStateTransitions) yield return t;
            foreach (var child in stateMachine.states)
            {
                foreach (var t in child.state.transitions) yield return t;
            }
            foreach (var child in stateMachine.stateMachines)
            {
                foreach (var t in AllTransitions(child.stateMachine)) yield return t;
            }
        }

        internal static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine stateMachine)
        {
            foreach (var child in stateMachine.states) yield return child.state;
            foreach (var child in stateMachine.stateMachines)
            {
                foreach (var s in AllStates(child.stateMachine)) yield return s;
            }
        }
    }
}
