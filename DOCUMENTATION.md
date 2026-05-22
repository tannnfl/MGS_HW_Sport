# MGS_HW_Sport — Game Documentation

## Game Overview

This is a **2-player networked multiplayer dodgeball game** built with Unity and Unity Netcode for GameObjects (NGO).

- One player is the **Thrower**: picks up the ball and throws it at the other player.
- One player is the **Dodger**: tries to avoid the ball.
- Roles are assigned by the server at spawn based on `OwnerClientId` (clients 0 and 1 become Throwers; others become Dodgers).
- The camera follows the midpoint between the local player and the ball, zooming out as they get farther apart.

---

## Scripts

---

### `BallComponent.cs`

**What it does:**
Manages the ball's networked state — who is holding it, where it is, and how it moves when thrown.

**What it controls:**
- Tracks the ball's holder via two `NetworkVariable`s: `holderId` (the client ID of whoever holds it) and `holderObject` (a reference to that holder's `NetworkObject`).
- Every server `Update`, if the ball is held, its position is snapped to the holder's transform.
- Exposes `SetHeldBy()` for the server to assign the ball to a player.
- Exposes `DropAndThrow()` to release the ball and apply a throw velocity via `Rigidbody2D`.
- Has wall-bounce collision logic in `OnCollisionEnter2D` using Vector2.Reflect against the "OutsideWall" layer.

**Connections:**
- Called by `PlayerComponent` via `TryPickupBallServerRpc` → `ball.SetHeldBy(...)` and `RequestThrowBallServerRpc` → `ball.DropAndThrow(...)`.
- `holderObject` is set with the `holdBallPos` child transform from `PlayerComponent` (see known issues — this transform likely has no `NetworkObject`).

---

### `PlayerComponent.cs`

**What it does:**
Controls the local player's movement, aiming, ball pickup, and throwing, all via client-authoritative input sending ServerRpcs to validate actions on the server.

**What it controls:**
- **Movement** (`HandleMovement`): reads WASD/arrow keys and directly moves `transform.position`.
- **Facing** (`FaceMouse`): rotates the player toward the mouse cursor each frame.
- **Ball pickup** (`OnCollisionEnter2D` → `TryPickupBallServerRpc`): on collision with the ball, if the local player is the Thrower and the ball is free, requests the server to assign ball ownership.
- **Throwing** (`RequestThrowBallServerRpc`): on left mouse click, requests the server to release and throw the ball in the direction the player is facing.
- **UI** (`UpdateInfoClientRpc`): broadcasts the current role and ball state to all clients to update the info panel.
- **Camera setup** (`OnNetworkSpawn`): if this is the local owner, finds the ball by tag and calls `CameraFollow.SetTargets()`.

**Connections:**
- Calls `GameManager.Instance.RegisterPlayer(this)` on spawn to receive the shared UI panel reference.
- Calls `CameraFollow.SetTargets(transform, ball.transform)` to hook up the camera.
- Calls `BallComponent.SetHeldBy()` and `BallComponent.DropAndThrow()` on the server.
- Role assignment happens in `OnNetworkSpawn` on the server: `isThrower = OwnerClientId < 2`.

---

### `GameManager.cs`

**What it does:**
Singleton manager that provides spawn point references and a shared UI panel to player scripts.

**What it controls:**
- Holds `throwerSpawnPos` and `dodgerSpawnPos` transforms, returned to `PlayerComponent.OnNetworkSpawn` via `GetSpawnPosition(bool isThrower)`.
- Holds the shared `infoPanel` TextMeshProUGUI reference and assigns it to each registered player via `RegisterPlayer(PlayerComponent player)`.

**Connections:**
- Accessed by `PlayerComponent` via `GameManager.Instance` (singleton).
- Spawns no objects itself; player spawning is handled by NGO's NetworkManager (not in these scripts).

---

### `CameraFollow.cs`

**What it does:**
Smoothly follows the midpoint between the local player and the ball, and dynamically zooms an orthographic camera based on their separation distance.

**What it controls:**
- `playerTransform` and `ballTransform` — set externally via `SetTargets()`.
- Every `LateUpdate`: lerps camera position toward the midpoint of player + ball, then lerps `orthographicSize` toward a value proportional to their distance (clamped between `minZoom` and `maxZoom`).

**Connections:**
- `SetTargets()` is called from `PlayerComponent.OnNetworkSpawn` (owner only).
- Reads `Camera.main` for the component reference inside `PlayerComponent`; the camera itself must have this script attached.

---

## Known Issues

### Critical Bugs

1. **`BallComponent.Awake()` writes NetworkVariable on all clients (line 23)**
   `holderId.Value = ulong.MaxValue` is called in `Awake()`, which runs on every client. Writing to a `NetworkVariable` with `NetworkVariableWritePermission.Server` from a non-server instance throws a runtime exception. This should be guarded with `if (IsServer)` — but `IsServer` is not reliable in `Awake`. The default value should be set in the `NetworkVariable` constructor instead: `new NetworkVariable<ulong>(ulong.MaxValue, ...)`.

