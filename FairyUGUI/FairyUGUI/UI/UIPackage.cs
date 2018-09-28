﻿using Core.Xml;
using FairyUGUI.Core;
using FairyUGUI.Core.Fonts;
using FairyUGUI.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Logger = Core.Misc.Logger;
using Object = UnityEngine.Object;

namespace FairyUGUI.UI
{
	/// <summary>
	/// A UI Package contains a description file and some texture,sound assets.
	/// </summary>
	public class UIPackage
	{
		/// <summary>
		/// Package id. It is generated by the Editor, or set by customId.
		/// </summary>
		public string id { get; private set; }

		/// <summary>
		/// Package name.
		/// </summary>
		public string name { get; private set; }

		/// <summary>
		/// The path relative to the resources folder.
		/// </summary>
		public string assetPath { get; private set; }

		/// <summary>
		/// 
		/// </summary>
		/// <param name="name"></param>
		/// <param name="extension"></param>
		/// <param name="type"></param>
		/// <returns></returns>
		public delegate Object LoadResource( string name, string extension, Type type );

		private readonly List<PackageItem> _items = new List<PackageItem>();
		private Dictionary<string, PackageItem> _itemsById;
		private Dictionary<string, PackageItem> _itemsByName;
		private Dictionary<string, string> _descPack;
		//private Dictionary<string, PixelHitTestData> _hitTestDatas = new Dictionary<string, PixelHitTestData>();;
		AssetBundle _resBundle;
		string _assetNamePrefix;
		string _customId;

		private readonly Dictionary<string, string> _spriteIdToAltasId = new Dictionary<string, string>();

		static readonly Dictionary<string, UIPackage> PACKAGE_INST_BY_ID = new Dictionary<string, UIPackage>();
		static readonly Dictionary<string, UIPackage> PACKAGE_INST_BY_NAME = new Dictionary<string, UIPackage>();
		static readonly List<UIPackage> PACKAGE_LIST = new List<UIPackage>();
		static Dictionary<string, Dictionary<string, string>> _stringsSource;

		static readonly char[] SEP0 = { ',' };
		static readonly char[] SEP1 = { '\n' };
		static readonly char[] SEP2 = { ' ' };
		static readonly char[] SEP3 = { '=' };

		internal static int constructing;
		internal const string URL_PREFIX = "ui://";

		/// <summary>
		/// Set a custom id for package, then you can use it in GetById.
		/// </summary>
		public string customId
		{
			get => this._customId;
			set
			{
				if ( this._customId != null )
					PACKAGE_INST_BY_ID.Remove( this._customId );
				this._customId = value;
				if ( this._customId != null )
					PACKAGE_INST_BY_ID[this._customId] = this;
			}
		}

		private void Dispose( bool allowDestroyingAssets )
		{
			int cnt = this._items.Count;
			for ( int i = 0; i < cnt; i++ )
			{
				PackageItem pi = this._items[i];
				if ( pi.texture != null )
				{
					pi.texture.Dispose( allowDestroyingAssets );
					pi.texture = null;
				}
				else if ( pi.audioClip != null )
				{
					if ( allowDestroyingAssets )
						Object.DestroyImmediate( pi.audioClip, true );
					pi.audioClip = null;
				}
				else if ( pi.bitmapFont != null )
				{
					FontManager.UnregisterFont( pi.bitmapFont );
				}
			}
			this._items.Clear();
			this._resBundle?.Unload( true );
		}

		/// <summary>
		/// Return a UIPackage with a certain id.
		/// </summary>
		/// <param name="id">ID of the package.</param>
		/// <returns>UIPackage</returns>
		public static UIPackage GetById( string id )
		{
			UIPackage pkg;
			if ( PACKAGE_INST_BY_ID.TryGetValue( id, out pkg ) )
				return pkg;
			return null;
		}

		/// <summary>
		/// Return a UIPackage with a certain name.
		/// </summary>
		/// <param name="name">Name of the package.</param>
		/// <returns>UIPackage</returns>
		public static UIPackage GetByName( string name )
		{
			UIPackage pkg;
			if ( PACKAGE_INST_BY_NAME.TryGetValue( name, out pkg ) )
				return pkg;
			return null;
		}

		/// <summary>
		/// Add a UI package from assetbundle.
		/// </summary>
		/// <param name="bundle">A assetbundle.</param>
		/// <returns>UIPackage</returns>
		public static UIPackage AddPackage( AssetBundle bundle )
		{
			return AddPackage( bundle, bundle );
		}

