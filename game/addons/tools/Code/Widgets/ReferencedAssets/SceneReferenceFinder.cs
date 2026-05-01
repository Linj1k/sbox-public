using System.Text.Json.Nodes;
using System.IO;

namespace Editor;

/// <summary>
/// Represents a reference to an asset within a specific scene
/// </summary>
public class SceneAssetReference
{
	/// <summary>
	/// The scene file containing the reference
	/// </summary>
	public Asset SceneAsset { get; set; }

	/// <summary>
	/// The GameObject GUID where the reference was found
	/// </summary>
	public Guid GameObjectId { get; set; }

	/// <summary>
	/// Name of the GameObject if available
	/// </summary>
	public string GameObjectName { get; set; }

	/// <summary>
	/// The component type or property path where the reference was found
	/// </summary>
	public string ComponentPath { get; set; }

	/// <summary>
	/// The raw value/path that was found referencing the asset
	/// </summary>
	public string ReferenceValue { get; set; }
}

	/// <summary>
	/// Finds references to assets within scene files by parsing their JSON content
	/// </summary>
public static class SceneReferenceFinder
{
	/// <summary>
	/// Opens the scene containing this reference and selects the GameObject
	/// </summary>
	public static void OpenAndSelect( SceneAssetReference reference )
	{
		if ( reference?.SceneAsset is null )
			return;

		// Open or get existing scene session
		SceneEditorSession session;
		try
		{
			session = SceneEditorSession.CreateFromPath( reference.SceneAsset.Path );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to open scene {reference.SceneAsset.Path}: {ex.Message}" );
			return;
		}

		if ( session is null )
			return;

		// Make this session active
		session.MakeActive();

		// Select the GameObject if we have a valid ID
		GameObject selectedObject = null;
		if ( reference.GameObjectId != Guid.Empty )
		{
			var gameObject = session.Scene.Directory.FindByGuid( reference.GameObjectId ) as GameObject;
			if ( gameObject is not null && gameObject.IsValid() )
			{
				session.Selection.Clear();
				session.Selection.Add( gameObject );
				selectedObject = gameObject;
			}
		}

		// Teleport camera to look at the selected object
		if ( selectedObject is not null )
		{
			TeleportCameraToObject( selectedObject );
		}
	}

	/// <summary>
	/// Teleports the editor camera to look at the specified GameObject
	/// </summary>
	private static void TeleportCameraToObject( GameObject gameObject )
	{
		if ( gameObject is null || !gameObject.IsValid() )
			return;

		// Get the current scene view widget
		var sceneView = SceneViewWidget.Current;
		if ( sceneView is null )
			return;

		// Get the last selected viewport or the first available one
		var viewport = sceneView.LastSelectedViewportWidget;
		if ( viewport is null && sceneView._viewports.Count > 0 )
		{
			viewport = sceneView._viewports.Values.FirstOrDefault();
		}

		if ( viewport is null )
			return;

		// Calculate camera position to look at the object
		var targetPosition = gameObject.WorldPosition;
		var targetBounds = gameObject.GetBounds();
		var targetSize = targetBounds.Size.Length;

		// Default distance based on object size, minimum 200 units
		var distance = Math.Max( targetSize * 2.0f, 50.0f );

		// Position camera at an angle looking at the object
		var cameraOffset = new Vector3( distance, distance * 0.5f, distance * 0.75f );
		var cameraPosition = targetPosition + cameraOffset;

		// Calculate rotation to look at the target
		var lookDirection = (targetPosition - cameraPosition).Normal;
		var cameraRotation = Rotation.LookAt( lookDirection, Vector3.Up );

		// Apply to viewport state
		viewport.State.CameraPosition = cameraPosition;
		viewport.State.CameraRotation = cameraRotation;
	}
	/// <summary>
	/// Searches all scene files for references to the specified asset
	/// </summary>
	public static List<SceneAssetReference> FindReferencesInScenes( Asset targetAsset )
	{
		if ( targetAsset is null )
			return new List<SceneAssetReference>();

		var references = new List<SceneAssetReference>();

		// Get all scene assets using FileExtension
		var sceneAssets = AssetSystem.All.Where( a =>
			a?.AssetType?.FileExtension == "scene" &&
			!a.IsDeleted
		).ToList();

		foreach ( var sceneAsset in sceneAssets )
		{
			try
			{
				var sceneReferences = FindReferencesInScene( sceneAsset, targetAsset );
				references.AddRange( sceneReferences );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"Failed to search scene {sceneAsset.Path}: {ex.Message}" );
			}
		}

