﻿using System;
using ClassicalSharp.GraphicsAPI;
using OpenTK;

namespace ClassicalSharp {
	
	// TODO: optimise chunk rendering
	//  --> reduce iterations: liquid and sprite pass only need 1 row
	public class MapRenderer : IDisposable {
		
		class ChunkInfo {
			
			public short CentreX, CentreY, CentreZ;
			public bool Visible = true;
			public bool Empty = false;
			
			public ChunkPartInfo[] SolidParts;
			public ChunkPartInfo[] SpriteParts;
			public ChunkPartInfo[] TranslucentParts;
			
			public ChunkInfo( int x, int y, int z ) {
				CentreX = (short)( x + 8 );
				CentreY = (short)( y + 8 );
				CentreZ = (short)( z + 8 );
			}
		}
		
		public Game Window;
		public IGraphicsApi Graphics;
		
		int _1Dcount = 1;
		ChunkMeshBuilder builder;
		
		int width, height, length;
		ChunkInfo[] chunks, unsortedChunks;
		Vector3I chunkPos = new Vector3I( int.MaxValue, int.MaxValue, int.MaxValue );
		int elementsPerBitmap = 0;
		
		public MapRenderer( Game window ) {
			Window = window;
			_1Dcount = window.TerrainAtlas1D.TexIds.Length;
			builder = new ChunkMeshBuilder( window );
			Graphics = window.Graphics;
			elementsPerBitmap = window.TerrainAtlas1D.elementsPerBitmap;
			Window.TerrainAtlasChanged += TerrainAtlasChanged;
			Window.OnNewMap += OnNewMap;
			Window.OnNewMapLoaded += OnNewMapLoaded;
			Window.EnvVariableChanged += EnvVariableChanged;
		}
		
		public void Dispose() {
			ClearChunkCache();
			chunks = null;
			unsortedChunks = null;
			Window.TerrainAtlasChanged -= TerrainAtlasChanged;
			Window.OnNewMap -= OnNewMap;
			Window.OnNewMapLoaded -= OnNewMapLoaded;
			Window.EnvVariableChanged -= EnvVariableChanged;
			builder.Dispose();
		}
		
		public void Refresh() {
			if( chunks != null && !Window.Map.IsNotLoaded ) {
				ClearChunkCache();
				CreateChunkCache();
			}
		}
		
		void EnvVariableChanged( object sender, EnvVariableEventArgs e ) {
			if( e.Variable == EnvVariable.SunlightColour || e.Variable == EnvVariable.ShadowlightColour ) {
				Refresh();
			}
		}

		void TerrainAtlasChanged( object sender, EventArgs e ) {
			_1Dcount = Window.TerrainAtlas1D.TexIds.Length;
			bool fullResetRequired = elementsPerBitmap != Window.TerrainAtlas1D.elementsPerBitmap;
			if( fullResetRequired ) {
				Refresh();
			}
			elementsPerBitmap = Window.TerrainAtlas1D.elementsPerBitmap;
		}
		
		void OnNewMap( object sender, EventArgs e ) {
			Window.ChunkUpdates = 0;
			ClearChunkCache();
			chunks = null;
			unsortedChunks = null;
			chunkPos = new Vector3I( int.MaxValue, int.MaxValue, int.MaxValue );
			builder.OnNewMap();
		}
		
		int chunksX, chunksY, chunksZ;
		void OnNewMapLoaded( object sender, EventArgs e ) {
			width = NextMultipleOf16( Window.Map.Width );
			height = NextMultipleOf16( Window.Map.Height );
			length = NextMultipleOf16( Window.Map.Length );
			chunksX = width >> 4;
			chunksY = height >> 4;
			chunksZ = length >> 4;
			
			chunks = new ChunkInfo[chunksX * chunksY * chunksZ];
			unsortedChunks = new ChunkInfo[chunksX * chunksY * chunksZ];
			distances = new int[chunks.Length];
			CreateChunkCache();
			builder.OnNewMapLoaded();
		}
		
		void ClearChunkCache() {
			if( chunks == null ) return;
			for( int i = 0; i < chunks.Length; i++ ) {
				DeleteChunk( chunks[i] );
			}
		}
		
		void DeleteChunk( ChunkInfo info ) {
			info.Empty = false;
			DeleteData( ref info.SolidParts );
			DeleteData( ref info.SpriteParts );
			DeleteData( ref info.TranslucentParts );
		}
		
		void DeleteData( ref ChunkPartInfo[] parts ) {
			if( parts == null ) return;
			
			for( int i = 0; i < parts.Length; i++ ) {
				Graphics.DeleteVb( parts[i].VbId );
				Graphics.DeleteIb( parts[i].IbId );
			}
			parts = null;
		}
		
