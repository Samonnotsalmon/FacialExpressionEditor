using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Control = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;
using Object = UnityEngine.Object;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情メニュー（表情セットと固定の表情）とパーツのメニュー、パラメータを、MAのコンポーネントとして生成する。
    /// 元のメニューとパラメータは保持し、整理はModular Avatar側で行う。
    /// </summary>
    internal class GenerateMenuPass : Pass<GenerateMenuPass>
    {
        private const int MaxControls = 8;

        public override string DisplayName => "表情メニューを生成";

        protected override void Execute(BuildContext context)
        {
            var avatar = context.AvatarRootObject.GetComponentInChildren<FacialExpressionAvatar>(true);
            var set = avatar != null ? avatar.expressionSet : null;
            if (set == null) return;

            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return;

            var plan = BuildPlan.Create(set);
            context.GetState<MenuBuildState>().Generated = true;

            GameObject holder;
            if (avatar.editableMenu != null)
            {
                EditableMenuExport.Validate(avatar);
                holder = new GameObject("FacialExpressionEditor parameters (generated)");
                holder.transform.SetParent(avatar.transform, false);
            }
            else
            {
                var icons = set.menuIcons ? RenderIcons(context, set, plan, avatar.faceVariant) : new Dictionary<string, Texture2D>();
                var root = BuildRootMenu(context, set, plan, icons);
                if (root == null) return;
                holder = new GameObject("FacialExpressionEditor (generated)");
                holder.transform.SetParent(avatar.transform, false);
                holder.transform.SetAsFirstSibling();
                holder.AddComponent<ModularAvatarMenuInstaller>().menuToAppend = root;
            }

            // メニューの選択はワールドを移動したら戻す（保存しない）。
            var parameters = holder.AddComponent<ModularAvatarParameters>();
            parameters.parameters.Add(Parameter(BuildPlan.BlinkOffParameter, ParameterSyncType.Bool, false));
            parameters.parameters.Add(Parameter(BuildPlan.DanceEnabledParameter, ParameterSyncType.Bool, false));
            if (set.manualEyeLook)
            {
                parameters.parameters.Add(Parameter(ManualEyeLook.Enabled, ParameterSyncType.Bool, false));
                parameters.parameters.Add(Parameter(ManualEyeLook.X, ParameterSyncType.Float, false));
                parameters.parameters.Add(Parameter(ManualEyeLook.Y, ParameterSyncType.Float, false));
            }
            if (plan.UsesModeParameter) parameters.parameters.Add(Parameter(BuildPlan.ModeParameter, ParameterSyncType.Int, false));
            if (plan.UsesEmoteParameter) parameters.parameters.Add(Parameter(BuildPlan.EmoteParameter, ParameterSyncType.Int, false));
            foreach (var parameter in plan.Parts.Select(p => (p.Parameter, p.IsGrouped)).Distinct())
            {
                parameters.parameters.Add(Parameter(parameter.Parameter,
                    parameter.IsGrouped ? ParameterSyncType.Int : ParameterSyncType.Bool, false));
            }
            if (plan.Parts.Count > 0)
            {
                // 「すべてOFF」は押した本人の画面でパーツのパラメータを0にするだけなので、同期しない。
                var reset = Parameter(BuildPlan.PartResetParameter, ParameterSyncType.Bool, false);
                reset.localOnly = true;
                parameters.parameters.Add(reset);
            }
        }

        internal static TextureFormat IconFormat(BuildTarget target) =>
            target == BuildTarget.Android || target == BuildTarget.iOS ? TextureFormat.ASTC_6x6 : TextureFormat.DXT5;

        private const string NeutralIconKey = MenuIcons.NeutralIconKey;
        private const string BaseIconKey = MenuIcons.BaseIconKey;

        /// <summary>
        /// FaceEmoと同じ構成のメニューを作る。
        /// - モード選択：表情セットが2つ以上あるとき、表情メニューの木構造のとおりに FEE/Mode のトグルで並べる
        /// - 表情選択：表情メニューの木構造のとおりに、表情セットはフォルダにしてそのジェスチャーに割り当てた表情を、
        ///   固定だけの表情はそのまま、FEE/Emote のトグルで並べる（選ぶと固定）
        /// - パーツ
        /// </summary>
        private static VRCExpressionsMenu BuildRootMenu(BuildContext context, ExpressionSet set, BuildPlan plan,
            Dictionary<string, Texture2D> icons)
            => CreateMenu(context, set, plan, icons, null);

        internal static VRCExpressionsMenu CreateMenu(BuildContext context, ExpressionSet set, BuildPlan plan,
            Dictionary<string, Texture2D> icons, Dictionary<Control, string> keys)
        {
            Control Key(Control control, string key) { if (keys != null) keys[control] = key; return control; }
            var symbols = new Dictionary<MenuIconKind, Texture2D>();
            Texture2D Symbol(MenuIconKind kind)
            {
                if (symbols.TryGetValue(kind, out var texture)) return texture;
                var source = MenuSymbolIcons.Get(set, kind);
                // 標準画像だけをビルド用に複製。差し替え画像とプレビューキャッシュを変更しない。
                if (source.hideFlags == HideFlags.HideAndDontSave)
                {
                    texture = Object.Instantiate(source); texture.hideFlags = HideFlags.None;
                    if (context != null) EditorUtility.CompressTexture(texture, IconFormat(EditorUserBuildSettings.activeBuildTarget), TextureCompressionQuality.Normal);
                    context?.AssetSaver.SaveAsset(texture);
                }
                else texture = source;
                symbols[kind] = texture;
                return texture;
            }
            Control WithIcon(Control control, MenuIconKind kind) { control.icon = Symbol(kind); return control; }
            VRCExpressionsMenu Page(string name, List<Control> controls) => Menu(context, name, controls, Symbol(MenuIconKind.Next));
            Control Folder(string name, VRCExpressionsMenu menu, MenuIconKind kind = MenuIconKind.Folder) => Key(WithIcon(SubMenu(name, menu), kind), "role:" + kind);

            Control ModeToggle(BuildPlan.Mode mode, string name)
            {
                var control = Toggle(name, BuildPlan.ModeParameter, mode.Value);
                control.icon = Symbol(MenuIconKind.Check);
                return Key(control, "mode:" + (set.menu.Count == 0 || string.IsNullOrEmpty(mode.Node?.id) ? mode.GestureSet.id : mode.Node.id));
            }

            List<Control> FolderControls(string parentId)
            {
                var controls = new List<Control>();
                foreach (var node in ExpressionSetUtility.Children(set, parentId))
                {
                    if (node.kind == MenuNodeKind.Folder)
                    {
                        var children = FolderControls(node.id);
                        if (children.Count > 0) controls.Add(Key(Folder(node.name, Page(node.name, children)), "folder:" + node.id));
                        continue;
                    }

                    var mode = plan.ModeOf(node);
                    if (mode != null) controls.Add(ModeToggle(mode, ExpressionSetUtility.NodeName(set, node)));
                }
                return controls;
            }

            var rootControls = new List<Control>();
            if (plan.UsesModeParameter)
            {
                var modeControls = set.menu.Count > 0
                    ? FolderControls("")
                    : plan.Modes.Select(m => ModeToggle(m, m.GestureSet.name)).ToList();
                rootControls.Add(Folder("表情セット", Page("表情セット", modeControls), MenuIconKind.Sets));
            }

            if (plan.UsesEmoteParameter)
            {
                Control EmoteToggle(Expression expression)
                {
                    var control = Toggle(expression.name, BuildPlan.EmoteParameter, plan.EmoteValues[expression.id]);
                    if (icons.TryGetValue(expression.id, out var icon)) control.icon = icon;
                    return Key(control, "expression:" + expression.id);
                }

                Control GestureSetFolder(BuildPlan.Mode mode, string name)
                {
                    return Key(Folder(name, Page(name, mode.Emotes.Select(EmoteToggle).ToList())), "set:" + (set.menu.Count == 0 || string.IsNullOrEmpty(mode.Node?.id) ? mode.GestureSet.id : mode.Node.id));
                }

                List<Control> EmoteFolderControls(string parentId)
                {
                    var controls = new List<Control>();
                    foreach (var node in ExpressionSetUtility.Children(set, parentId))
                    {
                        if (node.kind == MenuNodeKind.Folder)
                        {
                            var children = EmoteFolderControls(node.id);
                            if (children.Count > 0) controls.Add(Key(Folder(node.name, Page(node.name, children)), "folder:" + node.id));
                            continue;
                        }

                        var mode = plan.ModeOf(node);
                        if (mode != null)
                        {
                            if (mode.Emotes.Count > 0) controls.Add(GestureSetFolder(mode, ExpressionSetUtility.NodeName(set, node)));
                            continue;
                        }

                        var expression = node.kind == MenuNodeKind.Expression ? set.FindExpression(node.expressionId) : null;
                        if (expression != null) controls.Add(Key(EmoteToggle(expression), "expression:" + expression.id + ":node:" + node.id));
                    }
                    return controls;
                }

                // 表情セット1つだけで固定だけの表情も無ければ、フォルダを挟まずに並べる。
                var emoteModes = plan.Modes.Where(m => m.Emotes.Count > 0).ToList();
                var hasFixedOnly = set.menu.Any(n => n.kind == MenuNodeKind.Expression && set.FindExpression(n.expressionId) != null);
                var emoteControls = emoteModes.Count == 1 && !hasFixedOnly
                    ? emoteModes[0].Emotes.Select(EmoteToggle).ToList()
                    : set.menu.Count > 0
                        ? EmoteFolderControls("")
                        : emoteModes.Select(m => GestureSetFolder(m, m.GestureSet.name)).ToList();
                // 値0のButtonは押している間も離した後も0。表情選択だけ解除し、パーツは保持する。
                var release = Toggle("固定解除", BuildPlan.EmoteParameter, 0);
                release.type = Control.ControlType.Button;
                emoteControls.Insert(0, WithIcon(release, MenuIconKind.Reset));
                var baseControl = Toggle("Base", BuildPlan.EmoteParameter, plan.BaseFaceValue);
                baseControl.icon = icons.TryGetValue(BaseIconKey, out var baseIcon) ? baseIcon : Symbol(MenuIconKind.Base);
                emoteControls.Insert(1, Key(baseControl, "base"));
                rootControls.Add(Folder("表情選択", Page("表情選択", emoteControls), MenuIconKind.Selection));
            }

            if (plan.Parts.Count > 0)
            {
                Control PartControl(BuildPlan.PartEntry p)
                {
                    var control = Toggle(p.Part.name, p.Parameter, p.Value);
                    if (icons.TryGetValue(MenuIcons.PartIconKey(p.Part), out var icon)) control.icon = icon;
                    return Key(control, "part:" + p.Part.id);
                }
                var partControls = plan.Parts.Where(p => PartCategoryOrder.Key(p.Part) == "folder:")
                    .Select(PartControl).ToList();
                foreach (var key in PartCategoryOrder.Keys(set))
                {
                    var children = plan.Parts.Where(p => PartCategoryOrder.Key(p.Part) == key).Select(PartControl).ToList();
                    if (children.Count == 0) continue;
                    var name = key.Substring(key.StartsWith("exclusive:") ? 10 : 7);
                    partControls.Add(Key(Folder(name, Page(name, children)), "category:" + key));
                }
                // 先頭に「すべてOFF」（押すと、ONにしているパーツをまとめてOFFにする）。
                var reset = Toggle("すべてOFF", BuildPlan.PartResetParameter, 1);
                reset.type = Control.ControlType.Button;
                partControls.Insert(0, WithIcon(reset, MenuIconKind.Reset));
                rootControls.Add(Folder("パーツ", Page("パーツ", partControls), MenuIconKind.Parts));
            }

            if (set.manualEyeLook)
            {
                ManualEyeLook.Validate(set);
                rootControls.Add(Folder("視線操作", Page("視線操作", new List<Control>
                {
                    WithIcon(Toggle("手動操作ON", ManualEyeLook.Enabled, 1), MenuIconKind.Power),
                    new Control
                    {
                        name = "目線", type = Control.ControlType.TwoAxisPuppet, icon = Symbol(MenuIconKind.EyeLook),
                        parameter = new Control.Parameter { name = "" },
                        subParameters = new[] { new Control.Parameter { name = ManualEyeLook.X }, new Control.Parameter { name = ManualEyeLook.Y } },
                        labels = new[] { new Control.Label { name = "上" }, new Control.Label { name = "右" }, new Control.Label { name = "下" }, new Control.Label { name = "左" } },
                    },
                }), MenuIconKind.EyeLook));
            }
            rootControls.Add(Folder("設定", Page("設定", new List<Control>
            {
                WithIcon(Toggle("まばたきOFF", BuildPlan.BlinkOffParameter, 1), MenuIconKind.BlinkOff),
                WithIcon(Toggle("ダンスギミック有効（MMD対応）", BuildPlan.DanceEnabledParameter, 1), MenuIconKind.Dance),
            }), MenuIconKind.Settings));

            var expressionMenu = Page("表情", rootControls);
            return Page("FacialExpressionEditor", new List<Control> { Folder("表情", expressionMenu, MenuIconKind.Root) });
        }

        /// <summary>
        /// メニューのアイコンを用意する。表情エディタのサムネイルと同じもの（Library/ のキャッシュ）を使う。
        /// - 編集モードのビルド（アップロードなど）では、足りないものをその場で描く
        /// - プレイモードに入るときのビルドでは描けない（Unityのプレビュー描画が失敗する）ので、
        ///   プレイボタンを押した直後に MenuIconPrewarmer が描いておいたキャッシュを使う
        /// - アイコンはおまけなので、何かに失敗してもビルドは止めず、アイコン無しで続ける
        /// </summary>
        internal static Dictionary<string, Texture2D> RenderIcons(BuildContext context, ExpressionSet set, BuildPlan plan,
            FaceVariant variant, GameObject editingRoot = null)
        {
            var icons = new Dictionary<string, Texture2D>();
            var avatarRoot = context != null ? context.AvatarRootObject : editingRoot;

            try
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) MenuIcons.EnsureCached(avatarRoot, set, variant);

                var faceHash = ThumbnailKeys.FaceHash(avatarRoot, set);
                var capturedKeys = EditorApplication.isPlayingOrWillChangePlaymode
                    ? MenuIconPrewarmer.Read(avatarRoot.GetComponentInChildren<FacialExpressionAvatar>(true)) : null;
                var missing = 0;

                void Load(string id, string key, string name)
                {
                    // Use the complete pre-Play key: not only the appearance, but also
                    // asset instance references can change during reloads/build processing.
                    if (capturedKeys != null && capturedKeys.TryGetValue(id, out var capturedKey)) key = capturedKey;
                    var texture = ThumbnailCache.LoadFromDisk(key);
                    if (texture == null)
                    {
                        missing++;
                        return;
                    }
                    texture.name = name;
                    if (context != null) EditorUtility.CompressTexture(texture, IconFormat(EditorUserBuildSettings.activeBuildTarget), TextureCompressionQuality.Normal);
                    context?.AssetSaver.SaveAsset(texture);
                    icons[id] = texture;
                }

                foreach (var expression in MenuIcons.Expressions(set, plan))
                {
                    Load(expression.id, ThumbnailKeys.Expression(expression, variant, faceHash), expression.name);
                }
                Load(BaseIconKey, ThumbnailKeys.BaseFace(variant, faceHash), "Base");
                foreach (var entry in plan.Parts)
                {
                    var part = entry.Part;
                    Load(MenuIcons.PartIconKey(part), ThumbnailKeys.Part(part, faceHash), part.name);
                }
                if (MenuIcons.NeedsNeutral(plan)) Load(NeutralIconKey, ThumbnailKeys.Neutral(faceHash), "無表情");

                if (missing > 0)
                {
                    Debug.LogWarning($"[FacialExpressionEditor] メニューのアイコンが {missing} 件用意できなかったので、アイコン無しにしました。");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FacialExpressionEditor] メニューのアイコンを用意できなかったため、アイコン無しで続けます：{e.Message}");
            }
            return icons;
        }
        /// <summary>
        /// 9個以上あるときは「次へ」のサブメニューで分ける。
        /// </summary>
        private static VRCExpressionsMenu Menu(BuildContext context, string name, List<Control> controls, Texture2D nextIcon = null)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = name;
            if (controls.Count <= MaxControls)
            {
                menu.controls = controls;
            }
            else
            {
                menu.controls = controls.Take(MaxControls - 1).ToList();
                var next = SubMenu("次へ", Menu(context, name, controls.Skip(MaxControls - 1).ToList(), nextIcon));
                next.icon = nextIcon;
                menu.controls.Add(next);
            }

            context?.AssetSaver.SaveAsset(menu);
            return menu;
        }

        private static Control Toggle(string name, string parameter, float value)
        {
            return new Control
            {
                name = name,
                type = Control.ControlType.Toggle,
                parameter = new Control.Parameter { name = parameter },
                value = value,
                subParameters = new Control.Parameter[0],
                labels = new Control.Label[0],
            };
        }

        private static Control SubMenu(string name, VRCExpressionsMenu subMenu)
        {
            return new Control
            {
                name = name,
                type = Control.ControlType.SubMenu,
                subMenu = subMenu,
                parameter = new Control.Parameter { name = "" },
                subParameters = new Control.Parameter[0],
                labels = new Control.Label[0],
            };
        }

        private static ParameterConfig Parameter(string name, ParameterSyncType type, bool saved)
        {
            return new ParameterConfig
            {
                nameOrPrefix = name,
                syncType = type,
                saved = saved,
                defaultValue = 0,
                // Zero is meaningful: default gesture set and no fixed expression.
                // Otherwise MA can allocate an unused value for an unselected menu.
                hasExplicitDefaultValue = true,
            };
        }

    }
}
