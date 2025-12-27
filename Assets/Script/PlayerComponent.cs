using Unity.Netcode;
using UnityEngine;
using TMPro;

public class PlayerComponent : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float speed = 5f;

    [Header("References")]
    [SerializeField] public Transform holdBallPos; // 拖到 Inspector
    public TextMeshProUGUI infoPanel;

    [Header("Gameplay State")]
    public NetworkVariable<bool> isThrower;

    private GameObject ball;

    // ===== Netcode Lifecycle =====

    public override void OnNetworkSpawn()
    {
        infoPanel = GameObject.Find("PlayerInfoPanel").GetComponent<TextMeshProUGUI>();
        if (IsServer)
        {
            // Server 设置角色身份和出生点
            
            
        }

        if (IsOwner)
        {
            print("Player" + OwnerClientId + " Spawned");
            GameManager.Instance.RegisterPlayer(this);
            //UpdateInfoPanel();
            var camFollow = Camera.main.GetComponent<CameraFollow>();
            ball = GameObject.FindWithTag("Ball"); // 假设你只存在一个 Ball

            if (camFollow != null && ball != null)
            {
                camFollow.SetTargets(transform, ball.transform);
            }
            isThrower.Value = OwnerClientId < 2;
            Transform spawnPos = GameManager.Instance.GetSpawnPosition(isThrower.Value);
            transform.position = spawnPos.position;
            transform.rotation = spawnPos.rotation;
            //if (!isThrower) GetComponent<SpriteRenderer>().color = new Color(0.2f, 0.5f, 0.35f, 0);
        }
    }

    // ===== Unity Lifecycle =====

    private void Update()
    {
        if (!IsOwner) return;

        UpdateInfoClientRpc();
        HandleMovement();
        FaceMouse();

        var ballComponent = ball.GetComponent<BallComponent>();
        if (ballComponent.holderId.Value != ulong.MaxValue && ballComponent.holderId.Value == NetworkManager.Singleton.LocalClientId)
        {
            if (Input.GetMouseButtonDown(0)) // 左键投掷
            {
                RequestThrowBallServerRpc();
            }
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!IsOwner || !isThrower.Value) return;

        if (collision.gameObject.CompareTag("Ball"))
        {
            Debug.Log("Ball collided!");

            var ballComponent = collision.gameObject.GetComponent<BallComponent>();
            Debug.Log("Ball holderId: " + ballComponent.holderId.Value);
			if(ballComponent == null) Debug.Log("BallComponent is null");
            if (ballComponent.holderId.Value != ulong.MaxValue)
                Debug.Log("Ball is holded by" + ballComponent.holderId.Value);
            if (ballComponent != null && ballComponent.holderId.Value == ulong.MaxValue)
            {
                Debug.Log("Trying to pick up ball...");
                TryPickupBallServerRpc(ballComponent.NetworkObject);
            }
        }
    }

    // ===== Movement & Facing =====

    private void HandleMovement()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 movement = new Vector3(h, v, 0) * speed * Time.deltaTime;
        transform.position += movement;
    }

    private void FaceMouse()
    {
        Vector3 mouseScreenPos = Input.mousePosition;
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);
        mouseWorldPos.z = 0;

        Vector3 direction = mouseWorldPos - transform.position;

        // ✅ 阈值限制，避免小抖动导致转向
        if (direction.magnitude < 0.1f) return;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    // ===== Server RPCs =====

    [ServerRpc(RequireOwnership = false)]
    private void TryPickupBallServerRpc(NetworkObjectReference ballRef)
    {
        Debug.Log($"[Server] Player {OwnerClientId} tried to pick up ball");

        //if (!isThrower) return;

        if (ballRef.TryGet(out NetworkObject ballObj))
        {
            Debug.Log($"[Server] Player {OwnerClientId} picking condition 111 passed");
            var ball = ballObj.GetComponent<BallComponent>();
            if (ball != null && ball.holderId.Value == ulong.MaxValue)
            {
                Debug.Log($"[Server] Player {OwnerClientId} picking condition 222 passed");
                ball.PickUp(OwnerClientId);

                Debug.Log($"[Server] Player {OwnerClientId} picked up ball");
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestThrowBallServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong requestingClientId = rpcParams.Receive.SenderClientId;
        GameObject ballObj = GameObject.FindWithTag("Ball"); // 确保球的 tag 设置为 "Ball"
        if (ballObj == null) return;
        var ballComponent = ballObj.GetComponent<BallComponent>();

        if (ballComponent.holderId.Value == ulong.MaxValue || ballComponent.holderId.Value != requestingClientId)
            return;

        Vector2 throwDir = transform.right.normalized;
        ballComponent.DropAndThrow(throwDir * 10f);
    }

    // ===== Client RPCs =====

    [ClientRpc]
    private void UpdateInfoClientRpc()
    {
        Debug.Log("UpdateInfoClient：" + infoPanel);
        if (infoPanel == null) return;

        //var ballComponent = ball.GetComponent<BallComponent>();

        //bool hasBall = ballComponent.holderId.Value != ulong.MaxValue && ballComponent.holderId.Value == NetworkManager.Singleton.LocalClientId;

        infoPanel.text = (isThrower.Value ? "Thrower" : "Dodger");
    }

    /*
    public void LoseAndBecomeDodger()
    {
        if (!isThrower) return; // 已经是 thrower 就不重复转换

        isThrower = false;

        // 移动到 thrower 区域
        Transform spawnPos = GameManager.Instance.GetSpawnPosition(true);
        transform.position = spawnPos.position;
        transform.rotation = spawnPos.rotation;

        // 更新客户端 UI
        UpdateInfoClientRpc();
    }
    */
}
