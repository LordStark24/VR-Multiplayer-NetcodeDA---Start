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
    void Start()
    {
        quickJoinButton.onClick.AddListener(connectionManager.QuickJoinOrCreateSession);
        disconnectButton.onClick.AddListener(connectionManager.Disconnect);

        UpdateUI(ConnectionManager.ConnectionState.Disconnected);

        connectionManager.OnConnectionEvent.AddListener(UpdateUI);
    }

    public void UpdateUI(ConnectionManager.ConnectionState state)
    {
        offlinePanel.SetActive(state == ConnectionManager.ConnectionState.Failed || state == ConnectionManager.ConnectionState.Disconnected);
        connectingPanel.SetActive(state == ConnectionManager.ConnectionState.Connecting || state == ConnectionManager.ConnectionState.Disconnecting);
        connectedPanel.SetActive(state == ConnectionManager.ConnectionState.Success);
    }
}
