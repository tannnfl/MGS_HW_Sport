using UnityEngine;
using Unity.Netcode;

public class CameraFollow : MonoBehaviour
{
    private Transform playerTransform;
    private Transform ballTransform;

    [Header("Position Settings")]
    [SerializeField] private float smoothSpeed = 5f;
    [SerializeField] private Vector3 offset = new Vector3(0, 0, -10f);

    [Header("Zoom Settings")]
    [SerializeField] private float minZoom = 5f;  // 最靠近
    [SerializeField] private float maxZoom = 12f; // 最远
    [SerializeField] private float zoomLimiter = 10f; // 控制缩放灵敏度
    [SerializeField] private float zoomSpeed = 5f;

    private Camera cam;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    public void SetTargets(Transform player, Transform ball)
    {
        playerTransform = player;
        ballTransform = ball;
    }

    private void LateUpdate()
    {
        if (playerTransform == null || ballTransform == null) return;

        // 中心点跟随
        Vector3 center = (playerTransform.position + ballTransform.position) / 2f;
        Vector3 desiredPosition = center + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

        // 缩放调整
        float distance = Vector3.Distance(playerTransform.position, ballTransform.position);
        float desiredZoom = Mathf.Clamp(distance / zoomLimiter, minZoom, maxZoom);
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, desiredZoom, zoomSpeed * Time.deltaTime);
    }
}