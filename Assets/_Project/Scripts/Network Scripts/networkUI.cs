using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class networkUI : MonoBehaviour
{
    public Button quickJoinButton;
    public Button disconnectButton; public ConnectionManager connectionManager;

    public GameObject offlinePanel;
    public GameObject connectingPanel;
    public GameObject connectedPanel;
    public Button playMansionButton;
    private MansionTravelManager mansionTravelManager;

    void Start()
    {
        // Find the surviving manager after duplicate objects are removed.
        connectionManager =
            UnityEngine.Object.FindFirstObjectByType<ConnectionManager>();

        if (connectionManager == null)
        {
            Debug.LogError("[UI] ConnectionManager was not found.");
            UpdateUI(ConnectionManager.ConnectionState.Failed);
            return;
        }

        mansionTravelManager = connectionManager.GetComponent<MansionTravelManager>();

        if (playMansionButton != null && mansionTravelManager != null)
        {
            playMansionButton.onClick.AddListener(
                mansionTravelManager.StartMansion);
        }

        quickJoinButton.onClick.AddListener(
            connectionManager.QuickJoinOrCreateSession);

        disconnectButton.onClick.AddListener(
            connectionManager.Disconnect);

        connectionManager.OnConnectionEvent.AddListener(UpdateUI);

        if (connectionManager.IsBusy)
        {
            UpdateUI(ConnectionManager.ConnectionState.Connecting);
        }
        else if (connectionManager.CurrentSession != null)
        {
            UpdateUI(ConnectionManager.ConnectionState.Success);
        }
        else
        {
            UpdateUI(ConnectionManager.ConnectionState.Disconnected);

            connectionManager.QuickJoinOrCreateSession();
        }
    }

    public void UpdateUI(ConnectionManager.ConnectionState state)
    {
        offlinePanel.SetActive(state == ConnectionManager.ConnectionState.Failed || state == ConnectionManager.ConnectionState.Disconnected);
        connectingPanel.SetActive(state == ConnectionManager.ConnectionState.Connecting || state == ConnectionManager.ConnectionState.Disconnecting);
        connectedPanel.SetActive(state == ConnectionManager.ConnectionState.Success);
    }

    private void OnDestroy()
    {
        if (playMansionButton != null && mansionTravelManager != null)
        {
            playMansionButton.onClick.RemoveListener(
                mansionTravelManager.StartMansion);
        }

        if (connectionManager == null)
            return;

        if (quickJoinButton != null)
        {
            quickJoinButton.onClick.RemoveListener(
                connectionManager.QuickJoinOrCreateSession);
        }

        if (disconnectButton != null)
        {
            disconnectButton.onClick.RemoveListener(
                connectionManager.Disconnect);
        }

        connectionManager.OnConnectionEvent?.RemoveListener(UpdateUI);
    }

}
