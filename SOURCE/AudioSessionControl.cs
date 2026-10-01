/*
  LICENSE
  -------
  Copyright (C) 2007-2010 Ray Molenkamp

  This source code is provided 'as-is', without any express or implied
  warranty.  In no event will the authors be held liable for any damages
  arising from the use of this source code or the software it produces.

  Permission is granted to anyone to use this source code for any purpose,
  including commercial applications, and to alter it and redistribute it
  freely, subject to the following restrictions:

  1. The origin of this source code must not be misrepresented; you must not
     claim that you wrote the original source code.  If you use this source code
     in a product, an acknowledgment in the product documentation would be
     appreciated but is not required.
  2. Altered source versions must be plainly marked as such, and must not be
     misrepresented as being the original source code.
  3. This notice may not be removed or altered from any source distribution.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CoreAudioApi.Interfaces;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    public class AudioSessionControl : ComObject
    {
        private IAudioSessionControl2 _AudioSessionControl { get { return GetInterface<IAudioSessionControl2>(); } }
        internal AudioMeterInformation _AudioMeterInformation;
        internal SimpleAudioVolume _SimpleAudioVolume;
        private readonly List<IAudioSessionEvents> _EventConsumers = new List<IAudioSessionEvents>();

        public AudioMeterInformation AudioMeterInformation
        {
            get
            {
                ThrowIfDisposed();
                return _AudioMeterInformation;
            }
        }

        public SimpleAudioVolume SimpleAudioVolume
        {
            get
            {
                ThrowIfDisposed();
                return _SimpleAudioVolume;
            }
        }


        internal AudioSessionControl(IAudioSessionControl2 realAudioSessionControl)
            : base(realAudioSessionControl)
        {
            try
            {
                IAudioMeterInformation _meters = realAudioSessionControl as IAudioMeterInformation;
                ISimpleAudioVolume _volume = realAudioSessionControl as ISimpleAudioVolume;
                if (_meters != null)
                    _AudioMeterInformation = new CoreAudioApi.AudioMeterInformation(_meters, false);
                if (_volume != null)
                    _SimpleAudioVolume = new SimpleAudioVolume(_volume, false);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void RegisterAudioSessionNotification(IAudioSessionEvents eventConsumer)
        {
            Marshal.ThrowExceptionForHR(_AudioSessionControl.RegisterAudioSessionNotification(eventConsumer));
            if (!_EventConsumers.Contains(eventConsumer)) _EventConsumers.Add(eventConsumer);
        }

        public void UnregisterAudioSessionNotification(IAudioSessionEvents eventConsumer)
        {
            Marshal.ThrowExceptionForHR(_AudioSessionControl.UnregisterAudioSessionNotification(eventConsumer));
            _EventConsumers.Remove(eventConsumer);
        }

        public AudioSessionState State
        {
            get
            {
                AudioSessionState res;
                Marshal.ThrowExceptionForHR(_AudioSessionControl.GetState(out res));
                return res;
            }
        }

        public string DisplayName
        {
            get
            {
                IntPtr NamePtr = IntPtr.Zero;
                try
                {
                    Marshal.ThrowExceptionForHR(_AudioSessionControl.GetDisplayName(out NamePtr));
                    return Marshal.PtrToStringUni(NamePtr);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(NamePtr);
                }
            }
        }

        public string IconPath
        {
            get
            {
                IntPtr NamePtr = IntPtr.Zero;
                try
                {
                    Marshal.ThrowExceptionForHR(_AudioSessionControl.GetIconPath(out NamePtr));
                    return Marshal.PtrToStringUni(NamePtr);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(NamePtr);
                }
            }
        }

        public string SessionIdentifier
        {
            get
            {
                IntPtr NamePtr = IntPtr.Zero;
                try
                {
                    Marshal.ThrowExceptionForHR(_AudioSessionControl.GetSessionIdentifier(out NamePtr));
                    return Marshal.PtrToStringUni(NamePtr);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(NamePtr);
                }
            }
        }

        public string SessionInstanceIdentifier
        {
            get
            {
                IntPtr NamePtr = IntPtr.Zero;
                try
                {
                    Marshal.ThrowExceptionForHR(_AudioSessionControl.GetSessionInstanceIdentifier(out NamePtr));
                    return Marshal.PtrToStringUni(NamePtr);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(NamePtr);
                }
            }
        }

        public uint ProcessID
        {
            get
            {
                uint pid;
                Marshal.ThrowExceptionForHR(_AudioSessionControl.GetProcessId(out pid));
                return pid;
            }
        }

        public bool IsSystemIsSystemSoundsSession
        {
            get
            {
                return (_AudioSessionControl.IsSystemSoundsSession() == 0);  //S_OK
            }

        }



        protected override void DisposeResources()
        {
            Exception error = null;
            try
            {
                foreach (IAudioSessionEvents consumer in _EventConsumers)
                {
                    try { Marshal.ThrowExceptionForHR(_AudioSessionControl.UnregisterAudioSessionNotification(consumer)); }
                    catch (Exception ex) { if (error == null) error = ex; }
                }
                _EventConsumers.Clear();
                DisposeAll(_AudioMeterInformation, _SimpleAudioVolume);
                if (error != null) throw error;
            }
            finally
            {
                _AudioMeterInformation = null;
                _SimpleAudioVolume = null;
            }
        }
    }
}
