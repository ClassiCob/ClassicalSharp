﻿using System;
using ClassicalSharp.GraphicsAPI;
using OpenTK;

namespace ClassicalSharp {
	
	// TODO: optimise chunk rendering
	//  --> reduce iterations: liquid and sprite pass only need 1 row
	public partial class MapRenderer : IDisposable {
		
		class ChunkInfo {
			
			public ushort CentreX, CentreY, CentreZ;
			public bool Visible = true, Occluded = false;
			public bool Visited = false, Empty = false;
			public bool DrawLeft, DrawRight, DrawFront, DrawBack, DrawBottom, DrawTop;
			public byte OcclusionFlags, OccludedFlags, DistanceFlags;
			
			public ChunkPartInfo[] NormalParts;
			public ChunkPartInfo[] TranslucentParts;
			
			public ChunkInfo( int x, int y, int z ) {
				CentreX = (ushort)( x + 8 );
				CentreY = (ushort)( y + 8 );
				CentreZ = (ushort)( z + 8 );
			}
		}
		
		Game game;
		IGraphicsApi api;
		int _1DUsed = 1;
		ChunkMeshBuilder builder;
		BlockInfo info;
		
		int width, height, length;
		ChunkInfo[] chunks, unsortedChunks;
		int[] distances;
		Vector3I chunkPos = new Vector3I( int.MaxValue, int.MaxValue, int.MaxValue );
		int elementsPerBitmap = 0;
		bool[] usedTranslucent, usedNormal, pendingTranslucent, pendingNormal;
		
		public MapRenderer( Game game ) {
			this.game = game;
			_1DUsed = game.TerrainAtlas1D.CalcMaxUsedRow( game.TerrainAtlas, game.BlockInfo );
			RecalcBooleans( true );
			
			builder = new ChunkMeshBuilder( game );
			api = game.Graphics;
			elementsPerBitmap = game.TerrainAtlas1D.elementsPerBitmap;
			info = game.BlockInfo;
			
			game.Events.TerrainAtlasChanged += TerrainAtlasChanged;
			game.MapEvents.OnNewMap += OnNewMap;
			game.MapEvents.OnNewMapLoaded += OnNewMapLoaded;
			game.MapEvents.EnvVariableChanged += EnvVariableChanged;
			game.Events.BlockDefinitionChanged += BlockDefinitionChanged;
			game.Events.ViewDistanceChanged += ViewDistanceChanged;
		}
		
		public void Dispose() {
			ClearChunkCache();
			chunks = null;
			unsortedChunks = null;
			game.Events.TerrainAtlasChanged -= TerrainAtlasChanged;
			game.MapEvents.OnNewMap -= OnNewMap;
			game.MapEvents.OnNewMapLoaded -= OnNewMapLoaded;
			game.MapEvents.EnvVariableChanged -= EnvVariableChanged;
			game.MapEvents.BlockDefinitionChanged -= BlockDefinitionChanged;
			game.Events.ViewDistanceChanged -= ViewDistanceChanged;
			builder.Dispose();
		}
		
		public void Refresh() {
			if( chunks != null && !game.Map.IsNotLoaded ) {
				ClearChunkCache();
				CreateChunkCache();
			}
			chunkPos = new Vector3I( int.MaxValue, int.MaxValue, int.MaxValue );
		}
		
		void EnvVariableChanged( object sender, EnvVarEventArgs e ) {
			if( e.Var == EnvVar.SunlightColour || e.Var == EnvVar.ShadowlightColour ) {
				Refresh();
			} else if( e.Var == EnvVar.EdgeLevel ) {
				builder.clipLevel = Math.Max( 0, game.Map.SidesHeight );
				Refresh();
			}
		}

		void TerrainAtlasChanged( object sender, EventArgs e ) {
			bool refreshRequired = elementsPerBitmap != game.TerrainAtlas1D.elementsPerBitmap;
			if( refreshRequired )
				Refresh();
			
			elementsPerBitmap = game.TerrainAtlas1D.elementsPerBitmap;
			_1DUsed = game.TerrainAtlas1D.CalcMaxUsedRow( game.TerrainAtlas, game.BlockInfo );
			RecalcBooleans( true );
		}
		
		void BlockDefinitionChanged( object sender, EventArgs e ) {
			_1DUsed = game.TerrainAtlas1D.CalcMaxUsedRow( game.TerrainAtlas, game.BlockInfo );
			RecalcBooleans( true );
			Refresh();
		}
		
		void OnNewMap( object sender, EventArgs e ) {
			game.ChunkUpdates = 0;
			ClearChunkCache();
			chunks = null;
			unsortedChunks = null;
			chunkPos = new Vector3I( int.MaxValue, int.MaxValue, int.MaxValue );
			builder.OnNewMap();
		}
		
