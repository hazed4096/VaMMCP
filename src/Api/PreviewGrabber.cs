using System;
using System.IO;
using System.Threading;
using UnityEngine;
using MVR.FileManagementSecure;

namespace VaMMCP.Api {
	/// <summary>
	/// Grabs the real window back buffer right after the monitor camera renders
	/// a frame. This is what makes person skin/clothing render in captures: a
	/// plain cam.Render() into a RenderTexture drops DAZCharacter meshes, while
	/// the actual game view includes them.
	/// Attached to the monitor camera's GameObject; the CaptureView tool sets
	/// <see cref="pending"/>, this reads the back buffer at the end of the frame
	/// and the tool polls <see cref="lastSeq"/> from its HTTP worker thread.
	/// </summary>
	public class PreviewGrabber : MonoBehaviour {
		/// <summary>Set on the main thread by CaptureView; consumed once by OnPostRender.</summary>
		public volatile bool pending;
		public volatile int reqWidth = 1280;
		public volatile int reqHeight = 720;
		public volatile string reqPath = "Saves/PluginData/vam-mcp/preview.png";

		/// <summary>
		/// Render-texture mode. When set, the owner attaches <see cref="rtTarget"/>
		/// to the camera's targetTexture so the camera renders into it as part of
		/// the normal frame loop. That matters: a manual cam.Render() drops the
		/// DAZCharacter meshes VaM submits per frame, which is why off-screen
		/// captures used to come out as black silhouettes. Grabbing here (after the
		/// normal render) keeps them, and needs no foreground window.
		/// </summary>
		public volatile bool rtMode;
		public volatile RenderTexture rtTarget;

		/// <summary>Completed-capture counter (increments per grab); poll target for the tool.</summary>
		public long lastSeq;
		/// <summary>Copy of the encoded PNG of the last completed grab (inline images).</summary>
		public volatile byte[] lastBytes;
		public volatile int lastWidth;
		public volatile int lastHeight;
		public volatile string lastError;

		private void OnPostRender() {
			if (!pending) return;
			pending = false;
			try {
				DoGrab();
				lastError = null;
				Interlocked.Increment(ref lastSeq);
			} catch (Exception e) {
				lastError = e.ToString();
			}
		}

		private void DoGrab() {
			int w = Mathf.Clamp(reqWidth, 64, 4096);
			int h = Mathf.Clamp(reqHeight, 64, 4096);
			RenderTexture srcRt = rtTarget;
			if (rtMode && srcRt != null) {
				// The camera just rendered into this RT in the normal frame loop,
				// so person skin/clothing are in it.
				RenderTexture prevActive = RenderTexture.active;
				Texture2D shot = null;
				try {
					RenderTexture.active = srcRt;
					shot = new Texture2D(srcRt.width, srcRt.height, TextureFormat.RGB24, false);
					shot.ReadPixels(new Rect(0, 0, srcRt.width, srcRt.height), 0, 0);
					shot.Apply();
					byte[] rtBytes = shot.EncodeToPNG();
					lastWidth = shot.width;
					lastHeight = shot.height;
					if (!string.IsNullOrEmpty(reqPath)) {
						string dir = Path.GetDirectoryName(reqPath.Replace('\\', '/'));
						if (!string.IsNullOrEmpty(dir)) FileManagerSecure.CreateDirectory(dir);
						FileManagerSecure.WriteAllBytes(reqPath, rtBytes);
					}
					lastBytes = rtBytes;
				} finally {
					RenderTexture.active = prevActive;
					if (shot != null) UnityEngine.Object.Destroy(shot);
				}
				return;
			}
			// The camera just rendered to the screen back buffer; read it while
			// RenderTexture.active is null (only safe from inside OnPostRender).
			Texture2D full = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
			Texture2D sized = null;
			try {
				RenderTexture.active = null;
				full.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
				full.Apply();
				if (w != Screen.width || h != Screen.height) {
					// GPU downscale through a temp RT, then read back at target size.
					RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
					RenderTexture prevActive = RenderTexture.active;
					Graphics.Blit(full, rt);
					RenderTexture.active = rt;
					sized = new Texture2D(w, h, TextureFormat.RGB24, false);
					sized.ReadPixels(new Rect(0, 0, w, h), 0, 0);
					sized.Apply();
					RenderTexture.active = prevActive;
					RenderTexture.ReleaseTemporary(rt);
				}
				Texture2D outTex = sized != null ? sized : full;
				byte[] bytes = outTex.EncodeToPNG();
				lastWidth = outTex.width;
				lastHeight = outTex.height;
				if (!string.IsNullOrEmpty(reqPath)) {
					string dir = Path.GetDirectoryName(reqPath.Replace('\\', '/'));
					if (!string.IsNullOrEmpty(dir)) FileManagerSecure.CreateDirectory(dir);
					FileManagerSecure.WriteAllBytes(reqPath, bytes);
				}
				lastBytes = bytes;
			} finally {
				if (sized != null) UnityEngine.Object.Destroy(sized);
				UnityEngine.Object.Destroy(full);
			}
		}
	}
}