		void CreateChunkCache() {
			int index = 0;
			for( int z = 0; z < length; z += 16 ) {
				for( int y = 0; y < height; y += 16 ) {
					for( int x = 0; x < width; x += 16 ) {
						chunks[index] = new ChunkInfo( x, y, z );
						unsortedChunks[index] = chunks[index];
						index++;
					}
				}
			}
		}
		
		static int NextMultipleOf16( int value ) {
			return ( value + 0x0F ) & ~0x0F;
		}
		
		public void RedrawBlock( int x, int y, int z, byte block, int oldHeight, int newHeight ) {
			int cx = x >> 4;
			int cy = y >> 4;
			int cz = z >> 4;
			// NOTE: It's a lot faster to only update the chunks that are affected by the change in shadows,
			// rather than the entire column.
			int newLightcy = newHeight == -1 ? 0 : newHeight >> 4;
			int oldLightcy = oldHeight == -1 ? 0 : oldHeight >> 4;
			
			ResetChunkAndBelow( cx, cy, cz, newLightcy, oldLightcy );
			int bX = x & 0x0F; // % 16
			int bY = y & 0x0F;
			int bZ = z & 0x0F;
			
			if( bX == 0 && cx > 0 ) ResetChunkAndBelow( cx - 1, cy, cz, newLightcy, oldLightcy );
			if( bY == 0 && cy > 0 ) ResetChunkAndBelow( cx, cy - 1, cz, newLightcy, oldLightcy );
			if( bZ == 0 && cz > 0 ) ResetChunkAndBelow( cx, cy, cz - 1, newLightcy, oldLightcy );
			if( bX == 15 && cx < chunksX - 1 ) ResetChunkAndBelow( cx + 1, cy, cz, newLightcy, oldLightcy );
			if( bY == 15 && cy < chunksY - 1 ) ResetChunkAndBelow( cx, cy + 1, cz, newLightcy, oldLightcy );
			if( bZ == 15 && cz < chunksZ - 1 ) ResetChunkAndBelow( cx, cy, cz + 1, newLightcy, oldLightcy );
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
			UpdateChunks();
			int[] texIds = Window.TerrainAtlas1D.TexIds;
			
			// Render solid and fully transparent to fill depth buffer.
			// These blocks are treated as having an alpha value of either none or full.
			Graphics.BeginIndexedVbBatch();
			Graphics.Texturing = true;
			Graphics.AlphaTest = true;
			Graphics.FaceCulling = true;
			for( int batch = 0; batch < _1Dcount; batch++ ) {
				Graphics.Bind2DTexture( texIds[batch] );
				RenderSolidBatch( batch );
			}
			Graphics.FaceCulling = false;
			for( int batch = 0; batch < _1Dcount; batch++ ) {
				Graphics.Bind2DTexture( texIds[batch] );
				RenderSpriteBatch( batch );
			}
			Graphics.AlphaTest = false;
			Graphics.Texturing = false;
			Graphics.EndIndexedVbBatch();
			Window.MapEnvRenderer.RenderMapSides( deltaTime );
			Window.MapEnvRenderer.RenderMapEdges( deltaTime );
			
			// Render translucent(liquid) blocks. These 'blend' into other blocks.
			Graphics.BeginIndexedVbBatch();
			bool canCullTranslucent = !Window.BlockInfo.IsTranslucent(
				(byte)Window.LocalPlayer.BlockAtHead );
			if( canCullTranslucent )
				Graphics.FaceCulling = true;
			
			// First fill depth buffer
			Graphics.Texturing = false;
			Graphics.AlphaBlending = false;
			Graphics.ColourWrite = false;
			for( int batch = 0; batch < _1Dcount; batch++ ) {
				RenderTranslucentBatchDepthPass( batch );
			}
			// Then actually draw the transluscent blocks
			Graphics.AlphaBlending = true;
			Graphics.Texturing = true;
			Graphics.ColourWrite = true;
			Graphics.DepthWrite = false; // we already calculated depth values in depth pass
			for( int batch = 0; batch < _1Dcount; batch++ ) {
				Graphics.Bind2DTexture( texIds[batch] );
				RenderTranslucentBatch( batch );
			}
			Graphics.DepthWrite = true;
			Graphics.AlphaTest = false;
			Graphics.AlphaBlending = false;
			Graphics.Texturing = false;
			if( canCullTranslucent )
				Graphics.FaceCulling = false;
			Graphics.EndIndexedVbBatch();
		}

		int[] distances;
		void UpdateSortOrder() {
			Player p = Window.LocalPlayer;
			Vector3I newChunkPos = Vector3I.Floor( p.Position );
			newChunkPos.X = ( newChunkPos.X & ~0x0F ) + 8;
			newChunkPos.Y = ( newChunkPos.Y & ~0x0F ) + 8;
			newChunkPos.Z = ( newChunkPos.Z & ~0x0F ) + 8;
			if( newChunkPos != chunkPos ) {
				chunkPos = newChunkPos;
				for( int i = 0; i < distances.Length; i++ ) {
					ChunkInfo info = chunks[i];
					distances[i] = Utils.DistanceSquared( info.CentreX, info.CentreY, info.CentreZ, chunkPos.X, chunkPos.Y, chunkPos.Z );
				}
				// NOTE: Over 5x faster compared to normal comparison of IComparer<ChunkInfo>.Compare
				Array.Sort( distances, chunks );
			}
		}
		
