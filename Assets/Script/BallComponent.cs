using Unity.Netcode;
using UnityEngine;

public class BallComponent : NetworkBehaviour
{
    [Header("Physics Settings")]
    public float friction = 3f;
    public float bounceDamping = 0.6f;

    /*public NetworkVariable<bool> isHeld = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
*/
    
    public NetworkVariable<ulong> holderId = new NetworkVariable<ulong>(
        writePerm: NetworkVariableWritePermission.Server);


    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        if (IsServer)
        {
            if (holderId.Value != ulong.MaxValue && NetworkManager.Singleton.ConnectedClients.ContainsKey(holderId.Value))
            {
                var playerObj = NetworkManager.Singleton.ConnectedClients[holderId.Value].PlayerObject;
                if (playerObj != null)
                {
                    // ✅ 跟随持有者的位置
                    transform.position = playerObj.GetComponent<PlayerComponent>().holdBallPos.position;
                    rb.linearVelocity = Vector2.zero;
                    rb.angularVelocity = 0;
                }
            }
            else
            {
                // 模拟摩擦：每帧减速
                rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, Vector2.zero, friction * Time.deltaTime);
            }
        }
    }

    public void PickUp(ulong clientId)
    {
        holderId.Value = clientId;
        //isHeld.Value = true;

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0;
        rb.bodyType = RigidbodyType2D.Kinematic;

        Debug.Log($"[Server] Ball now held by client {clientId}");
    }

    public void DropAndThrow(Vector2 velocity)
    {
        holderId.Value = ulong.MaxValue;
        //isHeld.Value = false;

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.AddForce(velocity, ForceMode2D.Impulse);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!IsServer || holderId.Value == ulong.MaxValue) return;

        // 反弹逻辑（只在 server 上）
        if (collision.collider.gameObject.layer == LayerMask.NameToLayer("OutsideWall"))
        {
            Vector2 reflect = Vector2.Reflect(rb.linearVelocity, collision.contacts[0].normal);
            rb.linearVelocity = reflect * bounceDamping;
        }
    }
}
