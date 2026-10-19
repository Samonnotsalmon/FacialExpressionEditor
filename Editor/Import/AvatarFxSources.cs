using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using nadena.dev.modular_avatar.core;

namespace Samon.FacialExpressionEditor.Editor
{
    // 解析専用。元のコントローラ・ステート・クリップは変更しない。
    [InitializeOnLoad]
    internal static class AvatarFxSources
    {
        private static readonly Dictionary<int, AnimatorController> Cache = new Dictionary<int, AnimatorController>();
        static AvatarFxSources()
        {
            EditorApplication.projectChanged += Clear;
            EditorApplication.hierarchyChanged += Clear;
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            Undo.undoRedoPerformed += Clear;
        }
        // 取り込みの途中で表情のクリップを複製すると projectChanged が来るが、そのとき取り込み中の処理はまだ
        // 解析用FXを使っている。その場で壊すと続きが読めなくなる（MissingReferenceException）ので、
        // キャッシュからは外すだけにして、壊すのは今の処理が終わってからにする。
        private static void Clear()
        {
            var old = Cache.Values.Where(c => c != null).ToList();
            Cache.Clear();
            if (old.Count == 0) return;
            EditorApplication.delayCall += () =>
            {
                foreach (var controller in old) if (controller != null) Object.DestroyImmediate(controller);
            };
        }
        public static AnimatorController Get(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) return null;
            if (Cache.TryGetValue(descriptor.GetInstanceID(), out var cached) && cached != null) return cached;
            var sources = new List<AnimatorController>();
            if (descriptor.customizeAnimationLayers)
                sources.AddRange(descriptor.baseAnimationLayers.Where(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX && !l.isDefault)
                    .Select(l => l.animatorController as AnimatorController).Where(c => c != null));
            sources.AddRange(descriptor.GetComponentsInChildren<ModularAvatarMergeAnimator>(true)
                .Where(m => m.layerType == VRCAvatarDescriptor.AnimLayerType.FX)
                .Select(m => m.animator as AnimatorController).Where(c => c != null));
            if (sources.Count == 0) return null;
            var result = new AnimatorController { name = "表情解析用FX", hideFlags = HideFlags.HideAndDontSave };
            result.layers = sources.Distinct().SelectMany(c => c.layers).ToArray();
            result.parameters = sources.SelectMany(c => c.parameters).GroupBy(p => p.name).Select(g => g.First()).ToArray();
            Cache[descriptor.GetInstanceID()] = result;
            return result;
        }
    }
}
