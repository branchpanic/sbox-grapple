using System;
using Sandbox;
using Sandbox.Joints;

[Library( "weapon_grappler", Title = "Grappler", Spawnable = true )]
public partial class Grappler : Carriable
{
	[ConVar.Replicated( "grappler_max_dist" )]
	public static float MaxDistance { get; set; } = 2000.0f;

	[ConVar.Replicated( "grappler_retract_rate" )]
	public static float RetractRate { get; set; } = 2.0f;

	[Net] public Vector3 AnchorPoint { get; set; }
	[Net] public float RopeLength { get; set; }
	[Net] public bool Grappling { get; set; }

	public Particles RopeParticle { get; set; }

	public override string ViewModelPath => "weapons/rust_pistol/v_rust_pistol.vmdl";

	public override void Spawn()
	{
		base.Spawn();

		SetModel( "weapons/rust_pistol/rust_pistol.vmdl" );

		CollisionGroup = CollisionGroup.Weapon;
		SetInteractsAs( CollisionLayer.Debris );
	}

	private bool StartGrappling()
	{
		if ( Owner is not Player player ) return false;

		var eyePos = player.EyePos;
		var result = Trace.Ray( eyePos, eyePos + MaxDistance * player.EyeRot.Forward )
			.WorldOnly()
			.Ignore( Owner )
			.Ignore( this )
			.Size( 6f )
			.Run();

		if ( !result.Hit || result.Body == null ) return false;

		AnchorPoint = result.EndPos;
		RopeLength = (AnchorPoint - player.Position).Length;
		Grappling = true;

		return true;
	}

	private void StopGrappling()
	{
		Grappling = false;
		
		RopeParticle?.Destroy( true );
		RopeParticle = null;
	}

	public override void Simulate( Client cl )
	{
		if ( Owner is not Player player ) return;

		if ( Input.Pressed( InputButton.Attack1 ) && !StartGrappling() )
		{
			return;
		}

		if ( Input.Released( InputButton.Attack1 ) )
		{
			StopGrappling();
		}

		if ( !Grappling ) return;

		if ( Input.Down( InputButton.Attack2 ) )
		{
			RopeLength = MathF.Max( 50.0f, RopeLength - RetractRate );
		}

		var dt = Time.Delta;
		var goal = player.Position + player.Velocity * dt;

		if ( (goal - AnchorPoint).Length > RopeLength )
		{
			goal = AnchorPoint + (goal - AnchorPoint).Normal * RopeLength;
		}

		player.Velocity = (goal - player.Position) / dt;

		// TODO: Rotate player based on direction to anchor point (except when on ground)
		
		// TODO: this makes the player all slidey
		player.GroundEntity = null;
	}

	[Event.Frame]
	private void OnFrame()
	{
		if ( !Grappling ) return;

		RopeParticle ??= Particles.Create( "particles/rope.vpcf" );
		RopeParticle.SetEntityAttachment( 0, EffectEntity, "muzzle" );
		RopeParticle.SetPosition( 1, AnchorPoint );
	}

	public override void ActiveStart( Entity ent )
	{
		base.ActiveStart( ent );
		StopGrappling();
	}

	public override void ActiveEnd( Entity ent, bool dropped )
	{
		base.ActiveEnd( ent, dropped );
		StopGrappling();
	}
}
