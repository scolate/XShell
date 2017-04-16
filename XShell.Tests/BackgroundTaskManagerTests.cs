﻿using System;
using System.Collections.Concurrent;
using System.Threading;
using NUnit.Framework;
using XShell.Services;

namespace XShell.Tests
{
    [TestFixture]
    public class BackgroundTaskManagerTests
    {
        [Test]
        public void DispatchIndeterminateTest()
        {
            var uiDispatcher = new MockUiDispatcher();
            var manager = new BackgroundTaskManager(uiDispatcher);

            var countdown = new CountdownEvent(4);
            
            var state = "State";

            manager.TaskStarted += (t, s) =>
            {
                Assert.IsTrue(t.IsIndeterminated);
                Assert.AreEqual(s, state);
                Assert.AreEqual(uiDispatcher.ThreadId, Thread.CurrentThread.ManagedThreadId);
                Assert.AreEqual(4, countdown.CurrentCount);
                countdown.Signal();
            };

            manager.TaskCompleted += (t, s) =>
            {
                Assert.IsTrue(t.IsIndeterminated);
                Assert.AreEqual(s, state);
                Assert.AreEqual(uiDispatcher.ThreadId, Thread.CurrentThread.ManagedThreadId);
                Assert.AreEqual(1, countdown.CurrentCount);
                countdown.Signal();
            };

            manager.Dispatch(s =>
            {
                Assert.AreEqual(state, s);
                Assert.AreNotEqual(uiDispatcher.ThreadId, Thread.CurrentThread.ManagedThreadId);
                Assert.AreEqual(3, countdown.CurrentCount);
                countdown.Signal();
                return "Result";
            }, (r, s) =>
            {
                Assert.AreEqual("Result", r);
                Assert.AreEqual(uiDispatcher.ThreadId, Thread.CurrentThread.ManagedThreadId);
                Assert.AreEqual(2, countdown.CurrentCount);
                countdown.Signal();
            }, state);

            Assert.IsTrue(countdown.Wait(5000));
        }

        [Test]
        public void DispatchAndReportTest()
        {
            var uiDispatcher = new MockUiDispatcher();
            var manager = new BackgroundTaskManager(uiDispatcher);

            const int steps = 10;

            var countdown = new CountdownEvent(4 + steps);

            var state = "State";

            manager.TaskStarted += (t, s) =>
            {
                Assert.IsFalse(t.IsIndeterminated);
                Assert.AreEqual(s, state);
                Assert.AreEqual(uiDispatcher.ThreadId, Thread.CurrentThread.ManagedThreadId);
                Assert.AreEqual(4 + steps, countdown.CurrentCount);
                countdown.Signal();
            };

            manager.ReportStateChanged += (p, s) =>
            {
                Assert.AreEqual(p + " %", s);
                Assert.AreEqual(uiDispatcher.ThreadId, Thread.CurrentThread.ManagedThreadId);
                Assert.AreEqual(3 + steps - p / steps, countdown.CurrentCount);
                countdown.Signal();
            };

            manager.TaskCompleted += (t, s) =>
            {
                Assert.IsFalse(t.IsIndeterminated);
                Assert.AreEqual(s, state);
                Assert.AreEqual(uiDispatcher.ThreadId, Thread.CurrentThread.ManagedThreadId);
                Assert.AreEqual(1, countdown.CurrentCount);
                countdown.Signal();
            };

            manager.Dispatch((t, s) =>
            {
                Assert.AreEqual(state, s);
                Assert.AreNotEqual(uiDispatcher.ThreadId, Thread.CurrentThread.ManagedThreadId);

                for (var i = 0; i < steps; i++)
                {
                    t.ReportState(i * 10, (i*10)+" %");
                    Thread.Sleep(10);
                }

                Assert.AreEqual(3, countdown.CurrentCount);
                countdown.Signal();
                return "Result";
            }, (r, s) =>
            {
                Assert.AreEqual("Result", r);
                Assert.AreEqual(uiDispatcher.ThreadId, Thread.CurrentThread.ManagedThreadId);
                Assert.AreEqual(2, countdown.CurrentCount);
                countdown.Signal();
            }, state);

            Assert.IsTrue(countdown.Wait(5000));
        }

        public class MockUiDispatcher : IUiDispatcher, IDisposable
        {
            private readonly ManualResetEvent waiter = new ManualResetEvent(false);
            private readonly Thread thread;
            private readonly ConcurrentQueue<Action> tasks = new ConcurrentQueue<Action>();
            private bool isRunning = true;

            public int ThreadId { get { return thread.ManagedThreadId; } }

            public MockUiDispatcher()
            {
                thread = new Thread(Work){ IsBackground = true };
                thread.Start();
            }

            private void Work()
            {
                while (isRunning)
                {
                    waiter.WaitOne();

                    while (tasks.Count > 0)
                    {
                        Action action;
                        if (tasks.TryDequeue(out action))
                            action();
                    }

                    waiter.Reset();
                }
            }

            #region Implementation of IUiDispatcher

            public void Dispatch(Action action)
            {
                if (action == null) return;

                tasks.Enqueue(action);
                waiter.Set();
            }

            #endregion

            #region Implementation of IDisposable

            public void Dispose()
            {
                isRunning = false;
            }

            #endregion
        }
    }
}
