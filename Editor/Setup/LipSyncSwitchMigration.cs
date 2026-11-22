using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Samon.FacialExpressionEditor.Editor
{
    internal static class LipSyncSwitchMigration
    {
        internal static MonoBehaviour Find(GameObject root) => root ? root.GetComponentsInChildren<MonoBehaviour>(true)
            .FirstOrDefault(c => c && c.GetType().FullName == "NotSalmon.LipSyncSwitch.LipSyncSwitch") : null;

        internal static void Import(FacialExpressionAvatar owner, MonoBehaviour legacy)
        {
            if (!owner || !legacy) throw new ArgumentException("移行元・移行先がありません。");
            Undo.RecordObjects(new UnityEngine.Object[] { owner, legacy }, "単体版リップシンク設定を取り込む");
            var settings = JsonUtility.FromJson<LipSyncSwitchSettings>(JsonUtility.ToJson(legacy));
            settings.enabled = true;
            if (!settings.visibilityTarget) settings.visibilityTarget = legacy.gameObject;
            owner.lipSyncSwitch = settings;
            legacy.enabled = false;
            EditorUtility.SetDirty(owner);
            EditorUtility.SetDirty(legacy);
            PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
            PrefabUtility.RecordPrefabInstancePropertyModifications(legacy);
        }
    }
}
