using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonFrontier
{
    /// <summary>
    /// Verification-only IMGUI repaint into an explicit target when a hidden swapchain
    /// does not dispatch Repaint events. Mirrors Unity's IMGUIContainer GUI-state lifecycle.
    /// The reflection signatures are checked against Unity 6000.3.10f1; this is not a
    /// supported cross-version public Unity API. Pixel validation remains the caller's job.
    /// </summary>
    public static class OffscreenGuiRenderer
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static bool TryRender(RenderTexture target, Action draw, out string diagnostic)
        {
            diagnostic = "Offscreen GUI was not entered.";
            try { return TryRenderCore(target, draw, out diagnostic); }
            catch (Exception exception)
            {
                exception = Unwrap(exception);
                diagnostic += $" Unhandled verification helper failure: {exception.GetType().Name}: {exception.Message}";
                return false;
            }
        }

        static bool TryRenderCore(RenderTexture target, Action draw, out string diagnostic)
        {
            diagnostic = "Offscreen GUI was not entered.";
            if (draw == null || !target || !target.IsCreated()) return false;
            int instanceId = draw.Target is UnityEngine.Object owner && owner ? owner.GetInstanceID() : 613927;
            object nativeState = null;
            object oldLayout = null, oldSkinMode = null, oldOwnerId = null, oldPixels = null;
            Type utility = typeof(GUIUtility);
            Type clip = typeof(GUI).Assembly.GetType("UnityEngine.GUIClip");
            Type state = typeof(GUI).Assembly.GetType("UnityEngine.ObjectGUIState");
            Type layout = typeof(GUI).Assembly.GetType("UnityEngine.GUILayoutUtility");
            FieldInfo layoutCurrent = layout?.GetField("current", Static);
            FieldInfo skinMode = utility.GetField("s_SkinMode", Static);
            FieldInfo ownerId = utility.GetField("s_OriginalID", Static);
            PropertyInfo pixels = utility.GetProperty("pixelsPerPoint", Static);
            PropertyInfo depth = utility.GetProperty("guiDepth", Static);
            Event oldEvent = Event.current;
            RenderTexture oldTarget = RenderTexture.active;
            bool oldSrgbWrite = GL.sRGBWrite;
            Matrix4x4 oldMatrix = Matrix4x4.identity;
            bool entered = false, parentPushed = false, matricesPushed = false, matrixCaptured = false, succeeded = false;
            string phase = "reflection setup";
            try
            {
                if (clip == null || state == null || layout == null || depth == null)
                    throw new MissingMemberException("Unity internal IMGUI state types are unavailable.");
                if ((int)depth.GetValue(null) != 0)
                    throw new InvalidOperationException("Forced repaint requires an outermost call, outside any OnGUI.");
                MethodInfo begin = utility.GetMethod("BeginContainer", Static, null, new[] { state }, null);
                MethodInfo push = clip.GetMethod("Internal_PushParentClip", Static, null,
                    new[] { typeof(Matrix4x4), typeof(Rect) }, null);
                if (begin == null || push == null) throw new MissingMethodException("Unity internal IMGUI container methods are unavailable.");

                oldLayout = layoutCurrent?.GetValue(null);
                oldSkinMode = skinMode?.GetValue(null);
                oldOwnerId = ownerId?.GetValue(null);
                oldPixels = pixels?.GetValue(null);
                nativeState = Activator.CreateInstance(state, true);
                phase = "native BeginContainer";
                begin.Invoke(null, new[] { nativeState });
                entered = true;
                Event.current = new Event { type = EventType.Repaint, mousePosition = new Vector2(-10000, -10000) };
                skinMode?.SetValue(null, 0);
                ownerId?.SetValue(null, instanceId);
                pixels?.SetValue(null, 1f);
                // Begin provides the native control ID and empty GUILayout caches without
                // depending on a hidden window receiving its own Layout event.
                layout.GetMethod("Begin", Static, null, new[] { typeof(int) }, null)?.Invoke(null, new object[] { instanceId });
                utility.GetMethod("ResetGlobalState", Static)?.Invoke(null, null);
                oldMatrix = GUI.matrix;
                matrixCaptured = true;

                phase = "explicit GUI clip and render target";
                push.Invoke(null, new object[] { Matrix4x4.identity, new Rect(0, 0, target.width, target.height) });
                parentPushed = true;
                RenderTexture.active = target;
                using (var commands = new CommandBuffer { name = "Verification offscreen IMGUI state" })
                {
                    commands.SetRenderTarget(target);
                    commands.SetViewport(new Rect(0, 0, target.width, target.height));
                    commands.DisableScissorRect();
                    Graphics.ExecuteCommandBuffer(commands);
                }
                GL.PushMatrix(); matricesPushed = true;
                GL.LoadPixelMatrix(0, target.width, target.height, 0);
                GUI.matrix = Matrix4x4.identity;
                // Legacy IMGUI produces gamma-space colors. Unlike an ordinary OnGUI
                // repaint, this synthetic pass inherits the preceding URP target state.
                // Disable a second linear-to-sRGB conversion only for the GUI overlay:
                // https://docs.unity3d.com/ScriptReference/GL-sRGBWrite.html
                // https://docs.unity3d.com/2019.4/Documentation/Manual/LinearRendering-LinearTextures.html
                GL.sRGBWrite = false;
                int activeDepth = (int)depth.GetValue(null);
                object visible = clip.GetProperty("visibleRect", Static)?.GetValue(null);
                phase = "same controller OnGUI";
                draw();
                GL.Flush();
                diagnostic = $"Forced native IMGUI Repaint completed; depth={activeDepth}; clip={visible}; target={target.width}x{target.height}; sRGBTarget={target.sRGB}; sRGBWrite={GL.sRGBWrite} (inherited {oldSrgbWrite}).";
                succeeded = true;
            }
            catch (Exception exception)
            {
                exception = Unwrap(exception);
                diagnostic = $"Offscreen GUI failed during {phase}: {exception.GetType().Name}: {exception.Message}";
            }
            finally
            {
                // Keep the texture bound through native container exit in case a backend
                // flushes GUI draw commands there. The caller reads it after this returns.
                if (entered)
                {
                    Restore("capture target", () => RenderTexture.active = target, ref diagnostic, ref succeeded);
                    Restore("GUI color conversion", () => GL.sRGBWrite = false, ref diagnostic, ref succeeded);
                    if (matrixCaptured) Restore("GUI matrix", () => GUI.matrix = oldMatrix, ref diagnostic, ref succeeded);
                    if (parentPushed) Restore("parent clip", () => clip.GetMethod("Internal_PopParentClip", Static)?.Invoke(null, null), ref diagnostic, ref succeeded);
                    // Native exit must still run when an earlier cleanup step failed.
                    Restore("native EndContainer", () => utility.GetMethod("EndContainer", Static)?.Invoke(null, null), ref diagnostic, ref succeeded);
                    Restore("GUI flush", GL.Flush, ref diagnostic, ref succeeded);
                }
                if (matricesPushed) Restore("GL matrix", GL.PopMatrix, ref diagnostic, ref succeeded);
                Restore("previous render target", () => RenderTexture.active = oldTarget, ref diagnostic, ref succeeded);
                Restore("previous color conversion", () => GL.sRGBWrite = oldSrgbWrite, ref diagnostic, ref succeeded);
                // Event.current returns null outside a GUI event, but its native setter
                // dereferences the supplied Event. EndContainer restores native context;
                // assigning that null snapshot would throw and mask the render diagnostic.
                if (oldEvent != null) Restore("previous GUI event", () => Event.current = oldEvent, ref diagnostic, ref succeeded);
                if (oldLayout != null) Restore("layout cache", () => layoutCurrent?.SetValue(null, oldLayout), ref diagnostic, ref succeeded);
                if (oldSkinMode != null) Restore("skin mode", () => skinMode?.SetValue(null, oldSkinMode), ref diagnostic, ref succeeded);
                if (oldOwnerId != null) Restore("GUI owner", () => ownerId?.SetValue(null, oldOwnerId), ref diagnostic, ref succeeded);
                if (oldPixels != null) Restore("pixel scale", () => pixels?.SetValue(null, oldPixels), ref diagnostic, ref succeeded);
                if (nativeState is IDisposable disposable) Restore("native state disposal", disposable.Dispose, ref diagnostic, ref succeeded);
            }
            return succeeded;
        }

        static void Restore(string label, Action action, ref string diagnostic, ref bool succeeded)
        {
            try { action(); }
            catch (Exception exception)
            {
                exception = Unwrap(exception);
                diagnostic += $" Cleanup {label}: {exception.GetType().Name}: {exception.Message}";
                succeeded = false;
            }
        }

        static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
            return exception;
        }
    }
}
