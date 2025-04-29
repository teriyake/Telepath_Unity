using UnityEngine;

public class TelepathReceiverTest : MonoBehaviour
{
	[Tooltip("Filter received messages by this exact address pattern. Leave empty to receive all.")]
	public string addressFilter = "/telepath/tx";

	void Start()
	{
		if (TelepathManager.Instance == null)
		{
			Debug.LogError(
				"[TelepathReceiverTest] TelepathManager instance not found! Make sure it's in the scene and initialized before this script.",
				this
			);
			return;
		}

		TelepathManager.Instance.OnMessageReceived += HandleMessageReceived;
		Debug.Log("[TelepathReceiverTest] Subscribed to Telepath messages.", this);
	}

	void OnDestroy()
	{
		if (TelepathManager.Instance != null)
		{
			TelepathManager.Instance.OnMessageReceived -= HandleMessageReceived;
			Debug.Log("[TelepathReceiverTest] Unsubscribed from Telepath messages.", this);
		}
	}

	private void HandleMessageReceived(string address, float value)
	{
		if (string.IsNullOrEmpty(addressFilter) || address == addressFilter)
		{
			Debug.Log(
				$"[TelepathReceiverTest] Received OSC: Address='{address}', Value={value}",
				this
			);
		}
	}
}