		/// <summary>
		/// Add a UI package from two assetbundles with a optional main asset name.
		/// </summary>
		/// <param name="desc">A assetbunble contains description file.</param>
		/// <param name="res">A assetbundle contains resources.</param>
		/// <param name="mainAssetName">Main asset name.</param>
		/// <returns>UIPackage</returns>
		public static UIPackage AddPackage( AssetBundle desc, AssetBundle res, string mainAssetName = null )
		{
			byte[] source = null;
			if ( mainAssetName != null )
			{
				TextAsset ta = desc.LoadAsset<TextAsset>( mainAssetName );
				if ( ta != null )
					source = ta.bytes;
			}
			else
			{
				string[] names = desc.GetAllAssetNames();
				foreach ( string n in names )
				{
					if ( n.IndexOf( "@", StringComparison.Ordinal ) == -1 )
					{
						TextAsset ta = desc.LoadAsset<TextAsset>( n );
						if ( ta != null )
						{
							source = ta.bytes;
							mainAssetName = Path.GetFileNameWithoutExtension( n );
							break;
						}
					}
				}
			}
			if ( source == null )
				throw new Exception( "FairyGUI: invalid package." );
			if ( desc != res )
				desc.Unload( true );

			UIPackage pkg = new UIPackage();
			pkg.Create( source, res, mainAssetName );
			PACKAGE_INST_BY_ID[pkg.id] = pkg;
			PACKAGE_INST_BY_NAME[pkg.name] = pkg;
			PACKAGE_LIST.Add( pkg );
			return pkg;
		}

		/// <summary>
		/// Add a UI package from a description text and a assetbundle, with a optional main asset name.
		/// </summary>
		/// <param name="desc">Description text.</param>
		/// <param name="res">A assetbundle contains resources.</param>
		/// <param name="mainAssetName">Main asset name.</param>
		/// <returns>UIPackage</returns>
		public static UIPackage AddPackage( byte[] desc, AssetBundle res, string mainAssetName = null )
		{
			UIPackage pkg = new UIPackage();
			pkg.Create( desc, res, mainAssetName );
			PACKAGE_INST_BY_ID[pkg.id] = pkg;
			PACKAGE_INST_BY_NAME[pkg.name] = pkg;
			PACKAGE_LIST.Add( pkg );
			return pkg;
		}

		/// <summary>
		/// Add a UI package from a path relative to Unity Resources path.
		/// </summary>
		/// <param name="assetPath">Path relative to Unity Resources path.</param>
		/// <returns>UIPackage</returns>
		public static UIPackage AddPackage( string assetPath )
		{
			if ( PACKAGE_INST_BY_ID.ContainsKey( assetPath ) )
				return PACKAGE_INST_BY_ID[assetPath];

			TextAsset asset = Resources.Load<TextAsset>( assetPath );
			if ( asset == null )
			{
				if ( Application.isPlaying )
					throw new Exception( "FairyGUI: Cannot load ui package in '" + assetPath + "'" );
				Logger.Warn( "FairyGUI: Cannot load ui package in '" + assetPath + "'" );
			}
			else
			{
				UIPackage pkg = new UIPackage();
				pkg.Create( asset.bytes, null, assetPath );
				PACKAGE_INST_BY_ID[pkg.id] = pkg;
				PACKAGE_INST_BY_NAME[pkg.name] = pkg;
				PACKAGE_INST_BY_ID[assetPath] = pkg;
				PACKAGE_LIST.Add( pkg );
				pkg.assetPath = assetPath;
				return pkg;
			}
			return null;
		}

		/// <summary>
		/// 使用自定义的加载方式载入一个包。
		/// </summary>
		/// <param name="descData">描述文件数据。</param>
		/// <param name="assetNamePrefix">资源文件名前缀。如果包含，则载入资源时名称将传入assetNamePrefix@resFileName这样格式。可以为空。</param>
		/// <returns></returns>
		public static UIPackage AddPackage( byte[] descData, string assetNamePrefix )
		{
			UIPackage pkg = new UIPackage();
			pkg.Create( descData, null, assetNamePrefix );
			if ( PACKAGE_INST_BY_ID.ContainsKey( pkg.id ) )
				Debug.LogWarning( "FairyGUI: Package id conflicts, '" + pkg.name + "' and '" + PACKAGE_INST_BY_ID[pkg.id].name + "'" );
			PACKAGE_INST_BY_ID[pkg.id] = pkg;
			PACKAGE_INST_BY_NAME[pkg.name] = pkg;
			PACKAGE_LIST.Add( pkg );

			return pkg;
		}

