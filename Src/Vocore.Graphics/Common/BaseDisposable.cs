using System;

namespace Vocore.Graphics
{
    public abstract class BaseDisposable : IDisposable
    {
        private volatile bool _disposed;

        public bool Disposed => _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Dispose(true);
            _disposed = true;
        }

        protected virtual void Dispose(bool disposing);
    }
}