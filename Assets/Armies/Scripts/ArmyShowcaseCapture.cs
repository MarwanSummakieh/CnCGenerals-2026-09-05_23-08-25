using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonFrontier
{
    /// <summary>Opt-in render verification: the normal interactive app is unaffected.</summary>
    public class ArmyShowcaseCapture : MonoBehaviour
    {
        IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-armyCapture");
            if (index < 0 || index + 1 >= args.Length) yield break;
            Application.runInBackground = true;
            string directory = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(directory);
            var controller = GetComponent<ArmyShowcaseController>();
            yield return new WaitForSeconds(3);
            SaveCameraFrame(controller.sceneCamera, Path.Combine(directory, "01_ArmyOverview.png"));
            yield return new WaitForSeconds(1);
            controller.FocusFaction("Vanguard");
            yield return new WaitForSeconds(2);
            SaveCameraFrame(controller.sceneCamera, Path.Combine(directory, "02_PacificVanguard.png"));
            yield return new WaitForSeconds(1);
            controller.FocusFaction("Dynasty");
            yield return new WaitForSeconds(2);
            SaveCameraFrame(controller.sceneCamera, Path.Combine(directory, "03_CrimsonDynasty.png"));
            yield return new WaitForSeconds(1);
            controller.SelectDefinition("Vanguard_Heavy");
            yield return new WaitForSeconds(2);
            SaveCameraFrame(controller.sceneCamera, Path.Combine(directory, "04_AtlasInspection.png"));
            yield return new WaitForSeconds(1);
            controller.SelectDefinition("Dynasty_Superweapon");
            yield return new WaitForSeconds(2);
            SaveCameraFrame(controller.sceneCamera, Path.Combine(directory, "05_DynastySuperweapon.png"));
            yield return new WaitForSeconds(1);
            controller.SelectDefinition("Vanguard_Rifle");
            yield return new WaitForSeconds(2);
            SaveCameraFrame(controller.sceneCamera, Path.Combine(directory, "06_InfantryInspection.png"));
            yield return new WaitForSeconds(1);
            controller.enabled = false;
            var heroCamera = controller.sceneCamera;
            heroCamera.transform.rotation = Quaternion.Euler(50, -18, 0);
            heroCamera.transform.position = new Vector3(0, 0, -6) - heroCamera.transform.forward * 205;
            heroCamera.orthographicSize = 84;
            SaveCameraFrame(heroCamera, Path.Combine(directory, "07_ArmyPerspective.png"));
            Debug.Log("NEON FRONTIER RENDER CAPTURE COMPLETE");
            Application.Quit();
        }

        // Explicit GPU render requests also work when Windows has hidden the player window.
        // These verification images capture the 3D scene; the live IMGUI browser is separate.
        static void SaveCameraFrame(Camera camera, string path)
        {
            var target = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
            target.Create();
            var previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                int visibleSamples = 0;
                for (int y = 0; y < pixels.height; y += 40)
                    for (int x = 0; x < pixels.width; x += 40)
                        if (pixels.GetPixel(x, y).maxColorComponent > .05f) visibleSamples++;
                Debug.Log("RENDER VERIFIED: " + Path.GetFileName(path) + " / " + visibleSamples + " lit samples");
                if (visibleSamples < 10) Debug.LogError("Blank offscreen render: " + path);
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                Destroy(target);
                Destroy(pixels);
            }
        }
    }
}
