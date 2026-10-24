using System;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using VaMMCP.Mcp;

namespace VaMMCP {
	[BepInPlugin("com.vammcp.core", "VaMMCP", "1.0.1")]
	public class Plugin : BaseUnityPlugin {
		public static Plugin instance;
		/// <summary>ManagedThreadId of the Unity main thread, captured in Awake so the
		/// dispatcher can run nested Mt.Run calls inline instead of deadlocking on them.</summary>
		public static int mainThreadId;
		public static ConfigEntry<bool> cfgEnabled;
		public static ConfigEntry<int> cfgPort;
		public static ConfigEntry<bool> cfgAllowEval;
		public static ConfigEntry<int> cfgEvalTimeoutSec;
		public static ConfigEntry<bool> cfgKeepAliveBackground;

		/// <summary>Main-thread frame counter, incremented once per Update() call.
		/// Worker threads (e.g. performance_profile) must poll this instead of
		/// Time.frameCount: Unity's static frame info is unreliable to read from
		/// off the main thread, while this counter is a plain volatile int that
		/// every frame publishes. Stops advancing when the player loop stops
		/// (minimized window), which is exactly what the profiler should observe.</summary>
		public static volatile int frameTick;

		private MainThreadDispatcher dispatcher;
		private HttpServer httpServer;

		private void Awake() {
			instance = this;
			mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
			Log.Init(Logger);

			cfgEnabled = Config.Bind("Server", "Enabled", true, "Start the MCP HTTP server when VaM launches");
			cfgPort = Config.Bind("Server", "Port", 9837, "Port for the MCP Streamable HTTP endpoint (bound to 127.0.0.1 only)");
			cfgAllowEval = Config.Bind("Security", "AllowEval", false, "Enable the eval_cs tool (runs arbitrary C# inside the VaM process). Only enable on trusted machines.");
			cfgEvalTimeoutSec = Config.Bind("Security", "EvalTimeoutSec", 30, "Timeout for eval_cs executions");
			cfgKeepAliveBackground = Config.Bind("Server", "KeepAliveBackground", true,
				"Keep Unity's player loop running when the VaM window loses focus (runInBackground stays true, Windows power/EcoQoS throttling disabled for this process). IdleSaver still caps the FPS, so the resource cost is small - but MCP tools that wait on the main thread (atom creation, plugins, clothing) no longer stall for minutes while VaM sits unfocused on another monitor/desktop.");

			dispatcher = new MainThreadDispatcher();

			if (cfgKeepAliveBackground.Value) {
				ApplyKeepAlive();
			}

			if (cfgEnabled.Value) {
				try {
					httpServer = new HttpServer(new McpServer(dispatcher, cfgAllowEval.Value), cfgPort.Value);
					httpServer.Start();
					Log.Info("VaMMCP ready. MCP endpoint: http://127.0.0.1:" + cfgPort.Value + "/mcp");
				} catch (Exception e) {
					Log.Error("VaMMCP failed to start the MCP server: " + e);
				}
			} else {
				Log.Info("VaMMCP loaded but the MCP server is disabled (Server.Enabled=false).");
			}
		}

		private void Update() {
			frameTick++;
			// Defeat anything that resets the background-run flag (VaM itself sets
			// it false somewhere; measured live: even with UiPatch forcing it true
			// at plugin Init, an unfocused window stopped ticking entirely).
			if (cfgKeepAliveBackground != null && cfgKeepAliveBackground.Value) {
				if (!Application.runInBackground) {
					Application.runInBackground = true;
					Log.Info("VaMMCP: re-enabled Application.runInBackground (something reset it)");
				}
				// A fullscreen (exclusive) player STOPS its whole loop on focus loss -
				// that is why an idle VaM sat at ~0.25 fps, Screen.width collapsed to
				// <=1 (IdleSaver read it as "minimized") and every main-thread tool
				// timed out. Borderless window (FullScreenWindow) keeps the game
				// covering the whole monitor but keeps the loop alive unfocused.
				if (!borderlessApplied && Screen.width > 1 && Screen.fullScreen &&
					Screen.fullScreenMode != FullScreenMode.FullScreenWindow) {
					try {
						Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
						borderlessApplied = true;
						Log.Info("VaMMCP: display switched to borderless window (FullScreenWindow) so the player loop keeps running when VaM is unfocused");
					} catch (Exception e) {
						borderlessApplied = true;
						Log.Info("VaMMCP: borderless switch failed: " + e.Message);
					}
				}
			}
			if (dispatcher != null) {
				dispatcher.Update();
			}
		}

		private bool borderlessApplied;

		/// <summary>Ensure the player keeps its loop when unfocused. On this desktop
		// build the EcoQoS disable API (SetProcessInformation / ProcessPowerThrottling)
		// returned ERROR_BAD_LENGTH and is not usable, so nothing to do there: the
		// fixes that actually matter are runInBackground + borderless display mode,
		// handled in Update().</summary>
		private void ApplyKeepAlive() {
			try { Application.runInBackground = true; Log.Info("VaMMCP: keepalive on (runInBackground=true, borderless pending first frame)"); } catch { }
		}

		private void OnDestroy() {
			if (httpServer != null) {
				try {
					httpServer.Stop();
				} catch (Exception e) {
					Log.Debug("stop: " + e.Message);
				}
			}
		}
	}
}