		void ViewDistanceChanged( object sender, EventArgs e ) {
			lastCamPos = new Vector3( float.MaxValue );
			lastYaw = float.MaxValue;
			lastPitch = float.MaxValue;
		}
		
		void RecalcBooleans( bool sizeChanged ) {
			if( sizeChanged ) {
				usedTranslucent = new bool[_1DUsed];
				usedNormal = new bool[_1DUsed];
				pendingTranslucent = new bool[_1DUsed];
				pendingNormal = new bool[_1DUsed];
			}
			
			for( int i = 0; i < _1DUsed; i++ ) {
				pendingTranslucent[i] = true; usedTranslucent[i] = false;
				pendingNormal[i] = true; usedNormal[i] = false;
			}
		}
		
		int chunksX, chunksY, chunksZ;
		void OnNewMapLoaded( object sender, EventArgs e ) {
			width = NextMultipleOf16( game.Map.Width );
			height = NextMultipleOf16( game.Map.Height );
			length = NextMultipleOf16( game.Map.Length );
			chunksX = width >> 4;
			chunksY = height >> 4;
			chunksZ = length >> 4;
			
			chunks = new ChunkInfo[chunksX * chunksY * chunksZ];
			unsortedChunks = new ChunkInfo[chunksX * chunksY * chunksZ];
			distances = new int[chunks.Length];
			CreateChunkCache();
			builder.OnNewMapLoaded();
			lastCamPos = new Vector3( float.MaxValue );
			lastYaw = float.MaxValue;
			lastPitch = float.MaxValue;
		}
		
		void ClearChunkCache() {
			if( chunks == null ) return;
			for( int i = 0; i < chunks.Length; i++ )
				DeleteChunk( chunks[i] );
		}
		
		void DeleteChunk( ChunkInfo info ) {
			info.Empty = false;
			info.OcclusionFlags = 0;
			info.OccludedFlags = 0;
			DeleteData( ref info.NormalParts );
			DeleteData( ref info.TranslucentParts );
		}
		
		void DeleteData( ref ChunkPartInfo[] parts ) {
			if( parts == null ) return;
			
			for( int i = 0; i < parts.Length; i++ ) {
				api.DeleteVb( parts[i].VbId );
			}
			parts = null;
		}
		
		void CreateChunkCache() {
			int index = 0;
			for( int z = 0; z < length; z += 16 )
				for( int y = 0; y < height; y += 16 )
					for( int x = 0; x < width; x += 16 )
			{
				chunks[index] = new ChunkInfo( x, y, z );
				unsortedChunks[index] = chunks[index];
				index++;
			}
		}
		
		static int NextMultipleOf16( int value ) {
			return (value + 0x0F) & ~0x0F;
		}
		
		public void RedrawBlock( int x, int y, int z, byte block, int oldHeight, int newHeight ) {
			int cx = x >> 4, bX = x & 0x0F;
			int cy = y >> 4, bY = y & 0x0F;
			int cz = z >> 4, bZ = z & 0x0F;
			// NOTE: It's a lot faster to only update the chunks that are affected by the change in shadows,
			// rather than the entire column.
			int newLightcy = newHeight < 0 ? 0 : newHeight >> 4;
			int oldLightcy = oldHeight < 0 ? 0 : oldHeight >> 4;
			ResetChunkAndBelow( cx, cy, cz, newLightcy, oldLightcy );
			
			if( bX == 0 && cx > 0 && NeedsUpdate( x, y, z, x - 1, y, z ) )
				ResetChunkAndBelow( cx - 1, cy, cz, newLightcy, oldLightcy );
			if( bY == 0 && cy > 0 && NeedsUpdate( x, y, z, x, y - 1, z ) )
				ResetChunkAndBelow( cx, cy - 1, cz, newLightcy, oldLightcy );
			if( bZ == 0 && cz > 0 && NeedsUpdate( x, y, z, x, y, z - 1 ) )
				ResetChunkAndBelow( cx, cy, cz - 1, newLightcy, oldLightcy );
			
			if( bX == 15 && cx < chunksX - 1 && NeedsUpdate( x, y, z, x + 1, y, z ) )
				ResetChunkAndBelow( cx + 1, cy, cz, newLightcy, oldLightcy );
			if( bY == 15 && cy < chunksY - 1 && NeedsUpdate( x, y, z, x, y + 1, z ) )
				ResetChunkAndBelow( cx, cy + 1, cz, newLightcy, oldLightcy );
			if( bZ == 15 && cz < chunksZ - 1 && NeedsUpdate( x, y, z, x, y, z + 1 ) )
				ResetChunkAndBelow( cx, cy, cz + 1, newLightcy, oldLightcy );
		}
		
