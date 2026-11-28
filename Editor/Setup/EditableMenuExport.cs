using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;
using Control = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;

namespace Samon.FacialExpressionEditor.Editor
{
    internal static class EditableMenuExport
    {
        private const string UpdateMessage = "出力済みメニューと表情設定が一致していません。表情エディタの「MAメニューを更新してPrefabへ保存」で更新してください。";

        private sealed class Node
        {
            public Control control;
            public string key;
            public List<Node> children = new List<Node>();
        }

        private sealed class Recipe : IDisposable
        {
            public readonly List<Node> nodes;
            private readonly HashSet<Object> temporary = new HashSet<Object>();
            public Recipe(FacialExpressionAvatar avatar, bool photos, FacialExpressionAvatar photoSource = null)
            {
                var set = avatar.expressionSet;
                var plan = BuildPlan.Create(set);
                var capture = photoSource ? photoSource : avatar;
                var icons = photos && set.menuIcons
                    ? GenerateMenuPass.RenderIcons(null, set, plan, capture.faceVariant, AvatarSetup.AvatarRootOf(capture))
                    : new Dictionary<string, Texture2D>();
                foreach (var icon in icons.Values) Own(icon);
                if (photos && set.menuIcons && (!icons.ContainsKey(MenuIcons.BaseIconKey) ||
                    MenuIcons.Expressions(set, plan).Any(e => !icons.ContainsKey(e.id)) ||
                    plan.Parts.Any(p => !icons.ContainsKey(MenuIcons.PartIconKey(p.Part)))))
                {
                    Dispose();
                    throw new InvalidOperationException("表情・パーツの写真を生成できなかったため、メニューを更新しませんでした。撮影するアバターとConsoleを確認してください。");
                }
                var keys = new Dictionary<Control, string>();
                var menu = GenerateMenuPass.CreateMenu(null, set, plan, icons, keys);
                nodes = Read(menu, "", keys);
            }
            private void Own(Object obj) { if (obj && !EditorUtility.IsPersistent(obj)) temporary.Add(obj); }
            private List<Node> Read(VRCExpressionsMenu menu, string parent, Dictionary<Control, string> keys, int page = 0)
            {
                Own(menu);
                var result = new List<Node>();
                var occurrences = new Dictionary<string, int>();
                foreach (var control in menu.controls)
                {
                    Own(control.icon);
                    var id = keys.TryGetValue(control, out var key) ? key :
                        control.subMenu ? "page" : "operation:" + control.parameter?.name + ":" + control.type;
                    occurrences.TryGetValue(id, out var count); occurrences[id] = count + 1;
                    var node = new Node { control = control, key = parent + "/" + id + (id == "page" ? ":" + page : count == 0 ? "" : "#" + count) };
                    // Page boundaries must not change the identity of expression items.
                    if (control.subMenu) node.children = Read(control.subMenu, id == "page" ? parent : node.key, keys, id == "page" ? page + 1 : 0);
                    result.Add(node);
                }
                return result;
            }
            public void Dispose() { foreach (var obj in temporary) if (obj) Object.DestroyImmediate(obj); }
        }

        private static string Signature(IEnumerable<Node> nodes)
        {
            var text = new StringBuilder();
            void Add(string value) { value = value ?? ""; text.Append(value.Length).Append(':').Append(value); }
            void Visit(IEnumerable<Node> items)
            {
                text.Append('[');
                foreach (var n in items)
                {
                    var c = n.control;
                    Add(n.key); Add(c.name); Add(c.type.ToString()); Add(c.parameter?.name);
                    Add(c.value.ToString("R", CultureInfo.InvariantCulture));
                    foreach (var p in c.subParameters ?? Array.Empty<Control.Parameter>()) Add(p?.name);
                    text.Append('|');
                    foreach (var label in c.labels ?? Array.Empty<Control.Label>()) Add(label.name);
                    Visit(n.children);
                }
                text.Append(']');
            }
            Visit(nodes);
            return Hash128.Compute(text.ToString()).ToString();
        }

