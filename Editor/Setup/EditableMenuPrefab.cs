using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using Object = UnityEngine.Object;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class EditableMenuPrefab
    {
        private static FacialExpressionAvatar Source(FacialExpressionAvatar avatar) => EditorUtility.IsPersistent(avatar)
            ? avatar : PrefabUtility.GetCorrespondingObjectFromSource(avatar);

        public static GameObject Save(FacialExpressionAvatar avatar)
        {
            if (!avatar || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("編集モードで表情設定を開いてください。");
            var source = Source(avatar);
            if (!source)
                throw new InvalidOperationException("先にFEEの表情設定をPrefabとして保存してください。");
            var path = AssetDatabase.GetAssetPath(source);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Assets内の編集可能なFEE Prefabを使用してください。");
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == path)
                throw new InvalidOperationException("このPrefabをPrefab Modeで編集中です。保存してPrefab Modeを閉じ、アバター上のFEEから実行してください。");
            if (source.expressionSet != avatar.expressionSet)
                throw new InvalidOperationException("配置先とPrefabで表情設定が異なります。同じ表情設定のFEE Prefabから実行してください。");
            if (!avatar.expressionSet) throw new InvalidOperationException("表情設定を指定してください。");
            if (avatar.expressionSet.menuIcons && !AvatarSetup.AvatarRootOf(avatar))
                throw new InvalidOperationException("撮影するアバターが見つかりません。アバター上のFEEから実行してください。");

            var ownerPath = AnimationUtility.CalculateTransformPath(source.transform, source.transform.root);
            var instances = Resources.FindObjectsOfTypeAll<FacialExpressionAvatar>()
                .Where(a => !EditorUtility.IsPersistent(a) && a.gameObject.scene.IsValid() &&
                    !EditorSceneManager.IsPreviewScene(a.gameObject.scene) && Source(a) == source).ToArray();
            // Old scene-only exports must not remain alongside the shared prefab menu.
            var localMenus = instances.Select(a => a.editableMenu)
                .Where(m => m && PrefabUtility.IsAddedGameObjectOverride(m.gameObject)).Distinct().ToArray();
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(path);
                var target = (string.IsNullOrEmpty(ownerPath) ? contents.transform : contents.transform.Find(ownerPath))
                    ?.GetComponent<FacialExpressionAvatar>();
                if (!target) throw new InvalidOperationException("保存先Prefabの表情設定が見つかりません。");
                // Render using the chosen scene avatar, but edit only the loaded prefab's menu.
                EditableMenuExport.GenerateCaptured(target, avatar);
                PrefabUtility.SaveAsPrefabAsset(contents, path, out var success);
                if (!success) throw new InvalidOperationException("MAメニューをPrefabへ保存できませんでした。");
            }
            finally { if (contents) PrefabUtility.UnloadPrefabContents(contents); }

            foreach (var local in localMenus)
                if (local) Object.DestroyImmediate(local.gameObject);
            foreach (var instance in instances)
            {
                if (!instance) continue;
                var serialized = new SerializedObject(instance);
                var reference = serialized.FindProperty("editableMenu");
                if (reference.prefabOverride) PrefabUtility.RevertPropertyOverride(reference, InteractionMode.AutomatedAction);
                // The shared menu is authoritative. Clear only generated menu icon overrides,
                // leaving avatar-specific bindings, transforms and unrelated prefab overrides alone.
                if (instance.editableMenu)
                    foreach (var item in instance.editableMenu.GetComponentsInChildren<ModularAvatarMenuItem>(true))
                    {
                        var data = new SerializedObject(item);
                        var icon = data.FindProperty("Control.icon");
                        if (icon != null && icon.prefabOverride)
                            PrefabUtility.RevertPropertyOverride(icon, InteractionMode.AutomatedAction);
                    }
                EditorSceneManager.MarkSceneDirty(instance.gameObject.scene);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
    }
}
