﻿using System;
using System.Drawing;
using OpenTK.Input;
#if ANDROID
using Android.Graphics;
#endif

namespace ClassicalSharp {
	
	public sealed class ButtonWidget : Widget {
		
		public ButtonWidget( Game game, Font font ) : base( game ) {
			this.font = font;
		}
		
		public static ButtonWidget Create( Game game, int x, int y, int width, int height, string text, Anchor horizontal,
		                                  Anchor vertical, Font font, ClickHandler onClick ) {
			ButtonWidget widget = new ButtonWidget( game, font );
			widget.Init();
			widget.HorizontalAnchor = horizontal;
			widget.VerticalAnchor = vertical;
			widget.XOffset = x; widget.YOffset = y;
			widget.DesiredMaxWidth = width; widget.DesiredMaxHeight = height;
			widget.SetText( text );
			widget.OnClick = onClick;
			return widget;
		}
		
		Texture texture;
		public int XOffset = 0, YOffset = 0;
		public int DesiredMaxWidth, DesiredMaxHeight;
		int defaultHeight;
		internal Font font;
		
		public override void Init() {
			DrawTextArgs args = new DrawTextArgs( "I", font, true );
			defaultHeight = game.Drawer2D.MeasureSize( ref args ).Height;
			Height = defaultHeight;
		}
		
		static Texture shadowTex = new Texture( 0, 0, 0, 0, 0, 
		                                       new TextureRec( 0, 66/256f, 200/256f, 20/256f ) );
		static Texture selectedTex = new Texture( 0, 0, 0, 0, 0,
		                                         new TextureRec( 0, 86/256f, 200/256f, 20/256f ) );
		static Texture disabledTex = new Texture( 0, 0, 0, 0, 0,
		                                         new TextureRec( 0, 46/256f, 200/256f, 20/256f ) );		
		public string Text;
		public void SetText( string text ) {
			graphicsApi.DeleteTexture( ref texture );
			Text = text;
			if( String.IsNullOrEmpty( text ) ) {
				texture = new Texture();
				Height = defaultHeight;
			} else {
				MakeTexture( text );
				X = texture.X1 = CalcOffset( game.Width, texture.Width, XOffset, HorizontalAnchor );
				Y = texture.Y1 = CalcOffset( game.Height, texture.Height, YOffset, VerticalAnchor );
				Height = texture.Height;
			}
			Width = texture.Width;
		}
		
		public override void Render( double delta ) {
			if( !texture.IsValid )
				return;
			Texture backTex = Active ? selectedTex : shadowTex;
			if( Disabled ) backTex = disabledTex;
			
			backTex.ID = game.UseClassicGui ? game.GuiClassicTexId : game.GuiTexId;
			backTex.X1 = X; backTex.Y1 = Y;
			backTex.Width = Width; backTex.Height = Height;
			
			backTex.Render( graphicsApi );
			FastColour col = Active ? FastColour.White : new FastColour( 200, 200, 200 );
			if( Disabled ) col = new FastColour( 150, 150, 150 );
			texture.Render( graphicsApi, col );
		}
		
		public override void Dispose() {
			graphicsApi.DeleteTexture( ref texture );
		}
		
		public override void MoveTo( int newX, int newY ) {
			int deltaX = newX - X, deltaY = newY - Y;
			texture.X1 += deltaX; texture.Y1 += deltaY;
			X = newX; Y = newY;
		}
		
		public Func<Game, string> GetValue;
		public Action<Game, string> SetValue;
		
		void MakeTexture( string text ) {
			DrawTextArgs args = new DrawTextArgs( text, font, true );
			Size size = game.Drawer2D.MeasureChatSize( ref args );
			
			int xOffset = Math.Max( size.Width, DesiredMaxWidth ) - size.Width;
			size.Width = Math.Max( size.Width, DesiredMaxWidth );
			int yOffset = Math.Max( size.Height, DesiredMaxHeight ) - size.Height;
			size.Height = Math.Max( size.Height, DesiredMaxHeight );
			
			using( Bitmap bmp = IDrawer2D.CreatePow2Bitmap( size ) )
				using( IDrawer2D drawer = game.Drawer2D )
			{
				drawer.SetBitmap( bmp );			
				args.SkipPartsCheck = true;
				drawer.DrawChatText( ref args, xOffset / 2, yOffset / 2 );
				texture = drawer.Make2DTexture( bmp, size, 0, 0 );
			}
		}
	}
}