        private static List<Node> ReadHierarchy(Transform parent)
        {
            var nodes = new List<Node>();
            foreach (Transform child in parent)
            {
                var item = child.GetComponent<ModularAvatarMenuItem>();
                var marker = child.GetComponent<EditableMenuItem>();
                if (!item || !marker || item.Control == null) throw new InvalidOperationException(UpdateMessage);
                if (item.Control.type == Control.ControlType.SubMenu && item.MenuSource != SubmenuSource.Children)
                    throw new InvalidOperationException(UpdateMessage);
                var control = JsonUtility.FromJson<Control>(JsonUtility.ToJson(item.Control));
                control.name = string.IsNullOrEmpty(item.label) ? child.name : item.label;
                nodes.Add(new Node { key = marker.key, control = control, children = ReadHierarchy(child) });
            }
            return nodes;
        }

        public static void Validate(FacialExpressionAvatar avatar)
        {
            var root = avatar.editableMenu;
            if (!root || !root.transform.IsChildOf(avatar.transform) ||
                !root.GetComponent<ModularAvatarMenuGroup>() || !root.GetComponent<ModularAvatarMenuInstaller>() ||
                !root.GetComponent<ModularAvatarMenuInstaller>().enabled || root.GetComponent<ModularAvatarMenuInstaller>().installTargetMenu)
                throw new InvalidOperationException(UpdateMessage);
            using (var recipe = new Recipe(avatar, false))
                if (root.signature != Signature(recipe.nodes) || root.signature != Signature(ReadHierarchy(root.transform)))
                    throw new InvalidOperationException(UpdateMessage);
        }

        public static GameObject Generate(FacialExpressionAvatar avatar) => GenerateCaptured(avatar, null);

