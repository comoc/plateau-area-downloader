using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using UnityEngine;
using UnityEngine.Networking;

namespace Zabaglione.PlateauAreaDownloader.Editor
{
    [Serializable]
    internal sealed class SelectedGml
    {
        public string cityCode;
        public string cityName;
        public string cityRoot;
        public int year;
        public string spec;
        public string type;
        public string code;
        public int maxLod;
        public string url;
        public long fileSize;
    }

    [Serializable]
    internal sealed class SavedFile
    {
        public string path;
        public long size;
        public string sha256;
    }

    [Serializable]
    internal sealed class PackManifest
    {
        public string key;
        public string packId;
        public string status;
        public string apiBase;
        public string placeName;
        public double west;
        public double south;
        public double east;
        public double north;
        public SelectedGml[] selectedGmls;
        public string[] metadataUrls;
        public long gmlBytes;
        public long zipBytes;
        public long expandedBytes;
        public SavedFile[] files;
        public string diagnostic;
    }

    internal sealed class PackLimits
    {
        public long MaxDownloadBytes = 10L * 1024 * 1024 * 1024;
        public long MaxExpandedBytes = 30L * 1024 * 1024 * 1024;
    }

    internal sealed class PackProgress
    {
        public string Stage;
        public long Bytes;
        public long TotalBytes;
    }

    internal static class PackDownloader
    {
        internal static string DefaultDataRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../PLATEAUData~"));

        internal static string CityRootName(CatalogCity city)
        {
            if (city == null || !Uri.TryCreate(city.url, UriKind.Absolute, out var uri))
                throw new ArgumentException("City catalog is missing its archive URL.");
            return Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        }

        internal static PackManifest CreateManifest(string placeName, double west, double south,
            double east, double north,
            IEnumerable<(CatalogCity city, string type, CatalogGml gml)> selected,
            string apiBase)
        {
            var selection = selected.ToArray();
            var entries = selection.Select(item => new SelectedGml
            {
                cityCode = item.city.cityCode,
                cityName = item.city.cityName,
                cityRoot = CityRootName(item.city),
                year = item.city.year,
                spec = item.city.spec,
                type = item.type,
                code = item.gml.code,
                maxLod = item.gml.maxLod,
                url = item.gml.url,
                fileSize = item.gml.fileSize
            }).OrderBy(item => item.url, StringComparer.Ordinal).ToArray();
            if (entries.Length == 0) throw new InvalidOperationException("No CityGML files selected.");
            var metadata = selection.SelectMany(item => item.city.metadataZipUrls ?? Array.Empty<string>())
                .Distinct(StringComparer.Ordinal).OrderBy(url => url, StringComparer.Ordinal).ToArray();
            var urls = entries.Select(entry => entry.url).Concat(metadata).ToArray();
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(apiBase + "\n" + string.Join("\n", urls)));
            var key = BitConverter.ToString(hash).Replace("-", "").Substring(0, 24).ToLowerInvariant();
            return new PackManifest
            {
                key = key,
                status = "new",
                apiBase = apiBase,
                placeName = placeName,
                west = west, south = south, east = east, north = north,
                selectedGmls = entries,
                metadataUrls = metadata,
                gmlBytes = entries.Any(entry => entry.fileSize <= 0) ? -1 : entries.Sum(entry => entry.fileSize)
            };
        }

        internal static string JobPath(string dataRoot, PackManifest manifest) =>
            Path.Combine(dataRoot, manifest.key);

        internal static PackManifest LoadManifest(string jobPath)
        {
            var path = Path.Combine(jobPath, "manifest.json");
            return File.Exists(path) ? JsonUtility.FromJson<PackManifest>(File.ReadAllText(path)) : null;
        }

