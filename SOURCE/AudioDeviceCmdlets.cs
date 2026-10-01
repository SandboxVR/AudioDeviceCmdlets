/*
  Copyright (c) 2016-2022 Francois Gendron <fg@frgn.ca>
  MIT License

  AudioDeviceCmdlets.cs
  AudioDeviceCmdlets is a suite of PowerShell Cmdlets to control audio devices on Windows
  https://github.com/frgnca/AudioDeviceCmdlets
*/

// To interact with MMDevice
using CoreAudioApi;
using System;
// To act as a PowerShell Cmdlet
using System.Management.Automation;

namespace AudioDeviceCmdlets
{
    // Class to interact with a MMDevice as an object with attributes
    public class AudioDevice : IDisposable
    {
        // Order in which this MMDevice appeared from MMDeviceEnumerator
        public int Index;
        // Default (for its Type) is either true or false
        public bool Default;
        // DefaultCommunication (for its Type) is either true or false
        public bool DefaultCommunication;
        // Type is either "Playback" or "Recording"
        public string Type;
        // Name of the MMDevice ex: "Speakers (Realtek High Definition Audio)"
        public string Name;
        // ID of the MMDevice ex: "{0.0.0.00000000}.{c4aadd95-74c7-4b3b-9508-b0ef36ff71ba}"
        public string ID;
        // The MMDevice itself
        public MMDevice Device;

        // Returned devices belong to the caller and stay usable after a cmdlet returns.
        public void Dispose()
        {
            MMDevice device = Device;
            Device = null;
            if (device != null) device.Dispose();
        }

        // To be created, a new AudioDevice needs an Index, and the MMDevice it will communicate with
        public AudioDevice(int Index, MMDevice BaseDevice, bool Default = false, bool DefaultCommunication = false)
        {
            // Set this object's Index to the received integer
            this.Index = Index;

            // Set this object's Default to the received boolean
            this.Default = Default;

            // Set this object's DefaultCommunication to the received boolean
            this.DefaultCommunication = DefaultCommunication;

            // If the received MMDevice is a playback device
            if (BaseDevice.DataFlow == EDataFlow.eRender)
            {
                // Set this object's Type to "Playback"
                this.Type = "Playback";
            }
            // If not, if the received MMDevice is a recording device
            else if (BaseDevice.DataFlow == EDataFlow.eCapture)
            {
                // Set this object's Type to "Recording"
                this.Type = "Recording";
            }

            // Set this object's Name to that of the received MMDevice's FriendlyName
            this.Name = BaseDevice.FriendlyName;

            // Set this object's Device to the received MMDevice
            this.Device = BaseDevice;

            // Set this object's ID to that of the received MMDevice's ID
            this.ID = BaseDevice.ID;
        }
    }

    // Class to get information on a MMDevice towards the creation of a corresponding AudioDevice
    public class AudioDeviceCreationToolkit
    {
        // The MMDeviceEnumerator
        public MMDeviceEnumerator DevEnum;

        // To be created, a new AudioDeviceCreationToolkit needs a MMDeviceEnumerator it will use to compare the ID its methods receive
        public AudioDeviceCreationToolkit(MMDeviceEnumerator DevEnum)
        {
            // Set this object's DeviceEnumerator to the received MMDeviceEnumerator
            this.DevEnum = DevEnum;
        }

        // Method to find out, in a collection of all enabled MMDevice, the Index of a MMDevice, given its ID
        public int FindIndex(string ID)
        {
            using (MMDeviceCollection devices = DevEnum.EnumerateAudioEndPoints(EDataFlow.eAll, EDeviceState.DEVICE_STATE_ACTIVE))
            {
                for (int i = 0; i < devices.Count; i++)
                    using (MMDevice device = devices[i])
                        if (device.ID == ID) return i + 1;
            }
            throw new Exception("No MMDevice with the given ID was found in the collection of all enabled MMDevice");
        }