		/// <summary>
		/// Remove a package. All resources in this package will be disposed.
		/// </summary>
		/// <param name="packageIdOrName"></param>
		/// <param name="allowDestroyingAssets"></param>
		public static void RemovePackage( string packageIdOrName, bool allowDestroyingAssets = false )
		{
			UIPackage pkg;
			if ( !PACKAGE_INST_BY_ID.TryGetValue( packageIdOrName, out pkg ) && !PACKAGE_INST_BY_NAME.TryGetValue( packageIdOrName, out pkg ) )
			{
				throw new Exception( "FairyGUI: '" + packageIdOrName + "' is not a valid package id or name." );
			}
			pkg.Dispose( allowDestroyingAssets );
			PACKAGE_INST_BY_ID.Remove( pkg.id );
			if ( pkg._customId != null )
			{
				PACKAGE_INST_BY_ID.Remove( pkg._customId );
			}
			if ( pkg.assetPath != null )
			{
				PACKAGE_INST_BY_ID.Remove( pkg.assetPath );
			}
			PACKAGE_INST_BY_NAME.Remove( pkg.name );
			PACKAGE_LIST.Remove( pkg );
		}

		public static void RemoveAllPackages()
		{
			RemoveAllPackages( false );
		}

		public static void RemoveAllPackages( bool allowDestroyAssets )
		{
			if ( PACKAGE_INST_BY_ID.Count > 0 )
			{
				UIPackage[] array = PACKAGE_LIST.ToArray();
				for ( int i = 0; i < array.Length; i++ )
				{
					array[i].Dispose( allowDestroyAssets );
				}
			}
			PACKAGE_INST_BY_ID.Clear();
			PACKAGE_INST_BY_ID.Clear();
			PACKAGE_INST_BY_NAME.Clear();
		}

		public static List<UIPackage> GetPackages()
		{
			return PACKAGE_LIST;
		}

		//public PixelHitTestData GetPixelHitTestData( string itemId )
		//{
		//	PixelHitTestData ret;
		//	if ( this._hitTestDatas.TryGetValue( itemId, out ret ) )
		//		return ret;
		//	else
		//		return null;
		//}

		void Create( byte[] desc, AssetBundle res, string mainAssetName )
		{
			this._descPack = new Dictionary<string, string>();
			this._resBundle = res;

			this.DecodeDesc( desc );

			if ( res != null )
			{
				if ( !string.IsNullOrEmpty( mainAssetName ) )
					this._assetNamePrefix = mainAssetName + "@";
				else
					this._assetNamePrefix = string.Empty;
			}
			else
			{
				this._assetNamePrefix = mainAssetName + "@";
			}

			this.LoadPackage();
		}

		/// <summary>
		/// Create a UI object.
		/// </summary>
		/// <param name="pkgName">Package name.</param>
		/// <param name="resName">Resource name.</param>
		/// <returns>A UI object.</returns>
		public static GObject CreateObject( string pkgName, string resName )
		{
			UIPackage pkg = GetByName( pkgName );
			return pkg?.CreateObject( resName );
		}

		/// <summary>
		///  Create a UI object.
		/// </summary>
		/// <param name="pkgName">Package name.</param>
		/// <param name="resName">Resource name.</param>
		/// <param name="userClass">Custom implementation of this object.</param>
		/// <returns>A UI object.</returns>
		public static GObject CreateObject( string pkgName, string resName, Type userClass )
		{
			UIPackage pkg = GetByName( pkgName );
			return pkg?.CreateObject( resName, userClass );
		}

		/// <summary>
		/// Create a UI object.
		/// </summary>
		/// <param name="url">Resource url.</param>
		/// <returns>A UI object.</returns>
		public static GObject CreateObjectFromURL( string url )
		{
			PackageItem pi = GetItemByURL( url );
			return pi?.owner.CreateObject( pi, null );
		}

		/// <summary>
		/// Create a UI object.
		/// </summary>
		/// <param name="url">Resource url.</param>
		/// <param name="userClass">Custom implementation of this object.</param>
		/// <returns>A UI object.</returns>
		public static GObject CreateObjectFromURL( string url, Type userClass )
		{
			PackageItem pi = GetItemByURL( url );
			return pi?.owner.CreateObject( pi, userClass );
		}

		/// <summary>
		/// Set strings source.
		/// </summary>
		/// <param name="source"></param>
		public static void SetStringsSource( XML source )
		{
			_stringsSource = new Dictionary<string, Dictionary<string, string>>();
			XMLList list = source.Elements( "string" );
			foreach ( XML cxml in list )
			{
				string key = cxml.GetAttribute( "name" );
				string text = cxml.text;
				int i = key.IndexOf( "-", StringComparison.Ordinal );
				if ( i == -1 )
					continue;

				string key2 = key.Substring( 0, i );
				string key3 = key.Substring( i + 1 );
				Dictionary<string, string> col = _stringsSource[key2];
				if ( col == null )
				{
					col = new Dictionary<string, string>();
					_stringsSource[key2] = col;
				}
				col[key3] = text;
			}
		}