		void UpdateChunks() {
			int chunksUpdatedThisFrame = 0;
			int adjViewDistSqr = ( Window.ViewDistance + 14 ) * ( Window.ViewDistance + 14 );
			for( int i = 0; i < chunks.Length; i++ ) {
				ChunkInfo info = chunks[i];
				if( info.Empty ) continue;
				int distSqr = distances[i];
				bool inRange = distSqr <= adjViewDistSqr;
				
				if( info.SolidParts == null && info.SpriteParts == null && info.TranslucentParts == null ) {
					if( inRange && chunksUpdatedThisFrame < 4 ) {
						Window.ChunkUpdates++;
						builder.GetDrawInfo( info.CentreX - 8, info.CentreY - 8, info.CentreZ - 8,
						                    ref info.SolidParts, ref info.SpriteParts, ref info.TranslucentParts );
						if( info.SolidParts == null && info.SpriteParts == null && info.TranslucentParts == null ) {
							info.Empty = true;
						}
						chunksUpdatedThisFrame++;
					}
				}
				info.Visible = inRange &&
					Window.Culling.SphereInFrustum( info.CentreX, info.CentreY, info.CentreZ, 14 ); // 14 ~ sqrt(3 * 8^2)
			}
		}
		
		const DrawMode mode = DrawMode.Triangles;
		const int maxVertex = 65536;
		const int maxIndices = maxVertex / 4 * 6;
		void RenderSolidBatch( int batch ) {
			for( int i = 0; i < chunks.Length; i++ ) {
				ChunkInfo info = chunks[i];
				if( info.SolidParts == null || !info.Visible ) continue;

				ChunkPartInfo part = info.SolidParts[batch];
				if( part.IndicesCount > 0 ) {
					if( part.IndicesCount > maxIndices ) {
						Graphics.DrawIndexedVbBatch( mode, part.VbId, part.IbId, maxIndices, 0, 0 );
						Graphics.DrawIndexedVbBatch( mode, part.VbId, part.IbId, part.IndicesCount - maxIndices, maxVertex, maxIndices );
					} else {
						Graphics.DrawIndexedVbBatch( mode, part.VbId, part.IbId, part.IndicesCount, 0, 0 );
					}
					Window.Vertices += part.IndicesCount;
				}
			}
		}
		
		void RenderSpriteBatch( int batch ) {
			for( int i = 0; i < chunks.Length; i++ ) {
				ChunkInfo info = chunks[i];
				if( info.SpriteParts == null || !info.Visible ) continue;

				ChunkPartInfo part = info.SpriteParts[batch];
				if( part.IndicesCount > 0 ) {
					Graphics.DrawIndexedVbBatch( mode, part.VbId, part.IbId, part.IndicesCount, 0, 0 );
					Window.Vertices += part.IndicesCount;
				}
			}
		}

		void RenderTranslucentBatch( int batch ) {
			for( int i = 0; i < chunks.Length; i++ ) {
				ChunkInfo info = chunks[i];
				if( info.TranslucentParts == null || !info.Visible ) continue;
				ChunkPartInfo part = info.TranslucentParts[batch];
				if( part.IndicesCount > 0 ) {
					if( part.IndicesCount > maxIndices ) {
						Graphics.DrawIndexedVbBatch( mode, part.VbId, part.IbId, maxIndices, 0, 0 );
						Graphics.DrawIndexedVbBatch( mode, part.VbId, part.IbId, part.IndicesCount - maxIndices, maxVertex, maxIndices );
					} else {
						Graphics.DrawIndexedVbBatch( mode, part.VbId, part.IbId, part.IndicesCount, 0, 0 );
					}
					Window.Vertices += part.IndicesCount;
				}
			}
		}
		
		void RenderTranslucentBatchDepthPass( int batch ) {
			for( int i = 0; i < chunks.Length; i++ ) {
				ChunkInfo info = chunks[i];
				if( info.TranslucentParts == null || !info.Visible ) continue;

				ChunkPartInfo part = info.TranslucentParts[batch];
				if( part.IndicesCount > 0 ) {
					if( part.IndicesCount > maxIndices ) {
						Graphics.DrawIndexedVbBatch( mode, part.VbId, part.IbId, maxIndices, 0, 0 );
						Graphics.DrawIndexedVbBatch( mode, part.VbId, part.IbId, part.IndicesCount - maxIndices, maxVertex, maxIndices );
					} else {
						Graphics.DrawIndexedVbBatch( mode, part.VbId, part.IbId, part.IndicesCount, 0, 0 );
					}
				}
			}
		}
	}
}