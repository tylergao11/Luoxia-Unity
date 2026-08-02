using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Luoxia.Assets
{
    /// <summary>
    /// Resolves AssetContentRef.content_hash to a Sprite via local StreamingAssets index.
    /// Missing hash or missing file ⇒ explicit miss (no fake placeholder art).
    /// </summary>
    public interface IContentHashSpriteResolver
    {
        bool TryResolve(string contentHash, out Sprite sprite, out string error);
    }

    /// <summary>
    /// Reads StreamingAssets/LuoxiaHash/hash-index.json → relative path under that folder.
    /// Accepts object-map entries (preferred) or array/entry_list (legacy export).
    /// </summary>
    public sealed class StreamingAssetsHashSpriteResolver : IContentHashSpriteResolver
    {
        public const string IndexRelativePath = "LuoxiaHash/hash-index.json";
        public const string RootFolderName = "LuoxiaHash";

        private readonly Dictionary<string, string> _hashToRelative =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Sprite> _cache =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private bool _indexLoaded;
        private string _indexError;

        public string IndexError => _indexError;
        public bool IndexLoaded => _indexLoaded;
        public int EntryCount => _hashToRelative.Count;

        public void EnsureIndexLoaded()
        {
            if (_indexLoaded)
            {
                return;
            }

            _indexLoaded = true;
            _hashToRelative.Clear();
            _indexError = null;

            var indexPath = Path.Combine(Application.streamingAssetsPath, IndexRelativePath);
            if (!File.Exists(indexPath))
            {
                _indexError = $"hash-index missing: {IndexRelativePath}";
                Debug.LogWarning($"[HashSprite] {_indexError}");
                return;
            }

            try
            {
                var json = File.ReadAllText(indexPath);
                var root = JObject.Parse(json);
                if (root == null)
                {
                    _indexError = "hash-index root missing";
                    return;
                }

                IngestEntriesToken(root["entries"]);
                IngestEntriesToken(root["entry_list"]);

                if (_hashToRelative.Count == 0)
                {
                    _indexError = "hash-index entries empty";
                }
            }
            catch (Exception ex)
            {
                _indexError = $"hash-index parse failed: {ex.Message}";
                Debug.LogError($"[HashSprite] {_indexError}");
            }
        }

        public bool TryResolve(string contentHash, out Sprite sprite, out string error)
        {
            sprite = null;
            error = null;
            EnsureIndexLoaded();

            if (string.IsNullOrEmpty(contentHash))
            {
                error = "content_hash empty";
                return false;
            }

            if (!string.IsNullOrEmpty(_indexError) && _hashToRelative.Count == 0)
            {
                error = _indexError;
                return false;
            }

            if (_cache.TryGetValue(contentHash, out var cached) && cached != null)
            {
                sprite = cached;
                return true;
            }

            if (!_hashToRelative.TryGetValue(contentHash, out var relative) ||
                string.IsNullOrEmpty(relative))
            {
                error = $"hash not in index: {Truncate(contentHash)}";
                return false;
            }

            relative = relative.Replace('\\', '/').TrimStart('/');
            if (relative.StartsWith(RootFolderName + "/", StringComparison.OrdinalIgnoreCase))
            {
                relative = relative.Substring(RootFolderName.Length + 1);
            }

            var absolute = Path.Combine(Application.streamingAssetsPath, RootFolderName, relative);
            absolute = absolute.Replace('\\', '/');
            if (!File.Exists(absolute))
            {
                error = $"asset file missing: {RootFolderName}/{relative}";
                return false;
            }

            if (relative.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                error = $"svg not loadable as Sprite: {relative}";
                return false;
            }

            try
            {
                var bytes = File.ReadAllBytes(absolute);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes, true))
                {
                    UnityEngine.Object.Destroy(tex);
                    error = $"image decode failed: {relative}";
                    return false;
                }

                tex.name = $"hash:{Truncate(contentHash)}";
                sprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                sprite.name = tex.name;
                _cache[contentHash] = sprite;
                return true;
            }
            catch (Exception ex)
            {
                error = $"load failed: {ex.Message}";
                return false;
            }
        }

        private void IngestEntriesToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return;
            }

            if (token is JObject map)
            {
                foreach (var prop in map.Properties())
                {
                    var path = prop.Value?.Type == JTokenType.String
                        ? prop.Value.Value<string>()
                        : null;
                    TryAdd(prop.Name, path);
                }

                return;
            }

            if (token is JArray array)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is not JObject item)
                    {
                        continue;
                    }

                    var hash = item.Value<string>("content_hash");
                    var path = item.Value<string>("relative_path");
                    TryAdd(hash, path);
                }
            }
        }

        private void TryAdd(string hash, string relativePath)
        {
            if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(relativePath))
            {
                return;
            }

            _hashToRelative[hash] = relativePath.Replace('\\', '/');
        }

        private static string Truncate(string hash)
        {
            if (string.IsNullOrEmpty(hash) || hash.Length <= 12)
            {
                return hash ?? string.Empty;
            }

            return hash.Substring(0, 12) + "…";
        }
    }

    /// <summary>Process-local default resolver; Bootstrap may replace via SetShared.</summary>
    public static class ContentHashSpriteResolverLocator
    {
        private static IContentHashSpriteResolver _shared;

        public static IContentHashSpriteResolver Shared =>
            _shared ??= new StreamingAssetsHashSpriteResolver();

        public static void SetShared(IContentHashSpriteResolver resolver)
        {
            _shared = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }
    }
}
