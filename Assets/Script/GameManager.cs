using Unity.Netcode;
using UnityEngine;
using TMPro;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Prefabs & Spawn Points")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform throwerSpawnPos;
    [SerializeField] private Transform dodgerSpawnPos;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI infoPanel;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public Transform GetSpawnPosition(bool isThrower)
    {
        return isThrower ? throwerSpawnPos : dodgerSpawnPos;
    }

    // Called by PlayerComponent (client-side) to bind the shared UI reference
    public void RegisterPlayer(PlayerComponent player)
    {
        player.infoPanel = infoPanel;
    }
}