using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using VaMMCP.Mcp;

namespace VaMMCP {
	[BepInPlugin("com.vammcp.core", "VaMMCP", "1.1.0")]
	public class Plugin : BaseUnityPlugin {
		public static Plugin instance;
		public static int mainThreadId;
		public static ConfigEntry<bool> cfgEnabled;
		public static ConfigEntry<int> cfgPort;
		public static ConfigEntry<bool> cfgAllowEval;
		public static ConfigEntry<int> cfgEvalTimeoutSec;
		public static ConfigEntry<bool> cfgKeepAliveBackground;

		public static volatile int frameTick;

		private MainThreadDispatcher dispatcher;
		private HttpServer httpServer;

		private void Awake() {
			instance = this;
			mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
			Log.Init(Logger);

			cfgEnabled = Config.Bind("Server", "Enabled", true, "Start the MCP HTTP server when VaM launches");
			cfgPort = Config.Bind("Port", "Port", 9837, "Port for the MCP Streamable HTTP endpoint (bound to 127.0.0.1 only)");
			cfgAllowEval = Config.Bind("Security", "AllowEval", false, "Enable the eval_cs tool (runs arbitrary C# inside the VaM process). Only enable on trusted machines.");
			cfgEvalTimeoutSec = Config.Bind("Security", "EvalTimeoutSec", 30, "Timeout for eval_cs executions");
			cfgKeepAliveBackground = Config.Bind("Server", "KeepAliveBackground", true,
				"Keep Unity's player loop running when the VaM window loses focus");

			dispatcher = new MainThreadDispatcher();

			// Patch VaM's scene-load crash paths
			try {
				var harmony = new Harmony("com.vammcp.safeload");
				var scType = typeof(SuperController);
				var dazType = Type.GetType("DAZCharacterSelector, Assembly-CSharp");
				if (dazType != null) {
					var om = dazType.GetMethod("OptimizeMemory", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
					if (om != null) {
						harmony.Patch(om, prefix: new HarmonyMethod(typeof(Plugin).GetMethod("SkipDuringLoad", BindingFlags.Public | BindingFlags.Static)));
						Log.Info("VaMMCP: patched DAZCharacterSelector.OptimizeMemory");
					}
				}
			// Find and patch SuperController.Load / LoadMerge to repair atom list BEFORE loading
			var allMethods = scType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
			foreach (var m in allMethods) {
				if (m.Name.Equals("Load", StringComparison.OrdinalIgnoreCase) ||
				    m.Name.Equals("LoadMerge", StringComparison.OrdinalIgnoreCase) ||
				    m.Name.Equals("LoadInternal", StringComparison.OrdinalIgnoreCase)) {
					try {
						harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin).GetMethod("RepairBeforeLoad", BindingFlags.Public | BindingFlags.Static)));
						Log.Info("VaMMCP: patched " + m.Name + " (" + m.GetParameters().Length + " params)");
					} catch (Exception e) {
						Log.Warn("VaMMCP: failed to patch " + m.Name + ": " + e.Message);
					}
				}
			}
			} catch (Exception e) {
				Log.Error("VaMMCP: harmony patch failed: " + e);
			}

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
			}
		}

		private void Update() {
			frameTick++;
			try { VaMMCP.Api.VaMApi.LoaderWatchdog(); } catch (Exception e) { Log.Debug("loader watchdog: " + e.Message); }
			try { if (Time.unscaledTime < VaMMCP.Api.VaMApi.HideScreenshotPromptUntil) VaMMCP.Api.VaMApi.HideScreenshotPrompt(); } catch { }
			if (cfgKeepAliveBackground != null && cfgKeepAliveBackground.Value) {
				if (!Application.runInBackground) {
					Application.runInBackground = true;
				}
			}
			if (dispatcher != null) {
				dispatcher.Update();
			}
		}

		public static bool RepairBeforeLoad() {
			try {
				VaMMCP.Api.VaMApi.LoaderWatchdog();
			} catch (Exception e) {
				Log.Debug("RepairBeforeLoad: " + e.Message);
			}
			return true; // continue with original Load
		}

		public static bool SkipDuringLoad() {
			try {
				var sc = SuperController.singleton;
				if (sc == null) return true;
				var fi = typeof(SuperController).GetField("_isLoading", BindingFlags.NonPublic | BindingFlags.Instance);
				if (fi == null) return true;
				object v = fi.GetValue(sc);
				if (v is bool && (bool)v) return false;
			} catch { }
			return true;
		}

		private bool borderlessApplied;
		private int watchdogTick;

		private void ApplyKeepAlive() {
			try { Application.runInBackground = true; } catch { }
		}

		private void OnDestroy() {
			if (httpServer != null) {
				try { httpServer.Stop(); } catch (Exception e) { Log.Debug("stop: " + e.Message); }
			}
		}
	}
}
