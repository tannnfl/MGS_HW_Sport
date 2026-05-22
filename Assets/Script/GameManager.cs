using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Collections;
using TMPro;

// Defined outside the class so it can be used as a NetworkVariable type
[System.Serializable]
public enum GameState
{
    Lobby,
    RoundActive,
    RoundEnd,
    GameEnd
}

[System.Serializable]
public struct PlayerScoreData : INetworkSerializable, System.IEquatable<PlayerScoreData>
{
    public ulong ClientId;
    public FixedString64Bytes Name;
    public float Score;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Name);
        serializer.SerializeValue(ref Score);
    }

    public bool Equals(PlayerScoreData other)
    {
        return ClientId == other.ClientId && Name == other.Name && Score == other.Score;
    }
}

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    // ===== Network State =====
    public NetworkVariable<GameState> gameState = new NetworkVariable<GameState>(
        GameState.Lobby,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> timerValue = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> currentRound = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> totalRounds = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Bug fix #9 (NetworkList): must be initialized in Awake, not in field initializer
    public NetworkList<PlayerScoreData> playerScores;

    // ===== Spawn Points =====
    [Header("Spawn Points")]
    [SerializeField] private Transform[] throwerSpawnPoints;
    [SerializeField] private Transform[] dodgerSpawnPoints;
    [SerializeField] private Transform ballSpawnPoint;

    // ===== UI =====
    [Header("UI")]
    [SerializeField] private Button startButton;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI leaderboardText;
    [SerializeField] private TextMeshProUGUI roundInfoText;
    [SerializeField] private TMP_InputField nameInputField;

    // ===== Server-Only State =====
    private Dictionary<ulong, PlayerComponent> registeredPlayers = new Dictionary<ulong, PlayerComponent>();
    private float roundTimer;
    private float roundTimerMax;
    private bool roundActive;
    private List<(ulong clientId, float eliminationTime)> eliminationOrder = new List<(ulong, float)>();
    private HashSet<ulong> originalThrowers = new HashSet<ulong>();

    // ===== Unity Lifecycle =====

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // DontDestroyOnLoad removed — this is a single-scene game and NGO manages
        // in-scene NetworkObjects internally. Calling DontDestroyOnLoad on an
        // in-scene NetworkObject confuses NGO's object tracking.

        // Bug fix (NetworkList): initialize in Awake
        playerScores = new NetworkList<PlayerScoreData>();
    }

    public override void OnNetworkSpawn()
    {
        // Subscribe to network variable changes for UI updates
        timerValue.OnValueChanged += OnTimerChanged;
        playerScores.OnListChanged += OnPlayerScoresChanged;
        gameState.OnValueChanged += OnGameStateChanged;

        // Host/server: set up start button
        if (IsHost || IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += _ => UpdateStartButton();

            if (startButton != null)
            {
                startButton.gameObject.SetActive(true);
                startButton.onClick.AddListener(OnStartButtonClicked);
                UpdateStartButton();
            }

            // Register any PlayerComponents already in the scene (covers the host's own
            // player, which may have spawned before GameManager.OnNetworkSpawn finished)
            StartCoroutine(RegisterExistingPlayers());
        }
        else
        {
            // Non-host clients don't see the start button
            if (startButton != null)
                startButton.gameObject.SetActive(false);
        }

        // Name input: all clients can type their name
        if (nameInputField != null)
        {
            nameInputField.onEndEdit.AddListener(OnNameSubmitted);
        }

        // Initial UI
        UpdateLeaderboardUI();
    }

    public override void OnNetworkDespawn()
    {
        timerValue.OnValueChanged -= OnTimerChanged;
        playerScores.OnListChanged -= OnPlayerScoresChanged;
        gameState.OnValueChanged -= OnGameStateChanged;

        if (IsHost || IsServer)
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= _ => UpdateStartButton();
            }
        }
    }

    private void Update()
    {
        if (!IsServer || !roundActive) return;

        roundTimer -= Time.deltaTime;
        timerValue.Value = Mathf.Max(0f, roundTimer);

        CheckWinConditions();

        if (roundTimer <= 0f)
        {
            roundActive = false;
            EndRound_TimerExpired();
        }
    }

    // ===== Player Registration =====

    /// <summary>Server only: register a player when they spawn.</summary>
    public void RegisterPlayer(PlayerComponent player)
    {
        if (!IsServer) return;

        ulong id = player.OwnerClientId;

        bool inDict = registeredPlayers.ContainsKey(id);
        bool inScores = false;
        foreach (var entry in playerScores)
            if (entry.ClientId == id) { inScores = true; break; }

        if (inDict && inScores) return; // already fully registered

        if (!inDict) registeredPlayers[id] = player;
        if (!inScores)
            playerScores.Add(new PlayerScoreData
            {
                ClientId = id,
                Name = new FixedString64Bytes($"Player#{id + 1}"),
                Score = 0f
            });

        UpdateStartButton();
    }

    /// <summary>Server only: unregister a player when they despawn.</summary>
    public void UnregisterPlayer(ulong clientId)
    {
        if (!IsServer) return;

        registeredPlayers.Remove(clientId);
        UpdateStartButton();
    }

    // ===== Spawn Helpers =====

    public Vector3 GetSpawnPosition(bool isThrower, int index)
    {
        if (isThrower)
        {
            if (throwerSpawnPoints == null || throwerSpawnPoints.Length == 0)
                return Vector3.zero;
            int i = Mathf.Clamp(index, 0, throwerSpawnPoints.Length - 1);
            return throwerSpawnPoints[i].position;
        }
        else
        {
            if (dodgerSpawnPoints == null || dodgerSpawnPoints.Length == 0)
                return Vector3.zero;
            int i = Mathf.Clamp(index, 0, dodgerSpawnPoints.Length - 1);
            return dodgerSpawnPoints[i].position;
        }
    }

    private Transform GetSpawnTransform(bool isThrower, int index)
    {
        if (isThrower)
        {
            if (throwerSpawnPoints == null || throwerSpawnPoints.Length == 0) return null;
            int i = Mathf.Clamp(index, 0, throwerSpawnPoints.Length - 1);
            return throwerSpawnPoints[i];
        }
        else
        {
            if (dodgerSpawnPoints == null || dodgerSpawnPoints.Length == 0) return null;
            int i = Mathf.Clamp(index, 0, dodgerSpawnPoints.Length - 1);
            return dodgerSpawnPoints[i];
        }
    }

    // ===== Game Flow =====

    private void OnStartButtonClicked()
    {
        if (!NetworkManager.Singleton.IsServer) return;
        int connectedCount = NetworkManager.Singleton.ConnectedClients.Count;
        if (connectedCount < 4) return;

        // Ensure all spawned PlayerComponents are registered (catches any missed during spawn order race)
        foreach (var player in FindObjectsByType<PlayerComponent>(FindObjectsSortMode.None))
        {
            if (!registeredPlayers.ContainsKey(player.OwnerClientId))
                RegisterPlayer(player);
        }

        int playerCount = registeredPlayers.Count;
        totalRounds.Value = Mathf.FloorToInt(1.5f * playerCount);
        currentRound.Value = 0;
        StartRound(null);
    }

    private void UpdateStartButton()
    {
        if (startButton == null) return;
        int count = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClients.Count : 0;
        bool canStart = count >= 4 && gameState.Value == GameState.Lobby;
        startButton.interactable = canStart;
    }

    private void OnClientConnected(ulong clientId)
    {
        UpdateStartButton();
        // Give the newly connected client's PlayerComponent one frame to spawn
        StartCoroutine(RegisterExistingPlayers());
    }

    private System.Collections.IEnumerator RegisterExistingPlayers()
    {
        yield return null; // wait one frame for PlayerComponent.OnNetworkSpawn
        foreach (var player in FindObjectsByType<PlayerComponent>(FindObjectsSortMode.None))
        {
            if (!registeredPlayers.ContainsKey(player.OwnerClientId))
                RegisterPlayer(player);
        }
    }

    /// <summary>
    /// Server only: start a new round.
    /// forcedThrowers = specific clientIds to be throwers (for round transitions).
    /// If null, pick 2 random throwers.
    /// </summary>
    private void StartRound(List<ulong> forcedThrowers)
    {
        if (!IsServer) return;

        currentRound.Value++;
        eliminationOrder.Clear();
        originalThrowers.Clear();

        List<ulong> allIds = new List<ulong>(registeredPlayers.Keys);
        int playerCount = allIds.Count;

        // Pick throwers
        List<ulong> throwerIds;
        if (forcedThrowers != null && forcedThrowers.Count >= 2)
        {
            throwerIds = new List<ulong> { forcedThrowers[0], forcedThrowers[1] };
        }
        else
        {
            // Bug fix #8: pick 2 random throwers from ALL players
            throwerIds = PickRandom2(allIds);
        }

        foreach (ulong id in throwerIds)
            originalThrowers.Add(id);

        // Assign roles and spawn positions
        int throwerIndex = 0;
        int dodgerIndex = 0;

        foreach (ulong id in allIds)
        {
            if (!registeredPlayers.TryGetValue(id, out PlayerComponent player)) continue;

            bool isThrower = throwerIds.Contains(id);
            Transform spawnT = GetSpawnTransform(isThrower, isThrower ? throwerIndex++ : dodgerIndex++);
            Vector3 pos = spawnT != null ? spawnT.position : Vector3.zero;
            Quaternion rot = spawnT != null ? spawnT.rotation : Quaternion.identity;
            player.SetRoleAndSpawn(isThrower, pos, rot);
        }

        // Reset ball
        var ballObj = GameObject.FindWithTag("Ball");
        ballObj?.GetComponent<BallComponent>()?.ResetBall(
            ballSpawnPoint != null ? ballSpawnPoint.position : Vector3.zero
        );

        roundTimerMax = 40f + 5f * playerCount;
        roundTimer = roundTimerMax;
        timerValue.Value = roundTimer;
        roundActive = true;

        gameState.Value = GameState.RoundActive;

        ShowRoundResultClientRpc($"Round {currentRound.Value} — GO!");
    }

    private List<ulong> PickRandom2(List<ulong> ids)
    {
        List<ulong> copy = new List<ulong>(ids);
        // Fisher-Yates shuffle
        for (int i = copy.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            ulong tmp = copy[i]; copy[i] = copy[j]; copy[j] = tmp;
        }
        List<ulong> result = new List<ulong>();
        for (int i = 0; i < Mathf.Min(2, copy.Count); i++)
            result.Add(copy[i]);
        return result;
    }

    // ===== Elimination =====

    /// <summary>Server only: eliminate a dodger, convert them to thrower.</summary>
    public void EliminatePlayer(ulong clientId)
    {
        if (!IsServer || !roundActive) return;
        if (!registeredPlayers.TryGetValue(clientId, out PlayerComponent player)) return;
        // Already a thrower — skip
        if (player.isThrower.Value) return;

        // Record elimination time
        eliminationOrder.Add((clientId, roundTimer));

        // Convert to thrower
        int throwerIndex = CountThrowers();
        Transform spawnT = GetSpawnTransform(true, throwerIndex);
        Vector3 pos = spawnT != null ? spawnT.position : Vector3.zero;
        Quaternion rot = spawnT != null ? spawnT.rotation : Quaternion.identity;
        player.SetRoleAndSpawn(true, pos, rot);

        // Check win conditions immediately after elimination
        CheckWinConditions();
    }

    // ===== Win Conditions =====

    private void CheckWinConditions()
    {
        if (!IsServer || !roundActive) return;

        int dodgerCount = CountDodgers();

        // All dodgers eliminated → original throwers win
        if (dodgerCount == 0)
        {
            roundActive = false;
            EndRound_ThrowersWin();
            return;
        }

        // 30 seconds left and exactly 1 dodger remains → that dodger wins
        if (roundTimer <= 30f && dodgerCount == 1)
        {
            roundActive = false;
            EndRound_LastDodgerWins();
            return;
        }
    }

    private int CountDodgers()
    {
        int count = 0;
        foreach (var kv in registeredPlayers)
            if (!kv.Value.isThrower.Value) count++;
        return count;
    }

    private int CountThrowers()
    {
        int count = 0;
        foreach (var kv in registeredPlayers)
            if (kv.Value.isThrower.Value) count++;
        return count;
    }

    // ===== Round End Variants =====

    // All dodgers eliminated — original throwers win playerCount*5 each.
    private void EndRound_ThrowersWin()
    {
        if (!IsServer) return;
        gameState.Value = GameState.RoundEnd;

        int playerCount = registeredPlayers.Count;
        float score = playerCount * 5f;

        foreach (ulong id in originalThrowers)
            AddScore(id, score);

        GiveDodgerSurvivedBonus(roundTimer);

        ShowRoundResultClientRpc($"All dodgers out! Throwers win {score:F0} pts each.");
        StartCoroutine(DelayedRoundEnd(4f));
    }

    // 30 s remaining, exactly 1 dodger left — that dodger wins playerCount*10.
    private void EndRound_LastDodgerWins()
    {
        if (!IsServer) return;
        gameState.Value = GameState.RoundEnd;

        int playerCount = registeredPlayers.Count;
        float score = playerCount * 10f;

        ulong lastDodgerId = ulong.MaxValue;
        foreach (var kv in registeredPlayers)
            if (!kv.Value.isThrower.Value) { lastDodgerId = kv.Key; break; }

        if (lastDodgerId != ulong.MaxValue)
            AddScore(lastDodgerId, score);

        GiveDodgerSurvivedBonus(roundTimer);

        ShowRoundResultClientRpc($"Last Dodger survives at 30 s! Score: {score:F0} pts.");
        StartCoroutine(DelayedRoundEnd(4f));
    }

    // Timer runs out with >1 dodger alive — all survivors win playerCount*5 each.
    private void EndRound_TimerExpired()
    {
        if (!IsServer) return;
        gameState.Value = GameState.RoundEnd;

        int playerCount = registeredPlayers.Count;
        float score = playerCount * 5f;

        List<ulong> survivors = new List<ulong>();
        foreach (var kv in registeredPlayers)
            if (!kv.Value.isThrower.Value) survivors.Add(kv.Key);

        foreach (ulong id in survivors)
            AddScore(id, score);

        GiveDodgerSurvivedBonus(0f); // 0 s remaining — full duration survived

        ShowRoundResultClientRpc($"Time's up! {survivors.Count} dodger(s) survive. Score: {score:F0} pts each.");
        StartCoroutine(DelayedRoundEnd(4f));
    }

    /// <summary>
    /// Give every original dodger floor(survivedTime * 0.5) bonus points.
    /// survivedTime = roundTimerMax − timer-value-when-they-left-or-round-ended.
    /// </summary>
    private void GiveDodgerSurvivedBonus(float timerAtRoundEnd)
    {
        foreach (var kv in registeredPlayers)
        {
            ulong id = kv.Key;
            if (originalThrowers.Contains(id)) continue; // only dodgers

            // Find when this dodger was eliminated; if not in eliminationOrder they survived to the end
            float timerWhenOut = timerAtRoundEnd;
            foreach (var (elimId, elimTimer) in eliminationOrder)
            {
                if (elimId == id) { timerWhenOut = elimTimer; break; }
            }

            float survivedTime = roundTimerMax - timerWhenOut;
            int bonus = Mathf.FloorToInt(survivedTime * 0.5f);
            if (bonus > 0) AddScore(id, bonus);
        }
    }

    private IEnumerator DelayedRoundEnd(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (currentRound.Value >= totalRounds.Value)
        {
            // Game over
            gameState.Value = GameState.GameEnd;
            ShowFinalLeaderboardClientRpc();
        }
        else
        {
            List<ulong> nextThrowers = PickNextThrowers();
            StartRound(nextThrowers);
        }
    }

    /// <summary>
    /// Pick 2 next throwers: survivors of original dodgers first (shuffled),
    /// then latest-eliminated original dodgers.
    /// </summary>
    private List<ulong> PickNextThrowers()
    {
        // Original dodgers = all registered players who were NOT original throwers
        HashSet<ulong> originalDodgers = new HashSet<ulong>();
        foreach (var kv in registeredPlayers)
            if (!originalThrowers.Contains(kv.Key)) originalDodgers.Add(kv.Key);

        // Survivors: original dodgers who are still dodgers (never eliminated this round)
        List<ulong> survivors = new List<ulong>();
        HashSet<ulong> eliminatedThisRound = new HashSet<ulong>();
        foreach (var e in eliminationOrder) eliminatedThisRound.Add(e.clientId);

        foreach (ulong id in originalDodgers)
            if (!eliminatedThisRound.Contains(id)) survivors.Add(id);

        // Shuffle survivors
        for (int i = survivors.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            ulong tmp = survivors[i]; survivors[i] = survivors[j]; survivors[j] = tmp;
        }

        List<ulong> result = new List<ulong>(survivors);

        // Fill from latest-eliminated original dodgers if needed
        if (result.Count < 2)
        {
            // eliminationOrder is oldest-first; we want latest-first
            for (int i = eliminationOrder.Count - 1; i >= 0 && result.Count < 2; i--)
            {
                ulong id = eliminationOrder[i].clientId;
                if (originalDodgers.Contains(id) && !result.Contains(id))
                    result.Add(id);
            }
        }

        // If still not enough, fall back to random
        if (result.Count < 2)
        {
            List<ulong> allIds = new List<ulong>(registeredPlayers.Keys);
            foreach (ulong id in allIds)
                if (!result.Contains(id) && result.Count < 2)
                    result.Add(id);
        }

        return result;
    }

    // ===== Scoring =====

    private void AddScore(ulong clientId, float amount)
    {
        if (!IsServer) return;
        for (int i = 0; i < playerScores.Count; i++)
        {
            if (playerScores[i].ClientId == clientId)
            {
                var data = playerScores[i];
                data.Score += amount;
                playerScores[i] = data;
                return;
            }
        }
    }

    // ===== Name Submission =====

    [ServerRpc(RequireOwnership = false)]
    public void SubmitNameServerRpc(string name, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        for (int i = 0; i < playerScores.Count; i++)
        {
            if (playerScores[i].ClientId == senderId)
            {
                var data = playerScores[i];
                // Truncate to FixedString64Bytes limit (63 chars safe)
                if (name.Length > 60) name = name.Substring(0, 60);
                data.Name = new FixedString64Bytes(name);
                playerScores[i] = data;
                return;
            }
        }
    }

    // ===== Client RPCs =====

    [ClientRpc]
    private void ShowRoundResultClientRpc(string message)
    {
        if (roundInfoText != null)
            roundInfoText.text = message;
    }

    [ClientRpc]
    private void ShowFinalLeaderboardClientRpc()
    {
        if (roundInfoText != null)
            roundInfoText.text = "GAME OVER\n" + BuildLeaderboardString();
    }

    // ===== UI Callbacks =====

    private void OnTimerChanged(float previous, float current)
    {
        if (timerText != null)
        {
            int seconds = Mathf.CeilToInt(current);
            timerText.text = $"Time: {seconds}s";
        }
    }

    private void OnPlayerScoresChanged(NetworkListEvent<PlayerScoreData> changeEvent)
    {
        UpdateLeaderboardUI();
        if (IsServer) UpdateStartButton();
    }

    private void OnGameStateChanged(GameState previous, GameState current)
    {
        if (IsServer) UpdateStartButton();
    }

    private void UpdateLeaderboardUI()
    {
        if (leaderboardText == null) return;
        leaderboardText.text = BuildLeaderboardString();
    }

    private string BuildLeaderboardString()
    {
        // Deduplicate by ClientId (guards against any double-registration edge cases)
        var seen = new HashSet<ulong>();
        var sorted = new List<PlayerScoreData>();
        foreach (var entry in playerScores)
            if (seen.Add(entry.ClientId))
                sorted.Add(entry);

        sorted.Sort((a, b) => b.Score.CompareTo(a.Score));

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Leaderboard ===");
        for (int i = 0; i < sorted.Count; i++)
            sb.AppendLine($"{i + 1}. {sorted[i].Name} — {sorted[i].Score:F0}");
        return sb.ToString();
    }

    private void OnNameSubmitted(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        SubmitNameServerRpc(value);
    }
}