		void DecodeDesc( byte[] descBytes )
		{
			if ( descBytes.Length < 4
				|| descBytes[0] != 0x50 || descBytes[1] != 0x4b || descBytes[2] != 0x03 || descBytes[3] != 0x04 )
			{
				string source = Encoding.UTF8.GetString( descBytes );
				int curr = 0;
				string fn;
				int size;
				while ( true )
				{
					int pos = source.IndexOf( "|", curr, StringComparison.Ordinal );
					if ( pos == -1 )
						break;
					fn = source.Substring( curr, pos - curr );
					curr = pos + 1;
					pos = source.IndexOf( "|", curr, StringComparison.Ordinal );
					size = int.Parse( source.Substring( curr, pos - curr ) );
					curr = pos + 1;
					this._descPack[fn] = source.Substring( curr, size );
					curr += size;
				}
			}
			else
			{
				ZipReader zip = new ZipReader( descBytes );
				ZipReader.ZipEntry entry = new ZipReader.ZipEntry();
				while ( zip.GetNextEntry( entry ) )
				{
					if ( entry.isDirectory )
						continue;

					this._descPack[entry.name] = Encoding.UTF8.GetString( zip.GetEntryData( entry ) );
				}
			}
		}

		void LoadPackage()
		{
			string str = this.LoadString( "sprites.bytes" );
			if ( str == null )
			{
				Logger.Error( "FairyGUI: cannot load package from " + this._assetNamePrefix );
				return;
			}
			string[] arr = str.Split( SEP1 );
			int cnt = arr.Length;
			for ( int i = 1; i < cnt; i++ )
			{
				str = arr[i];
				if ( str.Length == 0 )
					continue;

				string[] arr2 = str.Split( SEP2 );
				string itemId = arr2[0];
				string atlas;
				int binIndex = int.Parse( arr2[1] );
				if ( binIndex >= 0 )
					atlas = "atlas" + binIndex;
				else
				{
					int pos = itemId.IndexOf( "_", StringComparison.Ordinal );
					if ( pos == -1 )
						atlas = "atlas_" + itemId;
					else
						atlas = "atlas_" + itemId.Substring( 0, pos );
				}
				this._spriteIdToAltasId[itemId] = atlas;
			}

			//byte[] hittestData = this.LoadBinary( "hittest.bytes" );
			//if ( hittestData != null )
			//{
			//	ByteBuffer ba = new ByteBuffer( hittestData );
			//	while ( ba.bytesAvailable )
			//	{
			//		PixelHitTestData pht = new PixelHitTestData();
			//		this._hitTestDatas[ba.ReadString()] = pht;
			//		pht.Load( ba );
			//	}
			//}

			str = this._descPack["package.xml"];
			XML xml = new XML( str );

			this.id = xml.GetAttribute( "id" );
			this.name = xml.GetAttribute( "name" );

			XML rxml = xml.GetNode( "resources" );
			if ( rxml == null )
				throw new Exception( "Invalid package xml" );

			XMLList resources = rxml.Elements();

			this._itemsById = new Dictionary<string, PackageItem>();
			this._itemsByName = new Dictionary<string, PackageItem>();

			foreach ( XML cxml in resources )
			{
				PackageItem pi = new PackageItem
				{
					owner = this,
					type = FieldTypes.ParsePackageItemType( cxml.name ),
					id = cxml.GetAttribute( "id" ),
					name = cxml.GetAttribute( "name" ),
					exported = cxml.GetAttributeBool( "exported" ),
					file = cxml.GetAttribute( "file" )
				};
				str = cxml.GetAttribute( "size" );
				if ( str != null )
				{
					arr = str.Split( SEP0 );
					pi.width = int.Parse( arr[0] );
					pi.height = int.Parse( arr[1] );
				}
				switch ( pi.type )
				{
					case PackageItemType.Image:
						{
							string scale = cxml.GetAttribute( "scale" );
							switch ( scale )
							{
								case "9grid":
									pi.scaleMode = ImageScaleMode.Grid9;
									break;
								case "tile":
									pi.scaleMode = ImageScaleMode.Tile;
									break;
							}
							break;
						}

					case PackageItemType.Font:
						{
							pi.bitmapFont = new BitmapFont( pi );
							FontManager.RegisterFont( pi.bitmapFont, null );
							break;
						}
				}
				this._items.Add( pi );
				this._itemsById[pi.id] = pi;
				if ( pi.name != null )
					this._itemsByName[pi.name] = pi;
			}

			bool preloadAll = Application.isPlaying;
			if ( preloadAll )
			{
				cnt = this._items.Count;
				for ( int i = 0; i < cnt; i++ )
					this.GetItemAsset( this._items[i] );

				this._descPack = null;
				this._spriteIdToAltasId.Clear();
			}
			else
				this._items.Sort( ComparePackageItem );

			if ( this._resBundle != null )
			{
				this._resBundle.Unload( false );
				this._resBundle = null;
			}
		}

