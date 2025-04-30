using UnityEditor;
using UnityEngine;

public class TelepathEditorWindow : EditorWindow
{
    private TelepathSettings settings;
    private Vector2 scrollPos;
    private const int MaxLogEntries = 100;
    private static System.Collections.Generic.Queue<string> messageLog =
        new System.Collections.Generic.Queue<string>();

    [MenuItem("Window/Telepath Control Panel")]
    public static void ShowWindow()
    {
        GetWindow<TelepathEditorWindow>("Telepath Control");
    }

    private static void LogOscMessage(string address, float value)
    {
        if (messageLog.Count >= MaxLogEntries)
        {
            messageLog.Dequeue();
        }
        messageLog.Enqueue($"[{System.DateTime.Now:HH:mm:ss.fff}] OSC | {address}: {value}");

        if (HasOpenInstances<TelepathEditorWindow>())
        {
            GetWindow<TelepathEditorWindow>().Repaint();
        }
    }

    void OnEnable()
    {
        FindOrCreateSettingsAsset();

        if (Application.isPlaying && TelepathManager.Instance != null)
        {
            TelepathManager.Instance.OnMessageReceived += LogOscMessage;
        }
        EditorApplication.playModeStateChanged += HandlePlayModeStateChange;
    }

    void OnDisable()
    {
        if (TelepathManager.Instance != null)
        {
            TelepathManager.Instance.OnMessageReceived -= LogOscMessage;
        }
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChange;
    }

