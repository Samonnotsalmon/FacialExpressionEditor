using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Config=Samon.FacialExpressionEditor.LipSyncSwitchSettings;
namespace Samon.FacialExpressionEditor.Editor
{
 public static class LipSyncSetup
 {
  public static readonly string[] Visemes={"sil","PP","FF","TH","DD","kk","CH","SS","nn","RR","aa","E","ih","oh","ou"};
  public static string[] Detect(SkinnedMeshRenderer renderer)
  {
   var mesh=renderer?renderer.sharedMesh:null;if(!mesh)return new string[15];
   var names=Enumerable.Range(0,mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray();
   return Visemes.Select(v=>names.FirstOrDefault(n=>n=="vrc.v_"+v)??names.FirstOrDefault(n=>n.Equals(v,StringComparison.OrdinalIgnoreCase))??Suffix(names,v)??"").ToArray();
  }
  static string Suffix(string[] names,string v){var matches=names.Where(n=>!n.StartsWith("NSL_LSS_")&&(n.EndsWith("."+v,StringComparison.OrdinalIgnoreCase)||n.EndsWith("_"+v,StringComparison.OrdinalIgnoreCase))).ToArray();return matches.Length==1?matches[0]:null;}
  public static SkinnedMeshRenderer ResolveAvatar(Config c,VRCAvatarDescriptor d)
  {
   if(c.avatarMesh)return c.avatarMesh;if(d.VisemeSkinnedMesh&&d.VisemeSkinnedMesh!=c.mascotMesh)return d.VisemeSkinnedMesh;
   var candidates=d.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r!=c.mascotMesh&&Detect(r).Count(n=>!string.IsNullOrEmpty(n))>=14).ToArray();return candidates.Length==1?candidates[0]:null;
  }
  public static void Upgrade(Config c, VRCAvatarDescriptor descriptor)
  {
   if(c.schemaVersion>=4)return;
   UpgradeLegacy(c,descriptor);
   // Resolve the old initial Auto choice once; runtime selection never follows visibility.
   if(c.defaultSelection==0) {
    int selected=c.targets.FindLastIndex(t=>t.controlVisibility?t.defaultVisible:t.mesh&&t.mesh.gameObject.activeInHierarchy);
    if(selected>=0) {
     var t=c.targets[selected];
     c.defaultSelection=t.autoPattern>=0&&t.autoPattern<c.patterns.Count?c.targets.Count+2+t.autoPattern:selected+1;
    } else if(c.targets.Count>0) {
     string label="Silent";int suffix=1;
     while(c.targets.Any(t=>t.label==label)||c.patterns.Any(p=>p.label==label))label="Silent "+suffix++;
     c.patterns.Add(new LipSyncPattern{label=label});c.defaultSelection=c.targets.Count+1+c.patterns.Count;
    } else c.defaultSelection=1;
   }
   foreach(var t in c.targets){if(!t.photoRoot&&t.visibilityTarget)t.photoRoot=t.visibilityTarget;t.controlVisibility=false;t.autoPattern=-1;}
   c.showAuto=false;c.schemaVersion=4;
  }
  private static void UpgradeLegacy(Config c, VRCAvatarDescriptor descriptor)
  {
   if(c.schemaVersion>=3)return;
   if(c.targets==null)c.targets=new System.Collections.Generic.List<LipSyncTarget>();
   if(c.patterns==null)c.patterns=new System.Collections.Generic.List<LipSyncPattern>();
   if(c.schemaVersion<3) {
    c.showAuto=true;c.showAll=true;
    foreach(var t in c.targets){t.showInMenu=true;t.photoZoom=1;}
    foreach(var p in c.patterns)p.showInMenu=true;
   }
   if(c.schemaVersion==2){c.schemaVersion=3;return;}
   if(c.schemaVersion==1)
   {
    foreach(var target in c.targets)target.autoPattern=-1;
    c.schemaVersion=3;return;
   }
   if(c.targets==null)c.targets=new System.Collections.Generic.List<LipSyncTarget>();
   if(c.targets.Count==0 && (c.avatarMesh||c.mascotMesh))
   {
    c.targets.Add(new LipSyncTarget {label=c.avatarLabel,mesh=descriptor?ResolveAvatar(c,descriptor):c.avatarMesh,visemes=c.avatarVisemes,followExpressions=true});
    if(c.mascotMesh)c.targets.Add(new LipSyncTarget {label=c.mascotLabel,mesh=c.mascotMesh,visemes=c.mascotVisemes,controlVisibility=true,visibilityTarget=c.visibilityTarget?c.visibilityTarget:c.mascotMesh.gameObject,defaultVisible=c.defaultVisible,saveVisibility=c.saveVisibility});
    c.defaultSelection=(int)c.defaultMode;
   }
   c.schemaVersion=3;
  }
  public static string ActiveParameter(int index)=>"FEE/LipSyncTargetActive/"+index;
  public static string VisibleParameter(int index)=>"NSL/LipSyncSwitch/Visible/"+index;
  public static string[] ResolveNames(string[] names,SkinnedMeshRenderer mesh)=>names==null||names.All(string.IsNullOrEmpty)?Detect(mesh):Enumerable.Range(0,15).Select(i=>i<names.Length?names[i]??"":"").ToArray();
 }

}