		static int ComparePackageItem( PackageItem p1, PackageItem p2 )
		{
			if ( p1.name != null && p2.name != null )
				return String.Compare( p1.name, p2.name, StringComparison.Ordinal );
			return 0;
		}

		public GObject CreateObject( string resName )
		{
			PackageItem pi;
			if ( !this._itemsByName.TryGetValue( resName, out pi ) )
			{
				Logger.Error( "FairyGUI: resource not found - " + resName + " in " + this.name );
				return null;
			}

			return this.CreateObject( pi, null );
		}

		public GObject CreateObject( string resName, Type userClass )
		{
			PackageItem pi;
			if ( !this._itemsByName.TryGetValue( resName, out pi ) )
			{
				Logger.Error( "FairyGUI: resource not found - " + resName + " in " + this.name );
				return null;
			}

			return this.CreateObject( pi, userClass );
		}

		internal GObject CreateObject( PackageItem item, Type userClass )
		{
			GObject g;
			if ( item.type == PackageItemType.Component )
			{
				if ( userClass != null )
					g = ( GComponent ) userClass.Assembly.CreateInstance( userClass.FullName );
				else
					g = UIObjectFactory.NewObject( item );
			}
			else
				g = UIObjectFactory.NewObject( item );

			if ( g == null )
				return null;

			constructing++;
			g.ConstructFromResource( item );
			constructing--;
			return g;
		}

		public List<PackageItem> GetItems()
		{
			return this._items;
		}

		public PackageItem GetItem( string itemId )
		{
			PackageItem pi;
			if ( this._itemsById.TryGetValue( itemId, out pi ) )
				return pi;
			return null;
		}

		/// <summary>
		/// Get a asset with a certain name.
		/// </summary>
		/// <param name="url">Resource url.</param>
		/// <returns>If resource is atlas, returns NTexture; If resource is sound, returns AudioClip.</returns>
		public static object GetItemAssetByURL( string url )
		{
			PackageItem item = GetItemByURL( url );

			return item?.owner.GetItemAsset( item );
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="resName"></param>
		/// <returns></returns>
		public object GetItemAsset( string resName )
		{
			PackageItem pi;
			if ( !this._itemsByName.TryGetValue( resName, out pi ) )
			{
				Logger.Error( "FairyGUI: Resource not found - " + resName + " in " + this.name );
				return null;
			}

			return this.GetItemAsset( pi );
		}

		/// <summary>
		/// Get a asset with a certain name.
		/// </summary>
		/// <param name="pkgName">Package name.</param>
		/// <param name="resName">Resource name.</param>
		/// <returns>If resource is atlas, returns NTexture; If resource is sound, returns AudioClip.</returns>
		public static object GetItemAsset( string pkgName, string resName )
		{
			UIPackage pkg = GetByName( pkgName );
			return pkg?.GetItemAsset( resName );
		}

