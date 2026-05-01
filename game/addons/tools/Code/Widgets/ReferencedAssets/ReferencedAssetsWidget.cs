namespace Editor;

public class ReferencedAssetsWidget : Widget
{
	List<Asset> ReferencedAssets;
	Asset TargetAsset;
	List<SceneAssetReference> SceneReferences;
	bool IsSceneSearchMode;

	/// <summary>
	/// Simple mode: just display a list of assets
	/// </summary>
	public ReferencedAssetsWidget( List<Asset> referencedAssets, Widget parent ) : base( parent, false )
	{
		ReferencedAssets = referencedAssets;
		TargetAsset = null;
		SceneReferences = null;
		IsSceneSearchMode = false;

		InitializeUI();
		RebuildContent();
	}

	/// <summary>
	/// Scene search mode: display scene references for a specific asset
	/// </summary>
	public ReferencedAssetsWidget( Asset targetAsset, Widget parent, bool searchInScenes ) : base( parent, false )
	{
		ReferencedAssets = null;
		TargetAsset = targetAsset;
		SceneReferences = null;
		IsSceneSearchMode = searchInScenes;

		InitializeUI();

		if ( searchInScenes )
		{
			SearchInScenes();
		}

		RebuildContent();
	}

	private void InitializeUI()
	{
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;
		WindowTitle = TargetAsset is not null
			? $"Find '{TargetAsset.Name}' in Scenes"
			: "Referenced Assets";
		SetWindowIcon( "search" );
	}

	/// <summary>
	/// Perform the scene search
	/// </summary>
	private void SearchInScenes()
	{
		if ( TargetAsset is null )
			return;

		// Run search
		SceneReferences = SceneReferenceFinder.FindReferencesInScenes( TargetAsset );
	}

	private void RebuildContent()
	{
		// Clear existing content
		Layout.Clear( true );

		if ( IsSceneSearchMode && SceneReferences is not null )
		{
			BuildSceneReferencesUI();
		}
		else if ( ReferencedAssets is not null )
		{
			BuildSimpleAssetListUI();
		}
	}

	/// <summary>
	/// Build UI for simple asset list mode
	/// </summary>
	private void BuildSimpleAssetListUI()
	{
		if ( ReferencedAssets is null || ReferencedAssets.Count == 0 )
		{
			var emptyLabel = new Label( "No referenced assets found." );
			emptyLabel.Alignment = TextFlag.Center;
			Layout.Add( emptyLabel );
			return;
		}

		var scroll = new ScrollArea( this );
		Layout.Add( scroll, 1 );
		var container = scroll.Canvas = new Widget( this );
		container.Layout = Layout.Column();
		container.SetSizeMode( SizeMode.Default, SizeMode.CanGrow );

		foreach ( var asset in ReferencedAssets.Where( a => a is not null && !a.IsDeleted ) )
		{
			var button = new AssetReferenceButton( asset, container );
			button.Clicked = () =>
			{
				asset.OpenInEditor();
			};
			container.Layout.Add( button );
		}

		container.Layout.AddStretchCell();
	}

	/// <summary>
	/// Build UI for scene references mode
	/// </summary>
	private void BuildSceneReferencesUI()
	{
		if ( SceneReferences is null || SceneReferences.Count == 0 )
		{
			var emptyLabel = new Label( $"'{TargetAsset?.Name}' is not referenced in any scenes." );
			emptyLabel.Alignment = TextFlag.Center;
			Layout.Add( emptyLabel );
			Layout.AddStretchCell();
			return;
		}

		// Add header with summary
		var headerLayout = Layout.AddRow();
		headerLayout.Add( new Label( $"Found {SceneReferences.Count} reference(s) in {SceneReferences.Select( r => r.SceneAsset.Path ).Distinct().Count()} scene(s):" ) );
		headerLayout.AddStretchCell();

		// Refresh button
		var refreshButton = new Button( "Refresh", "refresh" );
		refreshButton.Clicked = () =>
		{
			SearchInScenes();
			RebuildContent();
		};
		headerLayout.Add( refreshButton );

		// Group references by scene
		var groupedReferences = SceneReferences
			.GroupBy( r => r.SceneAsset )
			.OrderBy( g => g.Key.Name )
			.ToList();

		var scroll = new ScrollArea( this );
		Layout.Add( scroll, 1 );
		var container = scroll.Canvas = new Widget( this );
		container.Layout = Layout.Column();
		container.Layout.Spacing = 4;
		container.Layout.Margin = 4;
		container.SetSizeMode( SizeMode.Default, SizeMode.CanGrow );

		foreach ( var group in groupedReferences )
		{
			var sceneAsset = group.Key;
			var references = group.ToList();

			// Scene header with expand/collapse
			var sceneHeader = new SceneHeaderWidget( sceneAsset, references.Count, container );
			container.Layout.Add( sceneHeader );

			// References for this scene
			var refsContainer = new Widget( container );
			refsContainer.Layout = Layout.Column();
			refsContainer.Layout.Margin = 16;
			refsContainer.Layout.Spacing = 2;

			foreach ( var reference in references )
			{
				var refWidget = new SceneReferenceRow( reference, refsContainer );
				refsContainer.Layout.Add( refWidget );
			}

			container.Layout.Add( refsContainer );
		}

		container.Layout.AddStretchCell();
	}
}

