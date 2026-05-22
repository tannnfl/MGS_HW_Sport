using UnityEngine;
using Unity.Netcode;
using TMPro;

public class PlayerComponent : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float speed = 5f;

    // Found at runtime — prefabs cannot hold scene object references
    private Transform throwerZone;
    private Transform dodgerZone;

    [Header("UI")]
    [SerializeField] private TextMeshPro localInfoText; // world-space TMP, no Canvas needed

    // Bug fix #7: isThrower and hasBall must be NetworkVariables, not plain bools
    // Bug fix #8 (role assignment): roles are now assigned by GameManager randomly
    public NetworkVariable<bool> isThrower = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> hasBall = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Bug fix #4: ball cached on ALL instances, not just owner
    private BallComponent ball;
    private SpriteRenderer spriteRenderer;

    // ===== Netcode Lifecycle =====

    public override void OnNetworkSpawn()
    {
        // Cache ball on ALL instances (not just owner) — bug fix #4
        var ballObj = GameObject.FindWithTag("Ball");
        if (ballObj != null)
            ball = ballObj.GetComponent<BallComponent>();

        // Find zone boundaries in the scene (can't be stored on prefab)
        var throwerRegionGO = GameObject.Find("ThrowerRegion");
        var dodgerRegionGO  = GameObject.Find("DodgerRegion");
        if (throwerRegionGO != null) throwerZone = throwerRegionGO.transform;
        if (dodgerRegionGO  != null) dodgerZone  = dodgerRegionGO.transform;

        // Cache sprite renderer (exists on every instance for color updates)
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        Debug.Log($"[PlayerSpawn] ClientId={OwnerClientId} IsOwner={IsOwner} IsServer={IsServer} isThrower={isThrower.Value}");

        if (IsServer)
        {
            GameManager.Instance?.RegisterPlayer(this);
        }

        // All instances subscribe to isThrower so every client sees the right colour
        isThrower.OnValueChanged += OnRoleChanged;
        UpdatePlayerColor();

        if (IsOwner)
        {
            var camFollow = Camera.main?.GetComponent<CameraFollow>();
            if (camFollow != null && ballObj != null)
                camFollow.SetTargets(transform, ballObj.transform);

            hasBall.OnValueChanged += OnHasBallChanged;
            UpdateLocalInfoText();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            GameManager.Instance?.UnregisterPlayer(OwnerClientId);
        }

        isThrower.OnValueChanged -= OnRoleChanged;

        if (IsOwner)
        {
            hasBall.OnValueChanged -= OnHasBallChanged;
        }
    }

    // ===== UI / Visual Callbacks =====

    private void OnRoleChanged(bool previous, bool current)
    {
        UpdatePlayerColor();
        if (IsOwner) UpdateLocalInfoText();
    }

    private void UpdatePlayerColor()
    {
        if (spriteRenderer == null) return;
        spriteRenderer.color = isThrower.Value ? new Color(1f, 0.5f, 0f) : Color.blue;
    }

    private void OnHasBallChanged(bool previous, bool current)
    {
        if (!current)
        {
            pickupRequested = false;
            pickupCooldown = PickupCooldownDuration; // prevent instant re-pickup after throw
        }
        UpdateLocalInfoText();
    }

    private void UpdateLocalInfoText()
    {
        if (localInfoText == null) return;
        string role = isThrower.Value ? "Thrower" : "Dodger";
        string ballStatus = hasBall.Value ? " (Has Ball)" : "";
        localInfoText.text = role + ballStatus;
    }

    // Prevent spamming pickup RPCs while waiting for server confirmation
    private bool pickupRequested;
    // Brief cooldown after throwing so the ball can travel past pickup radius first
    private float pickupCooldown;
    private const float PickupCooldownDuration = 0.4f;

    private const float PickupRadius = 0.8f;

    // ===== Unity Lifecycle =====

    private void Update()
    {
        if (!IsOwner) return;

        HandleMovement();
        FaceMouse();

        if (hasBall.Value && Input.GetMouseButtonDown(0) && IsInThrowerZone())
        {
            RequestThrowBallServerRpc();
        }

        // Proximity-based pickup: ball physics is disabled on clients (rb.simulated = false)
        // so OnCollisionEnter2D never fires for the ball. Check distance instead.
        if (pickupCooldown > 0f) pickupCooldown -= Time.deltaTime;

        if (isThrower.Value && !hasBall.Value && !pickupRequested && pickupCooldown <= 0f)
        {
            CheckBallProximity();
        }
    }

    private void CheckBallProximity()
    {
        if (ball == null)
        {
            var ballObj = GameObject.FindWithTag("Ball");
            if (ballObj != null) ball = ballObj.GetComponent<BallComponent>();
        }
        if (ball == null)
        {
            Debug.LogWarning($"[Pickup] Client {NetworkManager.Singleton.LocalClientId}: ball not found in scene");
            return;
        }
        if (ball.holderId.Value != ulong.MaxValue)
        {
            // Ball is held — don't log every frame, just skip silently
            return;
        }

        float dist = Vector2.Distance(transform.position, ball.transform.position);
        Debug.Log($"[Pickup] Client {NetworkManager.Singleton.LocalClientId}: dist={dist:F2} (need <{PickupRadius}), holderId={ball.holderId.Value}");

        if (dist < PickupRadius)
        {
            Debug.Log($"[Pickup] Client {NetworkManager.Singleton.LocalClientId}: close enough — sending TryPickupBallServerRpc");
            pickupRequested = true;
            TryPickupBallServerRpc(ball.NetworkObject);
        }
    }

    // ===== Movement & Facing =====

    private void HandleMovement()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 movement = new Vector3(h, v, 0f) * speed * Time.deltaTime;
        transform.position += movement;
        EnforceZoneBoundary();
    }

    private void EnforceZoneBoundary()
    {
        Vector2 pos = transform.position;

        if (isThrower.Value)
        {
            // Throwers: free to roam anywhere, including the dodger zone.
            // Only clamp to the outer arena wall.
            if (throwerZone != null)
            {
                Vector2 center = throwerZone.position;
                float radius   = throwerZone.lossyScale.x * 0.5f;
                Vector2 offset = pos - center;
                if (offset.magnitude > radius)
                    pos = center + offset.normalized * radius;
            }
        }
        else
        {
            // Dodgers: confined to the inner circle.
            if (dodgerZone != null)
            {
                Vector2 center = dodgerZone.position;
                float radius   = dodgerZone.lossyScale.x * 0.5f;
                Vector2 offset = pos - center;
                if (offset.magnitude > radius)
                    pos = center + offset.normalized * radius;
            }
        }

        transform.position = new Vector3(pos.x, pos.y, transform.position.z);
    }

    private void FaceMouse()
    {
        if (Camera.main == null) return;
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0f;
        Vector3 direction = mouseWorldPos - transform.position;
        if (direction.magnitude < 0.1f) return;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    // Returns true when the player is outside the dodger zone (i.e. in the thrower ring).
    // If zone data is missing, allow throwing to avoid blocking the player entirely.
    private bool IsInThrowerZone()
    {
        if (dodgerZone == null) return true;
        float radius = dodgerZone.lossyScale.x * 0.5f;
        return Vector2.Distance(transform.position, dodgerZone.position) >= radius;
    }

    // ===== Server Methods =====

    /// <summary>
    /// Called by GameManager on the server to assign a role and teleport the player.
    /// NetworkVariables are set server-side; teleport is sent directly to the owning
    /// client via ClientRpc because OwnerNetworkTransform is owner-authoritative
    /// (server-side transform writes get overridden by the client).
    /// </summary>
    public void SetRoleAndSpawn(bool isThrowerRole, Vector3 pos, Quaternion rot)
    {
        if (!IsServer) return;
        Debug.Log($"[RoleAssign] Assigning client {OwnerClientId} → isThrower={isThrowerRole}, pos={pos}");
        isThrower.Value = isThrowerRole;
        hasBall.Value = false;

        // Target only the owning client so they teleport their own character
        ClientRpcParams rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        TeleportOwnerClientRpc(pos, rot, rpcParams);
    }

    [ClientRpc]
    private void TeleportOwnerClientRpc(Vector3 pos, Quaternion rot, ClientRpcParams rpcParams = default)
    {
        // Only the targeted owner runs this; they set their own transform
        transform.position = pos;
        transform.rotation = rot;
    }

    // ===== Server RPCs =====

    [ServerRpc]
    private void TryPickupBallServerRpc(NetworkObjectReference ballRef)
    {
        Debug.Log($"[PickupRPC] Server received pickup request from client {OwnerClientId}: isThrower={isThrower.Value}, hasBall={hasBall.Value}");

        if (!isThrower.Value)  { Debug.LogWarning($"[PickupRPC] Rejected: client {OwnerClientId} is not a thrower"); return; }
        if (hasBall.Value)     { Debug.LogWarning($"[PickupRPC] Rejected: client {OwnerClientId} already has ball"); return; }

        BallComponent ballComp = ball;
        if (ballComp == null)
        {
            var ballObj = GameObject.FindWithTag("Ball");
            if (ballObj != null) ballComp = ballObj.GetComponent<BallComponent>();
        }
        if (ballRef.TryGet(out NetworkObject ballNetObj))
        {
            var refBall = ballNetObj.GetComponent<BallComponent>();
            if (refBall != null) ballComp = refBall;
        }

        if (ballComp == null) { Debug.LogWarning($"[PickupRPC] Rejected: ball component not found"); return; }

        Debug.Log($"[PickupRPC] Ball holderId={ballComp.holderId.Value} (MaxValue={ulong.MaxValue} means free)");
        if (ballComp.holderId.Value != ulong.MaxValue) { Debug.LogWarning($"[PickupRPC] Rejected: ball is already held by {ballComp.holderId.Value}"); return; }

        Debug.Log($"[PickupRPC] Pickup SUCCESS for client {OwnerClientId}");
        hasBall.Value = true;
        ballComp.SetHeldBy(OwnerClientId, NetworkObject);
    }

    [ServerRpc]
    private void RequestThrowBallServerRpc()
    {
        if (!hasBall.Value) return;

        // Bug fix #4: find ball with fallback — server may not have ball cached
        BallComponent ballComp = ball;
        if (ballComp == null)
        {
            var ballObj = GameObject.FindWithTag("Ball");
            if (ballObj != null)
                ballComp = ballObj.GetComponent<BallComponent>();
        }

        if (ballComp == null) return;

        Vector2 throwDir = transform.right.normalized;
        ballComp.DropAndThrow(throwDir * 10f);

        hasBall.Value = false;
    }
}