		internal object GetItemAsset( PackageItem item )
		{
			switch ( item.type )
			{
				case PackageItemType.Image:
					if ( !item.decoded )
					{
						item.decoded = true;
						item.sprite = this.GetSprite( item.id );
					}
					return item.sprite;

				case PackageItemType.Atlas:
					if ( !item.decoded )
					{
						item.decoded = true;
						string fileName = string.IsNullOrEmpty( item.file ) ? ( item.id + ".png" ) : item.file;
						string filePath = this._assetNamePrefix + Path.GetFileNameWithoutExtension( fileName );

						Texture2D tex;
						Sprite[] sprites;
						if ( this._resBundle != null )
						{
							tex = this._resBundle.LoadAsset<Texture2D>( filePath );
							sprites = this._resBundle.LoadAllAssets<Sprite>();
						}
						else
						{
							tex = Resources.Load<Texture2D>( filePath );
							sprites = Resources.LoadAll<Sprite>( filePath );
						}
						if ( tex == null )
						{
							Logger.Warn( "FairyGUI: texture '" + fileName + "' not found in " + this.name );
							item.texture = NTexture.emptyTexture;
						}
						else
						{
							if ( tex.mipmapCount > 1 )
								Logger.Warn( "FairyGUI: texture '" + fileName + "' in " + this.name + " is mipmaps enabled." );

							filePath = filePath + "!a";
							Texture2D alphaTex = this._resBundle != null ? this._resBundle.LoadAsset<Texture2D>( filePath ) : Resources.Load<Texture2D>( filePath );

							item.texture = new NTexture( tex, alphaTex, sprites );
						}
					}
					return item.texture;

				case PackageItemType.Sound:
					if ( !item.decoded )
					{
						item.decoded = true;
						string fileName = this._assetNamePrefix + Path.GetFileNameWithoutExtension( item.file );
						item.audioClip = this._resBundle != null ? this._resBundle.LoadAsset<AudioClip>( fileName ) : Resources.Load<AudioClip>( fileName );
					}
					return item.audioClip;

				case PackageItemType.Font:
					if ( !item.decoded )
					{
						item.decoded = true;
						this.LoadFont( item );
					}
					return item.bitmapFont;

				case PackageItemType.MovieClip:
					if ( !item.decoded )
					{
						item.decoded = true;
						this.LoadMovieClip( item );
					}
					return item.frames;

				case PackageItemType.Component:
					if ( !item.decoded )
					{
						item.decoded = true;
						string str = this._descPack[item.id + ".xml"];
						XML xml = new XML( str );
						if ( _stringsSource != null )
						{
							Dictionary<string, string> strings;
							if ( _stringsSource.TryGetValue( this.id + item.id, out strings ) )
								this.TranslateComponent( xml, strings );
						}
						item.componentData = xml;
					}
					return item.componentData;

				default:
					if ( !item.decoded )
					{
						item.decoded = true;
						item.binary = this.LoadBinary( item.file );
					}
					return item.binary;
			}
		}

		/// <summary>
		/// Get url of an item in package.
		/// </summary>
		/// <param name="pkgName">Package name.</param>
		/// <param name="resName">Resource name.</param>
		/// <returns>Url.</returns>
		public static string GetItemURL( string pkgName, string resName )
		{
			UIPackage pkg = GetByName( pkgName );
			if ( pkg == null )
				return null;

			PackageItem pi;
			if ( !pkg._itemsByName.TryGetValue( resName, out pi ) )
				return null;

			return URL_PREFIX + pkg.id + pi.id;
		}

		public static PackageItem GetItemByURL( string url )
		{
			if ( url == null )
			{
				return null;
			}
			int pos = url.IndexOf( "//", StringComparison.Ordinal );
			if ( pos == -1 )
			{
				return null;
			}
			int pos2 = url.IndexOf( '/', pos + 2 );
			if ( pos2 == -1 )
			{
				if ( url.Length > 13 )
				{
					UIPackage pkg = GetById( url.Substring( 5, 8 ) );
					if ( pkg != null )
					{
						string srcId = url.Substring( 13 );
						return pkg.GetItem( srcId );
					}
				}
			}
			else
			{
				UIPackage pkg2 = GetByName( url.Substring( pos + 2, pos2 - pos - 2 ) );
				if ( pkg2 != null )
				{
					string srcName = url.Substring( pos2 + 1 );
					return pkg2.GetItemByName( srcName );
				}
			}
			return null;
		}

		public PackageItem GetItemByName( string itemName )
		{
			PackageItem pi;
			if ( this._itemsByName.TryGetValue( itemName, out pi ) )
				return pi;
			return null;
		}

		private NSprite GetSprite( string spriteId )
		{
			return this.GetAtlasBySpriteId( spriteId ).GetSprite( spriteId );
		}

		private NTexture GetAtlas( string atlasId )
		{
			PackageItem atlasPi;
			if ( this._itemsById.TryGetValue( atlasId, out atlasPi ) )
				return ( NTexture ) this.GetItemAsset( atlasPi );
			return NTexture.emptyTexture;
		}

		private NTexture GetAtlasBySpriteId( string spriteId )
		{
			string atlasId;
			if ( this._spriteIdToAltasId.TryGetValue( spriteId, out atlasId ) )
				return this.GetAtlas( atlasId );
			return NTexture.emptyTexture;
		}

