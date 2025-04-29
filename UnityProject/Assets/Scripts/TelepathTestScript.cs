using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class TelepathTestScript : MonoBehaviour
{
	private const string PluginName = "Telepath";

	[DllImport(PluginName)]
	private static extern int GetPluginID();

	[DllImport(PluginName)]
	private static extern void SendGameData(string dataName, float value);

	[DllImport(PluginName)]
	private static extern bool InitializeTelepath();

	[DllImport(PluginName)]
	private static extern void ShutdownTelepath();

	private bool isLinkInitialized = false;

	void Start()
	{
		Debug.Log("TelepathTestScript: Attempting to initialize native link...");
		try
		{
			isLinkInitialized = InitializeTelepath();
			if (isLinkInitialized)
			{
				int pluginId = GetPluginID();
				Debug.Log(
					$"TelepathTestScript: Native link initialized successfully! Plugin ID: {pluginId}"
				);
			}
			else
			{
				Debug.LogError("TelepathTestScript: Failed to initialize native link!");
			}
		}
		catch (DllNotFoundException ex)
		{
			Debug.LogError(
				$"TelepathTestScript: Failed to load native plugin '{PluginName}'. Check if '{PluginName}.dylib' (or .bundle) exists in Assets/Plugins/macOS/ and has the correct architecture. Error: {ex.Message}"
			);
		}
		catch (System.Exception ex)
		{
			Debug.LogError(
				$"TelepathTestScript: An error occurred during initialization. Error: {ex.Message}"
			);
		}
	}

	void Update()
	{
		if (isLinkInitialized)
		{
			SendGameData("GameTime", Time.time);

			float mouseXNormalized = Input.mousePosition.x / Screen.width;
			SendGameData("MouseX_Norm", mouseXNormalized);

			SendGameData("0.2", 0.2f);
		}
	}

	void OnApplicationQuit()
	{
		if (isLinkInitialized)
		{
			Debug.Log("TelepathTestScript: Shutting down native link...");
			ShutdownTelepath();
			isLinkInitialized = false;
		}
	}

	void OnDestroy()
	{
		if (isLinkInitialized)
		{
			Debug.Log("TelepathTestScript: Shutting down native link on destroy...");
			ShutdownTelepath();
			isLinkInitialized = false;
		}
	}
}