﻿using System;
using ClassicalSharp.Model;
using OpenTK;

namespace ClassicalSharp {
	
	/// <summary> Contains a model, along with position, velocity, and rotation.
	/// May also contain other fields and properties. </summary>
	public abstract partial class Entity {
		
		/// <summary> Returns the bounding box that contains the model, assuming it is not rotated. </summary>
		public BoundingBox PickingBounds {
			get { UpdateModel(); return Model.PickingBounds.Offset( Position ); }
		}
		
		/// <summary> Bounding box of the model that collision detection
		/// is performed with, in world coordinates.  </summary>
		public virtual BoundingBox CollisionBounds {
			get {
				Vector3 pos = Position;
				Vector3 size = CollisionSize;
				return new BoundingBox( pos.X - size.X / 2, pos.Y, pos.Z - size.Z / 2,
				                       pos.X + size.X / 2, pos.Y + size.Y, pos.Z + size.Z / 2 );
			}
		}
		
		/// <summary> Determines whether any of the blocks that intersect the
		/// bounding box of this entity satisfy the given condition. </summary>
		public bool TouchesAny( Predicate<byte> condition ) {
			return TouchesAny( CollisionBounds, condition );
		}
		
		/// <summary> Determines whether any of the blocks that intersect the
		/// given bounding box satisfy the given condition. </summary>
		public bool TouchesAny( BoundingBox bounds, Predicate<byte> condition ) {
			Vector3I bbMin = Vector3I.Floor( bounds.Min );
			Vector3I bbMax = Vector3I.Floor( bounds.Max );
			
			// Order loops so that we minimise cache misses
			for( int y = bbMin.Y; y <= bbMax.Y; y++ )
				for( int z = bbMin.Z; z <= bbMax.Z; z++ )
					for( int x = bbMin.X; x <= bbMax.X; x++ )
			{
				if( !game.Map.IsValidPos( x, y, z ) ) continue;
				byte block = game.Map.GetBlock( x, y, z );
				Vector3 min = new Vector3( x, y, z ) + info.MinBB[block];
				Vector3 max = new Vector3( x, y, z ) + info.MaxBB[block];
				
				BoundingBox blockBB = new BoundingBox( min, max );
				if( !blockBB.Intersects( bounds ) ) continue;		
				if( condition( block ) ) return true;
			}
			return false;
		}
		
		/// <summary> Constant offset used to avoid floating point roundoff errors. </summary>
		public const float Adjustment = 0.001f;
		static readonly Vector3 liqExpand = new Vector3( 0.25f/16f, 0/16f, 0.25f/16f );
		
		/// <summary> Determines whether any of the blocks that intersect the
		/// bounding box of this entity are lava or still lava. </summary>
		protected bool TouchesAnyLava() {
			BoundingBox bounds = CollisionBounds.Expand( liqExpand );
			AdjustLiquidTestBounds( ref bounds );
			return TouchesAny( bounds,
			                  b => b == (byte)Block.Lava || b == (byte)Block.StillLava );
		}
		
		/// <summary> Determines whether any of the blocks that intersect the
		/// bounding box of this entity are rope. </summary>
		protected bool TouchesAnyRope() {
			BoundingBox bounds = CollisionBounds;
			bounds.Max.Y += 0.5f/16f;
			return TouchesAny( bounds, b => b == (byte)Block.Rope );
		}
		
		/// <summary> Determines whether any of the blocks that intersect the
		/// bounding box of this entity are water or still water. </summary>
		protected bool TouchesAnyWater() {
			BoundingBox bounds = CollisionBounds.Expand( liqExpand );
			AdjustLiquidTestBounds( ref bounds );
			return TouchesAny( bounds,
			                  b => b == (byte)Block.Water || b == (byte)Block.StillWater );
		}
		
		void AdjustLiquidTestBounds( ref BoundingBox bounds ) {
			// Even though we collide with lava 3 blocks above our feet, vanilla client thinks
			// that we do not.. so we have to maintain compatibility here.
			float height = bounds.Max.Y - bounds.Min.Y;
			const float pHeight = (28.5f - 4f)/16f;
			bounds.Max.Y = bounds.Min.Y + Math.Min( height, pHeight );
		}
	}
}