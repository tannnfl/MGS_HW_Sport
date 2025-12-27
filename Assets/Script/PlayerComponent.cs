using Unity.Netcode;
using UnityEngine;
using TMPro;

public class PlayerComponent : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float speed = 5f;

    [Header("References")]
    [SerializeField] private Transform holdBallPos; // 拖到 Inspector
    public TextMeshProUGUI infoPanel;

    [Header("Gameplay State")]
    public bool isThrower = false;
    private bool hasBall = false;

    private GameObject ball;

    // ===== Netcode Lifecycle =====

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Server 设置角色身份和出生点
            print(OwnerClientId);
            isThrower = OwnerClientId < 2;
            Transform spawnPos = GameManager.Instance.GetSpawnPosition(isThrower);
            transform.position = spawnPos.position;
            transform.rotation = spawnPos.rotation;
        }

        if (IsOwner)
        {
            GameManager.Instance.RegisterPlayer(this);
            //UpdateInfoPanel();
            var camFollow = Camera.main.GetComponent<CameraFollow>();
            ball = GameObject.FindWithTag("Ball"); // 假设你只存在一个 Ball

            if (camFollow != null && ball != null)
            {
                camFollow.SetTargets(transform, ball.transform);
            }
        }
    }

    // ===== Unity Lifecycle =====

    private void Update()
    {
        UpdateInfoClientRpc();
        if (!IsOwner) return;

        //UpdateInfoClientRpc();
        HandleMovement();
        FaceMouse();

        if (hasBall && Input.GetMouseButtonDown(0)) // 左键投掷
        {
            RequestThrowBallServerRpc();
        }
    }


    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!IsOwner || !isThrower || hasBall) return;

        if (collision.gameObject.CompareTag("Ball"))
        {
            Debug.Log("Ball collided!");

            var ball = collision.gameObject.GetComponent<BallComponent>();
            Debug.Log("Ball holderId: " + ball.holderId.Value);
            if (ball != null && ball.holderId.Value == ulong.MaxValue)
            {
                Debug.Log("Trying to pick up ball...");
                TryPickupBallServerRpc(ball.NetworkObject);
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

    [ServerRpc]
    private void TryPickupBallServerRpc(NetworkObjectReference ballRef)
    {
        if (!isThrower || hasBall) return;

        if (ballRef.TryGet(out NetworkObject ballObj))
        {
            var ball = ballObj.GetComponent<BallComponent>();
            if (ball != null && ball.holderId.Value != ulong.MaxValue && ball.holderId.Value != OwnerClientId)// this is a double check that's actually repetitive, i just realized
            {
                hasBall = true;
                ball.SetHeldBy(OwnerClientId, holdBallPos);
                UpdateInfoClientRpc();
            }
        }
    }

    [ServerRpc]
    private void RequestThrowBallServerRpc()
    {
        //if (!hasBall || heldBall == null) return;

        Vector2 throwDir = transform.right.normalized;
        Vector2 throwForce = throwDir * 10f;

        ball.GetComponent<BallComponent>().DropAndThrow(throwForce);

        hasBall = false;

        UpdateInfoClientRpc();
    }

    // ===== Client RPCs =====

    [ClientRpc]
    private void UpdateInfoClientRpc()
    {
        //if (!IsOwner) return;

        if (infoPanel == null) return;
        infoPanel.text = (isThrower ? "Thrower" : "Dodger") +
                         (hasBall ? " (Has Ball)" : "") +
                         (ball.GetComponent<BallComponent>().holderId.Value);
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
