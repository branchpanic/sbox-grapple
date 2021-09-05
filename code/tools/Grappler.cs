using System;
using Sandbox;
using Sandbox.Joints;

[Library( "weapon_grappler", Title = "Grappler", Spawnable = true )]
public partial class Grappler : Carriable
{
	[ConVar.Replicated( "grappler_max_dist" )]
	public static float MaxDistance { get; set; } = 2000.0f;

	[ConVar.Replicated( "grappler_retract_rate" )]
	public static float RetractRate { get; set; } = 6.0f;

	public const float MinRopeLength = 120.0f;

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
		RopeLength = MathF.Max( MinRopeLength, (AnchorPoint - player.Position).Length );
		Grappling = true;
		player.GroundEntity = null;

		// TODO: Better sound effect
		PlaySound( "grappling_hook" );

		// TODO: Bullet dust cloud is too small and decal is unnecessary
		result.Surface.DoBulletImpact( result );

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
		// TODO: Should all of this be predicted? I don't know anything about game networking 
		if ( Owner is not Player player ) return;

		if ( Input.Pressed( InputButton.Attack1 ) )
		{
			if ( !StartGrappling() ) return;
		}

		if ( Input.Released( InputButton.Attack1 ) )
		{
			StopGrappling();
		}

		if ( !Grappling ) return;

		if ( Input.Down( InputButton.Attack2 ) )
		{
			// TODO: Rope retract sound
			RopeLength = MathF.Max( MinRopeLength, RopeLength - RetractRate );

			// Don't stick the player to the ground when they're trying to pull themselves
			player.GroundEntity = null;
		}

		var dt = Time.Delta;
		var goal = player.Position + player.Velocity * dt;

		if ( (goal - AnchorPoint).Length > RopeLength )
		{
			goal = AnchorPoint + (goal - AnchorPoint).Normal * RopeLength;
		}

		// Try to prevent the player from hitting walls
		// Parameters may need more tuning, but they're alright for now
		const float obstructionLookahead = .3f; // how far should we extrapolate when looking for an obstruction?
		const float biasWeight = .15f; // how much should we affect the velocity as a result?
		var obstructions = Trace.Ray( player.Position, player.Position + obstructionLookahead * player.Velocity )
			.WorldOnly()
			.Ignore( player )
			.Ignore( this )
			.Size( player.CollisionBounds )
			.RunAll();

		var avoidanceBias = Vector3.Zero;

		// No, linq slow >:(
		// ReSharper disable once LoopCanBeConvertedToQuery
		if ( obstructions != null )
		{
			foreach ( var traceResult in obstructions )
			{
				if ( !traceResult.Hit ) continue;

				// Cross with up vector so we don't avoid the floor (looks weird)
				avoidanceBias += traceResult.Normal.Cross( Vector3.Up ).Length * traceResult.Normal;
			}
		}

		player.Velocity = (goal - player.Position) / dt + (biasWeight * player.Velocity.Length * avoidanceBias);
	}

	[Event.Frame]
	private void OnFrame()
	{
		if ( !Grappling ) return;

		RopeParticle ??= Particles.Create( "particles/grapple_rope.vpcf" );
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
