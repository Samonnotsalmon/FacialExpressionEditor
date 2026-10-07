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
    /// 置き換える元FXレイヤーだけが使っていたパラメータ（しなのの F_Set / F_Parts など）は、
    /// メニューとExpression Parametersから取り除く。
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
            var face = FaceControlPlan.Get(context, set, plan);
            RemoveReplacedParameters(context, descriptor, set,
                face.BlinkLayersToReplace.Concat(face.MouthCancelerLayersToReplace));

            var icons = set.menuIcons ? RenderIcons(context, set, plan, avatar.faceVariant) : new Dictionary<string, Texture2D>();
            var root = BuildRootMenu(context, set, plan, icons);
            if (root == null) return;

            var holder = new GameObject("FacialExpressionEditor (generated)");
            holder.transform.SetParent(context.AvatarRootObject.transform, false);

            var installer = holder.AddComponent<ModularAvatarMenuInstaller>();
            installer.menuToAppend = root;

            // メニューの選択はワールドを移動したら戻す（保存しない）。
            var parameters = holder.AddComponent<ModularAvatarParameters>();
            if (plan.UsesModeParameter) parameters.parameters.Add(Parameter(BuildPlan.ModeParameter, ParameterSyncType.Int, false));
            if (plan.UsesEmoteParameter) parameters.parameters.Add(Parameter(BuildPlan.EmoteParameter, ParameterSyncType.Int, false));
            foreach (var parameter in plan.Parts.Select(p => (p.Parameter, p.IsGrouped)).Distinct())
            {
                parameters.parameters.Add(Parameter(parameter.Parameter,
                    parameter.IsGrouped ? ParameterSyncType.Int : ParameterSyncType.Bool, false));
            }
        }

        private const string NeutralIconKey = "";

        /// <summary>
        /// FaceEmoと同じ構成のメニューを作る。
        /// - モード選択：表情セットが2つ以上あるとき、表情メニューの木構造のとおりに FEE/Mode のトグルで並べる
        /// - 表情選択：表情メニューの木構造のとおりに、表情セットはフォルダにしてそのジェスチャーに割り当てた表情を、
        ///   固定だけの表情はそのまま、FEE/Emote のトグルで並べる（選ぶと固定）
        /// - パーツ
        /// </summary>
        private static VRCExpressionsMenu BuildRootMenu(BuildContext context, ExpressionSet set, BuildPlan plan,
            Dictionary<string, Texture2D> icons)
        {
            Control ModeToggle(BuildPlan.Mode mode, string name)
            {
                var control = Toggle(name, BuildPlan.ModeParameter, mode.Value);
                if (icons.TryGetValue(NeutralIconKey, out var icon)) control.icon = icon;
                return control;
            }

            List<Control> FolderControls(string parentId)
            {
                var controls = new List<Control>();
                foreach (var node in ExpressionSetUtility.Children(set, parentId))
                {
                    if (node.kind == MenuNodeKind.Folder)
                    {
                        var children = FolderControls(node.id);
                        if (children.Count > 0) controls.Add(SubMenu(node.name, Menu(context, node.name, children)));
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
                rootControls.AddRange(set.menu.Count > 0
                    ? FolderControls("")
                    : plan.Modes.Select(m => ModeToggle(m, m.GestureSet.name)));
            }

            if (plan.UsesEmoteParameter)
            {
                Control EmoteToggle(Expression expression)
                {
                    var control = Toggle(expression.name, BuildPlan.EmoteParameter, plan.EmoteValues[expression.id]);
                    if (icons.TryGetValue(expression.id, out var icon)) control.icon = icon;
                    return control;
                }

                Control GestureSetFolder(BuildPlan.Mode mode, string name)
                {
                    return SubMenu(name, Menu(context, name, mode.Emotes.Select(EmoteToggle).ToList()));
                }

                List<Control> EmoteFolderControls(string parentId)
                {
                    var controls = new List<Control>();
                    foreach (var node in ExpressionSetUtility.Children(set, parentId))
                    {
                        if (node.kind == MenuNodeKind.Folder)
                        {
                            var children = EmoteFolderControls(node.id);
                            if (children.Count > 0) controls.Add(SubMenu(node.name, Menu(context, node.name, children)));
                            continue;
                        }

                        var mode = plan.ModeOf(node);
                        if (mode != null)
                        {
                            if (mode.Emotes.Count > 0) controls.Add(GestureSetFolder(mode, ExpressionSetUtility.NodeName(set, node)));
                            continue;
                        }

                        var expression = node.kind == MenuNodeKind.Expression ? set.FindExpression(node.expressionId) : null;
                        if (expression != null) controls.Add(EmoteToggle(expression));
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
                rootControls.Add(SubMenu("表情選択", Menu(context, "表情選択", emoteControls)));
            }

            if (plan.Parts.Count > 0)
            {
                var partControls = plan.Parts
                    .Select(p => Toggle(p.Part.name, p.Parameter, p.Value))
                    .ToList();
                rootControls.Add(SubMenu("パーツ", Menu(context, "パーツ", partControls)));
            }

            if (rootControls.Count == 0) return null;

            var expressionMenu = Menu(context, "表情", rootControls);
            return Menu(context, "FacialExpressionEditor", new List<Control> { SubMenu("表情", expressionMenu) });
        }

        /// <summary>
        /// メニューのアイコンを用意する。表情エディタのサムネイルと同じもの（Library/ のキャッシュ）を使う。
        /// - 編集モードのビルド（アップロードなど）では、足りないものをその場で描く
        /// - プレイモードに入るときのビルドでは描けない（Unityのプレビュー描画が失敗する）ので、
        ///   プレイボタンを押した直後に MenuIconPrewarmer が描いておいたキャッシュを使う
        /// - アイコンはおまけなので、何かに失敗してもビルドは止めず、アイコン無しで続ける
        /// </summary>
        private static Dictionary<string, Texture2D> RenderIcons(BuildContext context, ExpressionSet set, BuildPlan plan,
            FaceVariant variant)
        {
            var icons = new Dictionary<string, Texture2D>();
            var avatarRoot = context.AvatarRootObject;

            try
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) MenuIcons.EnsureCached(avatarRoot, set, variant);

                var faceHash = ThumbnailKeys.FaceHash(avatarRoot);
                var missing = 0;

                void Load(string id, string key, string name)
                {
                    var texture = ThumbnailCache.LoadFromDisk(key);
                    if (texture == null)
                    {
                        missing++;
                        return;
                    }
                    texture.name = name;
                    EditorUtility.CompressTexture(texture, TextureFormat.DXT5, TextureCompressionQuality.Normal);
                    context.AssetSaver.SaveAsset(texture);
                    icons[id] = texture;
                }

                foreach (var expression in MenuIcons.Expressions(set, plan))
                {
                    Load(expression.id, ThumbnailKeys.Expression(expression, variant, faceHash), expression.name);
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
        private static VRCExpressionsMenu Menu(BuildContext context, string name, List<Control> controls)
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
                menu.controls.Add(SubMenu("次へ", Menu(context, name, controls.Skip(MaxControls - 1).ToList())));
            }

            context.AssetSaver.SaveAsset(menu);
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
            };
        }

        /// <summary>
        /// 置き換える元FXレイヤーだけが使っていたパラメータを、メニューとExpression Parametersから取り除く。
        /// 元のアセットは変更せず、複製したものをアバターに設定し直す。
        /// </summary>
        private static void RemoveReplacedParameters(BuildContext context, VRCAvatarDescriptor descriptor, ExpressionSet set,
            IEnumerable<string> otherReplacedLayers)
        {
            var fx = FxImporter.GetFx(descriptor);
            if (fx == null || descriptor.expressionParameters == null) return;

            var replaced = new HashSet<string>(set.originalGestureLayers.Concat(set.originalPartLayers).Concat(otherReplacedLayers));
            if (replaced.Count == 0) return;

            var usedByReplaced = AnimatorParameterUsage.Collect(fx.layers.Where(l => replaced.Contains(l.name)));
            var usedByOthers = AnimatorParameterUsage.CollectFromAvatar(descriptor, fx, replaced);
            var removed = new HashSet<string>(descriptor.expressionParameters.parameters
                .Select(p => p.name)
                .Where(n => usedByReplaced.Contains(n) && !usedByOthers.Contains(n)));
            if (removed.Count == 0) return;

            var parameters = Object.Instantiate(descriptor.expressionParameters);
            parameters.name = descriptor.expressionParameters.name;
            parameters.parameters = parameters.parameters.Where(p => !removed.Contains(p.name)).ToArray();
            context.AssetSaver.SaveAsset(parameters);
            descriptor.expressionParameters = parameters;

            if (descriptor.expressionsMenu != null)
            {
                var clones = new Dictionary<VRCExpressionsMenu, VRCExpressionsMenu>();
                descriptor.expressionsMenu = CloneMenuWithout(context, descriptor.expressionsMenu, removed, clones);
            }
        }

        private static VRCExpressionsMenu CloneMenuWithout(BuildContext context, VRCExpressionsMenu menu,
            HashSet<string> removed, Dictionary<VRCExpressionsMenu, VRCExpressionsMenu> clones)
        {
            if (clones.TryGetValue(menu, out var existing)) return existing;

            var clone = Object.Instantiate(menu);
            clone.name = menu.name;
            clones[menu] = clone;

            var controls = new List<Control>();
            foreach (var control in menu.controls)
            {
                if (UsesRemoved(control, removed)) continue;

                if (control.type == Control.ControlType.SubMenu && control.subMenu != null)
                {
                    var sub = CloneMenuWithout(context, control.subMenu, removed, clones);
                    // 取り除いた結果サブメニューが空になったら、サブメニューごと外す。
                    if (sub.controls.Count == 0 && control.subMenu.controls.Count > 0) continue;

                    controls.Add(CopyControl(control, sub));
                    continue;
                }

                controls.Add(CopyControl(control, control.subMenu));
            }

            clone.controls = controls;
            context.AssetSaver.SaveAsset(clone);
            return clone;
        }

        private static bool UsesRemoved(Control control, HashSet<string> removed)
        {
            if (control.parameter != null && removed.Contains(control.parameter.name)) return true;
            return control.subParameters != null && control.subParameters.Any(p => p != null && removed.Contains(p.name));
        }

        private static Control CopyControl(Control control, VRCExpressionsMenu subMenu)
        {
            return new Control
            {
                name = control.name,
                icon = control.icon,
                type = control.type,
                parameter = control.parameter,
                value = control.value,
                style = control.style,
                subMenu = subMenu,
                subParameters = control.subParameters,
                labels = control.labels,
            };
        }
    }
}
