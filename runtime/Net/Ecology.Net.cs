// JOSCRAFT multiplayer – Ecology/Creature partials.
// Host: unchanged AI, but targets the NEAREST player (hooks in Ecology.cs call BlockWorld.NearestPlayerPosition/DamagePlayer).
// Client: creatures are visual proxies driven by the host; day time follows the host clock.
// NAVRH: runtime/Net/Ecology.Net.cs
using Godot;
using System;

public partial class Ecology
{
    /// <summary>True on clients (HOOK at the top of _PhysicsProcess): no spawning, no AI, only the clock + light.</summary>
    public bool RemoteMode { get; private set; }
    public void SetRemoteMode(bool remote) { RemoteMode = remote; if (remote) initialised = true; }

    /// <summary>Client clock: advance locally for smooth light, snap to the host when drift exceeds ~3 game-minutes.</summary>
    public void SetServerTime(float time, bool snap = false)
    {
        time = Mathf.PosMod(time, 1);
        float drift = Mathf.Wrap(time - TimeOfDay, -.5f, .5f);
        if (snap || Mathf.Abs(drift) > .002f) TimeOfDay = time;
        UpdateLight();
    }
    /// <summary>HOOK body for RemoteMode in _PhysicsProcess.</summary>
    void RemoteTick(float delta) { TimeOfDay = Mathf.PosMod(TimeOfDay + delta / DaySeconds, 1); UpdateLight(); }

    public Creature SpawnProxy(CreatureSave data, uint netId, float yaw)
    {
        var c = new Creature(world, this, data) { NetId = netId, IsProxy = true };
        c.Rotation = new Vector3(0, yaw, 0); c.NetTarget(c.Position, yaw);
        creatures.Add(c); AddChild(c); return c;
    }
    public void RemoveProxy(Creature c)
    {
        if (!creatures.Remove(c)) return;
        c.CollisionLayer = 0; c.Visible = false; c.QueueFree();
    }
    /// <summary>HOOK at the top of TryInteract/TryAttack on clients: aim locally, let the host decide.</summary>
    bool NetForwardInteract(bool attack)
    {
        var c = Target(attack ? 3.4f : 3.6f); if (c == null || c.NetId == 0) return false;
        if (attack && c.Kind != "zombie") return false;
        if (attack) NetSession.Instance?.Client?.Attack(c); else NetSession.Instance?.Client?.Interact(c);
        return true;
    }
}

public partial class Creature
{
    public uint NetId { get; set; }
    /// <summary>Client proxy: HOOK at the top of _PhysicsProcess calls NetFollow and returns (no AI, no damage).</summary>
    public bool IsProxy { get; set; }
    Vector3 netTarget; float netYaw;
    public void NetTarget(Vector3 position, float yaw) { netTarget = position; netYaw = yaw; }
    void NetFollow(float delta)
    {
        float k = 1 - MathF.Exp(-delta * 10f);
        var before = Position;
        Position = Position.DistanceSquaredTo(netTarget) > 64 ? netTarget : Position.Lerp(netTarget, k);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, netYaw, k), 0);
        bool moving = (Position - before).LengthSquared() > 1e-6f;
        if (Animator != null) Play(moving ? "Walk" : Tamed&&!Following?"Sit":"Idle");
        // Render the received grounded pose on the same terrain surface as the host.
        if(world.Terrain.IsLoadedAt(Position.X,Position.Z))
        {
            using var query=PhysicsRayQueryParameters3D.Create(Position+Vector3.Up*.2f,Position-Vector3.Up*.3f,1);
            var hit=world.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if(hit.Count>0){Model.Position=Vector3.Zero;Model.Position=Vector3.Up*(Position.Y-RenderedFloor());}
        }
        Visible = world.Terrain.IsLoadedAt(Position.X, Position.Z);
    }
}
