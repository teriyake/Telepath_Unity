using System.Runtime.InteropServices;
using System.Text;
using AOT;
using UnityEngine;

public static class NativeTelepath
{
    private const string PluginName = "Telepath";

#if UNITY_IOS && !UNITY_EDITOR
    private const string PluginName = "__Internal";
#endif

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void DebugLogFuncPtr([MarshalAs(UnmanagedType.LPStr)] string message);

    private static DebugLogFuncPtr nativeDebugCallback;
    private static bool isCallbackRegistered = false;

    [DllImport(PluginName)]
    public static extern int GetPluginID();

    [DllImport(PluginName)]
    private static extern bool InitializeTelepath();

    [DllImport(PluginName)]
    private static extern void ShutdownTelepath();

    [DllImport(PluginName)]
    public static extern bool IsTelepathChannelOpen();

    [DllImport(PluginName)]
    private static extern void SendGameData(string dataName, float value);

    [DllImport(PluginName)]
    private static extern bool InitializeTelepathListener(int port);

    [DllImport(PluginName)]
    private static extern void ShutdownTelepathListener();

    [DllImport(PluginName)]
    public static extern bool IsTelepathListenerRunning();

    [DllImport(PluginName)]
    private static extern bool GetNextOscMessage(
        StringBuilder addressBuffer,
        int addressBufferSize,
        out float outValue
    );

    [DllImport(PluginName)]
    public static extern bool InitializeTelepathAudioListener(int port);

    [DllImport(PluginName)]
    public static extern void ShutdownTelepathAudioListener();

    [DllImport(PluginName)]
    public static extern int GetTelepathAudioSamples(float[] buffer, int bufferSize);

    [DllImport(PluginName)]
    private static extern void RegisterDebugCallback(DebugLogFuncPtr callback);

    [DllImport(PluginName)]
    private static extern void SetTelepathLogLevel(int level);

    public enum LogLevel
    {
        LOG_DEBUG,
        LOG_INFO,
        LOG_WARNING,
        LOG_ERROR,
    }

    public static void EnsureInitialized()
    {
        if (!isCallbackRegistered)
        {
            if (nativeDebugCallback == null)
            {
                nativeDebugCallback = new DebugLogFuncPtr(OnNativeDebugLog);
            }
            RegisterDebugCallback(nativeDebugCallback);
            isCallbackRegistered = true;
            Debug.Log("[Telepath] Registered native plugin debug log callback.");
        }
    }

    public static void SetLogLevel(LogLevel level)
    {
        SetTelepathLogLevel((int)level);
    }

    [MonoPInvokeCallback(typeof(DebugLogFuncPtr))]
    private static void OnNativeDebugLog(string message)
    {
        Debug.Log($"{message}");
    }

    // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    // static void OnRuntimeLoad()
    // {
    //     EnsureInitialized();
    // }

    public static bool OpenChannel( /*string ip, int port*/
    )
    {
        EnsureInitialized();
        Debug.Log($"[Telepath] Attempting to open channel to VCV_RACK_IP:VCV_RACK_PORT...");
        bool success =
            InitializeTelepath( /*ip, port*/
            );
        return success;
    }

    public static void CloseChannel()
    {
        if (IsTelepathChannelOpen())
        {
            ShutdownTelepath();
        }
    }

    public static void Send(string addressSuffix, float value)
    {
        if (!IsTelepathChannelOpen())
        {
            return;
        }
        if (string.IsNullOrEmpty(addressSuffix))
        {
            Debug.LogWarning("[Telepath] Send called with empty address suffix.");
            return;
        }
        SendGameData(addressSuffix, value);
    }

    public static bool StartListener(int port)
    {
        EnsureInitialized();
        bool success = InitializeTelepathListener(port);
        return success;
    }

    public static void StopListener()
    {
        if (IsTelepathListenerRunning())
        {
            ShutdownTelepathListener();
        }
    }

    public static bool StartAudioListener(int port)
    {
        EnsureInitialized();
        bool success = InitializeTelepathAudioListener(port);
        return success;
    }

    public static void StopAudioListener()
    {
        ShutdownTelepathAudioListener();
    }

    public static bool GetNextMessage(out string address, out float value)
    {
        // if (!IsTelepathListenerRunning()) {  }

        const int addressBufferSize = 256;
        StringBuilder addressBuffer = new StringBuilder(addressBufferSize);

        bool messageAvailable = GetNextOscMessage(addressBuffer, addressBufferSize, out value);

        if (messageAvailable)
        {
            address = addressBuffer.ToString();
            return true;
        }
        else
        {
            address = null;
            value = 0f;
            return false;
        }
    }

    public static int GetAudioSamples(float[] buffer)
    {
        if (buffer == null || buffer.Length == 0)
        {
            return 0;
        }
        // if (!IsAudioListenerRunning()) return 0;

        return GetTelepathAudioSamples(buffer, buffer.Length);
    }
}