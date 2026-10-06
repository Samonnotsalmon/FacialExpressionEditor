using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Control = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情セット切り替え・表情固定・パーツのメニューとパラメータを、MAのコンポーネントとして生成する。
    /// 置き換える元FXレイヤーだけが使っていたパラメータ（しなのの F_Set / F_Parts など）は、
    /// メニューとExpression Parametersから取り除く。
    /// </summary>
    internal class GenerateMenuPass : Pass<GenerateMenuPass>
    {
        private const int MaxControls = 8;

        public override string DisplayName => "表情メニューを生成";

        protected override void Execute(BuildContext context)
        {
            var variant = context.AvatarRootObject.GetComponentInChildren<ExpressionVariant>(true);
            var set = variant != null ? variant.expressionSet : null;
            if (set == null) return;

            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return;

            RemoveReplacedParameters(context, descriptor, set);

            var plan = BuildPlan.Create(set);
            var root = BuildRootMenu(context, plan);
            if (root == null) return;

            var holder = new GameObject("FacialExpressionEditor (generated)");
            holder.transform.SetParent(context.AvatarRootObject.transform, false);

            var installer = holder.AddComponent<ModularAvatarMenuInstaller>();
            installer.menuToAppend = root;

            var parameters = holder.AddComponent<ModularAvatarParameters>();
            if (plan.UsesSetParameter) parameters.parameters.Add(Parameter(BuildPlan.SetParameter, ParameterSyncType.Int, true));
            if (plan.UsesFixedParameter) parameters.parameters.Add(Parameter(BuildPlan.FixedParameter, ParameterSyncType.Int, false));
            foreach (var parameter in plan.Parts.Select(p => (p.Parameter, p.IsGrouped)).Distinct())
            {
                parameters.parameters.Add(Parameter(parameter.Parameter,
                    parameter.IsGrouped ? ParameterSyncType.Int : ParameterSyncType.Bool, false));
            }
        }

        private static VRCExpressionsMenu BuildRootMenu(BuildContext context, BuildPlan plan)
        {
            var controls = new List<Control>();

            if (plan.UsesSetParameter)
            {
                var setControls = plan.GestureSets
                    .Select((s, i) => Toggle(s.name, BuildPlan.SetParameter, i))
                    .ToList();
                controls.Add(SubMenu("表情セット", Menu(context, "表情セット", setControls)));
            }

            if (plan.FixedMenus.Count == 1)
            {
                controls.Add(SubMenu("表情固定", Menu(context, "表情固定", FixedControls(plan, plan.FixedMenus[0]))));
            }
            else if (plan.FixedMenus.Count > 1)
            {
                var perSet = plan.FixedMenus
                    .Select(m => SubMenu(m.Set.name, Menu(context, m.Set.name, FixedControls(plan, m))))
                    .ToList();
                controls.Add(SubMenu("表情固定", Menu(context, "表情固定", perSet)));
            }

            if (plan.Parts.Count > 0)
            {
                var partControls = plan.Parts
                    .Select(p => Toggle(p.Part.name, p.Parameter, p.Value))
                    .ToList();
                controls.Add(SubMenu("パーツ", Menu(context, "パーツ", partControls)));
            }

            if (controls.Count == 0) return null;

            var expressionMenu = Menu(context, "表情", controls);
            return Menu(context, "FacialExpressionEditor", new List<Control> { SubMenu("表情", expressionMenu) });
        }

        private static List<Control> FixedControls(BuildPlan plan, BuildPlan.FixedMenu menu)
        {
            return menu.Expressions
                .Select(e => Toggle(e.name, BuildPlan.FixedParameter, plan.FixedValues[e.id]))
                .ToList();
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
        private static void RemoveReplacedParameters(BuildContext context, VRCAvatarDescriptor descriptor, ExpressionSet set)
        {
            var fx = FxImporter.GetFx(descriptor);
            if (fx == null || descriptor.expressionParameters == null) return;

            var replaced = new HashSet<string>(set.originalGestureLayers.Concat(set.originalPartLayers));
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
