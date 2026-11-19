// JOSCRAFT multiplayer – visual proxy of another player, using the ORIGINAL GLB heroes from assets/characters.
// No collision, no physics: it only interpolates towards the latest network sample.
// NAVRH: runtime/Net/RemoteAvatar.cs
using Godot;
using System;
using System.Linq;

public partial class RemoteAvatar : Node3D
{
    public int Slot { get; private set; }
    public int Character { get; private set; }
    AnimationPlayer? player; Node3D? visual; Label3D label = null!;
    Vector3 target; float targetHeading; byte anim = 255; string clip = "";

    public static RemoteAvatar Create(Node parent, int slot, string name, int character)
    {
        var a = new RemoteAvatar { Name = "RemotePlayer_" + slot, Slot = slot, Character = Math.Clamp(character, 0, Viewer.Ids.Length - 1) };
        parent.AddChild(a); a.Build(name); return a;
    }

    void Build(string name)
    {
        var packed = ResourceLoader.Load<PackedScene>($"res://assets/characters/{Viewer.Ids[Character]}.glb");
        if (packed != null)
        {
            visual = packed.Instantiate<Node3D>(); visual.Name = "AvatarVisual";visual.Scale=HeroSizing.Scale(Character); AddChild(visual);
            // Same material normalisation as BlockWorld.SelectCharacter (vertex colours are the albedo).
            foreach (var mesh in Viewer.Descendants(visual).OfType<MeshInstance3D>())
                for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
                    if (mesh.GetActiveMaterial(s) is StandardMaterial3D mat)
                    { mat = (StandardMaterial3D)mat.Duplicate(); mat.VertexColorUseAsAlbedo = true; mat.AlbedoColor = Colors.White; mat.Metallic = 0; mat.Roughness = 1; mesh.SetSurfaceOverrideMaterial(s, mat); }
            player = Viewer.Descendants(visual).OfType<AnimationPlayer>().FirstOrDefault();
        }
        label = new Label3D { Text = name, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 42, OutlineSize = 10, Position = new Vector3(0, HeroSizing.Height(Character)+.3f, 0), NoDepthTest = false, Modulate = new Color("fff3c4") };
        AddChild(label);
        Play(0);
    }

    public void SetTarget(Vector3 position, float heading, byte animation, bool snap = false)
    {
        target = position; targetHeading = heading;
        if (snap) { Position = position; Rotation = new Vector3(0, heading, 0); }
        if (animation != anim) Play(animation);
    }

    void Play(byte animation)
    {
        anim = animation; if (player == null) return;
        string wanted = animation switch { 1 => "Walk", 2 => "Run", 3 => "Jump", _ => "Idle" };
        if (wanted == clip) return;
        try
        {
            string name = BlockWorld.ClipName(player, wanted);
            if (wanted == "Jump") { player.Play(name, .05); player.Seek(.42, true); player.Pause(); }
            else { player.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear; player.Play(name, .12); }
            clip = wanted;
        }
        catch (InvalidOperationException) { /* model without this clip: keep previous pose */ }
    }

    public override void _Process(double delta)
    {
        float k = 1 - MathF.Exp(-(float)delta * 12f);   // ~80 ms smoothing at 15 Hz samples
        if (Position.DistanceSquaredTo(target) > 64) Position = target; else Position = Position.Lerp(target, k);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, targetHeading, k), 0);
        if(visual!=null)
        {
            visual.Position=Vector3.Zero;float floor=PoseGeometry.Floor(visual);
            if(float.IsFinite(floor))visual.Position=Vector3.Up*(Position.Y-floor);
        }
    }
}