		private void TranslateComponent( XML xml, Dictionary<string, string> strings )
		{
			XML listNode = xml.GetNode( "displayList" );
			if ( listNode == null )
				return;

			XMLList col = listNode.Elements();

			foreach ( XML cxml in col )
			{
				string ename = cxml.name;
				string elementId = cxml.GetAttribute( "id" );
				string value;
				if ( cxml.HasAttribute( "tooltips" ) )
				{
					if ( strings.TryGetValue( elementId + "-tips", out value ) )
						cxml.SetAttribute( "tooltips", value );
				}

				if ( ename == "text" || ename == "richtext" )
				{
					if ( strings.TryGetValue( elementId, out value ) )
						cxml.SetAttribute( "text", value );
				}
				else if ( ename == "list" )
				{
					XMLList items = cxml.Elements( "item" );
					int j = 0;
					foreach ( XML exml in items )
					{
						if ( strings.TryGetValue( elementId + "-" + j, out value ) )
							exml.SetAttribute( "title", value );
						j++;
					}
				}
				else if ( ename == "component" )
				{
					XML dxml = cxml.GetNode( "Button" );
					if ( dxml != null )
					{
						if ( strings.TryGetValue( elementId, out value ) )
							dxml.SetAttribute( "title", value );
						if ( strings.TryGetValue( elementId + "-0", out value ) )
							dxml.SetAttribute( "selectedTitle", value );
					}
					else
					{
						dxml = cxml.GetNode( "Label" );
						if ( dxml != null )
						{
							if ( strings.TryGetValue( elementId, out value ) )
								dxml.SetAttribute( "title", value );
						}
						else
						{
							dxml = cxml.GetNode( "ComboBox" );
							if ( dxml != null )
							{
								if ( strings.TryGetValue( elementId, out value ) )
									dxml.SetAttribute( "title", value );

								XMLList items = dxml.Elements( "item" );
								int j = 0;
								foreach ( XML exml in items )
								{
									if ( strings.TryGetValue( elementId + "-" + j, out value ) )
										exml.SetAttribute( "title", value );
									j++;
								}
							}
						}
					}
				}
			}
		}

		private byte[] LoadBinary( string fileName )
		{
			fileName = this._assetNamePrefix + Path.GetFileNameWithoutExtension( fileName );
			TextAsset ta = this._resBundle != null ? this._resBundle.LoadAsset<TextAsset>( fileName ) : Resources.Load<TextAsset>( fileName );
			return ta?.bytes;
		}

		private string LoadString( string fileName )
		{
			byte[] data = this.LoadBinary( fileName );
			if ( data != null )
				return Encoding.UTF8.GetString( data );
			return null;
		}

		private void LoadMovieClip( PackageItem item )
		{
			string str = this._descPack[item.id + ".xml"];
			XML xml = new XML( str );

			string[] arr = xml.GetAttributeArray( "pivot" );
			if ( arr != null )
			{
				item.pivot.x = int.Parse( arr[0] );
				item.pivot.y = int.Parse( arr[1] );
			}
			str = xml.GetAttribute( "interval" );
			if ( str != null )
				item.interval = float.Parse( str ) / 1000f;
			item.swing = xml.GetAttributeBool( "swing" );
			str = xml.GetAttribute( "repeatDelay" );
			if ( str != null )
				item.repeatDelay = float.Parse( str ) / 1000f;
			int frameCount = xml.GetAttributeInt( "frameCount" );
			item.frames = new MovieClip.Frame[frameCount];

			XMLList frameNodes = xml.GetNode( "frames" ).Elements();

			int i = 0;
			foreach ( XML frameNode in frameNodes )
			{
				MovieClip.Frame frame = new MovieClip.Frame();
				arr = frameNode.GetAttributeArray( "rect" );
				frame.rect = new Rect( float.Parse( arr[0] ), float.Parse( arr[1] ), float.Parse( arr[2] ), float.Parse( arr[3] ) );
				str = frameNode.GetAttribute( "addDelay" );
				if ( str != null )
					frame.addDelay = float.Parse( str ) / 1000f;

				frame.sprite = this.GetSprite( item.id + "_" + i );
				item.frames[i] = frame;
				i++;
			}
		}

