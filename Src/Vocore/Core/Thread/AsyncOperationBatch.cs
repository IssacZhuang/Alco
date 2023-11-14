using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;

namespace Vocore
{
    public class AsyncOperationBatch<T>
    {
        private struct AsyncOperation
        {
            public Func<T> action;
            public Action<T> success;
            public Action<Exception> fail;
        }

        private List<AsyncOperation> _operations = new List<AsyncOperation>();

        private T[] _results;
        private Exception[] _exceptions;

        public void Enqueue(Func<T> action, Action<T> success, Action<Exception> fail = null)
        {
            AsyncOperation operation = new AsyncOperation();
            operation.action = action;
            operation.success = success;
            operation.fail = fail;
            _operations.Add(operation);
        }

        public void Run()
        {
           if (_operations.Count == 0)
            {
                return;
            }

            if(_results == null || _results.Length != _operations.Count)
            {
                _results = new T[_operations.Count];
            }

            if(_exceptions == null || _exceptions.Length != _operations.Count)
            {
                _exceptions = new Exception[_operations.Count];
            }

            Parallel.For(0, _operations.Count, (i) =>
            {
                try
                {
                    _results[i] = _operations[i].action();
                }
                catch (Exception e)
                {
                    _exceptions[i] = e;
                }
            });

            for (int i = 0; i < _operations.Count; i++)
            {
                if (_exceptions[i] != null && _operations[i].fail != null)
                {
                    _operations[i].fail(_exceptions[i]);
                    continue;
                }

                try
                {
                    _operations[i].success(_results[i]);
                }
                catch (Exception e)
                {
                    if (_operations[i].fail != null)
                    {
                        _operations[i].fail(e);
                    }
                }
            }
        }
    }
}

