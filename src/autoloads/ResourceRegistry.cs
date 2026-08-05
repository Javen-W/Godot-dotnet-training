using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GodotDotnetTraining
{
	/// <summary>
	/// Centralized resource database that loads, validates, and indexes all game resources
	/// at startup. Provides a single generic registry that can be queried by type and key,
	/// replacing per-category registries such as BiomeRegistry and the old StaticResources
	/// dictionaries.
	///
	/// All content categories (e.g, items) are scanned from
	/// their respective <c>src/content/</c> subdirectories during <c>_Ready()</c>.
	///
	/// Usage:
	///   var item  = ResourceRegistry.Get&lt;ItemID, ItemDef&gt;(ItemID.EXAMPLE_ITEM);
	/// </summary>
	public partial class ResourceRegistry : Node
	{
		private const string CONTENT_ROOT_PATH = "res://src/content/";
		private const string ASSET_ROOT_PATH = "res://assets/";

		/// <summary>
		/// Shared singleton instance created from the autoload pipeline so gameplay,
		/// UI, and C# code can resolve authored resources through one manifest.
		/// It owns both content-directory registration and explicit asset/scene entries.
		/// </summary>
		public static ResourceRegistry Instance { get; private set; }

		/// <summary>
		/// Internal storage: keyed by (typeof(TValue).FullName) → Dictionary&lt;object, Resource&gt;.
		/// This allows multiple resource categories to coexist in a single registry.
		/// </summary>
		private readonly Dictionary<string, Dictionary<object, Resource>> _categories = new();

		/// <summary>
		/// Tracks the resolved project path for each registered resource key.
		/// </summary>
		private readonly Dictionary<string, Dictionary<object, string>> _resolvedPaths = new();

		/// <summary>
		/// Tracks validation warnings found during startup loading.
		/// </summary>
		private readonly List<string> _validationWarnings = new();

		/// <summary>
		/// Initializes the singleton and eagerly registers both directory-backed content
		/// and explicit path-based runtime assets during autoload startup so later systems
		/// can retrieve them by logical ID instead of hardcoded file paths.
		/// </summary>
		public override void _Ready()
		{
			Instance = this;
			LoadAllResources();
		}

		/// <summary>
		/// Loads every resource category the runtime expects, combining recursive content
		/// discovery for authored gameplay data with explicit registrations for shared
		/// scenes and assets that code instantiates directly.
		/// </summary>
		private void LoadAllResources()
		{
			RegisterDirectory<ItemID, ItemDef>(ContentDirectory("items"), w => w.ItemID);

			var totalResources = _categories.Values.Sum(c => c.Count);
			Logger.Info($"Loaded {totalResources} resources across {_categories.Count} categories");

			if (_validationWarnings.Count > 0)
			{
				foreach (var warning in _validationWarnings)
				{
					Logger.Warning(warning);
				}
			}
		}

		/// <summary>
		/// Builds a content-directory path from the shared content root so authored
		/// gameplay resources remain centralized in one place when folder layouts evolve.
		/// </summary>
		private static string ContentDirectory(string relativeDirectory)
		{
			var normalized = NormalizeRelativePath(relativeDirectory);
			return $"{CONTENT_ROOT_PATH}{normalized}/";
		}

		/// <summary>
		/// Builds an asset path from the shared asset root so art, themes, mesh libraries,
		/// and other raw assets can move without changing gameplay or UI code.
		/// </summary>
		public static string AssetPath(string relativePath)
		{
			return $"{ASSET_ROOT_PATH}{NormalizeRelativePath(relativePath)}";
		}

		/// <summary>
		/// Normalizes a project-relative fragment before it is combined with a registry
		/// root path, ensuring callers cannot accidentally double up separators.
		/// </summary>
		private static string NormalizeRelativePath(string relativePath)
		{
			return relativePath.Trim().TrimStart('/', '\\').TrimEnd('/', '\\').Replace('\\', '/');
		}

		/// <summary>
		/// Scans a directory recursively for .tres resource files, loads each as the given type,
		/// validates them, and registers them in the category dictionary keyed by
		/// <paramref name="keySelector"/>. Resources whose key equals the type default (e.g. enum
		/// value 0 / NULL) are skipped so that unfinished template resources are not indexed.
		/// </summary>
		private void RegisterDirectory<TKey, TValue>(string directory, Func<TValue, TKey> keySelector)
			where TValue : Resource
		{
			var categoryKey = GetCategoryKey<TValue>();
			var dict = new Dictionary<object, Resource>();
			var pathsForCategory = new Dictionary<object, string>();

			var paths = EnumerateProjectFiles(directory, ".tres");

			foreach (var path in paths)
			{
				try
				{
					var res = GD.Load<TValue>(path);
					if (res == null)
					{
						_validationWarnings.Add($"Failed to load resource at '{path}'");
						continue;
					}

					var key = keySelector(res);

					// skip resources with default/NULL key (base resource templates)
					if (EqualityComparer<TKey>.Default.Equals(key, default))
					{
						continue;
					}

					if (dict.ContainsKey(key))
					{
						_validationWarnings.Add(
							$"Duplicate key '{key}' in category {categoryKey} at '{path}' — skipping");
						continue;
					}

					// Successful registration.
					dict[key] = res;
					pathsForCategory[key] = path;
				} 
				catch (InvalidCastException)
				{
					continue;    
				}
			}

			_categories[categoryKey] = dict;
			_resolvedPaths[categoryKey] = pathsForCategory;
			Logger.Info($"Registered {dict.Count} {typeof(TValue).Name} resources");
		}

		private static IEnumerable<string> EnumerateProjectFiles(string directory, string extension)
		{
			var files = new List<string>();
			EnumerateProjectFilesRecursive(EnsureTrailingSlash(directory), extension, files);
			return files;
		}

		private static void EnumerateProjectFilesRecursive(string virtualDirectory, string extension, List<string> files)
		{
			using var dir = DirAccess.Open(ResolveDirectoryAccessPath(virtualDirectory));
			if (dir == null)
			{
				Logger.Warning($"Failed to open directory '{virtualDirectory}'");
				return;
			}

			dir.ListDirBegin();
			for (var fileName = dir.GetNext(); fileName != string.Empty; fileName = dir.GetNext())
			{
				if (fileName is "." or "..")
				{
					continue;
				}

				if (dir.CurrentIsDir())
				{
					EnumerateProjectFilesRecursive($"{EnsureTrailingSlash(virtualDirectory)}{fileName}/", extension, files);
					continue;
				}

				if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
				{
					files.Add($"{EnsureTrailingSlash(virtualDirectory)}{fileName}");
				}
			}
		}

		private static string ResolveDirectoryAccessPath(string directory)
		{
			return directory.StartsWith("res://", StringComparison.Ordinal)
				? ProjectSettings.GlobalizePath(directory)
				: directory;
		}

		private static string EnsureTrailingSlash(string path)
		{
			return path.EndsWith("/", StringComparison.Ordinal) ? path : $"{path}/";
		}

		/// <summary>
		/// Registers one explicit resource entry from the shared manifest by logical key.
		/// This is used for scenes and assets that are not discovered by scanning a folder
		/// but still need the same registry-based indirection as authored gameplay content.
		/// </summary>
		private void RegisterPath<TKey, TValue>(TKey key, string path)
			where TValue : Resource
		{
			if (!ResourceLoader.Exists(path))
			{
				_validationWarnings.Add($"Missing resource path '{path}' for key '{key}'");
				return;
			}

			var resource = GD.Load<TValue>(path);
			if (resource == null)
			{
				_validationWarnings.Add($"Failed to load resource '{path}' for key '{key}'");
				return;
			}

			var categoryKey = GetCategoryKey<TValue>();
			var dict = GetOrCreateCategory(categoryKey);
			if (dict.ContainsKey(key))
			{
				_validationWarnings.Add($"Duplicate key '{key}' in category {categoryKey} at '{path}' — skipping");
				return;
			}

			dict[key] = resource;
			GetOrCreateResolvedPathCategory(categoryKey)[key] = path;
		}

		private Dictionary<object, Resource> GetOrCreateCategory(string categoryKey)
		{
			if (!_categories.TryGetValue(categoryKey, out var dict))
			{
				dict = new Dictionary<object, Resource>();
				_categories[categoryKey] = dict;
			}

			return dict;
		}

		private Dictionary<object, string> GetOrCreateResolvedPathCategory(string categoryKey)
		{
			if (!_resolvedPaths.TryGetValue(categoryKey, out var dict))
			{
				dict = new Dictionary<object, string>();
				_resolvedPaths[categoryKey] = dict;
			}

			return dict;
		}

		private static string GetCategoryKey<TValue>() where TValue : Resource
		{
			return typeof(TValue).FullName;
		}

		/// <summary>
		/// Retrieves a single resource by key and type from the shared manifest.
		/// Returns null if the category or key is not found.
		/// </summary>
		public static TValue Get<TKey, TValue>(TKey key) where TValue : Resource
		{
			var categoryKey = GetCategoryKey<TValue>();
			if (Instance?._categories.TryGetValue(categoryKey, out var dict) == true
				&& dict.TryGetValue(key, out var resource))
			{
				return (TValue)resource;
			}

			return null;
		}

		/// <summary>
		/// Retrieves all resources in a category as a dictionary.
		/// Returns an empty dictionary if the category is not registered.
		/// </summary>
		public static Dictionary<TKey, TValue> GetAll<TKey, TValue>() where TValue : Resource
		{
			var categoryKey = GetCategoryKey<TValue>();
			if (Instance?._categories.TryGetValue(categoryKey, out var dict) == true)
			{
				return dict.ToDictionary(
					kvp => (TKey)kvp.Key,
					kvp => (TValue)kvp.Value);
			}
			return new Dictionary<TKey, TValue>();
		}

		/// <summary>
		/// Returns the count of resources in a category.
		/// </summary>
		public static int Count<TValue>() where TValue : Resource
		{
			var categoryKey = GetCategoryKey<TValue>();
			if (Instance?._categories.TryGetValue(categoryKey, out var dict) == true)
			{
				return dict.Count;
			}
			return 0;
		}

		/// <summary>
		/// Returns all validation warnings found during startup loading.
		/// Useful for diagnostics/debugging.
		/// </summary>
		public static IReadOnlyList<string> GetValidationWarnings()
		{
			return Instance?._validationWarnings ?? new List<string>();
		}

		/// <summary>
		/// Returns the concrete project path the registry resolved for a given resource key.
		/// This supports diagnostics and automation coverage for the central path manifest.
		/// </summary>
		public static string GetResolvedPath<TKey, TValue>(TKey key) where TValue : Resource
		{
			var categoryKey = GetCategoryKey<TValue>();
			if (Instance?._resolvedPaths.TryGetValue(categoryKey, out var paths) == true
				&& paths.TryGetValue(key, out var resolvedPath))
			{
				return resolvedPath;
			}

			return null;
		}
	}
}
