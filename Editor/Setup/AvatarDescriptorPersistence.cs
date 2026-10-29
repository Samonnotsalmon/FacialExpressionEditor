using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// Some SDK inspector fields are assigned directly instead of through SerializedProperty.
    /// A scene instance can then display a lip-sync override that is lost on reload/play.
    /// Record the user's current descriptor values; never infer or replace the lip-sync mesh.
    /// </summary>
    [InitializeOnLoad]
    internal static class AvatarDescriptorPersistence
    {
        private static readonly Dictionary<VRCAvatarDescriptor, string> Observed = new Dictionary<VRCAvatarDescriptor, string>();
        private static double _nextCheck;

        static AvatarDescriptorPersistence()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Flush();
            };
            AssemblyReloadEvents.beforeAssemblyReload += Flush;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        public static void Observe(GameObject avatar)
        {
            var descriptor = avatar != null ? avatar.GetComponent<VRCAvatarDescriptor>() : null;
            if (!CanRecord(descriptor) || Observed.ContainsKey(descriptor)) return;
            Observed.Add(descriptor, null);
            Record(descriptor);
        }

        private static bool CanRecord(VRCAvatarDescriptor descriptor) => descriptor != null &&
            !EditorApplication.isPlaying && !EditorUtility.IsPersistent(descriptor) &&
            descriptor.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewSceneObject(descriptor) &&
            PrefabUtility.IsPartOfPrefabInstance(descriptor);

        private static void Update()
        {
            if (EditorApplication.timeSinceStartup < _nextCheck) return;
            _nextCheck = EditorApplication.timeSinceStartup + .5;
            foreach (var descriptor in Observed.Keys.ToArray())
            {
                if (descriptor == null) { Observed.Remove(descriptor); continue; }
                Record(descriptor);
            }
        }

        private static void OnSceneSaving(Scene scene, string path) => Flush();

        private static void Flush()
        {
            if (EditorApplication.isPlaying) return;
            // Includes setups whose editor window is currently closed.
            foreach (var setup in Object.FindObjectsByType<FacialExpressionAvatar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Observe(AvatarSetup.AvatarRootOf(setup));
            foreach (var descriptor in Observed.Keys.ToArray()) Record(descriptor);
        }

        private static void Record(VRCAvatarDescriptor descriptor)
        {
            if (!CanRecord(descriptor)) return;
            var snapshot = EditorJsonUtility.ToJson(descriptor);
            if (Observed.TryGetValue(descriptor, out var previous) && previous == snapshot) return;
            var original = PrefabUtility.GetCorrespondingObjectFromSource(descriptor);
            var before = Modifications(descriptor, original);
            PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor);
            Observed[descriptor] = snapshot;
            if (before.SequenceEqual(Modifications(descriptor, original))) return;
            EditorUtility.SetDirty(descriptor);
            EditorSceneManager.MarkSceneDirty(descriptor.gameObject.scene);
        }

        private static string[] Modifications(VRCAvatarDescriptor descriptor, Object original) =>
            (PrefabUtility.GetPropertyModifications(descriptor) ?? new PropertyModification[0])
                .Where(p => p.target == original)
                .Select(p => p.propertyPath + "|" + p.value + "|" + (p.objectReference != null ? p.objectReference.GetInstanceID() : 0))
                .OrderBy(p => p).ToArray();
    }
}
