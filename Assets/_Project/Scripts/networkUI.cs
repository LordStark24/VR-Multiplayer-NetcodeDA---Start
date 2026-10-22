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


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    // void Start()
    // {
    //     quickJoinButton.onClick.AddListener(connectionManager.QuickJoinOrCreateSession);
    //     disconnectButton.onClick.AddListener(connectionManager.Disconnect);

    //     UpdateUI(ConnectionManager.ConnectionState.Disconnected);

    //     connectionManager.OnConnectionEvent.AddListener(UpdateUI);
    // }

    void Start()
    {
        quickJoinButton.onClick.AddListener(connectionManager.QuickJoinOrCreateSession);

        disconnectButton.onClick.AddListener(connectionManager.Disconnect);

        connectionManager.OnConnectionEvent.AddListener(UpdateUI);

        UpdateUI(ConnectionManager.ConnectionState.Disconnected);

        // Automatically connect after the UI is ready.
        connectionManager.QuickJoinOrCreateSession();
    }

    public void UpdateUI(ConnectionManager.ConnectionState state)
    {
        offlinePanel.SetActive(state == ConnectionManager.ConnectionState.Failed || state == ConnectionManager.ConnectionState.Disconnected);
        connectingPanel.SetActive(state == ConnectionManager.ConnectionState.Connecting || state == ConnectionManager.ConnectionState.Disconnecting);
        connectedPanel.SetActive(state == ConnectionManager.ConnectionState.Success);
    }

    private void OnDestroy()
    {
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
