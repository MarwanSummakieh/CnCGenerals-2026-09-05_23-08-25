using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonFrontier
{
    /// <summary>Creates a cached, lit identification photograph from every actual army prefab.</summary>
    public sealed class TacticalPortraitRenderer : MonoBehaviour
    {
        const int PortraitLayer = 30;
        readonly Dictionary<ArmyDefinition, RenderTexture> portraits = new Dictionary<ArmyDefinition, RenderTexture>();
        Camera portraitCamera;
        GameObject stage;
        GameObject subject;

        public Texture Get(ArmyDefinition definition)
        {
            return definition && portraits.TryGetValue(definition, out var texture) ? texture : null;
        }

        public void Initialize(ArmyDefinition[] definitions, Camera worldCamera)
        {
            if (worldCamera) worldCamera.cullingMask &= ~(1 << PortraitLayer);
            StartCoroutine(PhotographRoster(definitions));
        }

        IEnumerator PhotographRoster(ArmyDefinition[] definitions)
        {
            if (definitions == null) yield break;
            stage = new GameObject("Command console portrait studio");
            stage.transform.SetParent(transform, false);
            stage.transform.position = new Vector3(0, -1800, 0);
            portraitCamera = new GameObject("Unit identification camera", typeof(Camera)).GetComponent<Camera>();
            portraitCamera.transform.SetParent(stage.transform, false);
            portraitCamera.enabled = false;
            portraitCamera.orthographic = true;
            portraitCamera.clearFlags = CameraClearFlags.SolidColor;
            portraitCamera.backgroundColor = new Color(.14f, .155f, .14f, 1);
            portraitCamera.cullingMask = 1 << PortraitLayer;
            portraitCamera.nearClipPlane = .1f;
            portraitCamera.farClipPlane = 250;
            portraitCamera.allowHDR = false;
            var data = portraitCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            var light = new GameObject("Identification studio softbox", typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(stage.transform, false);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(48, -35, 0);
            light.intensity = 1.5f;
            light.color = new Color(1, .91f, .76f);
            light.cullingMask = 1 << PortraitLayer;
            light.shadows = LightShadows.None;
            // Wait for the active render pipeline to be initialized before submitting requests.
            yield return null;
            foreach (var definition in definitions)
            {
                if (!definition || !definition.prefab || portraits.ContainsKey(definition)) continue;
                subject = Instantiate(definition.prefab, stage.transform);
                subject.name = "Identification model / " + definition.id;
                subject.transform.localPosition = Vector3.zero;
                subject.transform.localRotation = Quaternion.Euler(0, -20, 0);
                foreach (var child in subject.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PortraitLayer;
                foreach (var behaviour in subject.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
                foreach (var collider in subject.GetComponentsInChildren<Collider>()) collider.enabled = false;
                foreach (var particles in subject.GetComponentsInChildren<ParticleSystem>()) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var bounds = new Bounds(subject.transform.position, Vector3.one);
                bool found = false;
                foreach (var renderer in subject.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer is LineRenderer) continue;
                    if (!found) { bounds = renderer.bounds; found = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                portraitCamera.transform.rotation = Quaternion.Euler(definition.category == "Infantry" ? 12 : 31, 145, 0);
                portraitCamera.transform.position = bounds.center - portraitCamera.transform.forward * 100;
                // Project all eight bounds corners so even long aircraft and broad structures fit.
                float extent = .1f;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3((corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                        (corner & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                        (corner & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                    extent = Mathf.Max(extent, Mathf.Abs(Vector3.Dot(offset, portraitCamera.transform.up)),
                        Mathf.Abs(Vector3.Dot(offset, portraitCamera.transform.right)) / (256f / 192));
                }
                portraitCamera.orthographicSize = extent * 1.10f;
                var texture = new RenderTexture(256, 192, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Command portrait / " + definition.id,
                    antiAliasing = 2,
                    filterMode = FilterMode.Bilinear
                };
                texture.Create();
                portraitCamera.targetTexture = texture;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = texture };
                if (RenderPipeline.SupportsRenderRequest(portraitCamera, request))
                {
                    RenderPipeline.SubmitRenderRequest(portraitCamera, request);
                    portraits.Add(definition, texture);
                }
                else { texture.Release(); Destroy(texture); }
                portraitCamera.targetTexture = null;
                subject.SetActive(false);
                Destroy(subject);
                subject = null;
                yield return null;
            }
            Destroy(stage);
            stage = null;
        }

        void OnDestroy()
        {
            StopAllCoroutines();
            if (stage) Destroy(stage);
            foreach (var texture in portraits.Values) if (texture) { texture.Release(); Destroy(texture); }
            portraits.Clear();
        }
    }
}
