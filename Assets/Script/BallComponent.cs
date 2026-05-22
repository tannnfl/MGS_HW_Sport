using UnityEngine;
using Unity.Netcode;

public class BallComponent : NetworkBehaviour
{
    [Header("Physics Settings")]
    public float bounceDamping = 0.85f;

    // Bug fix #1: default = ulong.MaxValue set in constructor, NOT in Awake
    // Bug fix #10: removed unused followTarget field
    public NetworkVariable<ulong> holderId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Stores the root NetworkObject of the player currently holding the ball
    private NetworkObject holderNetObj;
    private Rigidbody2D rb;

    // Cached at runtime for dodger-zone eviction
    private Transform dodgerZone;
    private Vector3 ballRescuePos = new Vector3(-5f, 0f, 0f); // outside DodgerRegion

    private float timeInDodgerZone;
    private const float DodgerZoneEvictionDelay = 2f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        // Do NOT set holderId.Value here — NetworkVariable with ServerWrite
        // cannot be written on all clients. Default is set in the constructor above.
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        Debug.Log($"[Ball] OnNetworkSpawn — IsServer={IsServer}, IsOwner={IsOwner}, rb.simulated={rb.simulated}");

        if (!IsServer)
        {
            rb.simulated = false;
            Debug.Log($"[Ball] Client instance: rb.simulated set to FALSE (server is authoritative)");
            return;
        }

        var dodgerRegionGO = GameObject.Find("DodgerRegion");
        if (dodgerRegionGO != null) dodgerZone = dodgerRegionGO.transform;

        var spawnGO = GameObject.Find("BallSpawnPos");
        if (spawnGO != null) ballRescuePos = spawnGO.transform.position;
    }

    private void Update()
    {
        if (!IsServer) return;

        // Position ball in front of holder each frame when held
        if (holderId.Value != ulong.MaxValue && holderNetObj != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            transform.position = holderNetObj.transform.position
                                 + holderNetObj.transform.right * 1.0f;
            timeInDodgerZone = 0f; // reset timer while held
            return;
        }

        // Dodger-zone rule: once the ball comes to a natural stop inside the zone,
        // wait 2 s then move it to the spawn point. Never zero velocity by hand.
        if (dodgerZone != null)
        {
            float dodgerRadius = dodgerZone.lossyScale.x * 0.5f;
            float dist = Vector2.Distance(transform.position, dodgerZone.position);
            bool insideZone = dist < dodgerRadius;
            bool almostStopped = rb.linearVelocity.magnitude < 0.05f;

            if (insideZone && almostStopped)
            {
                timeInDodgerZone += Time.deltaTime;
                if (timeInDodgerZone >= DodgerZoneEvictionDelay)
                {
                    timeInDodgerZone = 0f;
                    transform.position = ballRescuePos;
                }
            }
            else
            {
                timeInDodgerZone = 0f; // still moving or outside zone — reset timer
            }
        }
    }

    /// <summary>
    /// Server-only: attach this ball to the given player.
    /// Bug fix #3: takes the player's root NetworkObject, not a child Transform.
    /// </summary>
    public void SetHeldBy(ulong clientId, NetworkObject playerNetObj)
    {
        if (!IsServer) return;

        holderId.Value = clientId;
        holderNetObj = playerNetObj;

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
    }

    /// <summary>
    /// Server-only: release ball and apply throw velocity.
    /// </summary>
    public void DropAndThrow(Vector2 velocity)
    {
        if (!IsServer) return;

        holderId.Value = ulong.MaxValue;
        holderNetObj = null;

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.linearVelocity = velocity;
    }

    /// <summary>
    /// Server-only: reset ball position for round start.
    /// </summary>
    public void ResetBall(Vector3 position)
    {
        if (!IsServer) return;

        holderId.Value = ulong.MaxValue;
        holderNetObj = null;

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        transform.position = position;

        ballRescuePos = position;
        timeInDodgerZone = 0f;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!IsServer) return;

        // Bug fix #5: only process collisions when ball is FREE (not held)
        if (holderId.Value != ulong.MaxValue) return;

        // Reflect off outside walls
        int outsideWallLayer = LayerMask.NameToLayer("OutsideWall");
        if (outsideWallLayer >= 0 && collision.gameObject.layer == outsideWallLayer)
        {
            Vector2 reflected = Vector2.Reflect(rb.linearVelocity, collision.contacts[0].normal);
            rb.linearVelocity = reflected * bounceDamping;
            return;
        }

        // Check if we hit a Dodger with sufficient velocity.
        // Use collision.relativeVelocity (velocity at moment of contact, BEFORE Unity
        // resolves the impulse) rather than rb.linearVelocity (post-impulse, already
        // reduced by the bounce) so slow-ish hits aren't missed.
        PlayerComponent player = collision.gameObject.GetComponent<PlayerComponent>();
        if (player == null)
            player = collision.gameObject.GetComponentInParent<PlayerComponent>();

        if (player != null && !player.isThrower.Value)
        {
            float impactSpeed = collision.relativeVelocity.magnitude;
            Debug.Log($"[Hit] Ball hit client {player.OwnerClientId} isThrower={player.isThrower.Value} impactSpeed={impactSpeed:F2}");
            if (impactSpeed >= 0.5f)
                GameManager.Instance?.EliminatePlayer(player.OwnerClientId);
        }
    }
}
