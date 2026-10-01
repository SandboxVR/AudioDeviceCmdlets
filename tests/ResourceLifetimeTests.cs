using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CoreAudioApi;
using CoreAudioApi.Interfaces;

internal static class ResourceLifetimeTests
{
    private const int Failure = unchecked((int)0x80004005);
    private static int _passed;

    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static void Test(string name, Action action)
    {
        action();
        _passed++;
        Console.WriteLine("PASS " + name);
    }

    public static int Main()
    {
        try
        {
            Test("Property snapshots survive native cleanup; raw variants clear idempotently", Properties);
            Test("Volume reads do not subscribe; events register and unregister on demand", Notifications);
            Test("Abandoned volume subscription is not rooted by the native callback", AbandonedNotification);
            Test("Unregister failure still disposes the endpoint and borrowed channels", FailedUnregister);
            Test("Failed constructor releases its COM acquisition", FailedConstruction);
            Test("COM factory failure releases an out reference", FailedFactory);
            Test("Failed meter calls release pinned arrays", FailedMeter);
            Test("Session disposal unregisters every consumer and invalidates borrowed interfaces", Sessions);
            Console.WriteLine("Passed " + _passed + " resource lifetime tests.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static PropVariant StringVariant(string value)
    {
        object variant = new PropVariant();
        typeof(PropVariant).GetField("vt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(variant, (short)VarEnum.VT_LPWSTR);
        typeof(PropVariant).GetField("everything_else", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(variant, Marshal.StringToCoTaskMemUni(value));
        return (PropVariant)variant;
    }

    private static void Properties()
    {
        using (PropertyStore store = new PropertyStore(new FakeStore()))
        {
            PropertyStoreProperty snapshot = store[0];
            Assert((string)snapshot.Value == "Test endpoint", "Managed string snapshot differs");
            store.Dispose();
            Assert((string)snapshot.Value == "Test endpoint", "Snapshot depends on disposed store");
        }
        PropVariant raw = StringVariant("raw");
        raw.Dispose(); raw.Dispose();
        Assert((short)typeof(PropVariant).GetField("vt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(raw) == 0, "PropVariantClear did not reset vt");
        FakeStore failure = new FakeStore { Fail = true };
        using (PropertyStore store = new PropertyStore(failure))
            Expect<COMException>(() => store.GetValue(0));
    }

    private static void Notifications()
    {
        FakeEndpoint native = new FakeEndpoint();
        using (AudioEndpointVolume volume = new AudioEndpointVolume(native))
        {
            float level = volume.MasterVolumeLevelScalar;
            volume.MasterVolumeLevelScalar = level;
            Assert(native.Registered == 0, "Polling registered an unnecessary callback");
            AudioEndpointVolumeNotificationDelegate first = data => { };
            AudioEndpointVolumeNotificationDelegate second = data => { };
            volume.OnVolumeNotification += first;
            volume.OnVolumeNotification += second;
            Assert(native.Registered == 1, "Expected one native subscription");
            volume.OnVolumeNotification -= first;
            Assert(native.Unregistered == 0, "Unregistered while subscribers remain");
            volume.OnVolumeNotification -= second;
            Assert(native.Unregistered == 1, "Last subscriber did not unregister");
            volume.OnVolumeNotification += first;
        }
        Assert(native.Unregistered == 2, "Dispose did not unregister");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Abandon(FakeEndpoint native)
    {
        AudioEndpointVolume volume = new AudioEndpointVolume(native);
        volume.OnVolumeNotification += data => { };
        return new WeakReference(volume);
    }

    private static void Collect()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }

    private static void AbandonedNotification()
    {
        FakeEndpoint native = new FakeEndpoint();
        WeakReference weak = Abandon(native);
        Collect();
        Assert(!weak.IsAlive, "Native callback retained the wrapper");
        Assert(native.Unregistered == 1, "Finalizer did not unregister");
        GC.KeepAlive(native);
    }

    private static void FailedUnregister()
    {
        FakeEndpoint native = new FakeEndpoint { FailUnregister = true };
        AudioEndpointVolume volume = new AudioEndpointVolume(native);
        AudioEndpointVolumeChannel channel = volume.Channels[0];
        volume.OnVolumeNotification += data => { };
        Expect<COMException>(() => volume.Dispose());
        volume.Dispose();
        Expect<ObjectDisposedException>(() => { float value = channel.VolumeLevelScalar; });
        Expect<ObjectDisposedException>(() => { bool value = volume.Mute; });
        Assert(native.Unregistered == 1, "Repeated Dispose called native unregister again");
    }

    private static void FailedConstruction()
    {
        // A real RCW makes release observable even though the interface is fake.
        object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
        Expect<COMException>(() => new FailingOwner(instance));
        Expect<InvalidComObjectException>(() => Marshal.GetIUnknownForObject(instance));
    }

    private static void FailedFactory()
    {
        object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
        Expect<COMException>(() => ComObject.CheckResult(Failure, instance));
        Expect<InvalidComObjectException>(() => Marshal.GetIUnknownForObject(instance));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FailMeterCalls(AudioMeterInformation meter)
    {
        for (int i = 0; i < 1000; i++)
            Expect<COMException>(() => { float peak = meter.PeakValues[0]; });
    }

    private static void FailedMeter()
    {
        using (AudioMeterInformation meter = new AudioMeterInformation(new FakeMeter()))
        {
            Collect();
            long before = GC.GetTotalMemory(true);
            FailMeterCalls(meter);
            Collect();
            Assert(GC.GetTotalMemory(true) - before < 4 * 1024 * 1024, "Failed calls retained pinned channel buffers");
        }
    }

    private static void Sessions()
    {
        FakeSession native = new FakeSession();
        AudioSessionControl session = new AudioSessionControl(native);
        AudioMeterInformation meter = session.AudioMeterInformation;
        SimpleAudioVolume volume = session.SimpleAudioVolume;
        Assert(session.DisplayName == "session", "Session string conversion differs");
        session.RegisterAudioSessionNotification(new FakeEvents());
        session.RegisterAudioSessionNotification(new FakeEvents());
        native.FailUnregister = true;
        Expect<COMException>(() => session.Dispose());
        session.Dispose();
        Assert(native.Unregistered == 2, "Did not attempt every unregister after failure");
        Expect<ObjectDisposedException>(() => { float value = meter.MasterPeakValue; });
        Expect<ObjectDisposedException>(() => { bool value = volume.Mute; });
    }

    private sealed class FailingOwner : ComObject
    {
        internal FailingOwner(object instance) : base(instance)
        {
            try { Marshal.ThrowExceptionForHR(Failure); }
            catch { Dispose(); throw; }
        }
    }

    private sealed class FakeStore : IPropertyStore
    {
        internal bool Fail;
        public int GetCount(out int count) { count = 1; return 0; }
        public int GetAt(int index, out PropertyKey key) { key = new PropertyKey(); return 0; }
        public int GetValue(ref PropertyKey key, out PropVariant value) { value = StringVariant("Test endpoint"); return Fail ? Failure : 0; }
        public int SetValue(ref PropertyKey key, ref PropVariant value) { return 0; }
        public int Commit() { return 0; }
    }

    private sealed class FakeEndpoint : IAudioEndpointVolume
    {
        internal int Registered, Unregistered;
        internal bool FailUnregister;
        // Deliberately root the callback like the native audio service does.
        private IAudioEndpointVolumeCallback _callback;
        public int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback) { Registered++; _callback = callback; return 0; }
        public int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback) { Unregistered++; _callback = null; return FailUnregister ? Failure : 0; }
        public int GetChannelCount(out int count) { count = 2; return 0; }
        public int QueryHardwareSupport(out uint support) { support = 0; return 0; }
        public int GetVolumeRange(out float min, out float max, out float step) { min = 0; max = 1; step = 0.1f; return 0; }
        public int GetVolumeStepInfo(out uint step, out uint count) { step = 0; count = 10; return 0; }
        public int GetMasterVolumeLevel(out float level) { level = 0.5f; return 0; }
        public int GetMasterVolumeLevelScalar(out float level) { level = 0.5f; return 0; }
        public int GetChannelVolumeLevel(uint channel, out float level) { level = 0.5f; return 0; }
        public int GetChannelVolumeLevelScalar(uint channel, out float level) { level = 0.5f; return 0; }
        public int GetMute(out bool mute) { mute = false; return 0; }
        public int SetMasterVolumeLevel(float level, Guid context) { return 0; }
        public int SetMasterVolumeLevelScalar(float level, Guid context) { return 0; }
        public int SetChannelVolumeLevel(uint channel, float level, Guid context) { return 0; }
        public int SetChannelVolumeLevelScalar(uint channel, float level, Guid context) { return 0; }
        public int SetMute(bool mute, Guid context) { return 0; }
        public int VolumeStepUp(Guid context) { return 0; }
        public int VolumeStepDown(Guid context) { return 0; }
    }

    private class FakeMeter : IAudioMeterInformation
    {
        public int GetPeakValue(out float peak) { peak = 0; return 0; }
        public int GetMeteringChannelCount(out int count) { count = 8192; return 0; }
        public int GetChannelsPeakValues(int count, IntPtr values) { return Failure; }
        public int QueryHardwareSupport(out int support) { support = 0; return 0; }
    }

    private sealed class FakeSession : FakeMeter, IAudioSessionControl2, ISimpleAudioVolume
    {
        internal int Unregistered;
        internal bool FailUnregister;
        public int GetState(out AudioSessionState state) { state = AudioSessionState.AudioSessionStateActive; return 0; }
        public int GetDisplayName(out IntPtr name) { name = Marshal.StringToCoTaskMemUni("session"); return 0; }
        public int GetIconPath(out IntPtr name) { name = Marshal.StringToCoTaskMemUni("icon"); return 0; }
        public int GetSessionIdentifier(out IntPtr name) { name = Marshal.StringToCoTaskMemUni("id"); return 0; }
        public int GetSessionInstanceIdentifier(out IntPtr name) { name = Marshal.StringToCoTaskMemUni("instance"); return 0; }
        public int SetDisplayName(string value, Guid context) { return 0; }
        public int SetIconPath(string value, Guid context) { return 0; }
        public int GetGroupingParam(out Guid value) { value = Guid.Empty; return 0; }
        public int SetGroupingParam(Guid value, Guid context) { return 0; }
        public int RegisterAudioSessionNotification(IAudioSessionEvents consumer) { return 0; }
        public int UnregisterAudioSessionNotification(IAudioSessionEvents consumer) { Unregistered++; return FailUnregister ? Failure : 0; }
        public int GetProcessId(out uint pid) { pid = 1; return 0; }
        public int IsSystemSoundsSession() { return 1; }
        public int SetDuckingPreference(bool optOut) { return 0; }
        public int SetMasterVolume(float volume, ref Guid context) { return 0; }
        public int GetMasterVolume(out float volume) { volume = 0.5f; return 0; }
        public int SetMute(bool mute, ref Guid context) { return 0; }
        public int GetMute(out bool mute) { mute = false; return 0; }
    }

    private sealed class FakeEvents : IAudioSessionEvents
    {
        public int OnDisplayNameChanged(string name, Guid context) { return 0; }
        public int OnIconPathChanged(string path, Guid context) { return 0; }
        public int OnSimpleVolumeChanged(float volume, bool mute, Guid context) { return 0; }
        public int OnChannelVolumeChanged(uint count, IntPtr values, uint changed, Guid context) { return 0; }
        public int OnGroupingParamChanged(Guid value, Guid context) { return 0; }
        public int OnStateChanged(AudioSessionState state) { return 0; }
        public int OnSessionDisconnected(AudioSessionDisconnectReason reason) { return 0; }
    }
}