        internal static async Task<PackManifest> DownloadAsync(PackManifest desired, string dataRoot,
            PackLimits limits, Action<PackProgress> progress, CancellationToken token)
        {
            if (desired == null || desired.selectedGmls == null || desired.selectedGmls.Length == 0)
                throw new ArgumentException("No selected CityGML data.");
            if (limits.MaxDownloadBytes <= 0 || limits.MaxExpandedBytes <= 0)
                throw new ArgumentException("Download limits must be positive.");
            EnsureSafeDataRoot(dataRoot);
            EnsureDefaultDataIgnored(dataRoot);
            var jobPath = JobPath(dataRoot, desired);
            Directory.CreateDirectory(jobPath);
            var existing = LoadManifest(jobPath);
            var manifest = existing != null && existing.key == desired.key ? existing : desired;
            manifest.placeName = desired.placeName;
            manifest.west = desired.west;
            manifest.south = desired.south;
            manifest.east = desired.east;
            manifest.north = desired.north;
            var datasetPath = Path.Combine(jobPath, "dataset");
            try
            {
                if (manifest.status == "complete")
                {
                    var valid = await Task.Run(() =>
                    {
                        if (!VerifySavedFiles(datasetPath, manifest.files, token)) return false;
                        ValidateReferences(datasetPath, manifest.selectedGmls, token);
                        return true;
                    }, token);
                    if (valid)
                    {
                        SaveManifest(jobPath, manifest);
                        progress?.Invoke(new PackProgress { Stage = "cached", Bytes = manifest.zipBytes, TotalBytes = manifest.zipBytes });
                        return manifest;
                    }
                }
                var urls = manifest.selectedGmls.Select(entry => entry.url)
                    .Concat(manifest.metadataUrls ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
                if (string.IsNullOrWhiteSpace(manifest.packId))
                {
                    manifest.packId = await PlateauApi.CreatePackAsync(urls, manifest.apiBase, token);
                    manifest.status = "pack-created";
                    SaveManifest(jobPath, manifest);
                }

                var zipPath = Path.Combine(jobPath, "pack.zip");
                for (var downloadAttempt = 0; downloadAttempt < 2; downloadAttempt++)
                {
                    var recreated = false;
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        PackStatus status;
                        try
                        {
                            status = await PlateauApi.GetPackStatusAsync(manifest.packId, manifest.apiBase, token);
                        }
                        catch (System.Net.Http.HttpRequestException exception)
                            when (!recreated && (exception.Message.Contains("404") || exception.Message.Contains("410")))
                        {
                            manifest.packId = await PlateauApi.CreatePackAsync(urls, manifest.apiBase, token);
                            recreated = true;
                            SaveManifest(jobPath, manifest);
                            continue;
                        }
                        progress?.Invoke(new PackProgress { Stage = "preparing", Bytes = (long)(status.progress * 100), TotalBytes = 100 });
                        if (status.status == "succeeded") break;
                        if (status.status == "failed" || status.status == "expired")
                        {
                            if (!recreated)
                            {
                                manifest.packId = await PlateauApi.CreatePackAsync(urls, manifest.apiBase, token);
                                recreated = true;
                                SaveManifest(jobPath, manifest);
                                continue;
                            }
                            throw new IOException("Pack preparation failed: " + status.error);
                        }
                        await Task.Delay(TimeSpan.FromSeconds(3), token);
                    }
                    try
                    {
                        await DownloadZipAsync(manifest, zipPath, limits.MaxDownloadBytes, progress, token);
                        break;
                    }
                    catch (IOException exception) when (downloadAttempt == 0 &&
                        (exception.Message.Contains("HTTP 404") || exception.Message.Contains("HTTP 410")))
                    {
                        manifest.packId = await PlateauApi.CreatePackAsync(urls, manifest.apiBase, token);
                        manifest.status = "pack-created";
                        SaveManifest(jobPath, manifest);
                    }
                }
                manifest.zipBytes = new FileInfo(zipPath).Length;
                manifest.status = "downloaded";
                SaveManifest(jobPath, manifest);

                var stagingPath = Path.Combine(jobPath, "staging");
                if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, true);
                Directory.CreateDirectory(stagingPath);
                var files = await Task.Run(() =>
                {
                    var extracted = ExtractSafe(zipPath, stagingPath, limits.MaxExpandedBytes, progress, token);
                    ValidateReferences(stagingPath, manifest.selectedGmls, token);
                    return extracted;
                }, token);
                manifest.files = files;
                manifest.expandedBytes = files.Sum(file => file.size);
                if (Directory.Exists(datasetPath)) Directory.Delete(datasetPath, true);
                Directory.Move(stagingPath, datasetPath);
                manifest.status = "complete";
                manifest.diagnostic = "";
                SaveManifest(jobPath, manifest);
                return manifest;
            }
            catch (OperationCanceledException)
            {
                manifest.status = "interrupted";
                SaveManifest(jobPath, manifest);
                throw;
            }
            catch (Exception error)
            {
                manifest.status = "failed";
                manifest.diagnostic = error.ToString();
                SaveManifest(jobPath, manifest);
                throw;
            }
        }

        private static void EnsureSafeDataRoot(string dataRoot)
        {
            if (string.IsNullOrWhiteSpace(dataRoot)) throw new ArgumentException("Data root is empty.");
            var full = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (full.Equals(assets, comparison) ||
                full.StartsWith(assets + Path.DirectorySeparatorChar, comparison))
                throw new ArgumentException("CityGML data root must be outside Assets.");
        }

        private static void EnsureDefaultDataIgnored(string dataRoot)
        {
            if (!Path.GetFullPath(dataRoot).Equals(DefaultDataRoot,
                    Application.platform == RuntimePlatform.WindowsEditor
                        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!Directory.Exists(Path.Combine(projectRoot, ".git"))) return;
            var ignorePath = Path.Combine(projectRoot, ".gitignore");
            var ignore = File.Exists(ignorePath) ? File.ReadAllText(ignorePath) : "";
            if (ignore.Split('\n').Any(line => line.TrimEnd('\r').Trim() == "/PLATEAUData~/")) return;
            File.AppendAllText(ignorePath,
                (ignore.Length > 0 && !ignore.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "") +
                "/PLATEAUData~/\n");
        }

        private static async Task DownloadZipAsync(PackManifest manifest, string zipPath, long limit,
            Action<PackProgress> progress, CancellationToken token)
        {
            var partial = zipPath + ".part";
            if (File.Exists(partial)) File.Delete(partial);
            var url = manifest.apiBase.TrimEnd('/') + "/citygml/pack/" +
                Uri.EscapeDataString(manifest.packId) + ".zip";
            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerFile(partial);
            request.SetRequestHeader("User-Agent", "PLATEAU-Area-Downloader/0.1");
            using var cancellation = token.Register(request.Abort);
            var transfer = request.SendWebRequest();
            long total = -1;
            while (!transfer.isDone)
            {
                token.ThrowIfCancellationRequested();
                if (long.TryParse(request.GetResponseHeader("Content-Length"), out var length)) total = length;
                if (total > limit || (long)request.downloadedBytes > limit)
                {
                    request.Abort();
                    throw new IOException("Pack exceeds download limit.");
                }
                progress?.Invoke(new PackProgress
                {
                    Stage = "downloading", Bytes = (long)request.downloadedBytes, TotalBytes = total
                });
                await Task.Delay(200, token);
            }
            token.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException("Pack download failed: HTTP " + request.responseCode + " " + request.error);
            var size = new FileInfo(partial).Length;
            if (size > limit) throw new IOException("Pack exceeds download limit.");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(partial, zipPath);
        }

        internal static SavedFile[] ExtractSafe(string zipPath, string root, long limit,
            Action<PackProgress> progress, CancellationToken token)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries.Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal)).ToArray();
            var declared = entries.Sum(entry => entry.Length);
            if (declared > limit) throw new IOException("Pack exceeds expanded size limit.");
            var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            var result = new List<SavedFile>(entries.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expanded = 0;
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                var relative = entry.FullName;
                if (string.IsNullOrEmpty(relative) || relative.StartsWith("/", StringComparison.Ordinal) ||
                    relative.IndexOf('\\') >= 0 || relative.IndexOf(':') >= 0 ||
                    relative.Split('/').Any(part => part == ".." || part == "." || part.Length == 0) ||
                    ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new IOException("Unsafe ZIP entry path: " + relative);
                if (!seen.Add(relative)) throw new IOException("Duplicate ZIP entry path: " + relative);
                var destination = Path.GetFullPath(Path.Combine(root, relative));
                if (!destination.StartsWith(rootFull, StringComparison.Ordinal))
                    throw new IOException("ZIP entry escapes destination: " + relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? root);
                using var input = entry.Open();
                using var output = File.Create(destination);
                using var sha = SHA256.Create();
                var buffer = new byte[1024 * 1024];
                int read;
                long size = 0;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    size += read;
                    expanded += read;
                    if (expanded > limit) throw new IOException("Pack exceeds expanded size limit.");
                    output.Write(buffer, 0, read);
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                result.Add(new SavedFile
                {
                    path = relative,
                    size = size,
                    sha256 = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant()
                });
                progress?.Invoke(new PackProgress { Stage = "extracting", Bytes = expanded, TotalBytes = declared });
            }
            return result.ToArray();
        }

        internal static void ValidateReferences(string dataRoot, SelectedGml[] selected, CancellationToken token)
        {
            var root = Path.GetFullPath(dataRoot) + Path.DirectorySeparatorChar;
            var missing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var gml in selected)
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(new Uri(gml.url).AbsolutePath);
                var path = Path.Combine(dataRoot, gml.cityRoot, "udx", gml.type, name);
                if (!File.Exists(path))
                {
                    missing.Add(path);
                    continue;
                }
                var basePath = Path.GetDirectoryName(path) ?? dataRoot;
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using var reader = XmlReader.Create(path, settings);
                while (!reader.EOF)
                {
                    token.ThrowIfCancellationRequested();
                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        reader.Read();
                        continue;
                    }
                    if (reader.HasAttributes)
                    {
                        while (reader.MoveToNextAttribute())
                            if (reader.LocalName == "codeSpace" || reader.LocalName == "href")
                                CheckReference(reader.Value, basePath, root, missing);
                        reader.MoveToElement();
                    }
                    if (reader.LocalName == "imageURI" && !reader.IsEmptyElement)
                    {
                        CheckReference(reader.ReadElementContentAsString(), basePath, root, missing);
                        continue;
                    }
                    reader.Read();
                }
            }
            if (missing.Count > 0)
                throw new IOException("Missing local references (" + missing.Count + "): " +
                    string.Join(", ", missing.Take(8)));
        }

        private static void CheckReference(string reference, string basePath, string root,
            HashSet<string> missing)
        {
            if (string.IsNullOrWhiteSpace(reference) || reference[0] == '#') return;
            if (Uri.TryCreate(reference, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "urn")) return;
            var pathPart = Uri.UnescapeDataString(reference.Split('#')[0]);
            if (string.IsNullOrWhiteSpace(pathPart)) return;
            var full = Path.GetFullPath(Path.Combine(basePath, pathPart));
            if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full)) missing.Add(reference);
        }

        internal static bool VerifySavedFiles(string root, SavedFile[] files,
            CancellationToken token = default)
        {
            if (files == null || files.Length == 0) return false;
            var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            var buffer = new byte[1024 * 1024];
            foreach (var entry in files)
            {
                token.ThrowIfCancellationRequested();
                var path = Path.GetFullPath(Path.Combine(root, entry.path));
                if (!path.StartsWith(rootFull, StringComparison.Ordinal) || !File.Exists(path) ||
                    new FileInfo(path).Length != entry.size) return false;
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(path);
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(hash, entry.sha256, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static void SaveManifest(string jobPath, PackManifest manifest)
        {
            var path = Path.Combine(jobPath, "manifest.json");
            var pending = path + ".tmp";
            File.WriteAllText(pending, JsonUtility.ToJson(manifest, true));
            if (File.Exists(path)) File.Replace(pending, path, null);
            else File.Move(pending, path);
        }
    }
}