		return references;
	}

	/// <summary>
	/// Searches a specific scene file for references to the target asset
	/// </summary>
	private static List<SceneAssetReference> FindReferencesInScene( Asset sceneAsset, Asset targetAsset )
	{
		var references = new List<SceneAssetReference>();

		// Load the scene file as JSON
		if ( string.IsNullOrEmpty( sceneAsset.AbsolutePath ) || !File.Exists( sceneAsset.AbsolutePath ) )
			return references;

		JsonObject jsonContent;
		try
		{
			var jsonString = File.ReadAllText( sceneAsset.AbsolutePath );
			jsonContent = JsonNode.Parse( jsonString ) as JsonObject;
			if ( jsonContent is null )
				return references;
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to parse scene {sceneAsset.Path}: {ex.Message}" );
			return references;
		}

		// Get the target asset path to search for
		var targetPaths = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
		{
			targetAsset.Path,
			targetAsset.RelativePath,
			targetAsset.AbsolutePath
		};

		// Add extension variations
		var targetPathWithoutExt = Path.ChangeExtension( targetAsset.Path, null );
		if ( !string.IsNullOrEmpty( targetPathWithoutExt ) )
		{
			targetPaths.Add( targetPathWithoutExt );
		}

		// Search in GameObjects array
		if ( jsonContent["GameObjects"] is JsonArray gameObjects )
		{
			foreach ( var gameObjectNode in gameObjects )
			{
				if ( gameObjectNode is not JsonObject gameObject )
					continue;

				var gameObjectId = gameObject["__guid"]?.GetValue<Guid>() ?? Guid.Empty;
				var gameObjectName = gameObject["Name"]?.GetValue<string>() ?? "Unknown";

				// Search all properties of the GameObject for asset references
				SearchJsonNodeForReferences(
					gameObject,
					targetAsset,
					targetPaths,
					sceneAsset,
					gameObjectId,
					gameObjectName,
					"",
					references
				);
			}
		}

		// Also check __references field for package-level references
		if ( jsonContent["__references"] is JsonArray refArray )
		{
			foreach ( var refItem in refArray )
			{
				var refValue = refItem?.GetValue<string>();
				if ( string.IsNullOrEmpty( refValue ) )
					continue;

				// Check if this reference contains our asset's package
				if ( targetAsset.Package is not null )
				{
					var packageIdent = targetAsset.Package.FullIdent;
					if ( refValue.Equals( packageIdent, StringComparison.OrdinalIgnoreCase ) )
					{
						references.Add( new SceneAssetReference
						{
							SceneAsset = sceneAsset,
							GameObjectId = Guid.Empty,
							GameObjectName = "Scene Package References",
							ComponentPath = "__references",
							ReferenceValue = refValue
						} );
					}
				}
			}
		}

		return references;
	}

	/// <summary>
	/// Recursively searches a JSON node for references to the target asset
	/// </summary>
	private static void SearchJsonNodeForReferences(
		JsonNode node,
		Asset targetAsset,
		HashSet<string> targetPaths,
		Asset sceneAsset,
		Guid gameObjectId,
		string gameObjectName,
		string currentPath,
		List<SceneAssetReference> references )
	{
		if ( node is JsonObject jsonObj )
		{
			foreach ( var property in jsonObj )
			{
				var propertyPath = string.IsNullOrEmpty( currentPath )
					? property.Key
					: $"{currentPath}.{property.Key}";

				// Skip internal properties
				if ( property.Key.StartsWith( "__" ) && property.Key != "__Prefab" )
					continue;

				SearchJsonNodeForReferences(
					property.Value,
					targetAsset,
					targetPaths,
					sceneAsset,
					gameObjectId,
					gameObjectName,
					propertyPath,
					references
				);
			}
		}
		else if ( node is JsonArray jsonArray )
		{
			for ( int i = 0; i < jsonArray.Count; i++ )
			{
				var itemPath = $"{currentPath}[{i}]";
				SearchJsonNodeForReferences(
					jsonArray[i],
					targetAsset,
					targetPaths,
					sceneAsset,
					gameObjectId,
					gameObjectName,
					itemPath,
					references
				);
			}
		}
		else if ( node is JsonValue jsonValue )
		{
			// Check if this value is a string that references our asset
			if ( jsonValue.TryGetValue<string>( out var value ) )
			{
				CheckValueForAssetReference(
					value,
					targetAsset,
					targetPaths,
					sceneAsset,
					gameObjectId,
					gameObjectName,
					currentPath,
					references
				);
			}
		}
	}

	/// <summary>
	/// Checks if a string value references the target asset
	/// </summary>
	private static void CheckValueForAssetReference(
		string value,
		Asset targetAsset,
		HashSet<string> targetPaths,
		Asset sceneAsset,
		Guid gameObjectId,
		string gameObjectName,
		string componentPath,
		List<SceneAssetReference> references )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			return;

		// Quick checks to skip non-path values
		if ( value.Contains( ',' ) || value.Contains( ':' ) || value.Length < 3 )
			return;

		// Check if value contains any of our target paths
		foreach ( var targetPath in targetPaths )
		{
			if ( value.IndexOf( targetPath, StringComparison.OrdinalIgnoreCase ) >= 0 )
			{
				references.Add( new SceneAssetReference
				{
					SceneAsset = sceneAsset,
					GameObjectId = gameObjectId,
					GameObjectName = gameObjectName,
					ComponentPath = componentPath,
					ReferenceValue = value
				} );
				return;
			}
		}

		// Also try resolving the value as an asset path
		if ( value.Contains( "." ) && !value.Contains( " " ) )
		{
			try
			{
				var referencedAsset = AssetSystem.FindByPath( value );
				// Compare by path since AssetId is internal
				if ( referencedAsset?.Path == targetAsset.Path )
				{
					references.Add( new SceneAssetReference
					{
						SceneAsset = sceneAsset,
						GameObjectId = gameObjectId,
						GameObjectName = gameObjectName,
						ComponentPath = componentPath,
						ReferenceValue = value
					} );
				}
			}
			catch { /* Ignore resolution errors */ }
		}
	}
}
