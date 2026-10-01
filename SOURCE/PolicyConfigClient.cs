using System;
using System.Collections.Generic;
using System.Text;
using CoreAudioApi.Interfaces;
using System.Runtime.InteropServices;

namespace CoreAudioApi
{
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    internal class _PolicyConfigClient
    {
    }

    public class PolicyConfigClient : ComObject
    {
        private IPolicyConfig _PolicyConfig { get { return GetInterface<object>() as IPolicyConfig; } }
        private IPolicyConfigVista _PolicyConfigVista { get { return GetInterface<object>() as IPolicyConfigVista; } }
        private IPolicyConfig10 _PolicyConfig10 { get { return GetInterface<object>() as IPolicyConfig10; } }

        public PolicyConfigClient() : base(new _PolicyConfigClient())
        {
        }

        public void SetDefaultEndpoint(string devID, ERole eRole)
        {
            if (_PolicyConfig != null)
            {
                Marshal.ThrowExceptionForHR(_PolicyConfig.SetDefaultEndpoint(devID, eRole));
                return;
            }
            if (_PolicyConfigVista != null)
            {
                Marshal.ThrowExceptionForHR(_PolicyConfigVista.SetDefaultEndpoint(devID, eRole));
                return;
            }
            if (_PolicyConfig10 != null)
            {
                Marshal.ThrowExceptionForHR(_PolicyConfig10.SetDefaultEndpoint(devID, eRole));
            }
        }
    }
}
