// Resource ownership added for long-running Core Audio clients.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CoreAudioApi
{
    public abstract class ComObject : IDisposable
    {
        private object _instance;
        private readonly bool _ownsReference;
        private bool _disposed;
        private int _disposeStarted;

        protected ComObject(object instance, bool ownsReference = true)
        {
            if (instance == null) throw new ArgumentNullException("instance");
            _instance = instance;
            _ownsReference = ownsReference;
        }

        protected T GetInterface<T>() where T : class
        {
            ThrowIfDisposed();
            return (T)_instance;
        }

        protected void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }

        protected virtual void DisposeResources() { }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
            try
            {
                DisposeResources();
            }
            finally
            {
                _disposed = true;
                object instance = _instance;
                _instance = null;
                try { if (_ownsReference) ReleaseReference(instance); }
                finally { GC.SuppressFinalize(this); }
            }
        }

        internal static void ReleaseReference(object instance)
        {
            // Balance only our acquisition. Other wrappers may share this RCW.
            if (instance != null && Marshal.IsComObject(instance))
                Marshal.ReleaseComObject(instance);
        }

        internal static T CheckResult<T>(int result, T instance) where T : class
        {
            if (result < 0)
            {
                try { Marshal.ThrowExceptionForHR(result); }
                finally { ReleaseReference(instance); }
            }
            return instance;
        }

        internal static void DisposeAll(params IDisposable[] resources)
        {
            Exception error = null;
            foreach (IDisposable resource in resources)
            {
                try { if (resource != null) resource.Dispose(); }
                catch (Exception ex) { if (error == null) error = ex; }
            }
            if (error != null) throw error;
        }
    }
}
