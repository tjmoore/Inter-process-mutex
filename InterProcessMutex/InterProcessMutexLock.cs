using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace InterProcessMutex
{
    /// <summary>
    /// Provides a wrapped way to create critical sections across multiple processes using named mutexes
    /// Derived from https://github.com/devshorts/Inter-process-mutex
    /// </summary>
    public class InterProcessMutexLock : IDisposable
    {
        private readonly Mutex _currentMutex;

        private bool _created;
        public bool Created
        {
            get { return _created; }
            set { _created = value; }
        }


        /// <summary>
        /// Construct instance of InterProcessMutexLock with given name and acquire lock
        /// </summary>
        /// <param name="mutexName">Mutex name. Created if doesn't exist, otherwise attempts to open existing mutex</param>
        /// <exception cref="ArgumentException">Mutex must be given a name</exception>
        /// <exception cref="FailedToCreateOrAcquireMutexException">Failed to create or acquire mutex</exception>
        /// <exception cref="FailedToReaquireMutexException">Thrown when failing to re-acquire an abandoned mutex</exception>
        public InterProcessMutexLock(string mutexName)
        {
            if (string.IsNullOrEmpty(mutexName))
            {
                throw new ArgumentException("Mutex must be given a name", "mutexName");
            }

            try
            {
                try
                {
                    _currentMutex = Mutex.OpenExisting(mutexName);
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // grant everyone access to the mutex
                    var security = new MutexSecurity();
                    var everyoneIdentity = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                    var rule = new MutexAccessRule(everyoneIdentity, MutexRights.FullControl, AccessControlType.Allow);
                    security.AddAccessRule(rule);

                    // make sure to not initially own it, because if you do it also acquires the lock
                    // we want to explicitly attempt to acquire the lock ourselves so we know how many times
                    // this object acquired and released the lock
                    _currentMutex = new Mutex(false, mutexName, out _created, security);
                }

                AquireMutex();
            }
            catch (IOException ex)
            {
                throw new FailedToCreateOrAcquireMutexException(string.Format("Failed to create or acquire mutext: {0}", mutexName), ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new FailedToCreateOrAcquireMutexException(string.Format("Failed to create or acquire mutext: {0}", mutexName), ex);
            }
            catch (IdentityNotMappedException ex)
            {
                throw new FailedToCreateOrAcquireMutexException(string.Format("Failed to create or acquire mutext: {0}", mutexName), ex);
            }
            catch (WaitHandleCannotBeOpenedException ex)
            {
                throw new FailedToCreateOrAcquireMutexException(string.Format("Failed to create or acquire mutext: {0}", mutexName), ex);
            }
        }

        /// <exception cref="FailedToReaquireMutexException">Thrown when failing to re-acquire an abandoned mutex</exception>
        private void AquireMutex()
        {
            try
            {
                _currentMutex.WaitOne();
            }
            catch (AbandonedMutexException abandondedMutexEx)
            {
                try
                {
                    Log.Error(this, "An abandoned mutex was encountered, attempting to release", abandondedMutexEx);
                    _currentMutex.ReleaseMutex();

                    Log.Debug(this, "Abandonded mutex was released and now aquiring");

                    _currentMutex.WaitOne();
                }
                catch (ApplicationException ex)
                {
                    throw new FailedToReaquireMutexException("Tried to re-acquire abandoned mutex but failed", ex);
                }
                catch (ObjectDisposedException ex)
                {
                    throw new FailedToReaquireMutexException("Tried to re-acquire abandoned mutex but failed", ex);
                }
                catch (InvalidOperationException ex)
                {
                    throw new FailedToReaquireMutexException("Tried to re-acquire abandoned mutex but failed", ex);
                }
            }
        }

        #region IDisposable implementation
        private bool _disposed;

        protected void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (_currentMutex != null)
                {
                    try
                    {
                        _currentMutex.ReleaseMutex();
                    }
                    catch (ApplicationException ex)
                    {
                        Log.Error(this, "Exception during ReleaseMutex on disposal of InterProcessMutexLock", ex);
                    }
                    _currentMutex.Dispose();
                }
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }

    public static class Log
    {
        public static void Error(object source, string format, Exception ex = null)
        {
            if (ex != null)
                Console.WriteLine("{0}: {1} - {2}", source, format, ex);
            else
                Console.WriteLine("{0}: {1}", source, format);
        }

        public static void Debug(object source, string format, Exception ex = null)
        {
            Error(source, format, ex);
        }
    }

    #region Exceptions
    public class FailedToReaquireMutexException : Exception
    {
        public FailedToReaquireMutexException(string message, object source) : base(message)
        {
            Log.Debug(source, message);
        }

        public FailedToReaquireMutexException(string message, Exception innerException, object source) : base(message, innerException)
        {
            Log.Debug(source, message, innerException);
        }
    }

    public class FailedToCreateOrAcquireMutexException : Exception
    {
        public FailedToCreateOrAcquireMutexException(string message, object source) : base(message)
        {
            Log.Debug(source, message);
        }

        public FailedToCreateOrAcquireMutexException(string message, Exception innerException, object source) : base(message, innerException)
        {
            Log.Debug(source, message, innerException);
        }
    }
    #endregion
}
