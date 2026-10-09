using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// アバターの複製をプレビュー専用のシーンに置き、顔のアップを描画する。シーン上のアバターは変更しない。
    /// 負荷を抑えるため、クリップで変えたレンダラーとオブジェクトだけを元に戻し、変わったメッシュだけを焼き付け直す。
    /// </summary>
    internal sealed class FacePreview : IDisposable
    {
        private readonly PreviewRenderUtility _utility;
        private readonly GameObject _clone;
        private readonly Vector3 _eye;
        private readonly Vector3 _forward;
        private readonly float _distance;

        private readonly Dictionary<SkinnedMeshRenderer, float[]> _baseWeights = new Dictionary<SkinnedMeshRenderer, float[]>();
        private readonly Dictionary<Renderer, (Material[] materials, bool enabled)> _baseRenderers =
            new Dictionary<Renderer, (Material[], bool)>();
        private readonly Dictionary<GameObject, bool> _baseActive = new Dictionary<GameObject, bool>();
        private readonly List<(SkinnedMeshRenderer skinned, MeshRenderer target, Mesh mesh)> _baked =
            new List<(SkinnedMeshRenderer, MeshRenderer, Mesh)>();

        // 直前のクリップで変えたもの。次に適用する前に、これだけを元に戻す。
        private readonly HashSet<Renderer> _touchedRenderers = new HashSet<Renderer>();
        private readonly HashSet<GameObject> _touchedObjects = new HashSet<GameObject>();

        // 次の描画で焼き付け直すメッシュ。
        private readonly HashSet<SkinnedMeshRenderer> _needsBake = new HashSet<SkinnedMeshRenderer>();

        public GameObject Source { get; }

        // カメラの寄り具合（1で標準、大きいほど寄る）と、横から見る角度（度）。表情の編集ウィンドウで使う。
        public float Zoom { get; set; } = 1f;
        public float Yaw { get; set; }
        public float HeightOffset { get; set; } = 0.03f;

        private static readonly Color Background = new Color(0.19f, 0.19f, 0.2f);


        public FacePreview(GameObject source, ExpressionSet settings = null)
        {
            Source = source;
            if (settings != null) { HeightOffset = settings.previewHeight; Zoom = settings.previewZoom; }

            _utility = new PreviewRenderUtility();
            var camera = _utility.camera;
            camera.fieldOfView = 20f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 20f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;

            var descriptor = source.GetComponent<VRCAvatarDescriptor>();
            var view = descriptor != null ? descriptor.ViewPosition : new Vector3(0, 1.3f, 0.08f);

            // 途中で失敗したら、作りかけのものを片付けてから失敗を伝える（PreviewRenderUtility を残さないため）。
            try
            {
                _clone = CreateRenderOnlyClone(source);
                _clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                _utility.AddSingleGO(_clone);

                _eye = _clone.transform.TransformPoint(view);
                _forward = _clone.transform.forward;
                _distance = Mathf.Max(0.25f, view.y * 0.5f);

                var lightDirection = Quaternion.LookRotation(-_forward + Vector3.down * 0.4f + Vector3.right * 0.3f);
                _utility.lights[0].intensity = 1.1f;
                _utility.lights[0].transform.rotation = lightDirection;
                _utility.lights[1].intensity = 0.5f;
                _utility.ambientColor = new Color(0.45f, 0.45f, 0.45f);

                foreach (var renderer in _clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.sharedMesh == null) continue;
                    _baseWeights[renderer] = Enumerable.Range(0, renderer.sharedMesh.blendShapeCount)
                        .Select(renderer.GetBlendShapeWeight).ToArray();
                }
                foreach (var renderer in _clone.GetComponentsInChildren<Renderer>(true))
                {
                    _baseRenderers[renderer] = (renderer.sharedMaterials, renderer.enabled);
                }
                foreach (var transform in _clone.GetComponentsInChildren<Transform>(true))
                {
                    _baseActive[transform.gameObject] = transform.gameObject.activeSelf;
                }

                CreateBakedRenderers();

                // 作った直後の1回目は、焼き付けた形が空になって何も映らないので、一度描画してから全部焼き付け直す。
                Object.DestroyImmediate(RenderStatic(8));
                foreach (var (skinned, _, _) in _baked) _needsBake.Add(skinned);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// 描画に必要なもの（Transform、Renderer、MeshFilter、パーティクル）だけを残したアバターの複製を作る。
        /// スクリプト（PhysBone、Modular Avatar など）が付いたままだと、プレビュー用のシーンで動こうとしてエラーになる
        /// （プレイモード中は 'ShouldRunBehaviour()' のアサーションが大量に出る）。そのため、非アクティブな親の下に複製して
        /// 一度も動かさないうちに取り除いてから、表に出す。
        /// </summary>
        private static GameObject CreateRenderOnlyClone(GameObject source)
        {
            var holder = new GameObject("__FacialExpressionEditor_PreviewHolder") { hideFlags = HideFlags.HideAndDontSave };
            holder.SetActive(false);
            try
            {
                var clone = Object.Instantiate(source, holder.transform);
                clone.name = source.name;
                StripNonRenderComponents(clone);
                clone.transform.SetParent(null, false);
                clone.hideFlags = HideFlags.HideAndDontSave;
                return clone;
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        private static bool IsRenderComponent(Component component)
        {
            return component is Transform || component is Renderer || component is MeshFilter || component is ParticleSystem;
        }

        /// <summary>
        /// 描画に要らないコンポーネントを取り除く。ほかのコンポーネントから [RequireComponent] で必要とされているものは、
        /// 必要としている側を先に取り除く（取り除けない順で消すとエラーが出るため）。
        /// </summary>
        private static void StripNonRenderComponents(GameObject root)
        {
            var remaining = root.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && !IsRenderComponent(c))
                .ToList();

            for (var pass = 0; pass < 8 && remaining.Count > 0; pass++)
            {
                var removable = remaining.Where(c => !IsRequiredByOthers(c, remaining)).ToList();
                if (removable.Count == 0) break;
                foreach (var component in removable)
                {
                    Object.DestroyImmediate(component);
                    remaining.Remove(component);
                }
            }

            // 欠けたスクリプト（Missing Script）も取り除く。
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
            }
        }

        private static bool IsRequiredByOthers(Component component, List<Component> remaining)
        {
            var type = component.GetType();
            return remaining.Any(other =>
                other != component && other.gameObject == component.gameObject &&
                other.GetType().GetCustomAttributes(typeof(RequireComponent), true).Cast<RequireComponent>()
                    .Any(r => Requires(r.m_Type0, type) || Requires(r.m_Type1, type) || Requires(r.m_Type2, type)));
        }

        private static bool Requires(Type required, Type type) => required != null && required.IsAssignableFrom(type);

        /// <summary>
        /// プレビュー用のシーンでは、SkinnedMeshRendererのシェイプキーを変えても描画に反映されない。
        /// そのため、描画の直前に BakeMesh した形を、子に置いた MeshRenderer で描く。元のレンダラーは描画だけ止める。
        /// </summary>
        private void CreateBakedRenderers()
        {
            foreach (var skinned in _clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh == null) continue;

                var holder = new GameObject("__FacialExpressionEditor_Baked") { hideFlags = HideFlags.HideAndDontSave };
                holder.transform.SetParent(skinned.transform, false);
                var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                holder.AddComponent<MeshFilter>().sharedMesh = mesh;
                var target = holder.AddComponent<MeshRenderer>();

                skinned.forceRenderingOff = true;
                _baked.Add((skinned, target, mesh));
                _needsBake.Add(skinned);
            }
        }

        private void SyncBakedRenderers()
        {
            if (_needsBake.Count == 0) return;

            var block = new MaterialPropertyBlock();
            foreach (var (skinned, target, mesh) in _baked)
            {
                if (!_needsBake.Contains(skinned)) continue;

                skinned.BakeMesh(mesh);
                target.sharedMaterials = skinned.sharedMaterials;
                target.enabled = skinned.enabled;
                skinned.GetPropertyBlock(block);
                target.SetPropertyBlock(block);
            }
            _needsBake.Clear();
        }

        /// <summary>
        /// 顔を元の状態に戻してから、クリップを normalizedTime（0〜1）の位置で適用する。
        /// </summary>
        public void Apply(AnimationClip clip, float normalizedTime = 1f)
        {
            Restore();
            if (clip != null) ApplyClip(clip, clip.length * Mathf.Clamp01(normalizedTime));
        }

        /// <summary>
        /// 現在の状態に、別のクリップ（パーツなど）を重ねて適用する。
        /// </summary>
        public void Overlay(AnimationClip clip, float normalizedTime = 1f)
        {
            if (clip != null) ApplyClip(clip, clip.length * Mathf.Clamp01(normalizedTime));
        }

        private void ApplyClip(AnimationClip clip, float time)
        {
            ClipApplier.Apply(_clone, clip, time, _touchedRenderers, _touchedObjects);
            foreach (var renderer in _touchedRenderers)
            {
                if (renderer is SkinnedMeshRenderer skinned) _needsBake.Add(skinned);
            }
        }

        public Texture2D RenderStatic(int size)
        {
            _utility.BeginStaticPreview(new Rect(0, 0, size, size));
            var camera = _utility.camera;
            var target = _eye + Vector3.up * HeightOffset;
            var direction = Quaternion.AngleAxis(Yaw, Vector3.up) * _forward;
            camera.transform.position = target + direction * (_distance / Mathf.Max(0.1f, Zoom));
            camera.transform.LookAt(target);
            SyncBakedRenderers();
            _utility.Render(true);
            return _utility.EndStaticPreview();
        }

        private void Restore()
        {
            foreach (var renderer in _touchedRenderers)
            {
                if (renderer == null) continue;
                if (renderer is SkinnedMeshRenderer skinned && _baseWeights.TryGetValue(skinned, out var weights))
                {
                    for (var i = 0; i < weights.Length; i++) skinned.SetBlendShapeWeight(i, weights[i]);
                    _needsBake.Add(skinned);
                }
                renderer.SetPropertyBlock(null);
                if (_baseRenderers.TryGetValue(renderer, out var state))
                {
                    renderer.sharedMaterials = state.materials;
                    renderer.enabled = state.enabled;
                }
            }
            foreach (var gameObject in _touchedObjects)
            {
                if (gameObject != null && _baseActive.TryGetValue(gameObject, out var active)) gameObject.SetActive(active);
            }
            _touchedRenderers.Clear();
            _touchedObjects.Clear();
        }

        public void Dispose()
        {
            _utility?.Cleanup();
            foreach (var (_, _, mesh) in _baked)
            {
                if (mesh != null) Object.DestroyImmediate(mesh);
            }
            if (_clone != null) Object.DestroyImmediate(_clone);
        }
    }
}
