using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Owner-authoritative NetworkTransform.
/// Place this on the Player prefab instead of the default NetworkTransform
/// so each client can move their own character without server snap-back.
/// </summary>
[DisallowMultipleComponent]
public class OwnerNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative()
    {
        return false; // Owner drives their own position
    }
}
