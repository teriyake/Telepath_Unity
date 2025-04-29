using System;
using UnityEngine;

public class TelepathManager : MonoBehaviour
{
    [Tooltip("Assign the Telepath Settings asset here.")]
    public TelepathSettings settings;

    public event Action<string, float> OnMessageReceived;
    private const int MaxMessagesPerFrame = 50;

    public static TelepathManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (settings == null)
        {
            Debug.LogError("[TelepathManager] TelepathSettings asset not assigned!", this);
        }
    }

    void Start()
    {
        if (settings == null)
            return;

        NativeTelepath.SetLogLevel(settings.logLevel);

        if (settings.autoOpenChannelOnStart)
        {
            OpenChannel();
        }
        if (settings.autoStartListenerOnStart)
        {
            StartOscListener();
        }
        if (settings.autoStartAudioListenerOnStart)
        {
            StartAudioListener();
        }
    }

    void Update()
    {
        if (settings != null && settings.isOscListenerRunningRuntime)
        {
            int messagesProcessed = 0;
            while (
                messagesProcessed < MaxMessagesPerFrame
                && NativeTelepath.GetNextMessage(out string address, out float value)
            )
            {
                OnMessageReceived?.Invoke(address, value);
                messagesProcessed++;
            }
        }
    }

    public bool OpenChannel()
    {
        if (settings == null)
        {
            Debug.LogError("[TelepathManager] Cannot open channel: Settings asset is null.");
            return false;
        }
        if (settings.isChannelOpenRuntime)
        {
            Debug.LogWarning("[TelepathManager] Sending channel already open.");
            return true;
        }

        settings.isChannelOpenRuntime = NativeTelepath.OpenChannel(
        //settings.targetIP,
        //settings.targetPort
        );
        return settings.isChannelOpenRuntime;
    }

    public void CloseChannel()
    {
        if (settings == null)
            return;
        NativeTelepath.CloseChannel();
        settings.isChannelOpenRuntime = false;
    }

    public void Send(string addressSuffix, float value)
    {
        if (settings == null)
            return;
        if (!settings.isChannelOpenRuntime)
        {
            Debug.LogWarning("[TelepathManager] Cannot send: Are you channeling?");
            return;
        }

        string fullAddress = settings.addressPrefix.EndsWith("/")
            ? settings.addressPrefix
            : settings.addressPrefix + "/";
        fullAddress += addressSuffix.StartsWith("/") ? addressSuffix.Substring(1) : addressSuffix;

        NativeTelepath.Send(addressSuffix, value);
    }

    public bool StartOscListener()
    {
        if (settings == null)
        {
            Debug.LogError("[TelepathManager] Cannot start OSC listener: Settings asset is null.");
            return false;
        }
        if (settings.isOscListenerRunningRuntime)
        {
            Debug.LogWarning("[TelepathManager] OSC Listener already running.");
            return true;
        }

        settings.isOscListenerRunningRuntime = NativeTelepath.StartListener(settings.listenPort);
        return settings.isOscListenerRunningRuntime;
    }

    public void StopOscListener()
    {
        if (settings == null)
            return;
        NativeTelepath.StopListener();
        settings.isOscListenerRunningRuntime = false;
    }

    public bool StartAudioListener()
    {
        if (settings == null)
        {
            Debug.LogError(
                "[TelepathManager] Cannot start audio listener: Settings asset is null."
            );
            return false;
        }
        if (settings.isAudioListenerRunningRuntime)
        {
            Debug.LogWarning("[TelepathManager] Audio Listener already running.");
            return true;
        }

        settings.isAudioListenerRunningRuntime = NativeTelepath.InitializeTelepathAudioListener(
            settings.audioListenPort
        );
        return settings.isAudioListenerRunningRuntime;
    }

    public void StopAudioListener()
    {
        if (settings == null)
            return;
        NativeTelepath.ShutdownTelepathAudioListener();
        settings.isAudioListenerRunningRuntime = false;
    }

    void OnApplicationQuit()
    {
        CloseChannel();
        StopOscListener();
        StopAudioListener();
    }

    void OnDestroy()
    {
        if (settings != null)
        {
            if (settings.isChannelOpenRuntime)
                CloseChannel();
            if (settings.isOscListenerRunningRuntime)
                StopOscListener();
            if (settings.isAudioListenerRunningRuntime)
                StopAudioListener();
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    [ContextMenu("Open Telepath Channel")]
    void EditorOpenChannel()
    {
        if (Application.isPlaying)
            OpenChannel();
        else
            Debug.LogWarning("Can only open channel in Play Mode from Context Menu.");
    }

    [ContextMenu("Close Telepath Channel")]
    void EditorCloseChannel()
    {
        if (Application.isPlaying)
            CloseChannel();
        else
            Debug.LogWarning("Can only close channel in Play Mode from Context Menu.");
    }

    [ContextMenu("Start Telepath OSC Listener")]
    void EditorStartOscListener()
    {
        if (Application.isPlaying)
            StartOscListener();
        else
            Debug.LogWarning("Can only start OSC listener in Play Mode from Context Menu.");
    }

    [ContextMenu("Stop Telepath OSC Listener")]
    void EditorStopOscListener()
    {
        if (Application.isPlaying)
            StopOscListener();
        else
            Debug.LogWarning("Can only stop OSC listener in Play Mode from Context Menu.");
    }

    [ContextMenu("Start Telepath Audio Listener")]
    void EditorStartAudioListener()
    {
        if (Application.isPlaying)
            StartAudioListener();
        else
            Debug.LogWarning("Can only start audio listener in Play Mode from Context Menu.");
    }

    [ContextMenu("Stop Telepath Audio Listener")]
    void EditorStopAudioListener()
    {
        if (Application.isPlaying)
            StopAudioListener();
        else
            Debug.LogWarning("Can only stop audio listener in Play Mode from Context Menu.");
    }
}