		private void LoadFont( PackageItem item )
		{
			BitmapFont font = item.bitmapFont;
			Dictionary<string, string> kv = new Dictionary<string, string>();
			List<CharacterInfo> cis = new List<CharacterInfo>();
			NTexture texture = null;
			NSprite ttfSprite = null;
			int size = 0;
			int xadvance = 0;
			float lineHeight = 0;
			bool ttf = false;

			string str = this._descPack[item.id + ".fnt"];
			string[] arr = str.Split( SEP1 );
			int cnt = arr.Length;
			for ( int i = 0; i < cnt; i++ )
			{
				str = arr[i];
				if ( str.Length == 0 )
					continue;

				str = str.Trim();

				string[] arr2 = str.Split( SEP2, StringSplitOptions.RemoveEmptyEntries );
				for ( int j = 1; j < arr2.Length; j++ )
				{
					string[] arr3 = arr2[j].Split( SEP3, StringSplitOptions.RemoveEmptyEntries );
					kv[arr3[0]] = arr3[1];
				}

				str = arr2[0];
				if ( str == "info" )
				{
					if ( kv.TryGetValue( "face", out str ) )
					{
						ttf = true;
						ttfSprite = this.GetSprite( item.id );
						texture = ttfSprite.nTexture;
					}
					if ( kv.TryGetValue( "size", out str ) )
						size = int.Parse( str );
					//if ( kv.TryGetValue( "resizable", out str ) )
					//	resizable = str == "true";
				}
				else if ( str == "common" )
				{
					if ( size == 0 && kv.TryGetValue( "lineHeight", out str ) )
						size = int.Parse( str );
					if ( kv.TryGetValue( "xadvance", out str ) )
						xadvance = int.Parse( str );
				}
				else if ( str == "char" )
				{
					if ( !ttf )
					{
						if ( kv.TryGetValue( "img", out str ) )
						{
							NSprite nSprite = this.GetSprite( str );
							texture = nSprite.nTexture;

							int offsetX = kv.TryGetValue( "xoffset", out str ) ? int.Parse( str ) : 0;
							int offsetY = kv.TryGetValue( "yoffset", out str ) ? int.Parse( str ) : 0;
							int advance = kv.TryGetValue( "xadvance", out str ) ? int.Parse( str ) : xadvance;
							int width = Mathf.CeilToInt( nSprite.rect.width );
							int height = Mathf.CeilToInt( nSprite.rect.height );
							Vector4 uv = UnityEngine.Sprites.DataUtility.GetInnerUV( nSprite.sprite );

							if ( advance == 0 )
								advance = width + offsetX;

							CharacterInfo ci = new CharacterInfo
							{
								index = int.Parse( kv["id"] ),
								uvBottomLeft = new Vector2( uv.x, uv.y ),
								uvTopLeft = new Vector2( uv.x, uv.w ),
								uvTopRight = new Vector2( uv.z, uv.w ),
								uvBottomRight = new Vector2( uv.z, uv.y ),
								advance = advance,
								bearing = offsetX,
								minX = 0,
								maxX = width,
								minY = -height + offsetY,
								maxY = offsetY
							};
							cis.Add( ci );

							lineHeight = Mathf.Max( lineHeight, offsetY < 0 ? height : ( offsetY + height ) );
						}
					}
					else
					{
						int bx = kv.TryGetValue( "x", out str ) ? int.Parse( str ) : 0;
						int by = kv.TryGetValue( "y", out str ) ? int.Parse( str ) : 0;
						int offsetX = kv.TryGetValue( "xoffset", out str ) ? int.Parse( str ) : 0;
						int offsetY = kv.TryGetValue( "yoffset", out str ) ? int.Parse( str ) : 0;
						int advance = kv.TryGetValue( "xadvance", out str ) ? int.Parse( str ) : xadvance;
						int width = kv.TryGetValue( "width", out str ) ? int.Parse( str ) : 0;
						int height = kv.TryGetValue( "height", out str ) ? int.Parse( str ) : 0;

						if ( advance == 0 )
							advance = width + offsetX;

						Vector4 region = new Vector4( bx + ttfSprite.rect.x, ttfSprite.rect.height - ( @by + height ) + ttfSprite.rect.y, width, height );
						Vector4 uv = new Vector4( region.x / texture.width, region.y / texture.height,
							region.z / texture.width, region.w / texture.height );

						CharacterInfo ci = new CharacterInfo
						{
							index = int.Parse( kv["id"] ),
							uvBottomLeft = new Vector2( uv.x, uv.y ),
							uvTopLeft = new Vector2( uv.x, uv.y + uv.w ),
							uvTopRight = new Vector2( uv.x + uv.z, uv.y + uv.w ),
							uvBottomRight = new Vector2( uv.x + uv.z, uv.y ),
							advance = advance,
							bearing = offsetX,
							minX = 0,
							maxX = width,
							minY = -height + offsetY,
							maxY = offsetY
						};
						cis.Add( ci );

						lineHeight = size;
					}
				}
			}
			if ( texture != null && cis.Count > 0 )
				font.MakeFont( texture, cis, Mathf.CeilToInt( lineHeight ) );
		}
	}
}
