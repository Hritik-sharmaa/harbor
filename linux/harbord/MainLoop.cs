using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Harbor
{
    /// <summary>
    /// harbord's one "UI" thread. The shared code was written for WinForms, where everything runs on one thread and
    /// background work posts its results back; this loop gives it the same guarantee on Linux.
    /// </summary>
    sealed class MainLoop : SynchronizationContext
    {
        readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        int threadId;

        public bool OnLoopThread { get { return Environment.CurrentManagedThreadId == threadId; } }

        public override void Post(SendOrPostCallback d, object state) { Enqueue(() => d(state)); }

        public override void Send(SendOrPostCallback d, object state)
        {
            if (OnLoopThread) { d(state); return; }
            using (var done = new ManualResetEventSlim())
            {
                Exception err = null;
                Enqueue(() => { try { d(state); } catch (Exception ex) { err = ex; } finally { done.Set(); } });
                done.Wait();
                if (err != null) throw err;
            }
        }

        public void Enqueue(Action a)
        {
            if (a == null) return;
            try { queue.Add(a); } catch (InvalidOperationException) { } // stopping
        }

        /// <summary>Runs queued work on this thread until Stop().</summary>
        public void Run()
        {
            threadId = Environment.CurrentManagedThreadId;
            SetSynchronizationContext(this);
            foreach (var a in queue.GetConsumingEnumerable())
            {
                try { a(); }
                catch (Exception ex) { Core.Log(ex); }
            }
        }

        public void Stop() { queue.CompleteAdding(); }
    }
}
