using System;
using System.Collections.Generic;
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
        private const string SessionKey = "Samon.FacialExpressionEditor.PlayMenuIcons";

        [Serializable]
        private sealed class Snapshot
        {
            public string token;
            public List<string> ids = new List<string>();
            public List<string> keys = new List<string>();
        }

        // Scene reload restores serialized scene data, discarding changes made in
        // ExitingEditMode. Keep a second reference outside the scene for that case.
        // Sibling indices distinguish identically named avatars, while ignoring the
        // temporary "(Clone)" rename made by NDMF's Apply on Play.
        private static string LocationKey(FacialExpressionAvatar avatar)
        {
            var indices = new List<int>();
            for (var t = avatar.transform; t != null; t = t.parent) indices.Add(t.GetSiblingIndex());
            var scene = avatar.gameObject.scene;
            return SessionKey + ".location|" + scene.path + "|" + scene.name + "|" + string.Join("/", indices)
                + "|" + AssetDatabase.GetAssetPath(avatar.expressionSet)
                + "|" + AssetDatabase.GetAssetPath(avatar.faceVariant);
        }

        // SessionState survives domain reloads. Each scene component carries a unique
        // token through scene reloads/build clones, so avatars sharing a set cannot mix photos.
        internal static void Capture(FacialExpressionAvatar avatar, Dictionary<string, string> keys)
        {
            var snapshot = new Snapshot { token = Guid.NewGuid().ToString("N") };
            foreach (var pair in keys)
            {
                snapshot.ids.Add(pair.Key);
                snapshot.keys.Add(pair.Value);
            }
            SessionState.SetString(SessionKey + snapshot.token, JsonUtility.ToJson(snapshot));
            SessionState.SetString(LocationKey(avatar), snapshot.token);
            avatar.playModeMenuIconToken = snapshot.token;
        }

        internal static Dictionary<string, string> Read(FacialExpressionAvatar avatar)
        {
            if (avatar == null) return null;
            var token = avatar.playModeMenuIconToken;
            if (string.IsNullOrEmpty(token)) token = SessionState.GetString(LocationKey(avatar), "");
            if (string.IsNullOrEmpty(token)) return null;
            var json = SessionState.GetString(SessionKey + token, "");
            if (string.IsNullOrEmpty(json)) return null;
            var snapshot = JsonUtility.FromJson<Snapshot>(json);
            if (snapshot == null || snapshot.token != token || snapshot.ids.Count != snapshot.keys.Count)
                return null;
            var result = new Dictionary<string, string>();
            for (var i = 0; i < snapshot.ids.Count; i++) result[snapshot.ids[i]] = snapshot.keys[i];
            return result;
        }

        internal static void Clear(FacialExpressionAvatar avatar)
        {
            if (!string.IsNullOrEmpty(avatar.playModeMenuIconToken))
                SessionState.EraseString(SessionKey + avatar.playModeMenuIconToken);
            var location = LocationKey(avatar);
            var token = SessionState.GetString(location, "");
            if (!string.IsNullOrEmpty(token)) SessionState.EraseString(SessionKey + token);
            SessionState.EraseString(location);
            avatar.playModeMenuIconToken = null;
        }

        static MenuIconPrewarmer()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                foreach (var avatar in UnityEngine.Object.FindObjectsByType<FacialExpressionAvatar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    Clear(avatar);
                return;
            }
            if (change != PlayModeStateChange.ExitingEditMode) return;

            foreach (var avatar in UnityEngine.Object.FindObjectsByType<FacialExpressionAvatar>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Clear(avatar);
                var set = avatar.expressionSet;
                if (set == null || !set.menuIcons) continue;

                // ビルドされるのは、アバターの中に入っている表情設定だけ。
                var root = avatar.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true);
                if (root == null) continue;

                try
                {
                    var keys = new Dictionary<string, string>();
                    MenuIcons.EnsureCached(root.gameObject, set, avatar.faceVariant, keys);
                    Capture(avatar, keys);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FacialExpressionEditor] {avatar.name} のメニューのアイコンを用意できませんでした：{e.Message}");
                }
            }
        }
    }
}
