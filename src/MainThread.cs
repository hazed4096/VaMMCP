using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace VaMMCP {
	/// <summary>
	/// Marshals work from MCP HTTP worker threads onto the Unity main thread.
	/// Plugin.Update() drains the queue every frame.
	/// (No ConcurrentQueue / ManualResetEventSlim: VaM runs the .NET 3.5 API level.)
	/// </summary>
	public class MainThreadDispatcher {
		private readonly object gate = new object();
		private Queue<Action> queue = new Queue<Action>();

		public void Enqueue(Action action) {
			lock (gate) {
				queue.Enqueue(action);
			}
		}

		public void Update() {
			Queue<Action> drain;
			lock (gate) {
				if (queue.Count == 0) return;
				drain = queue;
				queue = new Queue<Action>();
			}
			while (drain.Count > 0) {
				Action a = drain.Dequeue();
				try {
					a();
				} catch (Exception e) {
					Log.Error("main-thread op failed: " + e);
				}
			}
		}

		/// <summary>Run f on the main thread and block the caller until it completes.
		/// If the caller IS the main thread (a tool handler nesting another Mt.Run
		/// inside the drain of the dispatcher), the queue can never drain while the
		/// outer token blocks - that measured as a hard 10 s timeout on trigger Fire.
		/// In that case run inline instead.</summary>
		public T Run<T>(Func<T> f, int timeoutMs = 30000) {
			if (Plugin.mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == Plugin.mainThreadId) {
				return f();
			}
			Exception error = null;
			T result = default(T);
			ManualResetEvent done = new ManualResetEvent(false);
			Enqueue(delegate {
				try {
					result = f();
				} catch (Exception e) {
					error = e;
				} finally {
					done.Set();
				}
			});
			if (!done.WaitOne(timeoutMs)) {
				throw new ApiError("timed out waiting for the VaM main thread (" + timeoutMs + " ms)");
			}
			if (error != null) {
				throw error;
			}
			return result;
		}

		public void Run(Action a, int timeoutMs = 30000) {
			Run(delegate {
				a();
				return 0;
			}, timeoutMs);
		}
	}
}
