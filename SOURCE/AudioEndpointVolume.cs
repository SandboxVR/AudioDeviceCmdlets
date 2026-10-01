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
using System.Text;
using CoreAudioApi.Interfaces;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{

    public class AudioEndpointVolume : ComObject
    {
        internal IAudioEndpointVolume EndpointInterface { get { return GetInterface<IAudioEndpointVolume>(); } }
        private IAudioEndpointVolume _AudioEndPointVolume { get { return EndpointInterface; } }
        private AudioEndpointVolumeChannels _Channels;
        private AudioEndpointVolumeStepInformation _StepInformation;
        private AudioEndPointVolumeVolumeRange _VolumeRange;
        private EEndpointHardwareSupport _HardwareSupport;
        private AudioEndpointVolumeCallback _CallBack;
        private AudioEndpointVolumeNotificationDelegate _OnVolumeNotification;
        private readonly object _NotificationLock = new object();

        public event AudioEndpointVolumeNotificationDelegate OnVolumeNotification
        {
            add
            {
                lock (_NotificationLock)
                {
                    ThrowIfDisposed();
                    if (value == null) return;
                    if (_CallBack == null)
                    {
                        AudioEndpointVolumeCallback callback = new AudioEndpointVolumeCallback(this);
                        Marshal.ThrowExceptionForHR(_AudioEndPointVolume.RegisterControlChangeNotify(callback));
                        _CallBack = callback;
                    }
                    _OnVolumeNotification += value;
                }
            }
            remove
            {
                lock (_NotificationLock)
                {
                    ThrowIfDisposed();
                    _OnVolumeNotification -= value;
                    if (_OnVolumeNotification == null && _CallBack != null)
                    {
                        Marshal.ThrowExceptionForHR(_AudioEndPointVolume.UnregisterControlChangeNotify(_CallBack));
                        _CallBack = null;
                    }
                }
            }
        }

        public AudioEndPointVolumeVolumeRange VolumeRange
        {
            get
            {
                return _VolumeRange;
            }
        }
        public EEndpointHardwareSupport HardwareSupport
        {
            get
            {
                return _HardwareSupport;
            }
        }
        public AudioEndpointVolumeStepInformation StepInformation
        {
            get
            {
                return _StepInformation;
            }
        }
        public AudioEndpointVolumeChannels Channels
        {
            get
            {
                return _Channels;
            }
        }
        public float MasterVolumeLevel
        {
            get
            {
                float result;
                Marshal.ThrowExceptionForHR(_AudioEndPointVolume.GetMasterVolumeLevel(out result));
                return result;
            }
            set
            {
                Marshal.ThrowExceptionForHR(_AudioEndPointVolume.SetMasterVolumeLevel(value, Guid.Empty));
            }
        }
        public float MasterVolumeLevelScalar
        {
            get
            {
                float result;
                Marshal.ThrowExceptionForHR(_AudioEndPointVolume.GetMasterVolumeLevelScalar(out result));
                return result;
            }
            set
            {
                Marshal.ThrowExceptionForHR(_AudioEndPointVolume.SetMasterVolumeLevelScalar(value, Guid.Empty));
            }
        }
        public bool Mute
        {
            get
            {
                bool result;
                Marshal.ThrowExceptionForHR(_AudioEndPointVolume.GetMute(out result));
                return result;
            }
            set
            {
                Marshal.ThrowExceptionForHR(_AudioEndPointVolume.SetMute(value, Guid.Empty));
            }
        }
        public void VolumeStepUp()
        {
            Marshal.ThrowExceptionForHR(_AudioEndPointVolume.VolumeStepUp(Guid.Empty));
        }
        public void VolumeStepDown()
        {
            Marshal.ThrowExceptionForHR(_AudioEndPointVolume.VolumeStepDown(Guid.Empty));
        }
        internal AudioEndpointVolume(IAudioEndpointVolume realEndpointVolume)
            : base(realEndpointVolume)
        {
            uint HardwareSupp;
            try
            {
                _Channels = new AudioEndpointVolumeChannels(this);
                _StepInformation = new AudioEndpointVolumeStepInformation(_AudioEndPointVolume);
                Marshal.ThrowExceptionForHR(_AudioEndPointVolume.QueryHardwareSupport(out HardwareSupp));
                _HardwareSupport = (EEndpointHardwareSupport)HardwareSupp;
                _VolumeRange = new AudioEndPointVolumeVolumeRange(_AudioEndPointVolume);
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        internal void FireNotification(AudioVolumeNotificationData NotificationData)
        {
            AudioEndpointVolumeNotificationDelegate del = _OnVolumeNotification;
            if (del != null)
            {
                del(NotificationData);
            }
        }
        #region IDisposable Members

        protected override void DisposeResources()
        {
            lock (_NotificationLock)
            {
                try
                {
                    if (_CallBack != null)
                        Marshal.ThrowExceptionForHR(_AudioEndPointVolume.UnregisterControlChangeNotify(_CallBack));
                }
                finally
                {
                    _CallBack = null;
                    _OnVolumeNotification = null;
                    _Channels = null;
                }
            }
        }

        ~AudioEndpointVolume()
        {
            // A callback holds only a weak reference, so abandoned subscriptions
            // can reach this finalizer. Never throw on the finalizer thread.
            try { Dispose(); } catch { }
        }

        #endregion
       
    }
}