2. **`TryPickupBallServerRpc` condition is inverted (line 122)**
   The pickup check reads:
   ```csharp
   if (ball != null && ball.holderId.Value != ulong.MaxValue && ball.holderId.Value != OwnerClientId)
   ```
   `ulong.MaxValue` is the "not held" sentinel. The condition `!= ulong.MaxValue` means it only attempts pickup when someone **else** is already holding the ball. A player can never pick up a free ball. It should be:
   ```csharp
   if (ball != null && ball.holderId.Value == ulong.MaxValue)
   ```

3. **`holdBallPos` has no `NetworkObject`, so `holderObject` is always null/default (BallComponent line 49 / PlayerComponent line 125)**
   `ball.SetHeldBy(OwnerClientId, holdBallPos)` passes `holdBallPos` (a plain child Transform) to `BallComponent.SetHeldBy`, which calls `holderTransform.GetComponent<NetworkObject>()`. A child transform helper object will not have a `NetworkObject`. The result is `holderObject.Value` is set to an empty/invalid reference, so `holderObject.Value.TryGet(...)` in `BallComponent.Update` never succeeds, and the ball never follows the player. The `holdBallPos` logic should either be replaced with the player's own `NetworkObject` (and offset calculated separately), or the ball's position offset should be handled differently.

4. **`RequestThrowBallServerRpc` uses a null `ball` reference on the server (PlayerComponent line 139)**
   `ball` is only assigned inside the `if (IsOwner)` block in `OnNetworkSpawn`. When a non-host client calls `RequestThrowBallServerRpc`, it runs on the server where `IsOwner` is false for that player object — so `ball` is null, causing a `NullReferenceException`. The server needs its own way to find the ball (e.g., via `GameObject.FindWithTag` at start, or passed as a `NetworkObjectReference` in the RPC).

5. **`OnCollisionEnter2D` bounce guard is inverted (BallComponent line 62)**
   ```csharp
   if (!IsServer || holderId.Value == ulong.MaxValue) return;
   ```
   This returns early (skips bounce logic) when `holderId == ulong.MaxValue`, meaning bounce only runs when the ball **is** held. When held, the ball's position is overridden every frame anyway, making bounce logic meaningless. Bounce should apply when the ball is **free** (`holderId.Value == ulong.MaxValue`). The condition should use `!=`.

---

### Logic Errors & Design Problems

6. **`UpdateInfoClientRpc()` called every frame in `Update()` (PlayerComponent line 52)**
   This broadcasts a ClientRpc to all clients on every single frame, regardless of whether anything changed. ClientRpcs have significant networking overhead. This should only be called when state actually changes (after pickup or throw), not in `Update`. The calls in `TryPickupBallServerRpc` and `RequestThrowBallServerRpc` are correct; the one in `Update` should be removed.

7. **`isThrower` and `hasBall` are not NetworkVariables (PlayerComponent lines 15–16)**
   Both are plain C# booleans, not synchronized across the network. The server sets `isThrower` in `OnNetworkSpawn`, but clients (other than the host) never receive this value. Similarly, `hasBall` is only local state on the server-side copy of the component. Any client logic that reads these (e.g., the `infoPanel` text) will show wrong values on non-host clients.

8. **`ball` found by tag — fragile and order-dependent (PlayerComponent line 39)**
   `ball = GameObject.FindWithTag("Ball")` is called in `OnNetworkSpawn`. If the ball's `NetworkObject` hasn't been spawned yet when the player spawns, `ball` will be null for the remainder of the session (no retry). This should be deferred or use a reliable spawn-time callback.

9. **Player movement is fully client-authoritative with no server validation (PlayerComponent lines 90–94)**
   `transform.position += movement` runs locally on the owner without telling the server. The `NetworkTransform` component (if attached) would sync this to other clients, but the server never validates the move. This opens the door to speed hacks and desync. Movement should go through a ServerRpc or use `NetworkRigidbody2D`.

10. **`GameManager` singleton has no `DontDestroyOnLoad` (GameManager lines 17–26)**
    If any scene transition occurs, the `GameManager` singleton will be destroyed and `Instance` will become null, causing `NullReferenceException` in `PlayerComponent.OnNetworkSpawn`. Add `DontDestroyOnLoad(gameObject)` if multi-scene support is needed.

11. **`UpdateInfoClientRpc` uses `ball` which may be null (PlayerComponent line 156)**
    `ball.GetComponent<BallComponent>().holderId.Value` is called inside `UpdateInfoClientRpc`, which runs on all clients. On any client where `ball` was not found in `OnNetworkSpawn` (or on the server), this throws a `NullReferenceException`.

12. **Role assignment only makes clients 0–1 Throwers (PlayerComponent line 28)**
    `isThrower = OwnerClientId < 2` means both client 0 and client 1 are Throwers. For a 2-player game with one Thrower and one Dodger, this should likely be `isThrower = OwnerClientId == 0` (or whichever ID is the host/first joiner).

13. **`BallComponent.Update` runs on server only but `followTarget` field is unused (lines 17, 30–35)**
    `followTarget` is declared and set to `null` in `DropAndThrow`, but never actually assigned or read. The ball follows via `holderObject`, not `followTarget`. This dead field adds confusion.
