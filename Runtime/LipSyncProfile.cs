using System;
using System.Collections.Generic;
using UnityEngine;

namespace Samon.FacialExpressionEditor
{
    // Owned by one expression setup prefab. Scene references live on its instances.
    public sealed class LipSyncProfile : ScriptableObject
    {
        public LipSyncSwitchSettings settings = new LipSyncSwitchSettings();
        public List<LipSyncReferencePaths> references = new List<LipSyncReferencePaths>();
        public string ownerPrefabGuid;
    }

    [Serializable]
    public sealed class LipSyncReferencePaths
    {
        public string targetId, mesh, photoFocus, photoRoot;
    }

    [Serializable]
    public sealed class LipSyncMeshBinding
    {
        public string targetId;
        public SkinnedMeshRenderer mesh;
    }
}
