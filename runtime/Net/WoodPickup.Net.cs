// JOSCRAFT multiplayer – WoodPickup partial. On clients a pickup is a frozen visual that follows the host's physics.
// It can never be collected locally: collection is decided by the host (NetServer.CollectPickups / BlockWorld.CollectWoodPickup).
// NAVRH: runtime/Net/WoodPickup.Net.cs
using Godot;
using System;

public partial class WoodPickup
{
    /// <summary>Set by NetClient right after spawning. HOOKs in UpdateSuspension and _PhysicsProcess check it first.</summary>
    public bool NetRemote { get; set; }
    Vector3 netPosition, netRotation;
    public void NetTarget(Vector3 position, Vector3 rotation, bool snap = false)
    {
        netPosition = position; netRotation = rotation;
        if (snap) { Position = position; Rotation = rotation; }
    }
    /// <summary>HOOK body: replaces UpdateSuspension for remote pickups.</summary>
    void NetSuspension()
    {
        if (!Freeze) Freeze = true;                         // never simulated locally
        Visible = world.Terrain.IsLoadedAt(Position.X, Position.Z);
    }
    /// <summary>HOOK body: replaces _PhysicsProcess for remote pickups (no Age, no collection).</summary>
    void NetFollow(double delta)
    {
        NetSuspension();
        float k = 1 - MathF.Exp(-(float)delta * 14f);
        Position = Position.DistanceSquaredTo(netPosition) > 25 ? netPosition : Position.Lerp(netPosition, k);
        Rotation = new Vector3(Mathf.LerpAngle(Rotation.X, netRotation.X, k), Mathf.LerpAngle(Rotation.Y, netRotation.Y, k), Mathf.LerpAngle(Rotation.Z, netRotation.Z, k));
    }
}