    void HandlePlayModeStateChange(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.delayCall += () =>
            {
                if (TelepathManager.Instance != null)
                {
                    TelepathManager.Instance.OnMessageReceived += LogOscMessage;
                    Repaint();
                }
            };
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            if (TelepathManager.Instance != null)
            {
                TelepathManager.Instance.OnMessageReceived -= LogOscMessage;
            }
            messageLog.Clear();
            Repaint();
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            Repaint();
        }
    }

    void FindOrCreateSettingsAsset()
    {
        string[] guids = AssetDatabase.FindAssets("t:TelepathSettings");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            settings = AssetDatabase.LoadAssetAtPath<TelepathSettings>(path);
        }
        else
        {
            settings = null;
        }
    }

    void OnGUI()
    {
        GUILayout.Label("Telepath Control Panel", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Manage OSC communication channels.", MessageType.None);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        settings = (TelepathSettings)
            EditorGUILayout.ObjectField(
                "Settings Asset",
                settings,
                typeof(TelepathSettings),
                false
            );
        if (EditorGUI.EndChangeCheck() && settings != null)
        {
            if (Application.isPlaying && TelepathManager.Instance != null)
            {
                Debug.LogWarning(
                    "[Telepath Editor] Telepath settings asset changed during runtime. Applying to TelepathManager."
                );
                TelepathManager.Instance.settings = settings;
            }
        }

        if (settings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create a TelepathSettings asset.",
                MessageType.Warning
            );
            if (GUILayout.Button("Find or Create Settings Asset"))
            {
                FindOrCreateSettingsAsset();
                if (settings == null)
                {
                    TelepathSettings newSettings =
                        ScriptableObject.CreateInstance<TelepathSettings>();
                    if (!AssetDatabase.IsValidFolder("Assets"))
                        AssetDatabase.CreateFolder("", "Assets");
                    AssetDatabase.CreateAsset(newSettings, "Assets/TelepathSettings.asset");
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    settings = newSettings;
                    Selection.activeObject = newSettings;
                    EditorGUIUtility.PingObject(newSettings);
                    Debug.Log(
                        "[Telepath Editor] Created new TelepathSettings asset at Assets/TelepathSettings.asset"
                    );
                }
                else
                {
                    Selection.activeObject = settings;
                    EditorGUIUtility.PingObject(settings);
                }
            }
            return;
        }

        SerializedObject serializedSettings = new SerializedObject(settings);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.LabelField("OSC Sending (Unity -> Target)", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(
            serializedSettings.FindProperty("targetIP"),
            new GUIContent("Target IP")
        );
        EditorGUILayout.PropertyField(
            serializedSettings.FindProperty("targetPort"),
            new GUIContent("Target Port")
        );
        EditorGUILayout.PropertyField(
            serializedSettings.FindProperty("addressPrefix"),
            new GUIContent("Address Prefix")
        );
        EditorGUILayout.PropertyField(
            serializedSettings.FindProperty("autoOpenChannelOnStart"),
            new GUIContent("Auto Open on Start")
        );
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("OSC Receiving (Target -> Unity)", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(
            serializedSettings.FindProperty("listenPort"),
            new GUIContent("Listen Port")
        );
        EditorGUILayout.PropertyField(
            serializedSettings.FindProperty("autoStartListenerOnStart"),
            new GUIContent("Auto Listen on Start")
        );
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Audio Receiving (Target -> Unity)", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(
            serializedSettings.FindProperty("audioListenPort"),
            new GUIContent("Audio Listen Port")
        );
        EditorGUILayout.PropertyField(
            serializedSettings.FindProperty("autoStartAudioListenerOnStart"),
            new GUIContent("Auto Listen on Start")
        );
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Logging Settings", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(
            serializedSettings.FindProperty("logLevel"),
            new GUIContent("Log Level")
        );
        if (EditorGUI.EndChangeCheck())
        {
            if (
                Application.isPlaying
                && TelepathManager.Instance != null
                && TelepathManager.Instance.settings != null
            )
            {
                NativeTelepath.SetLogLevel(TelepathManager.Instance.settings.logLevel);
				Debug.Log($"[TelepathEditorWindow]: Successfully set Telepath Log Level to {TelepathManager.Instance.settings.logLevel}.");
            }
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        serializedSettings.ApplyModifiedProperties();

        GUILayout.Label("Runtime Control", EditorStyles.boldLabel);

        bool isPlaying = Application.isPlaying;
        bool managerExists = isPlaying && TelepathManager.Instance != null;
        bool settingsAssigned = managerExists && TelepathManager.Instance.settings != null;

        bool senderOpen =
            settingsAssigned && TelepathManager.Instance.settings.isChannelOpenRuntime;
        bool oscListenerRunning =
            settingsAssigned && TelepathManager.Instance.settings.isOscListenerRunningRuntime;
        bool audioListenerRunning =
            settingsAssigned && TelepathManager.Instance.settings.isAudioListenerRunningRuntime;

        EditorGUILayout.LabelField("OSC Sending Channel:");
        EditorGUI.indentLevel++;
        GUI.color = senderOpen ? Color.green : Color.red;
        EditorGUILayout.LabelField(
            "Status:",
            senderOpen ? $"Open ({settings.targetIP}:{settings.targetPort})" : "Closed"
        );
        GUI.color = Color.white;
        DrawRuntimeButton(
            "Open Channel",
            !isPlaying || !settingsAssigned || senderOpen,
            () => TelepathManager.Instance?.OpenChannel()
        );
        DrawRuntimeButton(
            "Close Channel",
            !isPlaying || !settingsAssigned || !senderOpen,
            () => TelepathManager.Instance?.CloseChannel()
        );
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("OSC Receiving Listener:");
        EditorGUI.indentLevel++;
        GUI.color = oscListenerRunning ? Color.green : Color.red;
        EditorGUILayout.LabelField(
            "Status:",
            oscListenerRunning ? $"Running (Port {settings.listenPort})" : "Stopped"
        );
        GUI.color = Color.white;
        DrawRuntimeButton(
            "Start OSC Listener",
            !isPlaying || !settingsAssigned || oscListenerRunning,
            () => TelepathManager.Instance?.StartOscListener()
        );
        DrawRuntimeButton(
            "Stop OSC Listener",
            !isPlaying || !settingsAssigned || !oscListenerRunning,
            () => TelepathManager.Instance?.StopOscListener()
        );
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Audio Receiving Listener:");
        EditorGUI.indentLevel++;
        GUI.color = audioListenerRunning ? Color.green : Color.red;
        EditorGUILayout.LabelField(
            "Status:",
            audioListenerRunning ? $"Running (Port {settings.audioListenPort})" : "Stopped"
        );
        GUI.color = Color.white;
        DrawRuntimeButton(
            "Start Audio Listener",
            !isPlaying || !settingsAssigned || audioListenerRunning,
            () => TelepathManager.Instance?.StartAudioListener()
        );
        DrawRuntimeButton(
            "Stop Audio Listener",
            !isPlaying || !settingsAssigned || !audioListenerRunning,
            () => TelepathManager.Instance?.StopAudioListener()
        );
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        GUILayout.Label("Received OSC Messages Log", EditorStyles.boldLabel);
        EditorGUILayout.BeginScrollView(
            scrollPos,
            GUILayout.Height(150),
            GUILayout.ExpandHeight(false)
        );
        if (messageLog.Count > 0)
        {
            string[] logSnapshot = messageLog.ToArray();
            foreach (string logEntry in logSnapshot)
            {
                GUILayout.Label(logEntry, EditorStyles.miniLabel);
            }
        }
        else
        {
            GUILayout.Label(
                isPlaying ? "(Waiting for OSC messages...)" : "(OSC log available in Play Mode)",
                EditorStyles.centeredGreyMiniLabel
            );
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndScrollView();
    }

    void DrawRuntimeButton(string label, bool disabled, System.Action onClick)
    {
        EditorGUI.BeginDisabledGroup(disabled);
        if (GUILayout.Button(label))
        {
            if (TelepathManager.Instance != null && TelepathManager.Instance.settings != null)
            {
                onClick?.Invoke();
                Repaint();
            }
            else
            {
                Debug.LogError(
                    $"[Telepath Editor] Cannot execute '{label}'. TelepathManager or its settings are not available."
                );
            }
        }
        EditorGUI.EndDisabledGroup();
    }

    void OnInspectorUpdate()
    {
        Repaint();
    }
}