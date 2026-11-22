using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using nadena.dev.ndmf;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using Control=VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;
using Object=UnityEngine.Object;
namespace Samon.FacialExpressionEditor.Editor
{
 internal sealed class LipSyncSwitchPass : Pass<LipSyncSwitchPass>
 {
  public const string AvatarAllowed="FEE/LipSyncAvatarAllowed";
  public const string BodyActive="FEE/LipSyncBodyActive"; // legacy test/import compatibility
  public const string SelectionParameter="NSL/LipSyncSwitch/Mode";
  public override string DisplayName=>"複数対象のリップシンクを生成";
  internal static void CheckDuplicate(GameObject root)
  {
   LipSyncProfileUtility.PrepareBuild(root);
   var owners=root.GetComponentsInChildren<FacialExpressionAvatar>(true).Where(a=>a.lipSyncSwitch?.enabled==true).ToArray();
   if(owners.Length>1)throw new InvalidOperationException("リップシンク対象設定は1アバターにつき1個にしてください。");
   if(owners.Length==1&&root.GetComponentsInChildren<MonoBehaviour>(true).Any(b=>b&&b.enabled&&b.GetType().FullName=="NotSalmon.LipSyncSwitch.LipSyncSwitch"))throw new InvalidOperationException("単体版LipSyncSwitchを無効化または削除してください。");
  }
  private sealed class Route
  {
   public string name;
   public int selected; // -1 means all, -2 means none
   public List<int> members;
   public bool Includes(int index)=>members!=null?members.Contains(index):selected==-1||selected==index;
   public List<AnimatorCondition> conditions=new List<AnimatorCondition>();
  }
  private static AnimatorCondition Condition(string name, AnimatorConditionMode mode,float value=0)=>new AnimatorCondition{parameter=name,mode=mode,threshold=value};
  protected override void Execute(BuildContext context)
  {
   var root=context.AvatarRootObject;
   var owner=root.GetComponentInChildren<FacialExpressionAvatar>(true);
   var c=owner?.lipSyncSwitch;if(c==null||!c.enabled)return;
   if(!owner.expressionSet)throw new InvalidOperationException("表情データを指定してください。");
   var d=root.GetComponent<VRCAvatarDescriptor>();LipSyncSetup.Upgrade(c,d);
   var targets=c.targets;
   if(targets.Count<1||targets.Count>16)throw new InvalidOperationException("リップシンク対象は1～16個登録してください。");
   if(c.defaultSelection<1||c.defaultSelection>targets.Count+1+c.patterns.Count)throw new InvalidOperationException("初期モードを選び直してください。");
   if(targets.Count+1+c.patterns.Count>255)throw new InvalidOperationException("発話パターンが多すぎます（モード値の上限255）。");
   if(c.patterns.Any(p=>p==null||string.IsNullOrWhiteSpace(p.label)||p.targets==null||p.targets.Any(i=>i<0||i>=targets.Count)))throw new InvalidOperationException("発話パターンの名前と対象を確認してください。");
   var modeNames=new[]{"All"}.Concat(targets.Select(t=>t?.label)).Concat(c.patterns.Select(p=>p.label)).ToArray();
   if(modeNames.Distinct().Count()!=modeNames.Length)throw new InvalidOperationException("対象名・パターン名はAllを含め重複しない名前にしてください。");
   if(string.IsNullOrWhiteSpace(c.menuName))throw new InvalidOperationException("メニュー名を入力してください。");
   if(targets.Any(t=>t==null||!t.mesh||!t.mesh.sharedMesh||string.IsNullOrWhiteSpace(t.label)))throw new InvalidOperationException("各対象に名前と顔メッシュを指定してください。");
   if(targets.Select(t=>t.mesh).Distinct().Count()!=targets.Count)throw new InvalidOperationException("同じ顔メッシュを重複登録できません。");
   if(targets.Select(t=>t.label).Distinct().Count()!=targets.Count)throw new InvalidOperationException("対象名を区別できる名前にしてください。");
   foreach(var t in targets)
   {
    if(!t.mesh.transform.IsChildOf(root.transform))throw new InvalidOperationException(t.label+": メッシュをアバター内に指定してください。");
    Validate(t.mesh,LipSyncSetup.ResolveNames(t.visemes,t.mesh),t.label);

   }
   var meshes=(d.faceTrackingMeshes??new List<SkinnedMeshRenderer>()).Where(r=>r).Concat(targets.Select(t=>t.mesh)).Distinct().ToList();
   if(meshes.Count>16)throw new InvalidOperationException("Face Tracking Meshes合計16個を超えます。");
   var sets=new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>();
   foreach(var old in d.faceTrackingBlendshapesSetList??new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>())
   {
    if(old.setName.StartsWith("NSL_LSS_"))throw new InvalidOperationException("生成済みの設定が重複しています。");
    var copy=JsonUtility.FromJson<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>(JsonUtility.ToJson(old));copy.visemeBlendshapeNames=new string[15];copy.visemeBlendshapeMultipliers=new float[15];sets.Add(copy);
   }
   var holder=new GameObject("Expression Lip Sync (generated)");holder.transform.SetParent(root.transform,false);
   var controller=new AnimatorController{name="Expression Lip Sync"};controller.AddParameter(SelectionParameter,AnimatorControllerParameterType.Int);controller.AddParameter(AvatarAllowed,AnimatorControllerParameterType.Bool);
   var ma=holder.AddComponent<ModularAvatarParameters>();
   ma.parameters.Add(new ParameterConfig{nameOrPrefix=SelectionParameter,syncType=ParameterSyncType.Int,defaultValue=c.defaultSelection,hasExplicitDefaultValue=true,saved=false});
   for(int i=0;i<targets.Count;i++)controller.AddParameter(LipSyncSetup.ActiveParameter(i),AnimatorControllerParameterType.Bool);
   var routes=new List<Route>();
   for(int i=0;i<targets.Count;i++)routes.Add(new Route{name="Target "+i,selected=i,conditions=new List<AnimatorCondition>{Condition(SelectionParameter,AnimatorConditionMode.Equals,i+1)}});
   routes.Add(new Route{name="All",selected=-1,conditions=new List<AnimatorCondition>{Condition(SelectionParameter,AnimatorConditionMode.Equals,targets.Count+1)}});
   for(int i=0;i<c.patterns.Count;i++)routes.Add(new Route{name="Pattern "+i,members=c.patterns[i].targets,conditions=new List<AnimatorCondition>{Condition(SelectionParameter,AnimatorConditionMode.Equals,targets.Count+2+i)}});
   var initial=routes.Single(r=>r.conditions.Single().threshold==c.defaultSelection);
   // VRC toggles write zero when deselected. Return to the fixed initial speaker.
   routes.Add(new Route{name="Default",selected=initial.selected,members=initial.members,conditions=new List<AnimatorCondition>{Condition(SelectionParameter,AnimatorConditionMode.Equals,0)}});
   var empty=new AnimationClip{name="Lip Sync Empty"};
   for(int i=0;i<targets.Count;i++)
   {
    var t=targets[i];var aliases=CopyWithAliases(t.mesh,LipSyncSetup.ResolveNames(t.visemes,t.mesh),"NSL_LSS_T"+i+"_");string setName="NSL_LSS_Target"+i;
    sets.Add(MakeSet(setName,aliases,initial.Includes(i)?1:0));
    controller.AddLayer("Lip Sync Target "+i);var sm=controller.layers.Last().stateMachine;
    foreach(var route in routes)
    {
     bool selected=route.Includes(i);
     foreach(bool allowed in selected&&t.followExpressions?new[]{true,false}:new[]{true})
     {
      bool active=selected&&allowed;var state=sm.AddState(route.name+(allowed?"":" Expression Off"));state.motion=empty;state.writeDefaultValues=false;
      if(route==initial&&allowed)sm.defaultState=state;
      var behaviour=state.AddStateMachineBehaviour<VRCAnimatorFaceTrackingBlendshapesSet>();behaviour.blendSets=new List<VRC_AnimatorFaceTrackingBlendshapesSet.BlendSet>{new VRC_AnimatorFaceTrackingBlendshapesSet.BlendSet{blendshapesSetName=setName,weight=active?1:0}};
      var driver=state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();driver.localOnly=false;driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter{name=LipSyncSetup.ActiveParameter(i),type=VRC_AvatarParameterDriver.ChangeType.Set,value=active?1:0});
      var tr=sm.AddAnyStateTransition(state);tr.hasExitTime=false;tr.duration=0;tr.canTransitionToSelf=false;tr.conditions=route.conditions.ToArray();if(selected&&t.followExpressions)tr.AddCondition(allowed?AnimatorConditionMode.If:AnimatorConditionMode.IfNot,0,AvatarAllowed);
     }
    }
   }
   var ps=controller.parameters;ps.Single(p=>p.name==SelectionParameter).defaultInt=c.defaultSelection;ps.Single(p=>p.name==AvatarAllowed).defaultBool=true;
   for(int i=0;i<targets.Count;i++){ps.Single(p=>p.name==LipSyncSetup.ActiveParameter(i)).defaultBool=initial.Includes(i);}controller.parameters=ps;
   var layers=controller.layers;foreach(var layer in layers)layer.defaultWeight=1;controller.layers=layers;
   d.faceTrackingMeshes=meshes;d.faceTrackingBlendshapesSetList=sets;d.enableFaceTracking=true;d.lipSync=VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShapeFaceTracking;
   var merge=holder.AddComponent<ModularAvatarMergeAnimator>();merge.animator=controller;merge.layerType=VRCAvatarDescriptor.AnimLayerType.FX;merge.pathMode=MergeAnimatorPathMode.Absolute;merge.matchAvatarWriteDefaults=true;
   var controls=new List<Control>();for(int i=0;i<targets.Count;i++)if(targets[i].showInMenu)controls.Add(Toggle(targets[i].label,SelectionParameter,i+1,targets[i].icon));if(c.showAll)controls.Add(Toggle("All",SelectionParameter,targets.Count+1,c.allIcon));
   for(int i=0;i<c.patterns.Count;i++)if(c.patterns[i].showInMenu)controls.Add(Toggle(c.patterns[i].label,SelectionParameter,targets.Count+2+i,c.patterns[i].icon));
   var sub=new List<Control>{Sub("Speaker",PagedMenu("Speaker",controls))};
   var top=Sub(c.menuName,Menu(c.menuName,sub.ToArray()));top.icon=c.menuIcon;holder.AddComponent<ModularAvatarMenuInstaller>().menuToAppend=Menu("Lip Sync",top);
   // Capture source clip paths before MA Bone Proxy moves target renderers.
   FaceControlPlan.Get(context, owner.expressionSet, BuildPlan.Create(owner.expressionSet));
  }
  private static VRCExpressionsMenu PagedMenu(string name,List<Control> controls)
  {
   var menu=Menu(name);for(int i=0;i<Math.Min(controls.Count,controls.Count>8?7:8);i++)menu.controls.Add(controls[i]);
   if(controls.Count>8)menu.controls.Add(Sub("Next",PagedMenu(name,controls.Skip(7).ToList())));return menu;
  }
  static void Validate(SkinnedMeshRenderer mesh,string[] names,string label){if(!names.Skip(1).Any(n=>!string.IsNullOrEmpty(n)))throw new InvalidOperationException(label+"の口形が未設定です。自動割り当てか手動設定を行ってください。");foreach(var n in names.Where(n=>!string.IsNullOrEmpty(n)))if(mesh.sharedMesh.GetBlendShapeIndex(n)<0)throw new InvalidOperationException(label+"に口形がありません: "+n);}
  static string[] CopyWithAliases(SkinnedMeshRenderer renderer,string[] names,string prefix)
  {
   var source=renderer.sharedMesh;var copy=Object.Instantiate(source);copy.name=source.name+"_LipSyncSwitch";var aliases=new string[15];
   for(int i=0;i<15;i++){if(string.IsNullOrEmpty(names[i])){aliases[i]="";continue;}string alias=prefix+LipSyncSetup.Visemes[i];if(source.GetBlendShapeIndex(alias)>=0)throw new InvalidOperationException("専用口形名が重複しています: "+alias);int index=source.GetBlendShapeIndex(names[i]);for(int f=0;f<source.GetBlendShapeFrameCount(index);f++){var v=new Vector3[source.vertexCount];var n=new Vector3[v.Length];var t=new Vector3[v.Length];source.GetBlendShapeFrameVertices(index,f,v,n,t);copy.AddBlendShapeFrame(alias,source.GetBlendShapeFrameWeight(index,f),v,n,t);}aliases[i]=alias;}
   renderer.sharedMesh=copy;foreach(var n in names.Where(n=>!string.IsNullOrEmpty(n)))renderer.SetBlendShapeWeight(copy.GetBlendShapeIndex(n),0);return aliases;
  }
  static VRCAvatarDescriptor.FaceTrackingBlendshapesSet MakeSet(string n,string[] shapes,float weight)=>new VRCAvatarDescriptor.FaceTrackingBlendshapesSet{setName=n,setWeight=weight,visemeBlendshapeNames=shapes,visemeBlendshapeMultipliers=Enumerable.Repeat(1f,15).ToArray()};
  static Control Toggle(string n,string p,float v,Texture2D icon=null)=>new Control{icon=icon,name=n,type=Control.ControlType.Toggle,parameter=new Control.Parameter{name=p},value=v};
  static Control Sub(string n,VRCExpressionsMenu m)=>new Control{name=n,type=Control.ControlType.SubMenu,subMenu=m};
  static VRCExpressionsMenu Menu(string n,params Control[] controls){var m=ScriptableObject.CreateInstance<VRCExpressionsMenu>();m.name=n;m.controls.AddRange(controls);return m;}
 }
}
