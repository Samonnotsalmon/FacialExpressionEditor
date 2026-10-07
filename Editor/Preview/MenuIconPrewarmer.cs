using System;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// プレイボタンを押した直後（まだ編集モードのうち）に、シーン上のアバターのメニューのアイコンを用意しておく。
    /// プレイモードに入るときのビルドではアイコンを描けないので、ここで描いた Library/ のキャッシュを使う。
    /// </summary>
    [InitializeOnLoad]
    internal static class MenuIconPrewarmer
    {
        static MenuIconPrewarmer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;

            foreach (var avatar in UnityEngine.Object.FindObjectsByType<FacialExpressionAvatar>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var set = avatar.expressionSet;
                if (set == null || !set.menuIcons) continue;

                // ビルドされるのは、アバターの中に入っている表情設定だけ。
                var root = avatar.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true);
                if (root == null) continue;

                try
                {
                    MenuIcons.EnsureCached(root.gameObject, set, avatar.faceVariant);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FacialExpressionEditor] {avatar.name} のメニューのアイコンを用意できませんでした：{e.Message}");
                }
            }
        }
    }
}
