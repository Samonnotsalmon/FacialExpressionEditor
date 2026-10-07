using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情設定（FacialExpressionAvatar）とアバターの関係。
    /// 表情設定はプレハブにして各アバターの中へ入れる。表情エディタでは、アバターの中にあればそのアバター、
    /// プロジェクトのプレハブなら「編集に使うアバター」でプレビューし、元FXを読む。
    /// </summary>
    internal static class AvatarSetup
    {
        public const string DataFolder = "Assets/FacialExpressionEditor";

        /// <summary>
        /// 表情設定のアバター。アバターの中（ルートを含む）にあればそのアバター、無ければ編集に使うアバター。
        /// </summary>
        public static GameObject AvatarRootOf(FacialExpressionAvatar setup)
        {
            if (setup == null) return null;
            var descriptor = setup.GetComponentInParent<VRCAvatarDescriptor>(true);
            return descriptor != null ? descriptor.gameObject : setup.sourceAvatar;
        }

        /// <summary>
        /// ドロップしたもの（Hierarchy のオブジェクトか、Project のプレハブ）のアバターのルート。アバターでなければ null。
        /// </summary>
        public static GameObject FindAvatarRoot(Object dropped)
        {
            var gameObject = dropped as GameObject ?? (dropped as Component)?.gameObject;
            if (gameObject == null) return null;
            var descriptor = gameObject.GetComponentInParent<VRCAvatarDescriptor>(true);
            return descriptor != null ? descriptor.gameObject : null;
        }

        public static bool IsInScene(Object obj)
        {
            var gameObject = obj as GameObject ?? (obj as Component)?.gameObject;
            return gameObject != null && !EditorUtility.IsPersistent(gameObject) && gameObject.scene.IsValid();
        }

        /// <summary>
        /// 顔のメッシュ（アバターの設定のリップシンクのメッシュ。無ければシェイプキーがいちばん多いメッシュ）。
        /// </summary>
        public static SkinnedMeshRenderer FaceRenderer(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor != null && descriptor.VisemeSkinnedMesh != null) return descriptor.VisemeSkinnedMesh;
            return avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null)
                .OrderByDescending(r => r.sharedMesh.blendShapeCount)
                .FirstOrDefault();
        }

        public static string DisplayName(FacialExpressionAvatar setup)
        {
            if (setup == null) return "";
            if (!IsInScene(setup)) return setup.gameObject.name;
            var root = AvatarRootOf(setup);
            return $"{(root != null ? root.name : setup.gameObject.name)}（シーン：{setup.gameObject.scene.name}）";
        }

        /// <summary>
        /// プロジェクトの表情設定のプレハブ（表情データのフォルダにあるもの）と、開いているシーンの表情設定。プレハブが先。
        /// </summary>
        public static List<FacialExpressionAvatar> FindSetups()
        {
            var folders = AssetDatabase.FindAssets("t:" + nameof(ExpressionSet))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(p => Path.GetDirectoryName(p)?.Replace('\\', '/'))
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct()
                .ToArray();

            var result = new List<FacialExpressionAvatar>();
            if (folders.Length > 0)
            {
                result.AddRange(AssetDatabase.FindAssets("t:Prefab", folders)
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Distinct()
                    .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                    .Where(g => g != null)
                    .Select(g => g.GetComponent<FacialExpressionAvatar>())
                    .Where(s => s != null)
                    .OrderBy(s => s.gameObject.name));
            }
            result.AddRange(Object.FindObjectsByType<FacialExpressionAvatar>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(IsInScene)
                .OrderBy(s => s.gameObject.scene.name)
                .ThenBy(DisplayName));
            return result;
        }

        /// <summary>
        /// 表情設定のプレハブを folder に作る（シーンを変更しないよう、プレビュー用のシーンで組み立てる）。
        /// </summary>
        public static FacialExpressionAvatar CreateSetupPrefab(string folder, string name, ExpressionSet set, FaceVariant variant,
            GameObject sourceAvatar)
        {
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{AssetPathUtility.SafeFileName(name, "表情設定")}.prefab");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var gameObject = new GameObject(Path.GetFileNameWithoutExtension(path));
                SceneManager.MoveGameObjectToScene(gameObject, scene);
                var setup = gameObject.AddComponent<FacialExpressionAvatar>();
                setup.expressionSet = set;
                setup.faceVariant = variant;
                setup.sourceAvatar = sourceAvatar;
                var prefab = PrefabUtility.SaveAsPrefabAsset(gameObject, path);
                return prefab != null ? prefab.GetComponent<FacialExpressionAvatar>() : null;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// 表情設定のプレハブを、シーンのアバターの中に入れる（Ctrl+Z で戻せる）。
        /// </summary>
        public static FacialExpressionAvatar PlaceInAvatar(FacialExpressionAvatar prefab, GameObject avatarRoot)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, avatarRoot.transform);
            Undo.RegisterCreatedObjectUndo(instance, "表情設定をアバターに入れる");
            return instance.GetComponent<FacialExpressionAvatar>();
        }

        /// <summary>
        /// プレハブ（シーンのアバターなら、その元のプレハブ）。編集に使うアバターとして表情設定に覚えておく。
        /// </summary>
        public static GameObject PrefabOf(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;
            if (EditorUtility.IsPersistent(avatarRoot)) return avatarRoot;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(avatarRoot);
            return source != null && PrefabUtility.GetPrefabAssetType(source) != PrefabAssetType.Model ? source : null;
        }

        /// <summary>
        /// 新しい表情データの保存先（Assets/FacialExpressionEditor/アバター名/。既にあれば番号を付ける）。
        /// </summary>
        public static string DefaultDataFolder(string avatarName)
        {
            var name = AssetPathUtility.SafeFileName(avatarName, "Avatar");
            var folder = $"{DataFolder}/{name}";
            for (var i = 2; AssetDatabase.IsValidFolder(folder); i++) folder = $"{DataFolder}/{name} {i}";
            return folder;
        }
    }
}
