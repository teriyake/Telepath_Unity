using System.Runtime.InteropServices;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class TelepathAudioReceiver : MonoBehaviour
{
    public int listenPort = 7002;

    private AudioSource audioSource;
    private int audioSampleRate;
    private int audioChannels;
    private bool nativeListenerInitialized = false;

    private const int audioClipLengthSec = 1;
    private const int audioClipFrequency = 48000;
    private const int audioClipChannels = 2;

    void Awake()
    {
        NativeTelepath.EnsureInitialized();
        Debug.Log("[TelepathAudioReceiver] Ensured NativeTelepath is initialized.");

        audioSource = GetComponent<AudioSource>();
        if (audioSource.clip == null)
        {
            Debug.Log(
                $"[TelepathAudioReceiver] Creating AudioClip: {audioClipLengthSec}s, {audioClipFrequency}Hz, {audioClipChannels}ch"
            );
            audioSource.clip = AudioClip.Create(
                "TelepathAudioStream",
                audioClipLengthSec * audioClipFrequency,
                audioClipChannels,
                audioClipFrequency,
                true,
                OnPcmRead
            );
            audioSource.loop = true;
        }

        audioSampleRate = AudioSettings.outputSampleRate;
        audioChannels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
        Debug.Log(
            $"[TelepathAudioReceiver] Unity Audio Output Settings: Sample Rate = {audioSampleRate}, Channels = {GetUnityChannelCount(AudioSettings.speakerMode)}"
        );

        Debug.Log(
            $"[TelepathAudioReceiver] Attempting to initialize native listener on port {listenPort}."
        );
        nativeListenerInitialized = NativeTelepath.StartAudioListener(listenPort);
        if (nativeListenerInitialized)
        {
            Debug.Log(
                $"[TelepathAudioReceiver] Native audio listener initialization successful on port {listenPort}."
            );
            if (!audioSource.isPlaying)
            {
                audioSource.Play();
                Debug.Log("[TelepathAudioReceiver] AudioSource playback started.");
            }
        }
        else
        {
            Debug.LogError(
                "[TelepathAudioReceiver] Failed to initialize native audio listener. Audio will not be received."
            );
            // this.enabled = false;
        }
    }

    void OnPcmRead(float[] data)
    {
        if (!nativeListenerInitialized)
        {
            System.Array.Clear(data, 0, data.Length);
            return;
        }

        int samplesRead = NativeTelepath.GetAudioSamples(data);

        if (samplesRead < data.Length)
        {
            if (samplesRead > 0)
                Debug.LogWarning(
                    $"[TelepathAudioReceiver] Buffer underrun: Read {samplesRead}/{data.Length} samples."
                );
            System.Array.Clear(data, samplesRead, data.Length - samplesRead);
        }
    }

    void OnDestroy()
    {
        Debug.Log("[TelepathAudioReceiver] OnDestroy called.");
        if (nativeListenerInitialized)
        {
            Debug.Log("[TelepathAudioReceiver] Shutting down native audio listener...");
            NativeTelepath.StopAudioListener();
            nativeListenerInitialized = false;
            Debug.Log("[TelepathAudioReceiver] Native audio listener should be shut down.");
        }

        if (audioSource != null)
        {
            audioSource.Stop();
            // if(audioSource.clip != null) Destroy(audioSource.clip);
        }
    }

    void OnApplicationQuit()
    {
        Debug.Log("[TelepathAudioReceiver] OnApplicationQuit called.");
        if (nativeListenerInitialized)
        {
            NativeTelepath.StopAudioListener();
        }
    }

    int GetUnityChannelCount(AudioSpeakerMode mode)
    {
        switch (mode)
        {
            case AudioSpeakerMode.Mono:
                return 1;
            case AudioSpeakerMode.Stereo:
                return 2;
            case AudioSpeakerMode.Quad:
                return 4;
            case AudioSpeakerMode.Surround:
                return 5;
            case AudioSpeakerMode.Mode5point1:
                return 6;
            case AudioSpeakerMode.Mode7point1:
                return 8;
            case AudioSpeakerMode.Prologic:
                return 2;
            default:
                return 2;
        }
    }
}