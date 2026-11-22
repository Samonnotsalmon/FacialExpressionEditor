using System;
using System.Collections.Generic;
using UnityEngine;
namespace Samon.FacialExpressionEditor
{
    public enum LipSyncMode { Auto, Avatar, Mascot }

    [Serializable]
    public sealed class LipSyncTarget
    {
        [HideInInspector] public string id = Guid.NewGuid().ToString("N");
        public string label = "Target";
        public SkinnedMeshRenderer mesh;
        public string[] visemes = new string[15];
        public bool followExpressions;
        [HideInInspector] public bool controlVisibility;
        [HideInInspector] public GameObject visibilityTarget;
        [HideInInspector] public bool defaultVisible = true;
        [HideInInspector] public bool saveVisibility = true;
        [HideInInspector] public int autoPattern = -1;
        public bool showInMenu = true;
        public Texture2D icon;
        public Transform photoFocus;
        public GameObject photoRoot;
        public Vector3 photoOffset;
        public float photoZoom = 1;
        public float photoYaw;
        public float photoPitch;
        public Color photoBackground = Color.clear;
    }

    [Serializable]
    public sealed class LipSyncPattern
    {
        public string label = "Pattern";
        public List<int> targets = new List<int>();
        public bool showInMenu = true;
        public Texture2D icon;
    }

    [Serializable]
    public sealed class LipSyncSwitchSettings
    {
        public bool enabled;
        public List<LipSyncTarget> targets = new List<LipSyncTarget>();
        public List<LipSyncPattern> patterns = new List<LipSyncPattern>();
        public string menuName = "Lip Sync";
        public Texture2D menuIcon;
        [HideInInspector] public bool showAuto;
        public bool showAll = true;
        [HideInInspector] public Texture2D autoIcon;
        public Texture2D allIcon;
        public int defaultSelection = 1;
        [HideInInspector] public int schemaVersion;

        // Retained only to read earlier scene/prefab settings and standalone imports.
        [HideInInspector] public SkinnedMeshRenderer avatarMesh, mascotMesh;
        [HideInInspector] public GameObject visibilityTarget;
        [HideInInspector] public string[] avatarVisemes = new string[15], mascotVisemes = new string[15];
        [HideInInspector] public string avatarLabel = "Avatar", mascotLabel = "Mascot";
        [HideInInspector] public bool defaultVisible = true, saveVisibility = true;
        [HideInInspector] public LipSyncMode defaultMode;
    }
}