        private bool IsDefault(string ID, ERole role)
        {
            foreach (EDataFlow flow in new[] { EDataFlow.eRender, EDataFlow.eCapture })
            {
                try
                {
                    using (MMDevice device = DevEnum.GetDefaultAudioEndpoint(flow, role))
                        if (device.ID == ID) return true;
                }
                catch (System.Runtime.InteropServices.COMException) { }
            }
            return false;
        }

        public bool IsDefault(string ID) { return IsDefault(ID, ERole.eMultimedia); }
        public bool IsDefaultCommunication(string ID) { return IsDefault(ID, ERole.eCommunications); }

        // Takes ownership only when construction succeeds.
        public AudioDevice Create(int index, MMDevice device)
        {
            return new AudioDevice(index, device, IsDefault(device.ID), IsDefaultCommunication(device.ID));
        }
    }

    internal static class AudioDeviceCommand
    {
        internal const string VersionText = @"
  AudioDeviceCmdlets v3.2.0.0

  Copyright (c) 2016-2022 Francois Gendron <fg@frgn.ca>
  MIT License

  Thank you for considering a donation
  Bitcoin     (BTC) 3AffczXX4Jb2iN8QWQhHQAsj9AqGFXgYUF
  BitcoinCash (BCH) qraf6a3fklta7xkvwkh49zqn6mgnm2eyz589rkfvl3
  Ethereum    (ETH) 0xE4EA2A2356C04c8054Db452dCBd6f958F74722dE
";

        internal static MMDevice GetDefault(MMDeviceEnumerator enumerator, EDataFlow flow, ERole role)
        {
            try { return enumerator.GetDefaultAudioEndpoint(flow, role); }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                string type = flow == EDataFlow.eRender ? "playback" : "recording";
                string name = role == ERole.eCommunications ? "default communication" : "default";
                throw new ArgumentException("No " + type + " AudioDevice found with the " + name + " role", ex);
            }
        }

