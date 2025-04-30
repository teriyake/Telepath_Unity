
# Telepath (Unity)

*Telepath* is a communication bridge between Unity and OSC/audio-enabled applications, primarily designed for interaction with VCVRack modular synthesizer patches. This repo contains the Unity side of *Telepath*, including a native C++ plugin, a C# wrapper, helper components, a settings asset, and a Unity editor control panel.

*Note: Telepath (Unity) relies on [Telepath (VCVRack)](https://github.com/teriyake/Telepath_VCVRack) or other external applications with appropriate OSC/Audio UDP modules to send/receive data.*

## Overview

*Telepath* enables real-time interaction between your Unity project and external applications (like VCVRack) through:

1.  **OSC Sending**: Send game state variables (as floats) from Unity to a target application using OSC messages.
2.  **OSC Receiving**: Listen for incoming OSC messages (with single float arguments) from an external application to control game elements in Unity.
3.  **Audio Streaming**: Receive raw, real-time audio (as UDP float data) from an external application directly into Unity's audio system via an `AudioSource`.

## Features

-   **OSC Messaging**: Send float values from Unity via OSC using configurable addresses (`/prefix/yourDataName`). Receive OSC messages with a single float argument.
-   **Raw Audio Streaming**: Receive real-time UDP audio data (stereo, 48kHz floats) into a Unity `AudioSource`.
-   **Configuration Asset**: Uses a `TelepathSettings` ScriptableObject for easy configuration of ports, IP addresses, and auto-start behaviour.
-   **Editor Control Panel**: A dedicated `Window > Telepath Control Panel` for managing settings, viewing connection status, starting/stopping listeners/channels at runtime, and logging received OSC messages.
-   **Thread Management**: Safe multi-threaded operation for OSC listening and audio reception, separate from the Unity main thread.
-   **Circular Buffer**: Efficient internal circular buffer for incoming audio samples with overflow protection.
-   **Native Debug Logging**: Configurable logging levels for the native plugin, viewable in the Unity console.

## Installation

**Compatibility Note:** Currently, the pre-built native plugin is provided for **macOS (Intel & Apple Silicon Universal)** only. Windows and Linux support is planned. You can compile the C++ source (`src/telepath.cpp`) for other platforms if needed.
1.  Download the `libTelepath.dylib` file from the [releases page](https://github.com/teriyake/Telepath_Unity/releases) (or build it yourself).
2.  Create the folder `Assets/Plugins/macOS` in your Unity project if it doesn't exist.
3.  Place the `libTelepath.dylib` file inside `Assets/Plugins/macOS/`.
4.  Import the Unity scripts (`Assets/Scripts`) and the Editor script (`Assets/Editor`) into your project.
5.  **Important:** Add the `TelepathManager.cs` script to a persistent GameObject (e.g., a dedicated "Manager" object) in your scene.
6.  Create or assign a `TelepathSettings` asset to the `TelepathManager` component in the Inspector. You can create one via `Assets > Create > Telepath > Channel Settings` or use the button in the Telepath Control Panel (`Window > Telepath Control Panel`).

## Getting Started

The primary way to interact with *Telepath* is through the `TelepathManager` singleton instance and the `TelepathAudioReceiver` component.

### Configuration (`TelepathSettings` Asset)

Before starting, configure your `TelepathSettings` asset (assigned to `TelepathManager`):

-   **Sending:** Set `Target IP` (e.g., `127.0.0.1`) and `Target Port` (default `7001`) for the application you want to send OSC *to*. Define the `Address Prefix` (default `/telepath/`).
-   **OSC Receiving:** Set the `Listen Port` (default `9001`) for Unity to receive OSC messages *on*.
-   **Audio Receiving:** Set the `Audio Listen Port` (default `7002`) for Unity to receive raw audio data *on*.
-   **Auto-Start:** Check the boxes (`Auto Open on Start`, `Auto Listen on Start`) if you want channels/listeners to activate automatically when the game starts.
-   **Logging:** Adjust the `Log Level` (Debug, Info, Warning, Error).

### Initialization (Managed by `TelepathManager`)

The `TelepathManager` handles initialization based on the `TelepathSettings` asset's "Auto-Start" flags. You can also manually control the connections using the Manager or the Editor Window:

```csharp
// Access the manager instance
TelepathManager manager = TelepathManager.Instance;

// Manually open the OSC sending channel (if not auto-started)
if (!manager.settings.isChannelOpenRuntime)
{
    manager.OpenChannel();
}

// Manually start the OSC listener (if not auto-started)
if (!manager.settings.isOscListenerRunningRuntime)
{
    manager.StartOscListener();
}

// Manually start the Audio listener (if not auto-started)
// Note: Audio receiving also requires the TelepathAudioReceiver component
if (!manager.settings.isAudioListenerRunningRuntime)
{
    manager.StartAudioListener();
}
```

### Sending Game Data via OSC

Use the `Send` method of the `TelepathManager`. The `addressPrefix` from your settings will be prepended automatically.

```csharp
// Assuming default prefix "/telepath/"

// Send player health - VCVRack would receive on "/telepath/playerHealth"
TelepathManager.Instance.Send("playerHealth", 75.5f);

// Send position data
TelepathManager.Instance.Send("playerX", transform.position.x);
TelepathManager.Instance.Send("playerY", transform.position.y);

// Send to a sub-path - VCVRack receives on "/telepath/enemies/count"
TelepathManager.Instance.Send("enemies/count", 10f);
```

### Receiving OSC Messages

Subscribe to the `OnMessageReceived` event of the `TelepathManager`. This event happens on the main thread during `Update`.

```csharp
using UnityEngine;

public class MyGameController : MonoBehaviour
{
    public float intensity = 0.0f; // Example variable to control

    void Start()
    {
        if (TelepathManager.Instance != null)
        {
        	TelepathManager.Instance.OnMessageReceived += HandleOscMessage;
        }
        else
        {
            Debug.LogError("TelepathManager not found!");
        }
    }

    void OnDestroy()
    {
    	if (TelepathManager.Instance != null)
        {
        	TelepathManager.Instance.OnMessageReceived -= HandleOscMessage;
        }
    }

    private void HandleOscMessage(string address, float value)
    {
    	// Optional: Log all received messages
        // Debug.Log($"Received OSC: {address} = {value}");

        // Handle specific messages
        if (address == "/vcvrack/intensity") // Match the address sent FROM VCVRack
        {
        	this.intensity = value;
            // Apply intensity to game element, e.g., light brightness
        }
        else if (address == "/vcvrack/triggerEvent")
        {
            if (value > 0.5f) // Treat as a trigger on rising edge
            {
            	// Trigger some game event
                Debug.Log("Event Triggered via OSC!");
            }
        }
    }
}
```

*(See also: `TelepathReceivingTest.cs` for a simple example script)*

### Receiving Audio

1.  Add an `AudioSource` component to a GameObject in your scene.
2.  Add the `TelepathAudioReceiver.cs` script to the *same* GameObject.
3.  Ensure the `TelepathManager` is present in the scene and its `TelepathSettings` asset has `autoStartAudioListenerOnStart` checked, **OR** manually call `TelepathManager.Instance.StartAudioListener()`.
4.  The `TelepathAudioReceiver` script will automatically:
    *   Start the native audio listener on the port specified in its `Listen Port` field (defaults to the setting value 7002).
    *   Create a looping `AudioClip`.
    *   Continuously feed incoming audio data from the native plugin into the `AudioSource` via the `OnPcmRead` callback.
5.  The `AudioSource` will then play the received audio. Configure the `AudioSource`'s output (e.g., mixer group) as needed.

**Important**: Make sure to enable "Run in Background":
`Edit -> Project Settings -> Player -> Resolution -> Run in Background (check this box)`; otherwise, no audio can be received if the Unity window is not in focus. 

```csharp
// No extra code usually needed here if using TelepathAudioReceiver component!
// Just ensure the component is added and the listener is started.
// Don't forget the Audio Listener!
```

### Cleanup (Managed by `TelepathManager`)

The `TelepathManager` automatically shuts down listeners and closes the channel in its `OnDestroy` and `OnApplicationQuit` methods. You typically don't need to call shutdown methods manually unless you have specific needs during gameplay.

```csharp
// Manual shutdown (if needed)
TelepathManager.Instance.StopAudioListener();
TelepathManager.Instance.StopOscListener();
TelepathManager.Instance.CloseChannel();
```

## VCVRack Setup 

*Note: This plugin is designed for and tested to work with VCVRack, though you are welcome to experiment with other audio software as well!*

Check out the other half of *Telepath* [here](https://github.com/teriyake/Telepath_VCVRack) for more details on how to set up the VCVRack side.

## API Reference (Primary Components)

### `TelepathManager` (Singleton: `TelepathManager.Instance`)

-   **`TelepathSettings settings`**: Public field holding the configuration asset.
-   **`event Action<string, float> OnMessageReceived`**: Event fired when an OSC message is received. `string`: full address pattern, `float`: value.
-   **`bool OpenChannel()`**: Manually initializes the OSC sender connection based on settings. Returns `true` on success. Updates `settings.isChannelOpenRuntime`.
-   **`void CloseChannel()`**: Manually closes the OSC sender connection. Updates `settings.isChannelOpenRuntime`.
-   **`void Send(string addressSuffix, float value)`**: Sends an OSC message. `addressSuffix` is appended to the `addressPrefix` from settings.
-   **`bool StartOscListener()`**: Manually starts the OSC listener based on settings. Returns `true` on success. Updates `settings.isOscListenerRunningRuntime`.
-   **`void StopOscListener()`**: Manually stops the OSC listener. Updates `settings.isOscListenerRunningRuntime`.
-   **`bool StartAudioListener()`**: Manually starts the native audio listener based on settings. Returns `true` on success. Updates `settings.isAudioListenerRunningRuntime`. (Requires `TelepathAudioReceiver` component for playback).
-   **`void StopAudioListener()`**: Manually stops the native audio listener. Updates `settings.isAudioListenerRunningRuntime`.

### `TelepathAudioReceiver` (Component)

-   **`int listenPort`**: The port this specific receiver listens on (defaults to `settings.audioListenPort`).
-   *(Internal)* Uses `NativeTelepath.GetAudioSamples` and `AudioClip.Create` with `OnPcmRead` to feed the attached `AudioSource`.

### `TelepathSettings` (ScriptableObject)

-   Contains configuration fields for IPs, ports, prefix, auto-start flags, and log level. See the "Configuration" section above.
-   Contains non-serialized runtime status flags (`isChannelOpenRuntime`, `isOscListenerRunningRuntime`, `isAudioListenerRunningRuntime`) used by the Editor Window and potentially your code.

### `NativeTelepath` (Static Class - Lower Level)

*(You would generally use `TelepathManager` instead, but these are available as well if needed)*
-   `bool IsTelepathChannelOpen()`: Checks if the native sender socket is initialized.
-   `bool IsTelepathListenerRunning()`: Checks if the native OSC listener thread is active.
-   `bool GetNextMessage(out string address, out float value)`: Polls the native queue for the next OSC message.
-   `int GetAudioSamples(float[] buffer)`: Fills the provided buffer with available audio samples from the native circular buffer. Returns the number of samples actually read.
-   `void SetLogLevel(NativeTelepath.LogLevel level)`: Sets the native plugin's log verbosity.
-   *(Other native initialization/shutdown functions are wrapped by TelepathManager)*

## Technical Details

### Threading Model

*Telepath* uses separate native threads managed by the C++ plugin for:
-   OSC message listening (UDP socket, OSCPack parsing).
-   Audio reception (raw UDP socket, direct float reading).
These operate independently of the Unity main thread. Received OSC messages are queued and polled by `TelepathManager` in `Update`. Received audio samples are placed in a circular buffer, read by `TelepathAudioReceiver` via the `AudioClip` callback (which may run on Unity's audio thread). Synchronization uses mutexes.

### Audio Buffer

-   Internal Circular Buffer Size: **8 seconds** (hardcoded in C++)
-   Expected Sample Rate: **48000 Hz** (hardcoded)
-   Expected Channels: **2 (Stereo)**, interleaved floats (LRLRLR...) (hardcoded)
-   Transport: **Raw UDP datagrams** containing float samples

### OSC Implementation

-   Built using the [OSCPack](http://www.rossbencina.com/code/oscpack) library.
-   Supports sending OSC messages with a single float argument.
-   Supports receiving OSC messages with a single float argument.
-   Uses UDP transport.

## Troubleshooting

1.  **No Connection / No OSC Received in Target App:**
    *   Ensure the target application (VCVRack) is running *before* starting the Unity scene (or manually open the channel via Manager/Editor Window).
    *   Verify `Target IP` and `Target Port` in `TelepathSettings` match the receiver settings in the target app.
    *   Check firewalls on both machines (if not using localhost) allow UDP traffic on the target port (default 7001).
    *   Ensure the `TelepathManager`'s `settings.isChannelOpenRuntime` is `true` (check Editor Window).
2.  **No OSC Received in Unity:**
    *   Ensure the sending application is running and configured to send to Unity's IP and the correct `Listen Port` (default 9001).
    *   Check firewalls allow UDP traffic on the listen port.
    *   Verify `TelepathManager`'s `settings.isOscListenerRunningRuntime` is `true`.
    *   Make sure you have subscribed to the `TelepathManager.Instance.OnMessageReceived` event correctly.
    *   Check the OSC Log in the Telepath Editor Window (`Window > Telepath Control Panel`).
3.  **No Audio Received in Unity:**
    *   Confirm the audio sending application (VCVRack) is active and sending raw float UDP data to Unity's IP and the correct `Audio Listen Port` (default 7002).
    *   Verify `TelepathManager`'s `settings.isAudioListenerRunningRuntime` is `true`.
    *   Ensure the `TelepathAudioReceiver` component is on the same GameObject as an `AudioSource`.
	*	Ensure there is a GameObject with an `AudioListener` component.
    *   Check the `AudioSource` is playing and not muted, and its output routing is correct.
    *   Confirm the sample rate (48kHz) and channel count (Stereo) match what the plugin expects.
4.  **Error Messages / Plugin Not Found:**
    *   Ensure the `.dylib` file is correctly placed in `Assets/Plugins/macOS/`.
    *   Verify you are running on macOS.
    *   Check the Unity Console for specific error messages from the native plugin (enable Debug log level in settings for more detail).
5.  **Performance Issues / Audio Dropouts:**
    *   Reduce the rate of sending OSC messages from Unity or the external application if excessive.
    *   Monitor CPU usage in both Unity and the external audio application. Heavy processing can cause dropouts.
    *   The internal audio buffer helps smooth jitter, but sustained high CPU load can still cause issues.


## License

MIT

## Acknowledgments

-   [OSCPack](https://opensoundcontrol.stanford.edu/implementations/oscpack.html) library for the C++ OSC implementation.
-   The [VCVRack](https://vcvrack.com) community and developers.

---
![](telepathic_improvisation_oliveros.png)
---from Pauline Oliveros' *Sonic Meditations* (1974)
