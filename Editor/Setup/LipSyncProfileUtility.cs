using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Samon.FacialExpressionEditor.Editor
{
    [InitializeOnLoad]
    internal static class LipSyncProfileUtility
    {
        private static readonly Dictionary<int, (string stamp, LipSyncSwitchSettings config)> Cache = new Dictionary<int, (string, LipSyncSwitchSettings)>();
        static LipSyncProfileUtility() { EditorApplication.hierarchyChanged += Cache.Clear; Undo.undoRedoPerformed += Cache.Clear; }
        public static LipSyncSwitchSettings Copy(LipSyncSwitchSettings value) => JsonUtility.FromJson<LipSyncSwitchSettings>(JsonUtility.ToJson(value));
        public static string Stamp(LipSyncProfile profile)
        {
            if (!profile) return "";
            var path=AssetDatabase.GetAssetPath(profile);
            return string.IsNullOrEmpty(path) ? EditorJsonUtility.ToJson(profile) : AssetDatabase.GetAssetDependencyHash(path).ToString();
        }
        public static FacialExpressionAvatar Prefab(FacialExpressionAvatar owner) => EditorUtility.IsPersistent(owner)
            ? owner : PrefabUtility.GetCorrespondingObjectFromSource(owner);

        private static string Path(GameObject root, Transform target)
        {
            if (!target) return "";
            if (!root || !target.IsChildOf(root.transform)) throw new InvalidOperationException("参照先は配置先アバター内から指定してください。");
            var path = target == root.transform ? "." : AnimationUtility.CalculateTransformPath(target, root.transform);
            if (Find(root, path) != target) throw new InvalidOperationException("同名階層があるため参照を保存できません：" + path);
            return path;
        }
        private static Transform Find(GameObject root, string path)
        {
            if (!root || string.IsNullOrEmpty(path)) return null;
            var current = root.transform;
            if (path == ".") return current;
            foreach (var name in path.Split('/'))
            {
                var matches = current.Cast<Transform>().Where(t => t.name == name).ToArray();
                if (matches.Length != 1) return null;
                current = matches[0];
            }
            return current;
        }
        public static LipSyncSwitchSettings Resolve(FacialExpressionAvatar owner)
        {
            if (!owner) return null;
            var profile = owner.lipSyncProfile;
            if (!profile) return owner.lipSyncSwitch;
            var root = AvatarSetup.AvatarRootOf(owner);
            var stamp = profile.GetInstanceID()+":"+EditorUtility.GetDirtyCount(profile)+":"+(root ? root.GetInstanceID() : 0)+":"+
                string.Join(";",owner.lipSyncMeshBindings.Select(b=>b.targetId+":"+(b.mesh ? b.mesh.GetInstanceID() : 0)));
            if (Cache.TryGetValue(owner.GetInstanceID(),out var cached) && cached.stamp == stamp) return cached.config;
            var config = Copy(profile.settings);
            foreach (var target in config.targets)
            {
                var paths = profile.references.Find(p=>p.targetId == target.id);
                var binding = owner.lipSyncMeshBindings.Find(b=>b.targetId == target.id);
                target.mesh = binding != null ? binding.mesh : Find(root,paths?.mesh)?.GetComponent<SkinnedMeshRenderer>();
                if (target.mesh && (!root || !target.mesh.transform.IsChildOf(root.transform))) target.mesh = null;
                target.photoFocus = Find(root,paths?.photoFocus);
                target.photoRoot = Find(root,paths?.photoRoot)?.gameObject;
            }
            Cache[owner.GetInstanceID()] = (stamp,config);
            return config;
        }
        public static void Backup(FacialExpressionAvatar owner)
        {
            if (owner.hasLipSyncMigrationBackup) return;
            owner.lipSyncMigrationBackup = Copy(owner.lipSyncSwitch);
            owner.hasLipSyncMigrationBackup = true;
        }
        public static void LoadDraft(FacialExpressionAvatar owner)
        {
            if (!owner.lipSyncProfile) return;
            Undo.RecordObject(owner,"Prefabのリップシンク設定を読み込む");
            Backup(owner);
            owner.lipSyncSwitch = Copy(Resolve(owner));
            owner.lipSyncDraftProfileStamp = Stamp(owner.lipSyncProfile);
            Dirty(owner);
        }
        public static void EnsureDraft(FacialExpressionAvatar owner)
        {
            if (owner.lipSyncProfile && string.IsNullOrEmpty(owner.lipSyncDraftProfileStamp)) LoadDraft(owner);
        }
        public static void Dirty(FacialExpressionAvatar owner)
        {
            Cache.Remove(owner.GetInstanceID());
            EditorUtility.SetDirty(owner);
            if (PrefabUtility.IsPartOfPrefabInstance(owner)) PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
        }
        public static void Bind(FacialExpressionAvatar owner, string id, SkinnedMeshRenderer mesh)
        {
            var root = AvatarSetup.AvatarRootOf(owner);
            if (mesh && (!root || !mesh.transform.IsChildOf(root.transform))) throw new InvalidOperationException("このアバター内の顔メッシュを指定してください。");
            Undo.RecordObject(owner,"リップシンクのメッシュを割り当て");
            owner.lipSyncMeshBindings.RemoveAll(b=>b.targetId==id);
            owner.lipSyncMeshBindings.Add(new LipSyncMeshBinding{targetId=id,mesh=mesh});
            var target = owner.lipSyncSwitch.targets.Find(t=>t.id==id);
            if (target != null) target.mesh = mesh;
            Dirty(owner);
        }
        public static LipSyncProfile Save(FacialExpressionAvatar owner)
        {
            var prefab = Prefab(owner);
            if (!prefab) throw new InvalidOperationException("先に表情設定をPrefabとして保存してください。");
            var path = AssetDatabase.GetAssetPath(prefab);
            var guid = AssetDatabase.AssetPathToGUID(path);
            var root = AvatarSetup.AvatarRootOf(owner);
            var config = Copy(owner.lipSyncSwitch);
            LipSyncSetup.Upgrade(config,root ? root.GetComponent<VRCAvatarDescriptor>() : null);
            var references = new List<LipSyncReferencePaths>();
            var bindings = new List<LipSyncMeshBinding>();
            foreach (var target in config.targets)
            {
                if (string.IsNullOrEmpty(target.id)) target.id = Guid.NewGuid().ToString("N");
                if (config.enabled && !target.mesh) throw new InvalidOperationException(target.label+"の顔メッシュを指定してください。");
                var meshPath = Path(root,target.mesh ? target.mesh.transform : null);
                var existingPath = owner.lipSyncProfile ? owner.lipSyncProfile.references.Find(p=>p.targetId==target.id)?.mesh : null;
                references.Add(new LipSyncReferencePaths{targetId=target.id,mesh=existingPath ?? meshPath,photoFocus=Path(root,target.photoFocus),photoRoot=Path(root,target.photoRoot ? target.photoRoot.transform : null)});
                bindings.Add(new LipSyncMeshBinding{targetId=target.id,mesh=target.mesh});
                target.mesh=null;target.photoRoot=null;target.photoFocus=null;target.visibilityTarget=null;
            }
            config.avatarMesh=null;config.mascotMesh=null;config.visibilityTarget=null;
            var profile = prefab.lipSyncProfile;
            if (!profile || profile.ownerPrefabGuid != guid)
            {
                profile = ScriptableObject.CreateInstance<LipSyncProfile>();
                var directory = AssetPathUtility.EnsureFolder(AssetPathUtility.FolderOf(prefab),"Settings");
                AssetDatabase.CreateAsset(profile,AssetPathUtility.UniqueAssetPath(directory+"/"+prefab.name+"_LipSync.asset"));
            }
            Undo.RecordObject(profile,"Prefabへリップシンク設定を保存");
            profile.settings=config;profile.references=references;profile.ownerPrefabGuid=guid;
            EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
            prefab.lipSyncProfile=profile;EditorUtility.SetDirty(prefab);PrefabUtility.SavePrefabAsset(prefab.transform.root.gameObject);
            Undo.RecordObject(owner,"Prefabのリップシンク設定を適用");Backup(owner);
            owner.lipSyncProfile=profile;
            if (!EditorUtility.IsPersistent(owner)) owner.lipSyncMeshBindings=bindings;
            Cache.Clear();LoadDraft(owner);Dirty(owner);
            return profile;
        }
        public static void PrepareBuild(GameObject root)
        {
            foreach (var owner in root.GetComponentsInChildren<FacialExpressionAvatar>(true))
            {
                if (!owner.lipSyncProfile) continue;
                var config = Copy(Resolve(owner));
                if (config.enabled)
                    foreach(var target in config.targets)
                    {
                        if (!target.mesh) throw new InvalidOperationException("リップシンク対象が見つかりません："+target.label+"。Inspectorで顔メッシュを割り当ててください。");
                        var paths=owner.lipSyncProfile.references.Find(p=>p.targetId==target.id);
                        if ((!string.IsNullOrEmpty(paths?.photoFocus)&&!target.photoFocus)||(!string.IsNullOrEmpty(paths?.photoRoot)&&!target.photoRoot))
                            throw new InvalidOperationException("撮影対象が見つかりません："+target.label+"。詳細設定の撮影対象を確認してください。");
                    }
                owner.lipSyncSwitch=config;owner.lipSyncProfile=null; // Only the NDMF build clone.
            }
        }
        public static void Duplicate(FacialExpressionAvatar source, FacialExpressionAvatar destination)
        {
            LipSyncProfile profile = null;
            if (source.lipSyncProfile) profile=Object.Instantiate(source.lipSyncProfile);
            else if (source.lipSyncSwitch.enabled || source.lipSyncSwitch.targets.Count > 0)
            {
                var config=Copy(source.lipSyncSwitch);
                var root=AvatarSetup.AvatarRootOf(source);
                LipSyncSetup.Upgrade(config,root ? root.GetComponent<VRCAvatarDescriptor>() : null);
                var references=new List<LipSyncReferencePaths>();
                foreach(var target in config.targets)
                {
                    if(string.IsNullOrEmpty(target.id))target.id=Guid.NewGuid().ToString("N");
                    references.Add(new LipSyncReferencePaths{targetId=target.id,mesh=Path(root,target.mesh ? target.mesh.transform : null),photoFocus=Path(root,target.photoFocus),photoRoot=Path(root,target.photoRoot ? target.photoRoot.transform : null)});
                    target.mesh=null;target.photoFocus=null;target.photoRoot=null;target.visibilityTarget=null;
                }
                config.avatarMesh=null;config.mascotMesh=null;config.visibilityTarget=null;
                profile=ScriptableObject.CreateInstance<LipSyncProfile>();profile.settings=config;profile.references=references;
            }
            if(profile)
            {
                profile.ownerPrefabGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(destination));
                var folder=AssetPathUtility.EnsureFolder(AssetPathUtility.FolderOf(destination),"Settings");
                AssetDatabase.CreateAsset(profile,AssetPathUtility.UniqueAssetPath(folder+"/"+destination.name+"_LipSync.asset"));
                destination.lipSyncProfile=profile;
                EditorUtility.SetDirty(destination);PrefabUtility.SavePrefabAsset(destination.transform.root.gameObject);
            }
        }
    }
}
