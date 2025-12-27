using Unity.Netcode;
using UnityEngine;

public class BallComponent : NetworkBehaviour
{
    [Header("Physics Settings")]
    public float friction = 3f;
    public float bounceDamping = 0.6f;

    //public NetworkVariable<bool> isHeld = new NetworkVariable<bool>(
    //    false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<ulong> holderId = new NetworkVariable<ulong>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<NetworkObjectReference> holderObject = new NetworkVariable<NetworkObjectReference>();

    private Transform followTarget = null;
    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        holderId.Value = ulong.MaxValue;
    }

    private void Update()
    {
        if (IsServer)
        {
            if (holderId.Value != ulong.MaxValue && holderObject.Value.TryGet(out var holderNetObj))
            {
                transform.position = holderNetObj.transform.position;
                rb.velocity = Vector2.zero;
                rb.angularVelocity = 0;
            }
            else
            {
                rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, Vector2.zero, friction * Time.deltaTime);
            }
        }
    }


    public void SetHeldBy(ulong newHolderId, Transform holderTransform)
    {
        holderId.Value = newHolderId;
        holderObject.Value = holderTransform.GetComponent<NetworkObject>();
    }


    public void DropAndThrow(Vector2 velocity)
    {
        holderId.Value = ulong.MaxValue;
        followTarget = null;
        rb.linearVelocity = velocity;
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