		bool NeedsUpdate( int x1, int y1, int z1, int x2, int y2, int z2 ) {
			byte b1 = game.Map.SafeGetBlock( x1, y1, z1 );
			byte b2 = game.Map.SafeGetBlock( x2, y2, z2 );
			return (!info.IsOpaque[b1] && info.IsOpaque[b2]) || !(info.IsOpaque[b1] && b2 == 0);
		}
		
		void ResetChunkAndBelow( int cx, int cy, int cz, int newLightCy, int oldLightCy ) {
			if( newLightCy == oldLightCy ) {
				ResetChunk( cx, cy, cz );
			} else {
				int cyMax = Math.Max( newLightCy, oldLightCy );
				int cyMin = Math.Min( oldLightCy, newLightCy );
				for( cy = cyMax; cy >= cyMin; cy-- ) {
					ResetChunk( cx, cy, cz );
				}
			}
		}
		
		void ResetChunk( int cx, int cy, int cz ) {
			if( cx < 0 || cy < 0 || cz < 0 ||
			   cx >= chunksX || cy >= chunksY || cz >= chunksZ ) return;
			DeleteChunk( unsortedChunks[cx + chunksX * ( cy + cz * chunksY )] );
		}
		
		public void Render( double deltaTime ) {
			if( chunks == null ) return;
			UpdateSortOrder();
			UpdateChunks( deltaTime );
			
			RenderNormal();
			game.MapBordersRenderer.Render( deltaTime );
			RenderTranslucent();
			game.Players.DrawShadows();
		}

		int chunksTarget = 4;
		const double targetTime = (1.0 / 30) + 0.01;
		void UpdateChunks( double deltaTime ) {
			int chunkUpdates = 0;
			int viewDist = Utils.AdjViewDist( game.ViewDistance < 16 ? 16 : game.ViewDistance );
			int adjViewDistSqr = (viewDist + 24) * (viewDist + 24);
			chunksTarget += deltaTime < targetTime ? 1 : -1; // build more chunks if 30 FPS or over, otherwise slowdown.
			Utils.Clamp( ref chunksTarget, 4, 12 );
			
			LocalPlayer p = game.LocalPlayer;
			Vector3 cameraPos = game.CurrentCameraPos;
			bool samePos = cameraPos == lastCamPos && p.HeadYawDegrees == lastYaw
				&& p.PitchDegrees == lastPitch;
			if( samePos )
				UpdateChunksStill( deltaTime, ref chunkUpdates, adjViewDistSqr );
			else
				UpdateChunksAndVisibility( deltaTime, ref chunkUpdates, adjViewDistSqr );
			
			lastCamPos = cameraPos;
			lastYaw = p.HeadYawDegrees; lastPitch = p.PitchDegrees;
			if( !samePos || chunkUpdates != 0 )
				RecalcBooleans( false );
		}
		Vector3 lastCamPos;
		float lastYaw, lastPitch;
		
		void UpdateChunksAndVisibility( double deltaTime, ref int chunkUpdats, int adjViewDistSqr ) {
			for( int i = 0; i < chunks.Length; i++ ) {
				ChunkInfo info = chunks[i];
				if( info.Empty ) continue;
				int distSqr = distances[i];
				bool inRange = distSqr <= adjViewDistSqr;
				
				if( info.NormalParts == null && info.TranslucentParts == null ) {
					if( inRange && chunkUpdats < chunksTarget )
						BuildChunk( info, ref chunkUpdats );
				}
				info.Visible = inRange &&
					game.Culling.SphereInFrustum( info.CentreX, info.CentreY, info.CentreZ, 14 ); // 14 ~ sqrt(3 * 8^2)
			}
		}
		
		void UpdateChunksStill( double deltaTime, ref int chunkUpdates, int adjViewDistSqr ) {
			for( int i = 0; i < chunks.Length; i++ ) {
				ChunkInfo info = chunks[i];
				if( info.Empty ) continue;
				int distSqr = distances[i];
				bool inRange = distSqr <= adjViewDistSqr;
				
				if( info.NormalParts == null && info.TranslucentParts == null ) {
					if( inRange && chunkUpdates < chunksTarget ) {
						BuildChunk( info, ref chunkUpdates );
						// only need to update the visibility of chunks in range.
						info.Visible = inRange &&
							game.Culling.SphereInFrustum( info.CentreX, info.CentreY, info.CentreZ, 14 ); // 14 ~ sqrt(3 * 8^2)
					}
				}
			}
		}
		
		void BuildChunk( ChunkInfo info, ref int chunkUpdates ) {
			game.ChunkUpdates++;
			builder.GetDrawInfo( info.CentreX - 8, info.CentreY - 8, info.CentreZ - 8,
			                    ref info.NormalParts, ref info.TranslucentParts, ref info.OcclusionFlags );
			
			if( info.NormalParts == null && info.TranslucentParts == null )
				info.Empty = true;
			chunkUpdates++;
		}
	}
}