using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using AudioDeviceCmdlets;
using CoreAudioApi;

internal static class SoakHarness
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly WeakReference[] TrackedVolumes = new WeakReference[128];
    private static long _volumeCount, _commands, _reads, _expectedErrors;
    private static AudioDevice[] _cachedDevices;

    private static void DisposeOwned(object value)
    {
        IDisposable disposable = value as IDisposable;
        if (disposable != null) disposable.Dispose();
    }

    private static void Invoke(Cmdlet command, bool disposeOutput)
    {
        foreach (object output in command.Invoke())
            if (disposeOutput) DisposeOwned(output);
        _commands++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CmdletCycle(long cycle)
    {
        Invoke(new GetAudioDevice { PlaybackVolume = true }, false);
        Invoke(new GetAudioDevice { PlaybackMute = true }, false);
        Invoke(new GetAudioDevice { RecordingVolume = true }, false);
        Invoke(new GetAudioDevice { RecordingMute = true }, false);
        Invoke(new GetAudioDevice { PlaybackCommunicationVolume = true }, false);
        Invoke(new GetAudioDevice { PlaybackCommunicationMute = true }, false);
        Invoke(new GetAudioDevice { RecordingCommunicationVolume = true }, false);
        Invoke(new GetAudioDevice { RecordingCommunicationMute = true }, false);
        if (cycle % 10 == 0)
        {
            // Discard the objects exactly as older scripts did; rely on normal GC.
            Invoke(new GetAudioDevice { List = true }, false);
            Invoke(new GetAudioDevice { Playback = true }, false);
            Invoke(new GetAudioDevice { Recording = true }, false);
        }
        if (cycle % 1000 == 0)
        {
            try
            {
                Invoke(new GetAudioDevice { ID = "missing-soak-test-device" }, false);
                throw new InvalidOperationException("Invalid ID unexpectedly succeeded.");
            }
            catch (ArgumentException) { _expectedErrors++; }
        }
    }

    private static void ReadDevice(MMDevice device, bool track)
    {
        string name = device.FriendlyName;
        AudioEndpointVolume volume = device.AudioEndpointVolume;
        float level = volume.MasterVolumeLevelScalar;
        bool muted = volume.Mute;
        float peak = device.AudioMeterInformation.MasterPeakValue;
        if (track)
            TrackedVolumes[_volumeCount++ % TrackedVolumes.Length] = new WeakReference(volume);
        _reads += 4;
        GC.KeepAlive(name); GC.KeepAlive(level); GC.KeepAlive(muted); GC.KeepAlive(peak);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ChurnCycle(long cycle)
    {
        MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
        MMDeviceCollection collection = null;
        try
        {
            collection = enumerator.EnumerateAudioEndPoints(EDataFlow.eAll, EDeviceState.DEVICE_STATE_ACTIVE);
            for (int i = 0; i < Math.Min(3, collection.Count); i++)
            {
                MMDevice device = collection[i];
                try
                {
                    ReadDevice(device, true);
                    if (cycle % 100 == 0)
                    {
                        SessionCollection sessions = device.AudioSessionManager.Sessions;
                        for (int j = 0; j < sessions.Count; j++)
                        {
                            AudioSessionControl session = sessions[j];
                            try
                            {
                                string name = session.DisplayName;
                                string icon = session.IconPath;
                                string id = session.SessionIdentifier;
                                string instance = session.SessionInstanceIdentifier;
                                GC.KeepAlive(name); GC.KeepAlive(icon); GC.KeepAlive(id); GC.KeepAlive(instance);
                                _reads += 4;
                            }
                            finally { DisposeOwned(session); }
                        }
                    }
                }
                finally { DisposeOwned(device); }
            }
        }
        finally
        {
            try { DisposeOwned(collection); }
            finally { DisposeOwned(enumerator); }
        }
    }

    private static void ServiceCycle(long cycle)
    {
        if (_cachedDevices == null || cycle % 2000 == 0)
        {
            if (_cachedDevices != null)
                foreach (AudioDevice device in _cachedDevices) DisposeOwned(device);
            List<AudioDevice> devices = new List<AudioDevice>();
            foreach (AudioDevice device in new GetAudioDevice { List = true }.Invoke<AudioDevice>())
            {
                if (devices.Count < 3) devices.Add(device);
                else DisposeOwned(device);
            }
            _commands++;
            _cachedDevices = devices.ToArray();
        }
        foreach (AudioDevice device in _cachedDevices)
        {
            // The service's hot loop reads volume and mute from cached endpoints.
            AudioEndpointVolume volume = device.Device.AudioEndpointVolume;
            float level = volume.MasterVolumeLevelScalar;
            bool muted = volume.Mute;
            GC.KeepAlive(level); GC.KeepAlive(muted);
            _reads += 2;
        }
        if (cycle % 5 == 0)
        {
            Invoke(new GetAudioDevice { Playback = true }, true);
            Invoke(new GetAudioDevice { Recording = true }, true);
        }
    }

    private static string Json(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }

    private static string Sample(string phase, string workload, long cycles, Stopwatch clock, Process process)
    {
        process.Refresh();
        return "{\"utc\":" + Json(DateTime.UtcNow.ToString("o", Culture)) +
            ",\"phase\":" + Json(phase) + ",\"workload\":" + Json(workload) +
            ",\"pid\":" + process.Id + ",\"cycles\":" + cycles +
            ",\"commands\":" + _commands + ",\"reads\":" + _reads +
            ",\"expectedErrors\":" + _expectedErrors +
            ",\"elapsedSeconds\":" + clock.Elapsed.TotalSeconds.ToString("F3", Culture) +
            ",\"privateBytes\":" + process.PrivateMemorySize64 +
            ",\"workingSetBytes\":" + process.WorkingSet64 +
            ",\"managedBytes\":" + GC.GetTotalMemory(false) +
            ",\"handles\":" + process.HandleCount +
            ",\"gen0\":" + GC.CollectionCount(0) + ",\"gen1\":" + GC.CollectionCount(1) +
            ",\"gen2\":" + GC.CollectionCount(2) + "}";
    }

    private static void Collect()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }

    // workload, cycles, sample interval, delay ms, duration hours, memory limit MiB, output directory
    public static int Main(string[] args)
    {
        string workload = args[0];
        long target = long.Parse(args[1], Culture);
        long sampleEvery = long.Parse(args[2], Culture);
        int delay = int.Parse(args[3], Culture);
        double hours = double.Parse(args[4], Culture);
        long memoryLimit = long.Parse(args[5], Culture) * 1024 * 1024;
        string directory = Path.GetFullPath(args[6]);
        Directory.CreateDirectory(directory);
        Stopwatch clock = Stopwatch.StartNew();
        long completed = 0;
        string state = "running", error = null;
        long retained = 0;
        using (Process process = Process.GetCurrentProcess())
        using (StreamWriter samples = new StreamWriter(Path.Combine(directory, "samples.jsonl")))
        {
            samples.AutoFlush = true;
            try
            {
                Action<long> cycle = workload == "cmdlets" ? (Action<long>)CmdletCycle :
                    workload == "churn" ? (Action<long>)ChurnCycle :
                    workload == "service" ? (Action<long>)ServiceCycle : null;
                if (cycle == null) throw new ArgumentException("Unknown workload.");
                for (int i = 1; i <= 100; i++) cycle(i);
                Collect();
                _volumeCount = 0; Array.Clear(TrackedVolumes, 0, TrackedVolumes.Length);
                _commands = _reads = _expectedErrors = 0;
                clock.Restart();
                samples.WriteLine(Sample("start-after-gc", workload, 0, clock, process));
                double lastSample = 0;
                for (long i = 1; i <= target; i++)
                {
                    if (hours > 0 && clock.Elapsed.TotalHours >= hours) break;
                    cycle(i);
                    completed = i;
                    if (delay > 0) Thread.Sleep(delay);
                    if (i % sampleEvery == 0 || clock.Elapsed.TotalSeconds - lastSample >= 30)
                    {
                        samples.WriteLine(Sample("running", workload, i, clock, process));
                        lastSample = clock.Elapsed.TotalSeconds;
                        if (process.PrivateMemorySize64 >= memoryLimit)
                        {
                            state = "memory-limit";
                            break;
                        }
                    }
                }
                samples.WriteLine(Sample("end-before-gc", workload, completed, clock, process));
                if (_cachedDevices != null)
                    foreach (AudioDevice device in _cachedDevices) DisposeOwned(device);
                _cachedDevices = null;
                Collect();
                foreach (WeakReference weak in TrackedVolumes)
                    if (weak != null && weak.IsAlive) retained++;
                samples.WriteLine(Sample("end-after-gc", workload, completed, clock, process));
                if (state == "running") state = retained == 0 ? "completed" : "retained-wrappers";
            }
            catch (Exception ex)
            {
                state = "failed";
                error = ex.ToString();
                samples.WriteLine(Sample("failed", workload, completed, clock, process));
            }
            string summary = "{\"state\":" + Json(state) + ",\"workload\":" + Json(workload) +
                ",\"cycles\":" + completed + ",\"targetCycles\":" + target +
                ",\"elapsedSeconds\":" + clock.Elapsed.TotalSeconds.ToString("F3", Culture) +
                ",\"commands\":" + _commands + ",\"reads\":" + _reads +
                ",\"retainedVolumeWrappers\":" + retained +
                ",\"error\":" + (error == null ? "null" : Json(error)) + "}";
            File.WriteAllText(Path.Combine(directory, "summary.json"), summary);
            Console.WriteLine(summary);
        }
        return state == "completed" ? 0 : 1;
    }
}
