using System;
using Sandbox;
using Sandbox.Joints;

[Library( "weapon_grappler", Title = "Grappler", Spawnable = true )]
public partial class Grappler : Carriable
{
	#region Configuration - ConVars

	[ConVar.Replicated( "grappler_max_dist" )]
	public static float MaxDistance { get; set; } = 1100.0f;

	[ConVar.Replicated( "grappler_retract_rate" )]
	public static float RetractRate { get; set; } = 9.0f;

	#endregion


	#region Configuration - Constants

	// Grapple behavior
	private const float MinRopeLength = 120.0f;
	private const float TraceSize = 6f;

	// Wall avoidance
	private const float ObstructionLookahead = .3f; // how far should we extrapolate when looking for an obstruction?

	private const float
		WallAvoidanceBiasWeight = .15f; // how much should we affect the velocity as a result? (function of velocity)

	#endregion


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
			.Size( TraceSize )
			.Run();

		if ( !result.Hit || result.Body == null ) return false;

		AnchorPoint = result.EndPos;

		var newRopeLength = (AnchorPoint - player.Position).Length;

		RopeLength = MathF.Max( MinRopeLength, newRopeLength );
		Grappling = true;
		player.GroundEntity = null; // pull the player off the ground if necessary

		// TODO: Better FX
		player.SetAnimBool( "b_attack", true );
		if ( IsLocalPawn )
		{
			_ = new Sandbox.ScreenShake.Perlin();
		}

		PlaySound( "grappling_hook" );
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

		if ( !player.Velocity.IsNearlyZero() ) DebugOverlay.Axis( player.Position, player.Rotation, 6.0f, 45.0f );

		if ( Input.Pressed( InputButton.Attack1 ) && !Grappling )
		{
			if ( !StartGrappling() ) return;
			DebugOverlay.Sphere( AnchorPoint, 6.0f, Color.Red, duration: 45.0f );
		}

		if ( Input.Released( InputButton.Attack1 ) )
		{
			StopGrappling();
		}

		if ( !Grappling ) return;

		DebugOverlay.Line( player.Position, AnchorPoint, 45.0f );

		if ( Input.Down( InputButton.Attack2 ) )
		{
			RopeLength = MathF.Max( MinRopeLength, RopeLength - RetractRate );

			// Don't stick the player to the ground when they're trying to pull themselves
			if (Input.Pressed( InputButton.Attack2 )) player.GroundEntity = null;

			// TODO: More FX needing improvement, could possibly make pitch a function of (initial length - current length)
			if ( RopeLength - MinRopeLength > 0.1f ) PlaySound( "rope_pull" );
		}

		var dt = Time.Delta;
		var goal = player.Position + player.Velocity * dt;

		if ( (goal - AnchorPoint).Length > RopeLength )
		{
			goal = AnchorPoint + (goal - AnchorPoint).Normal * RopeLength;
		}

		// Try to prevent the player from hitting walls
		// Parameters may need more tuning, but they're alright for now
		var obstructions = Trace.Ray( player.Position, player.Position + ObstructionLookahead * player.Velocity )
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
		
		// TODO: It's possible to build up a ton of speed against a wall in a configuration like this
		//
		//             _____x (anchor)____
		// (player) o |
		//
		// The resulting movement is kinda annoying and I'm not sure how to address it
		player.Velocity = (goal - player.Position) / dt +
		                  (WallAvoidanceBiasWeight * player.Velocity.Length * avoidanceBias);
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
