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
        /// 割り当てと保存済みの顔を引き継いだ独立Prefabを作り、選択中のシーン設定だけを切り替える。
        /// folder は新しい設定専用の空フォルダ。元アセット・他のインスタンスは変更しない。
        /// </summary>
        public static FacialExpressionAvatar DuplicateForAvatar(FacialExpressionAvatar source, string folder, string name)
        {
            if (source == null || source.expressionSet == null) throw new System.ArgumentException("複製元の表情設定がありません。");
            var copy = ExpressionSetUtility.Duplicate(source.expressionSet, $"{folder}/{name}_表情データ.asset");
            FaceVariant baseline = null;
            if (source.faceVariant != null)
            {
                baseline = Object.Instantiate(source.faceVariant);
                AssetDatabase.CreateAsset(baseline, $"{folder}/{name}_ベース顔.asset");
                foreach (var replacement in baseline.overrides.Where(o => o.clip != null))
                {
                    var clip = Object.Instantiate(replacement.clip);
                    var clipsFolder = AssetPathUtility.EnsureFolder(folder, "Overrides");
                    AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath($"{clipsFolder}/{AssetPathUtility.SafeFileName(clip.name)}.anim"));
                    replacement.clip = clip;
                }
                EditorUtility.SetDirty(baseline);
            }
            var sourceAvatar = PrefabOf(AvatarRootOf(source)) ?? source.sourceAvatar;
            var prefab = CreateSetupPrefab(folder, name, copy, baseline, sourceAvatar);
            if (prefab == null) throw new System.InvalidOperationException("専用Prefabを作成できませんでした。");
            AssetDatabase.SaveAssets();

            // Project / Prefab Modeでは元Prefabを書き換えず、新しいPrefabを開く。
            if (!IsInScene(source) || PrefabStageUtility.GetPrefabStage(source.gameObject) != null) return prefab;

            const string undoName = "専用の表情設定Prefabに切り替え";
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            FacialExpressionAvatar applied;
            var go = source.gameObject;
            var standalone = go.GetComponent<VRCAvatarDescriptor>() == null && go.transform.childCount == 0 &&
                go.GetComponents<Component>().All(c => c is Transform || c is FacialExpressionAvatar);
            if (standalone && PrefabUtility.IsAnyPrefabInstanceRoot(go))
            {
                PrefabUtility.ReplacePrefabAssetOfPrefabInstance(go, prefab.gameObject, new PrefabReplacingSettings
                {
                    objectMatchMode = ObjectMatchMode.ByHierarchy,
                    prefabOverridesOptions = PrefabOverridesOptions.KeepAllPossibleOverrides,
                    changeRootNameToAssetName = true,
                }, InteractionMode.UserAction);
                applied = go.GetComponent<FacialExpressionAvatar>();
                // 旧インスタンスで上書きしていた参照も、新しいPrefabの参照へ切り替える。
                Undo.RecordObject(applied, undoName);
                applied.expressionSet = copy;
                applied.faceVariant = baseline;
                applied.sourceAvatar = sourceAvatar;
                PrefabUtility.RecordPrefabInstancePropertyModifications(applied);
            }
            else
            {
                // アバター直付けや他のコンポーネントを含む場合は、元オブジェクトを残して設定だけ移す。
                applied = PlaceInAvatar(prefab, go);
                Undo.RecordObject(applied, undoName);
                applied.enabled = source.enabled;
                PrefabUtility.RecordPrefabInstancePropertyModifications(applied);
                Undo.DestroyObjectImmediate(source);
            }
            EditorSceneManager.MarkSceneDirty(applied.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            return applied;
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
        /// 元設定の隣に専用フォルダを作る。同名があれば番号を付け、既存設定は上書きしない。
        /// </summary>
        public static string DedicatedSetupName(FacialExpressionAvatar source)
        {
            var name = AssetPathUtility.SafeFileName(source.gameObject.name);
            if (name.EndsWith("_FEE")) name = name.Substring(0, name.Length - 4);
            return name.EndsWith("_専用") ? name : name + "_専用";
        }

        public static string ValidateSetupName(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return "名前を入力してください。";
            if (AssetPathUtility.SafeFileName(name) != name || name.EndsWith(".") ||
                System.Text.RegularExpressions.Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return "フォルダ名に使える名前を入力してください。";
            return null;
        }

        public static string DedicatedSetupFolder(FacialExpressionAvatar source, string name)
        {
            var error = ValidateSetupName(name);
            if (error != null) throw new System.ArgumentException(error);
            var originalFolder = AssetPathUtility.FolderOf(source.expressionSet);
            var parent = Path.GetDirectoryName(originalFolder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) parent = "Assets";
            if (parent != "Assets" && !parent.StartsWith("Assets/")) parent = DataFolder;
            return AssetDatabase.GenerateUniqueAssetPath(parent + "/" + name.Trim());
        }

        public static FacialExpressionAvatar DuplicateBesideSource(FacialExpressionAvatar source, string name = null)
        {
            var folder = DedicatedSetupFolder(source, name ?? DedicatedSetupName(source));
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) AssetPathUtility.EnsureFolder("Assets", parent.Substring(7).Split('/'));
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            return DuplicateForAvatar(source, folder, Path.GetFileName(folder) + "_FEE");
        }
    }
}
