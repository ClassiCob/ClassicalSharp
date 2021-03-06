﻿using System;
using System.IO;
using System.Threading;
using OpenTK;
using SharpWave;
using SharpWave.Codecs;
using SharpWave.Codecs.Vorbis;

namespace ClassicalSharp.Audio {
	
	public sealed partial class AudioPlayer {
		
		Soundboard digBoard, stepBoard;
		const int maxSounds = 6;
		
		public void SetSound( bool enabled ) {
			if( enabled )
				InitSound();
			else
				DisposeSound();
		}
		
		void InitSound() {
			if( digBoard == null )
				InitSoundboards();
			
			monoOutputs = new IAudioOutput[maxSounds];
			stereoOutputs = new IAudioOutput[maxSounds];
		}
		
		void InitSoundboards() {
			digBoard = new Soundboard();
			digBoard.Init( "dig" );
			stepBoard = new Soundboard();
			stepBoard.Init( "step" );
		}

		public void Tick( double delta ) {
		}
		
		public void PlayDigSound( SoundType type ) { PlaySound( type, digBoard ); }
		
		public void PlayStepSound( SoundType type ) { PlaySound( type, stepBoard ); }
		
		AudioChunk chunk = new AudioChunk();
		void PlaySound( SoundType type, Soundboard board ) {
			if( type == SoundType.None || monoOutputs == null )
				return;
			Sound snd = board.PlayRandomSound( type );
			snd.Metadata = board == digBoard ? (byte)1 : (byte)2;
			chunk.Channels = snd.Channels;
			chunk.Frequency = snd.SampleRate;
			chunk.BitsPerSample = snd.BitsPerSample;
			chunk.BytesOffset = snd.Offset;
			chunk.BytesUsed = snd.Length;
			chunk.Data = board.Data;
			
			if( snd.Channels == 1 )
				PlayCurrentSound( monoOutputs );
			else if( snd.Channels == 2 )
				PlayCurrentSound( stereoOutputs );
		}
		
		IAudioOutput firstSoundOut;
		void PlayCurrentSound( IAudioOutput[] outputs ) {
			for( int i = 0; i < monoOutputs.Length; i++ ) {
				IAudioOutput output = outputs[i];
				if( output == null ) {
					output = GetPlatformOut();
					output.Create( 1, firstSoundOut );
					if( firstSoundOut == null )
						firstSoundOut = output;
					outputs[i] = output;
				}
				
				if( output.DoneRawAsync() ) {
					output.PlayRawAsync( chunk );
					return;
				}
			}
		}
		
		void DisposeSound() {
			DisposeOutputs( ref monoOutputs );
			DisposeOutputs( ref stereoOutputs );
			if( firstSoundOut != null ) {
				firstSoundOut.Dispose();
				firstSoundOut = null;
			}
		}
		
		void DisposeOutputs( ref IAudioOutput[] outputs ) {
			if( outputs == null ) return;
			bool soundPlaying = true;
			
			while( soundPlaying ) {
				soundPlaying = false;
				for( int i = 0; i < outputs.Length; i++ ) {
					if( outputs[i] == null ) continue;
					soundPlaying |= !outputs[i].DoneRawAsync();
				}
				if( soundPlaying )
					Thread.Sleep( 1 );
			}
			
			for( int i = 0; i < outputs.Length; i++ ) {
				if( outputs[i] == null || outputs[i] == firstSoundOut ) continue;
				outputs[i].Dispose();
			}
			outputs = null;
		}		
	}
}