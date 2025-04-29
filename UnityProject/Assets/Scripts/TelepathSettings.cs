using UnityEngine;

[CreateAssetMenu(fileName = "TelepathSettings", menuName = "Telepath/Channel Settings", order = 1)]
public class TelepathSettings : ScriptableObject
{
    [Header("Sending Settings (Unity -> Target OSC)")]
    [Tooltip("IP Address of the target OSC application (e.g., VCV Rack).")]
    public string targetIP = "127.0.0.1";

    [Tooltip("Port number the target OSC application is listening on.")]
    public int targetPort = 7001;

    [Tooltip("Default OSC address prefix for outgoing messages. Sent as /prefix/yourDataName.")]
    public string addressPrefix = "/telepath/";

    [Tooltip("Automatically open the sending channel when the game starts?")]
    public bool autoOpenChannelOnStart = true;

    [Header("Receiving Settings (Target OSC -> Unity)")]
    [Tooltip("Port number Unity should listen on for incoming OSC messages.")]
    public int listenPort = 9001;

    [Tooltip("Automatically start the OSC listener when the game starts?")]
    public bool autoStartListenerOnStart = true;

    [Header("Audio Receiving Settings (Target Audio -> Unity)")]
    [Tooltip("Port number Unity should listen on for incoming audio data.")]
    public int audioListenPort = 7002; // Default port from native plugin

    [Tooltip("Automatically start the audio listener when the game starts?")]
    public bool autoStartAudioListenerOnStart = true;

    [System.NonSerialized]
    public bool isChannelOpenRuntime = false;

    [System.NonSerialized]
    public bool isOscListenerRunningRuntime = false;

    [System.NonSerialized]
    public bool isAudioListenerRunningRuntime = false;

    [Header("Logging Settings")]
    [Tooltip("Set the verbosity level for native plugin logs.")]
    public NativeTelepath.LogLevel logLevel = NativeTelepath.LogLevel.LOG_INFO;

    private void OnEnable()
    {
        isChannelOpenRuntime = false;
        isOscListenerRunningRuntime = false;
        isAudioListenerRunningRuntime = false;
    }
}