/// <summary>
/// Button displaying an asset reference
/// </summary>
internal class AssetReferenceButton : Button
{
	public Asset Asset { get; }

	public AssetReferenceButton( Asset asset, Widget parent ) : base( parent )
	{
		Asset = asset;
		FixedHeight = 32;
		Cursor = CursorShape.Finger;

		Layout = Layout.Row();
		Layout.Spacing = 8;
		Layout.Margin = 4;

		// Name and path
		var textLayout = Layout.AddColumn();
		textLayout.Add( new Label( asset.Name ) ).SetStyles( "font-weight: bold;" );
		textLayout.Add( new Label( asset.Path ) ).SetStyles( "color: #888; font-size: 11px;" );

		Layout.AddStretchCell();

		// "Find in Scenes" button
		var findButton = new Button( "", "search", this );
		findButton.ToolTip = "Find in Scenes";
		findButton.FixedWidth = 28;
		findButton.Clicked = () =>
		{
			var widget = new ReferencedAssetsWidget( asset, null, searchInScenes: true );
            widget.Width = 800;
            widget.Height = 500;
			widget.Show();
		};
		Layout.Add( findButton );
	}
}

/// <summary>
/// Header widget for a scene in the references list
/// </summary>
internal class SceneHeaderWidget : Button
{
	public Asset SceneAsset { get; }
	public int ReferenceCount { get; }

	public SceneHeaderWidget( Asset sceneAsset, int referenceCount, Widget parent ) : base( parent )
	{
		SceneAsset = sceneAsset;
		ReferenceCount = referenceCount;

		FixedHeight = 36;
		Cursor = CursorShape.Finger;

		Layout = Layout.Row();
		Layout.Spacing = 8;
		Layout.Margin = 8;

		// Scene name and reference count
		var textLayout = Layout.AddColumn();
		textLayout.Add( new Label( sceneAsset.Name ) ).SetStyles( "font-weight: bold;" );
		textLayout.Add( new Label( $"{referenceCount} reference(s)" ) ).SetStyles( "color: #888; font-size: 11px;" );

		Layout.AddStretchCell();

		// Open scene button
		var openButton = new Button( "Open", "open_in_new", this );
		openButton.Clicked = () =>
		{
			sceneAsset.OpenInEditor();
		};
		Layout.Add( openButton );

		Clicked = () =>
		{
			// Toggle visibility of references container (next sibling)
			var index = Parent?.Children?.ToList()?.IndexOf( this ) ?? -1;
			if ( index >= 0 )
			{
				var children = Parent?.Children?.ToList();
				if ( children is not null && index + 1 < children.Count )
				{
					children[index + 1].Visible = !children[index + 1].Visible;
				}
			}
		};
	}
}

/// <summary>
/// Row widget displaying a single scene reference
/// </summary>
internal class SceneReferenceRow : Button
{
	public SceneAssetReference Reference { get; }

	public SceneReferenceRow( SceneAssetReference reference, Widget parent ) : base( parent )
	{
		Reference = reference;

		FixedHeight = 28;
		Cursor = CursorShape.Finger;

		Layout = Layout.Row();
		Layout.Spacing = 8;
		Layout.Margin = 4;

		// Indent
		Layout.Add( new Widget( this ) { FixedWidth = 16 } );

		// GameObject info
		var gameObjectName = string.IsNullOrEmpty( reference.GameObjectName )
			? $"GameObject ({reference.GameObjectId.ToString()[..8]}...)"
			: reference.GameObjectName;

		var textLayout = Layout.AddColumn();
		textLayout.Add( new Label( gameObjectName ) );

		// Component path and reference value
		if ( !string.IsNullOrEmpty( reference.ComponentPath ) )
		{
			var detailText = reference.ComponentPath;
			if ( !string.IsNullOrEmpty( reference.ReferenceValue ) && reference.ReferenceValue != reference.ComponentPath )
			{
				detailText += $" = {reference.ReferenceValue}";
			}
			textLayout.Add( new Label( detailText ) ).SetStyles( "color: #888; font-size: 11px;" );
		}

		Layout.AddStretchCell();

		// Click to open the scene and select the GameObject
		Clicked = () =>
		{
			SceneReferenceFinder.OpenAndSelect( reference );
		};
	}
}
