using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace Macro;

[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume([In] float fLevel, [In] ref Guid EventContext);
    [PreserveSig] int GetMasterVolume([Out] out float pfLevel);
    [PreserveSig] int SetMute([In] int bMute, [In] ref Guid EventContext);
    [PreserveSig] int GetMute([Out] out int pbMute);
}

[Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName(out IntPtr name);
    [PreserveSig] int SetDisplayName(string value, Guid EventContext);
    [PreserveSig] int GetIconPath(out IntPtr path);
    [PreserveSig] int SetIconPath(string value, Guid EventContext);
    [PreserveSig] int GetGroupingParam(out Guid groupingParam);
    [PreserveSig] int SetGroupingParam(Guid Override, Guid EventContext);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr NewNotifications);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr NewNotifications);
    [PreserveSig] int GetSessionIdentifier(out IntPtr retVal);
    [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr retVal);
    [PreserveSig] int GetProcessId(out uint retVal);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference(bool optOut);
}

[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int SessionCount);
    [PreserveSig] int GetSession(int SessionCount, out IAudioSessionControl2 Session);
}

[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    [PreserveSig] int GetAudioSessionControl(ref Guid AudioSessionGuid, uint StreamFlags, out IAudioSessionControl2 SessionControl);
    [PreserveSig] int GetSimpleAudioVolume(ref Guid AudioSessionGuid, uint StreamFlags, out ISimpleAudioVolume AudioVolume);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
}

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr ppDevices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

public static class AudioManager
{
    public static void SetProcessMute(int targetPid, string targetProcessName, bool mute)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice device);
            if (device == null) return;
            
            Guid IID_IAudioSessionManager2 = typeof(IAudioSessionManager2).GUID;
            int hres = device.Activate(ref IID_IAudioSessionManager2, 1, IntPtr.Zero, out IntPtr ptrManager);
            if (hres != 0 || ptrManager == IntPtr.Zero) return;
            
            var manager = (IAudioSessionManager2)Marshal.GetObjectForIUnknown(ptrManager);
            manager.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
            
            sessionEnumerator.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                sessionEnumerator.GetSession(i, out IAudioSessionControl2 control);
                
                bool match = false;
                
                control.GetProcessId(out uint pid);
                if (pid == targetPid && pid != 0) match = true;
                
                control.GetDisplayName(out IntPtr namePtr);
                if (namePtr != IntPtr.Zero)
                {
                    string name = Marshal.PtrToStringUni(namePtr) ?? "";
                    Marshal.FreeCoTaskMem(namePtr);
                    if (name.Contains(targetProcessName, StringComparison.OrdinalIgnoreCase)) match = true;
                }

                control.GetSessionIdentifier(out IntPtr idPtr);
                if (idPtr != IntPtr.Zero)
                {
                    string idStr = Marshal.PtrToStringUni(idPtr) ?? "";
                    Marshal.FreeCoTaskMem(idPtr);
                    if (idStr.Contains(targetProcessName, StringComparison.OrdinalIgnoreCase)) match = true;
                }

                if (match)
                {
                    var volume = (ISimpleAudioVolume)control;
                    Guid emptyId = Guid.Empty;
                    volume.SetMute(mute ? 1 : 0, ref emptyId);
                }
            }
        }
        catch { }
    }

    public static string DumpAllAudioSessions()
    {
        string debugStrings = "Active COM Audio Sessions:\n\n";
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice device);
            if (device == null) return debugStrings + "No default device.";
            
            Guid IID_IAudioSessionManager2 = typeof(IAudioSessionManager2).GUID;
            int hres = device.Activate(ref IID_IAudioSessionManager2, 1, IntPtr.Zero, out IntPtr ptrManager);
            if (hres != 0 || ptrManager == IntPtr.Zero) return debugStrings + $"Activation failed. HRESULT: {hres}";
            
            var manager = (IAudioSessionManager2)Marshal.GetObjectForIUnknown(ptrManager);
            manager.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
            
            sessionEnumerator.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                sessionEnumerator.GetSession(i, out IAudioSessionControl2 control);
                
                control.GetProcessId(out uint pid);
                string sessionInfo = $"[PID: {pid}]";
                
                control.GetDisplayName(out IntPtr namePtr);
                if (namePtr != IntPtr.Zero)
                {
                    string name = Marshal.PtrToStringUni(namePtr) ?? "";
                    Marshal.FreeCoTaskMem(namePtr);
                    if (!string.IsNullOrEmpty(name)) sessionInfo += $"\n  Name: {name}";
                }

                control.GetSessionIdentifier(out IntPtr idPtr);
                if (idPtr != IntPtr.Zero)
                {
                    string idStr = Marshal.PtrToStringUni(idPtr) ?? "";
                    Marshal.FreeCoTaskMem(idPtr);
                    if (!string.IsNullOrEmpty(idStr)) sessionInfo += $"\n  ID: {idStr}";
                }

                debugStrings += sessionInfo + "\n\n";
            }
        }
        catch (Exception ex) 
        { 
            debugStrings += "Audio Error: " + ex.Message;
        }
        return debugStrings;
    }
}
