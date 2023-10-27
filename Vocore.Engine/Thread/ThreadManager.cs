using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Vocore.Engine
{
    public class ThreadManager
    {
        private class AsyncTask : IThreadPoolWorkItem
        {
            //on thread
            private Action _onAsync;
            //on main thread
            private Action _onSync;
            private bool _isDone;
            private Exception? _exception;
            public bool IsDone
            {
                get
                {
                    return Volatile.Read(ref _isDone);
                }
            }
            public AsyncTask(Action task, Action success)
            {
                this._onAsync = task;
                this._onSync = success;
                _isDone = false;
                _exception = null;
            }

            //for reuse
            public void Reset(Action task, Action success)
            {
                this._onAsync = task;
                this._onSync = success;
                _isDone = false;
                _exception = null;
            }

            public void SetException(Exception exception)
            {
                this._exception = exception;
            }

            public bool TryGetException(out Exception? exception)
            {
                exception = this._exception;
                return exception != null;
            }

            public void SetDone()
            {
                Volatile.Write(ref _isDone, true);
            }

            //on thread
            public void Execute()
            {
                try
                {
                    _onAsync?.Invoke();
                }
                catch (Exception e)
                {
                    SetException(e);
                }
                finally
                {
                    SetDone();
                }
            }

            //on main thread
            public void DoSuccess()
            {
                try
                {
                    _onSync?.Invoke();
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }


        }
        private List<AsyncTask> _tasks = new List<AsyncTask>();
        private List<AsyncTask> _pendingRemove = new List<AsyncTask>();
        private ArrayPool<AsyncTask> _pool = new ArrayPool<AsyncTask>(100);
        //allways run in main thread
        internal void Update()
        {
            foreach (var task in _tasks)
            {
                if (task.IsDone)
                {
                    if (task.TryGetException(out var exception))
                    {
                        Log.Error(exception);
                    }
                    else
                    {
                        task.DoSuccess();
                    }
                    _pendingRemove.Add(task);
                }
            }

            foreach (var task in _pendingRemove)
            {
                _tasks.Remove(task);
                _pool.Return(task);
            }
            _pendingRemove.Clear();
        }

        public void AddAsyncTask(Action onAsync, Action onSync)
        {
            if (!IsCallFromMainThread())
            {
                Log.Error("AddAsyncTask should be called out of main thread");
                return;
            }

            if (onAsync == null)
            {
                Log.Error("function task should not be null");
                return;
            }

            if (onSync == null)
            {
                Log.Error("function success should not be null");
                return;
            }

            AsyncTask asyncTask = _pool.Get();
            if (asyncTask == null)
            {
                asyncTask = new AsyncTask(onAsync, onSync);
            }
            else
            {
                asyncTask.Reset(onAsync, onSync);
            }
            _tasks.Add(asyncTask);
            ThreadPool.UnsafeQueueUserWorkItem(asyncTask, false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsCallFromMainThread()
        {
            return Environment.CurrentManagedThreadId == Application.MainThread;
        }
    }
}