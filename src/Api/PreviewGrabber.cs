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