        // The caller owns the result, including when the endpoint is selected by index.
        internal static MMDevice GetTarget(MMDeviceEnumerator enumerator, string id, int? index)
        {
            if (index != null)
            {
                using (MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(EDataFlow.eAll, EDeviceState.DEVICE_STATE_ACTIVE))
                {
                    if (index.Value < 1 || index.Value > devices.Count)
                        throw new ArgumentException("No enabled AudioDevice found with that Index");
                    return devices[index.Value - 1];
                }
            }

            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("An AudioDevice ID is required.");
            MMDevice device;
            try { device = enumerator.GetDevice(id); }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                throw new ArgumentException("No enabled AudioDevice found with that ID", ex);
            }
            try
            {
                if (device.State != EDeviceState.DEVICE_STATE_ACTIVE)
                    throw new ArgumentException("No enabled AudioDevice found with that ID");
                return device;
            }
            catch { device.Dispose(); throw; }
        }
    }

    // Get Cmdlet
    [Cmdlet(VerbsCommon.Get, "AudioDevice")]
    public class GetAudioDevice : PSCmdlet
    {
        // Parameter called to list all devices
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "List")]
        public SwitchParameter List
        {
            get { return list; }
            set { list = value; }
        }
        private bool list;

        // Parameter receiving the ID of the device to get
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "ID")]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "IDVolume")]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "IDMute")]
        [ValidateNotNullOrEmpty]
        public string ID
        {
            get { return id; }
            set { id = value; }
        }
        private string id;

        // Parameter receiving the Index of the device to get
        [ValidateRange(1, 42)]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Index")]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "IndexVolume")]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "IndexMute")]
        public int? Index
        {
            get { return index; }
            set { index = value; }
        }
        private int? index;

        // Selected-endpoint volume is a numeric percentage; mute is a boolean.
        [Parameter(Mandatory = true, ParameterSetName = "IDVolume")]
        [Parameter(Mandatory = true, ParameterSetName = "IndexVolume")]
        public SwitchParameter Volume { get; set; }

        [Parameter(Mandatory = true, ParameterSetName = "IDMute")]
        [Parameter(Mandatory = true, ParameterSetName = "IndexMute")]
        public SwitchParameter Mute { get; set; }

        // Parameter called to list the default communication playback device
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackCommunication")]
        public SwitchParameter PlaybackCommunication
        {
            get { return playbackcommunication; }
            set { playbackcommunication = value; }
        }
        private bool playbackcommunication;

        // Parameter called to list the default communication playback device's mute state
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackCommunicationMute")]
        public SwitchParameter PlaybackCommunicationMute
        {
            get { return playbackcommunicationmute; }
            set { playbackcommunicationmute = value; }
        }
        private bool playbackcommunicationmute;

        // Parameter called to list the default communication playback device's volume
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackCommunicationVolume")]
        public SwitchParameter PlaybackCommunicationVolume
        {
            get { return playbackcommunicationvolume; }
            set { playbackcommunicationvolume = value; }
        }
        private bool playbackcommunicationvolume;

        // Parameter called to list the default playback device
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Playback")]
        public SwitchParameter Playback
        {
            get { return playback; }
            set { playback = value; }
        }
        private bool playback;

        // Parameter called to list the default playback device's mute state
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackMute")]
        public SwitchParameter PlaybackMute
        {
            get { return playbackmute; }
            set { playbackmute = value; }
        }
        private bool playbackmute;

        // Parameter called to list the default playback device's volume
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackVolume")]
        public SwitchParameter PlaybackVolume
        {
            get { return playbackvolume; }
            set { playbackvolume = value; }
        }
        private bool playbackvolume;

        // Parameter called to list the default communication recording device
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingCommunication")]
        public SwitchParameter RecordingCommunication
        {
            get { return recordingcommunication; }
            set { recordingcommunication = value; }
        }
        private bool recordingcommunication;

        // Parameter called to list the default communication recording device's mute state
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingCommunicationMute")]
        public SwitchParameter RecordingCommunicationMute
        {
            get { return recordingcommunicationmute; }
            set { recordingcommunicationmute = value; }
        }
        private bool recordingcommunicationmute;

        // Parameter called to list the default communication recording device's volume
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingCommunicationVolume")]
        public SwitchParameter RecordingCommunicationVolume
        {
            get { return recordingcommunicationvolume; }
            set { recordingcommunicationvolume = value; }
        }
        private bool recordingcommunicationvolume;

        // Parameter called to list the default recording device
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Recording")]
        public SwitchParameter Recording
        {
            get { return recording; }
            set { recording = value; }
        }
        private bool recording;

        // Parameter called to list the default recording device's mute state
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingMute")]
        public SwitchParameter RecordingMute
        {
            get { return recordingmute; }
            set { recordingmute = value; }
        }
        private bool recordingmute;

        // Parameter called to list the default recording device's volume
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingVolume")]
        public SwitchParameter RecordingVolume
        {
            get { return recordingvolume; }
            set { recordingvolume = value; }
        }
        private bool recordingvolume;

        // Parameter called to display version and credit info
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Version")]
        public SwitchParameter Version
        {
            get { return version; }
            set { version = value; }
        }
        private bool version;

        // Cmdlet execution
        protected override void ProcessRecord()
        {
            if (version) { WriteObject(AudioDeviceCommand.VersionText); return; }
            using (MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
            {
                if (ParameterSetName == "IDVolume" || ParameterSetName == "IndexVolume" ||
                    ParameterSetName == "IDMute" || ParameterSetName == "IndexMute")
                {
                    using (MMDevice target = AudioDeviceCommand.GetTarget(enumerator, id, index))
                    {
                        if (ParameterSetName.EndsWith("Volume", StringComparison.Ordinal))
                            WriteObject(target.AudioEndpointVolume.MasterVolumeLevelScalar * 100.0f);
                        else WriteObject(target.AudioEndpointVolume.Mute);
                    }
                    return;
                }
                AudioDeviceCreationToolkit toolkit = new AudioDeviceCreationToolkit(enumerator);
                if (list || !string.IsNullOrEmpty(id) || index != null)
                {
                    using (MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(EDataFlow.eAll, EDeviceState.DEVICE_STATE_ACTIVE))
                    {
                        for (int i = 0; i < devices.Count; i++)
                        {
                            MMDevice device = devices[i];
                            bool transferred = false;
                            try
                            {
                                if (!list && (index != null ? index.Value != i + 1 :
                                    !string.Equals(device.ID, id, StringComparison.CurrentCultureIgnoreCase))) continue;
                                WriteObject(toolkit.Create(i + 1, device));
                                transferred = true;
                                if (!list) return;
                            }
                            finally { if (!transferred) device.Dispose(); }
                        }
                    }
                    if (!list) throw new ArgumentException(index != null ? "No AudioDevice with that Index" : "No AudioDevice with that ID");
                    return;
                }

                bool communication = playbackcommunication || playbackcommunicationmute || playbackcommunicationvolume ||
                    recordingcommunication || recordingcommunicationmute || recordingcommunicationvolume;
                bool capture = recording || recordingmute || recordingvolume || recordingcommunication ||
                    recordingcommunicationmute || recordingcommunicationvolume;
                bool mute = playbackmute || playbackcommunicationmute || recordingmute || recordingcommunicationmute;
                bool volume = playbackvolume || playbackcommunicationvolume || recordingvolume || recordingcommunicationvolume;
                if (!(playback || playbackcommunication || recording || recordingcommunication || mute || volume)) return;
                MMDevice endpoint = AudioDeviceCommand.GetDefault(enumerator, capture ? EDataFlow.eCapture : EDataFlow.eRender,
                    communication ? ERole.eCommunications : ERole.eMultimedia);
                bool endpointTransferred = false;
                try
                {
                    if (mute) WriteObject(endpoint.AudioEndpointVolume.Mute);
                    else if (volume) WriteObject(string.Format("{0}%", endpoint.AudioEndpointVolume.MasterVolumeLevelScalar * 100));
                    else
                    {
                        WriteObject(toolkit.Create(toolkit.FindIndex(endpoint.ID), endpoint));
                        endpointTransferred = true;
                    }
                }
                finally { if (!endpointTransferred) endpoint.Dispose(); }
            }
        }

    }

    // Set Cmdlet
    [Cmdlet(VerbsCommon.Set, "AudioDevice")]
    public class SetAudioDevice : PSCmdlet
    {
        // Parameter receiving the AudioDevice to set as default
        [Parameter(Mandatory = true, ParameterSetName = "InputObject", ValueFromPipeline = true)]
        [Parameter(Mandatory = true, ParameterSetName = "InputObjectVolume", ValueFromPipeline = true)]
        [Parameter(Mandatory = true, ParameterSetName = "InputObjectMute", ValueFromPipeline = true)]
        [Parameter(Mandatory = true, ParameterSetName = "InputObjectMuteToggle", ValueFromPipeline = true)]
        [ValidateNotNull]
        public AudioDevice InputObject
        {
            get { return inputObject; }
            set { inputObject = value; }

        }
        private AudioDevice inputObject;

        // Parameter receiving the ID of the device to set as default
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "ID")]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "IDVolume")]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "IDMute")]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "IDMuteToggle")]
        [ValidateNotNullOrEmpty]
        public string ID
        {
            get { return id; }
            set { id = value; }
        }
        private string id;

        // Parameter receiving the Index of the device to set as default
        [ValidateRange(1, 42)]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Index")]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "IndexVolume")]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "IndexMute")]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "IndexMuteToggle")]
        public int? Index
        {
            get { return index; }
            set { index = value; }
        }
        private int? index;

        [Parameter(Mandatory = true, ParameterSetName = "IDVolume")]
        [Parameter(Mandatory = true, ParameterSetName = "IndexVolume")]
        [Parameter(Mandatory = true, ParameterSetName = "InputObjectVolume")]
        [ValidateNotNull]
        [ValidateRange(0, 100.0f)]
        public float? Volume { get; set; }

        [Parameter(Mandatory = true, ParameterSetName = "IDMute")]
        [Parameter(Mandatory = true, ParameterSetName = "IndexMute")]
        [Parameter(Mandatory = true, ParameterSetName = "InputObjectMute")]
        [ValidateNotNull]
        public bool? Mute { get; set; }

        [Parameter(Mandatory = true, ParameterSetName = "IDMuteToggle")]
        [Parameter(Mandatory = true, ParameterSetName = "IndexMuteToggle")]
        [Parameter(Mandatory = true, ParameterSetName = "InputObjectMuteToggle")]
        public SwitchParameter MuteToggle { get; set; }

        // Parameter called to set the default communication playback device's mute state
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackCommunicationMute")]
        public bool? PlaybackCommunicationMute
        {
            get { return playbackcommunicationmute; }
            set { playbackcommunicationmute = value; }
        }
        private bool? playbackcommunicationmute;

        // Parameter called to toggle the default communication playback device's mute state
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackCommunicationMuteToggle")]
        public SwitchParameter PlaybackCommunicationMuteToggle
        {
            get { return playbackcommunicationmutetoggle; }
            set { playbackcommunicationmutetoggle = value; }
        }
        private SwitchParameter playbackcommunicationmutetoggle;

        // Parameter receiving the volume level to set to the default communication playback device
        [ValidateRange(0, 100.0f)]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackCommunicationVolume")]
        public float? PlaybackCommunicationVolume
        {
            get { return playbackcommunicationvolume; }
            set { playbackcommunicationvolume = value; }
        }
        private float? playbackcommunicationvolume;

        // Parameter called to set the default playback device's mute state
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackMute")]
        public bool? PlaybackMute
        {
            get { return playbackmute; }
            set { playbackmute = value; }
        }
        private bool? playbackmute;

        // Parameter called to toggle the default playback device's mute state
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackMuteToggle")]
        public SwitchParameter PlaybackMuteToggle
        {
            get { return playbackmutetoggle; }
            set { playbackmutetoggle = value; }
        }
        private SwitchParameter playbackmutetoggle;

        // Parameter receiving the volume level to set to the default playback device
        [ValidateRange(0, 100.0f)]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackVolume")]
        public float? PlaybackVolume
        {
            get { return playbackvolume; }
            set { playbackvolume = value; }
        }
        private float? playbackvolume;

        // Parameter called to set the default communication recording device's mute state
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingCommunicationMute")]
        public bool? RecordingCommunicationMute
        {
            get { return recordingcommunicationmute; }
            set { recordingcommunicationmute = value; }
        }
        private bool? recordingcommunicationmute;

        // Parameter called to toggle the default communication recording device's mute state
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingCommunicationMuteToggle")]
        public SwitchParameter RecordingCommunicationMuteToggle
        {
            get { return recordingcommunicationmutetoggle; }
            set { recordingcommunicationmutetoggle = value; }
        }
        private SwitchParameter recordingcommunicationmutetoggle;

        // Parameter receiving the volume level to set to the default communication recording device
        [ValidateRange(0, 100.0f)]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingCommunicationVolume")]
        public float? RecordingCommunicationVolume
        {
            get { return recordingcommunicationvolume; }
            set { recordingcommunicationvolume = value; }
        }
        private float? recordingcommunicationvolume;

        // Parameter called to set the default recording device's mute state
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingMute")]
        public bool? RecordingMute
        {
            get { return recordingmute; }
            set { recordingmute = value; }
        }
        private bool? recordingmute;

        // Parameter called to toggle the default recording device's mute state
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingMuteToggle")]
        public SwitchParameter RecordingMuteToggle
        {
            get { return recordingmutetoggle; }
            set { recordingmutetoggle = value; }
        }
        private SwitchParameter recordingmutetoggle;

        // Parameter receiving the volume level to set to the default recording device
        [ValidateRange(0, 100.0f)]
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingVolume")]
        public float? RecordingVolume
        {
            get { return recordingvolume; }
            set { recordingvolume = value; }
        }
        private float? recordingvolume;

        // Parameter called to only set device as default and not default communication
        [Parameter(Mandatory = false, ParameterSetName = "InputObject")]
        [Parameter(Mandatory = false, ParameterSetName = "ID")]
        [Parameter(Mandatory = false, ParameterSetName = "Index")]
        public SwitchParameter DefaultOnly
        {
            get { return defaultOnly; }
            set { defaultOnly = value; }
        }
        private SwitchParameter defaultOnly;

        // Parameter called to only set device as default communication and not default
        [Parameter(Mandatory = false, ParameterSetName = "InputObject")]
        [Parameter(Mandatory = false, ParameterSetName = "ID")]
        [Parameter(Mandatory = false, ParameterSetName = "Index")]
        public SwitchParameter CommunicationOnly
        {
            get { return communicationOnly; }
            set { communicationOnly = value; }
        }
        private SwitchParameter communicationOnly;

        // Parameter called to display version and credit info
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Version")]
        public SwitchParameter Version
        {
            get { return version; }
            set { version = value; }
        }
        private bool version;

        // Cmdlet execution
        protected override void ProcessRecord()
        {
            if (defaultOnly.ToBool() && communicationOnly.ToBool())
                throw new ArgumentException("Impossible to do both DefaultOnly and CommunicationOnly at the same time.");
            if (version) { WriteObject(AudioDeviceCommand.VersionText); return; }
            if (Volume.HasValue && (float.IsNaN(Volume.Value) || float.IsInfinity(Volume.Value)))
                throw new ArgumentException("Volume must be a finite percentage between 0 and 100.");
            using (MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
            {
                if (ParameterSetName == "IDVolume" || ParameterSetName == "IndexVolume" || ParameterSetName == "InputObjectVolume" ||
                    ParameterSetName == "IDMute" || ParameterSetName == "IndexMute" || ParameterSetName == "InputObjectMute" ||
                    ParameterSetName == "IDMuteToggle" || ParameterSetName == "IndexMuteToggle" || ParameterSetName == "InputObjectMuteToggle")
                {
                    using (MMDevice endpoint = AudioDeviceCommand.GetTarget(enumerator, inputObject != null ? inputObject.ID : id, index))
                    {
                        AudioEndpointVolume volume = endpoint.AudioEndpointVolume;
                        if (Volume.HasValue) volume.MasterVolumeLevelScalar = Volume.Value / 100.0f;
                        else if (Mute.HasValue) volume.Mute = Mute.Value;
                        else if (MuteToggle.ToBool()) volume.Mute = !volume.Mute;
                    }
                    return;
                }
                AudioDeviceCreationToolkit toolkit = new AudioDeviceCreationToolkit(enumerator);
                if (inputObject != null || !string.IsNullOrEmpty(id) || index != null)
                {
                    using (MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(EDataFlow.eAll, EDeviceState.DEVICE_STATE_ACTIVE))
                    {
                        for (int i = 0; i < devices.Count; i++)
                        {
                            MMDevice device = devices[i];
                            bool transferred = false;
                            try
                            {
                                bool matches = inputObject != null ? device.ID == inputObject.ID :
                                    index != null ? index.Value == i + 1 :
                                    string.Equals(device.ID, id, StringComparison.CurrentCultureIgnoreCase);
                                if (!matches) continue;
                                using (PolicyConfigClient client = new PolicyConfigClient())
                                {
                                    if (!defaultOnly.ToBool()) client.SetDefaultEndpoint(device.ID, ERole.eCommunications);
                                    if (!communicationOnly.ToBool()) client.SetDefaultEndpoint(device.ID, ERole.eMultimedia);
                                }
                                bool isDefault = !communicationOnly.ToBool() || toolkit.IsDefault(device.ID);
                                bool isCommunication = !defaultOnly.ToBool() || toolkit.IsDefaultCommunication(device.ID);
                                WriteObject(new AudioDevice(i + 1, device, isDefault, isCommunication));
                                transferred = true;
                                return;
                            }
                            finally { if (!transferred) device.Dispose(); }
                        }
                    }
                    throw new ArgumentException(inputObject != null ? "No such enabled AudioDevice found" :
                        index != null ? "No enabled AudioDevice found with that Index" : "No enabled AudioDevice found with that ID");
                }

                if (playbackcommunicationmute != null || playbackcommunicationmutetoggle.ToBool() || playbackcommunicationvolume != null)
                {
                    using (MMDevice endpoint = AudioDeviceCommand.GetDefault(enumerator, EDataFlow.eRender, ERole.eCommunications))
                    {
                        AudioEndpointVolume volume = endpoint.AudioEndpointVolume;
                        if (playbackcommunicationmute != null) volume.Mute = playbackcommunicationmute.Value;
                        else if (playbackcommunicationmutetoggle.ToBool()) volume.Mute = !volume.Mute;
                        else volume.MasterVolumeLevelScalar = playbackcommunicationvolume.Value / 100.0f;
                    }
                    return;
                }
                if (playbackmute != null || playbackmutetoggle.ToBool() || playbackvolume != null)
                {
                    using (MMDevice endpoint = AudioDeviceCommand.GetDefault(enumerator, EDataFlow.eRender, ERole.eMultimedia))
                    {
                        AudioEndpointVolume volume = endpoint.AudioEndpointVolume;
                        if (playbackmute != null) volume.Mute = playbackmute.Value;
                        else if (playbackmutetoggle.ToBool()) volume.Mute = !volume.Mute;
                        else volume.MasterVolumeLevelScalar = playbackvolume.Value / 100.0f;
                    }
                    return;
                }
                if (recordingcommunicationmute != null || recordingcommunicationmutetoggle.ToBool() || recordingcommunicationvolume != null)
                {
                    using (MMDevice endpoint = AudioDeviceCommand.GetDefault(enumerator, EDataFlow.eCapture, ERole.eCommunications))
                    {
                        AudioEndpointVolume volume = endpoint.AudioEndpointVolume;
                        if (recordingcommunicationmute != null) volume.Mute = recordingcommunicationmute.Value;
                        else if (recordingcommunicationmutetoggle.ToBool()) volume.Mute = !volume.Mute;
                        else volume.MasterVolumeLevelScalar = recordingcommunicationvolume.Value / 100.0f;
                    }
                    return;
                }
                if (recordingmute != null || recordingmutetoggle.ToBool() || recordingvolume != null)
                {
                    using (MMDevice endpoint = AudioDeviceCommand.GetDefault(enumerator, EDataFlow.eCapture, ERole.eMultimedia))
                    {
                        AudioEndpointVolume volume = endpoint.AudioEndpointVolume;
                        if (recordingmute != null) volume.Mute = recordingmute.Value;
                        else if (recordingmutetoggle.ToBool()) volume.Mute = !volume.Mute;
                        else volume.MasterVolumeLevelScalar = recordingvolume.Value / 100.0f;
                    }
                    return;
                }
            }
        }

    }

    // Write Cmdlet
    [Cmdlet(VerbsCommunications.Write, "AudioDevice")]
    public class WriteAudioDevice : Cmdlet
    {
        // Parameter called to output audiometer result of the default communication playback device as a progress bar
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackCommunicationMeter")]
        public SwitchParameter PlaybackCommunicationMeter
        {
            get { return playbackcommunicationmeter; }
            set { playbackcommunicationmeter = value; }
        }
        private bool playbackcommunicationmeter;

        // Parameter called to output audiometer result of the default communication playback device as a stream of values
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackCommunicationStream")]
        public SwitchParameter PlaybackCommunicationStream
        {
            get { return playbackcommunicationstream; }
            set { playbackcommunicationstream = value; }
        }
        private bool playbackcommunicationstream;

        // Parameter called to output audiometer result of the default playback device as a progress bar
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackMeter")]
        public SwitchParameter PlaybackMeter
        {
            get { return playbackmeter; }
            set { playbackmeter = value; }
        }
        private bool playbackmeter;

        // Parameter called to output audiometer result of the default playback device as a stream of values
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "PlaybackStream")]
        public SwitchParameter PlaybackStream
        {
            get { return playbackstream; }
            set { playbackstream = value; }
        }
        private bool playbackstream;

        // Parameter called to output audiometer result of the default communication recording device as a progress bar
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingCommunicationMeter")]
        public SwitchParameter RecordingCommunicationMeter
        {
            get { return recordingcommunicationmeter; }
            set { recordingcommunicationmeter = value; }
        }
        private bool recordingcommunicationmeter;

        // Parameter called to output audiometer result of the default communication recording device as a stream of values
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingCommunicationStream")]
        public SwitchParameter RecordingCommunicationStream
        {
            get { return recordingcommunicationstream; }
            set { recordingcommunicationstream = value; }
        }
        private bool recordingcommunicationstream;

        // Parameter called to output audiometer result of the default recording device as a progress bar
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingMeter")]
        public SwitchParameter RecordingMeter
        {
            get { return recordingmeter; }
            set { recordingmeter = value; }
        }
        private bool recordingmeter;

        // Parameter called to output audiometer result of the default recording device as a stream of values
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "RecordingStream")]
        public SwitchParameter RecordingStream
        {
            get { return recordingstream; }
            set { recordingstream = value; }
        }
        private bool recordingstream;

        // Parameter called to display version and credit info
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "Version")]
        public SwitchParameter Version
        {
            get { return version; }
            set { version = value; }
        }
        private bool version;

        // Cmdlet execution
        protected override void ProcessRecord()
        {
            if (version) { WriteObject(AudioDeviceCommand.VersionText); return; }
            bool meter = playbackmeter || playbackcommunicationmeter || recordingmeter || recordingcommunicationmeter;
            bool stream = playbackstream || playbackcommunicationstream || recordingstream || recordingcommunicationstream;
            if (!meter && !stream) return;
            EDataFlow flow = recordingmeter || recordingstream || recordingcommunicationmeter || recordingcommunicationstream
                ? EDataFlow.eCapture : EDataFlow.eRender;
            ERole role = playbackcommunicationmeter || playbackcommunicationstream || recordingcommunicationmeter || recordingcommunicationstream
                ? ERole.eCommunications : ERole.eMultimedia;
            using (MMDeviceEnumerator enumerator = new MMDeviceEnumerator())
            {
                ProgressRecord progress = null;
                while (!Stopping)
                {
                    // Re-query the default each tick so device changes are still followed.
                    // Release the endpoint, property store and meter before the next tick.
                    using (MMDevice endpoint = AudioDeviceCommand.GetDefault(enumerator, flow, role))
                    {
                        int peak = Convert.ToInt32(endpoint.AudioMeterInformation.MasterPeakValue * 100);
                        if (meter)
                        {
                            string name = endpoint.FriendlyName;
                            if (progress == null) progress = new ProgressRecord(0, name, "Peak Value");
                            progress.Activity = name;
                            progress.PercentComplete = peak;
                            WriteProgress(progress);
                        }
                        else WriteObject(peak);
                    }
                    System.Threading.Thread.Sleep(100);
                }
            }
        }

    }
}