        internal static GameObject GenerateCaptured(FacialExpressionAvatar avatar, FacialExpressionAvatar photoSource)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !AvatarSetup.IsInScene(avatar) || !avatar.expressionSet)
                throw new InvalidOperationException("編集モードでHierarchyの表情設定を開いてください。PrefabはPrefab Modeで編集できます。");
            if (avatar.editableMenu && PrefabUtility.IsPartOfPrefabInstance(avatar.editableMenu) &&
                !PrefabUtility.IsAddedGameObjectOverride(avatar.editableMenu.gameObject))
                throw new InvalidOperationException("出力済みメニューを更新するには、FEEのPrefabをPrefab Modeで開いてください。");
            var setPath = AssetDatabase.GetAssetPath(avatar.expressionSet);
            if (!setPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("表情設定をAssets内に保存してから生成してください。");
            using (var recipe = new Recipe(avatar, true, photoSource))
            {
                var overrides = new Dictionary<string, Texture2D>();
                var movableOverrides = new Dictionary<string, Texture2D>();
                string Identity(string key) => key.Substring(key.LastIndexOf('/') + 1);
                IEnumerable<Node> All(IEnumerable<Node> nodes) => nodes.SelectMany(n => new[] { n }.Concat(All(n.children)));
                var newCounts = All(recipe.nodes).GroupBy(n => Identity(n.key)).ToDictionary(g => g.Key, g => g.Count());
                var previousMenu = photoSource ? photoSource.editableMenu : avatar.editableMenu;
                if (previousMenu)
                {
                    var markers = previousMenu.GetComponentsInChildren<EditableMenuItem>(true);
                    var oldCounts = markers.GroupBy(m => Identity(m.key)).ToDictionary(g => g.Key, g => g.Count());
                    foreach (var marker in markers)
                    {
                        var item = marker.GetComponent<ModularAvatarMenuItem>();
                        if (item && item.Control != null && item.Control.icon != marker.defaultIcon)
                        {
                            overrides[marker.key] = item.Control.icon; // null deliberately means no icon.
                            var identity = Identity(marker.key);
                            if (oldCounts[identity] == 1 && newCounts.TryGetValue(identity, out var count) && count == 1)
                                movableOverrides[identity] = item.Control.icon;
                        }
                    }
                }
                var folder = AssetPathUtility.EnsureFolder(AssetPathUtility.SetupFolder(avatar.expressionSet), "Icons", "Menu");
                var saved = new Dictionary<Texture2D, Texture2D>();
                Texture2D Save(Texture2D texture)
                {
                    if (!texture || EditorUtility.IsPersistent(texture)) return texture;
                    if (saved.TryGetValue(texture, out var result)) return result;
                    var bytes = texture.EncodeToPNG();
                    string hash;
                    using (var md5 = System.Security.Cryptography.MD5.Create()) hash = BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "");
                    var path = folder + "/NSL_Menu_" + hash + ".png";
                    if (!File.Exists(path))
                    {
                        File.WriteAllBytes(path, bytes);
                        AssetDatabase.ImportAsset(path);
                        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                        importer.textureType = TextureImporterType.Default;
                        importer.alphaIsTransparency = true;
                        importer.mipmapEnabled = false;
                        importer.SaveAndReimport();
                    }
                    result = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    saved[texture] = result;
                    return result;
                }
                // Complete disk writes before changing the scene. Undo does not remove shared icon files.
                void SaveIcons(IEnumerable<Node> nodes)
                {
                    foreach (var node in nodes) { node.control.icon = Save(node.control.icon); SaveIcons(node.children); }
                }
                SaveIcons(recipe.nodes);
                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("MAメニューを生成・更新");
                try
                {
                    var previous = avatar.editableMenu;
                    var index = avatar.editableMenu ? avatar.editableMenu.transform.GetSiblingIndex() : 0;
                    var root = new GameObject("NSL_表情メニュー");
                    root.transform.SetParent(avatar.transform, false);
                    root.AddComponent<ModularAvatarMenuInstaller>();
                    root.AddComponent<ModularAvatarMenuGroup>();
                    var state = root.AddComponent<EditableMenuRoot>();
                    state.signature = Signature(recipe.nodes);
                    void Create(IEnumerable<Node> nodes, Transform parent)
                    {
                        foreach (var node in nodes)
                        {
                            var go = new GameObject(node.control.name);
                            go.transform.SetParent(parent, false);
                            var item = go.AddComponent<ModularAvatarMenuItem>();
                            var marker = go.AddComponent<EditableMenuItem>();
                            marker.key = node.key;
                            marker.defaultIcon = node.control.icon;
                            var control = JsonUtility.FromJson<Control>(JsonUtility.ToJson(node.control));
                            control.subMenu = null;
                            if (overrides.TryGetValue(node.key, out var icon) || movableOverrides.TryGetValue(Identity(node.key), out icon)) control.icon = icon;
                            item.Control = control;
                            item.automaticValue = false;
                            item.isDefault = control.parameter?.name == BuildPlan.ModeParameter && control.value == 0;
                            item.isSaved = false;
                            item.label = control.name;
                            item.MenuSource = SubmenuSource.Children;
                            Create(node.children, go.transform);
                        }
                    }
                    Create(recipe.nodes, root.transform);
                    Undo.RegisterCreatedObjectUndo(root, "MAメニューを生成・更新");
                    Undo.RecordObject(avatar, "MAメニューを生成・更新");
                    avatar.editableMenu = state;
                    Undo.FlushUndoRecordObjects();
                    if (previous) Undo.DestroyObjectImmediate(previous.gameObject);
                    root.transform.SetSiblingIndex(index);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(avatar);
                    EditorUtility.SetDirty(avatar);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(avatar.gameObject.scene);
                    Undo.CollapseUndoOperations(group);
                    return root;
                }
                catch { Undo.RevertAllDownToGroup(group); throw; }
            }
        }
    }
}
