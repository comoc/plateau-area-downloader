using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Zabaglione.PlateauAreaDownloader.Editor
{
    [Serializable]
    internal sealed class PhotonCacheEntry
    {
        public long savedUtcTicks;
        public PhotonFeature[] features;
    }

    internal sealed class WheelZoomGate
    {
        private const double TicksPerStep = 3;
        private const double MinimumStepInterval = 0.2;
        private const double IdleResetInterval = 0.3;
        private double accumulatedTicks;
        private double lastInputTime = double.NegativeInfinity;
        private double lastStepTime = double.NegativeInfinity;

        internal int Consume(float verticalTicks, double now, int currentZoom, int minZoom, int maxZoom)
        {
            if (float.IsNaN(verticalTicks) || float.IsInfinity(verticalTicks) || verticalTicks == 0)
                return 0;
            if (now - lastInputTime > IdleResetInterval ||
                (accumulatedTicks != 0 && Math.Sign(accumulatedTicks) != Math.Sign(verticalTicks)))
                accumulatedTicks = 0;
            lastInputTime = now;
            if ((verticalTicks < 0 && currentZoom >= maxZoom) ||
                (verticalTicks > 0 && currentZoom <= minZoom))
            {
                accumulatedTicks = 0;
                return 0;
            }
            if (now - lastStepTime < MinimumStepInterval) return 0;
            if (accumulatedTicks == 0 && Math.Abs(verticalTicks) >= 1)
            {
                lastStepTime = now;
                return verticalTicks < 0 ? 1 : -1;
            }
            accumulatedTicks += Math.Max(-TicksPerStep, Math.Min(TicksPerStep, verticalTicks));
            if (Math.Abs(accumulatedTicks) < TicksPerStep) return 0;
            var step = accumulatedTicks < 0 ? 1 : -1;
            accumulatedTicks = 0;
            lastStepTime = now;
            return step;
        }
    }

    internal sealed class AreaDownloaderWindow : EditorWindow
    {
        private sealed class TileRequest
        {
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
        }

        private const string PackagePath = "Packages/com.zabaglione.plateau-area-downloader/Editor/";
        private const string Prefs = "Zabaglione.PlateauAreaDownloader.";
        private readonly ConcurrentQueue<PackProgress> progressQueue = new ConcurrentQueue<PackProgress>();
        private readonly Dictionary<string, Texture2D> tileTextures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, TileRequest> tileRequests = new Dictionary<string, TileRequest>();
        private readonly SemaphoreSlim tileDownloadSlots = new SemaphoreSlim(4);
        private readonly Dictionary<string, PhotonFeature[]> searchCache = new Dictionary<string, PhotonFeature[]>();
        private readonly WheelZoomGate wheelZoom = new WheelZoomGate();
        private FontAsset japaneseFont;
        private CancellationTokenSource operation;
        private GeoBounds bounds;
        private double centerLatitude = 35.6586;
        private double centerLongitude = 139.7454;
        private int zoom = 15;
        private Vector2 pointerStart;
        private double pointerCenterLatitude;
        private double pointerCenterLongitude;
        private bool selecting;
        private bool pointerActive;
        private HashSet<string> neededTileKeys = new HashSet<string>();
        private int previewGeneration;
        private string placeName = "東京タワー";
        private PackManifest desired;
        private PackManifest downloaded;
        private string downloadedDataRoot;
        private VisualElement map;
        private VisualElement tiles;
        private VisualElement meshOverlay;
        private VisualElement boundsOverlay;
        private VisualElement candidates;
        private Label previewResult;
        private Label progressLabel;
        private Label handoffResult;
        private VisualElement handoffCities;
        private TextField searchField;

        [MenuItem("Tools/PLATEAU Area Downloader")]
        private static void Open()
        {
            var window = GetWindow<AreaDownloaderWindow>("PLATEAU Area Downloader");
            window.minSize = new Vector2(900, 740);
            if (window.position.width < 900)
            {
                var position = window.position;
                position.width = 960;
                position.height = 900;
                window.position = position;
            }
            window.Show();
        }

        private void OnEnable()
        {
            bounds = GeoBounds.FromCenter(centerLatitude, centerLongitude);
            EditorApplication.update += DrainProgress;
        }

        private void OnDisable()
        {
            EditorApplication.update -= DrainProgress;
            operation?.Cancel();
            operation?.Dispose();
            foreach (var request in tileRequests.Values) request.Cancellation.Cancel();
            tileRequests.Clear();
            foreach (var texture in tileTextures.Values)
                if (texture != null) DestroyImmediate(texture);
            tileTextures.Clear();
            if (japaneseFont != null) DestroyImmediate(japaneseFont);
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PackagePath + "AreaWindow.uxml");
            if (layout == null)
            {
                rootVisualElement.Add(new Label("Package UI is missing."));
                return;
            }
            layout.CloneTree(rootVisualElement);
            if (japaneseFont == null)
            {
                japaneseFont = Application.platform == RuntimePlatform.OSXEditor
                    ? FontAsset.CreateFontAsset("Hiragino Sans", "W3")
                    : FontAsset.CreateFontAsset("Yu Gothic UI", "Regular");
                if (japaneseFont == null)
                    japaneseFont = FontAsset.CreateFontAsset("Noto Sans CJK JP", "Regular");
                if (japaneseFont != null) japaneseFont.isMultiAtlasTexturesEnabled = true;
            }
            if (japaneseFont != null)
                rootVisualElement.style.unityFontDefinition = new StyleFontDefinition(japaneseFont);
            map = Q<VisualElement>("map");
            tiles = Q<VisualElement>("tiles");
            meshOverlay = Q<VisualElement>("mesh-overlay");
            boundsOverlay = Q<VisualElement>("bounds-overlay");
            candidates = Q<VisualElement>("candidates");
            previewResult = Q<Label>("preview-result");
            progressLabel = Q<Label>("progress");
            handoffResult = Q<Label>("handoff-result");
            handoffCities = Q<VisualElement>("handoff-cities");
            searchField = Q<TextField>("search");
            searchField.value = placeName;
            Q<TextField>("photon-url").value = EditorPrefs.GetString(Prefs + "photon", PlateauApi.DefaultPhotonBase);
            Q<TextField>("api-url").value = EditorPrefs.GetString(Prefs + "api", PlateauApi.DefaultApiBase);
            Q<TextField>("tiles-url").value = EditorPrefs.GetString(Prefs + "tiles", PlateauApi.DefaultGsiTiles);
            Q<TextField>("save-root").value = EditorPrefs.GetString(Prefs + "save", PackDownloader.DefaultDataRoot);
            Q<FloatField>("download-limit").value = EditorPrefs.GetFloat(Prefs + "downloadLimit", 10);
            Q<FloatField>("expanded-limit").value = EditorPrefs.GetFloat(Prefs + "expandedLimit", 30);
            Q<Button>("search-button").clicked += Search;
            Q<Button>("preview").clicked += Preview;
            Q<Button>("apply-bounds").clicked += ApplyBounds;
            Q<Button>("download").clicked += Download;
            Q<Button>("cancel").clicked += () => operation?.Cancel();
            searchField.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return) Search(); });
            map.RegisterCallback<GeometryChangedEvent>(_ => RefreshMap());
            map.RegisterCallback<PointerDownEvent>(OnPointerDown);
            map.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            map.RegisterCallback<PointerUpEvent>(OnPointerUp);
            map.RegisterCallback<WheelEvent>(OnWheel);
            foreach (var name in new[] { "bldg", "tran", "dem" })
                Q<Toggle>(name).RegisterValueChangedCallback(_ => InvalidatePreview());
            foreach (var name in new[] { "photon-url", "api-url", "tiles-url", "save-root" })
            {
                var field = Q<TextField>(name);
                field.RegisterValueChangedCallback(e =>
                {
                    EditorPrefs.SetString(Prefs + name.Split('-')[0], e.newValue);
                    if (name == "api-url" || name == "save-root") InvalidatePreview();
                });
            }
            WriteBounds();
            RefreshMap();
        }

        private T Q<T>(string name) where T : VisualElement => rootVisualElement.Q<T>(name);
        private string ApiBase => Q<TextField>("api-url").value.TrimEnd('/');
        private string DataRoot => Path.GetFullPath(Q<TextField>("save-root").value);

        private async void Search()
        {
            var query = searchField.value?.Trim();
            if (string.IsNullOrEmpty(query)) return;
            candidates.Clear();
            candidates.Add(new Label("検索中…"));
            try
            {
                var searchKey = Q<TextField>("photon-url").value + "\n" + query;
                if (!searchCache.TryGetValue(searchKey, out var results))
                {
                    var cacheKey = SearchCacheKey(Q<TextField>("photon-url").value, query);
                    var stored = EditorPrefs.GetString(cacheKey, "");
                    var cached = string.IsNullOrEmpty(stored) ? null : JsonUtility.FromJson<PhotonCacheEntry>(stored);
                    if (cached != null && cached.features != null &&
                        DateTime.UtcNow.Ticks - cached.savedUtcTicks < TimeSpan.FromDays(7).Ticks)
                        results = cached.features;
                    else
                    {
                        results = await PlateauApi.SearchPlacesAsync(query, Q<TextField>("photon-url").value,
                            CancellationToken.None);
                        EditorPrefs.SetString(cacheKey, JsonUtility.ToJson(new PhotonCacheEntry
                        {
                            savedUtcTicks = DateTime.UtcNow.Ticks,
                            features = results
                        }));
                    }
                    searchCache[searchKey] = results;
                }
                candidates.Clear();
                if (results.Length == 0) candidates.Add(new Label("候補なし。地図または座標入力で進められます。"));
                foreach (var item in results)
                {
                    if (item.geometry?.coordinates == null || item.geometry.coordinates.Length < 2) continue;
                    var p = item.properties;
                    var name = p?.name ?? query;
                    var location = string.Join(" / ", new[] { p?.state, p?.city, p?.district, p?.street }
                        .Where(part => !string.IsNullOrWhiteSpace(part)));
                    var button = new Button(() =>
                    {
                        placeName = name;
                        centerLongitude = item.geometry.coordinates[0];
                        centerLatitude = item.geometry.coordinates[1];
                        bounds = GeoBounds.FromCenter(centerLatitude, centerLongitude);
                        WriteBounds();
                        InvalidatePreview();
                        RefreshMap();
                    }) { text = name + "　" + location + "　[" + (p?.osm_value ?? "施設") + "]" };
                    candidates.Add(button);
                }
            }
            catch (Exception error)
            {
                candidates.Clear();
                candidates.Add(new Label("検索できませんでした: " + error.Message + "。地図または座標入力で進められます。"));
            }
        }

        private void ApplyBounds()
        {
            try
            {
                bounds = new GeoBounds(Q<DoubleField>("west").value, Q<DoubleField>("south").value,
                    Q<DoubleField>("east").value, Q<DoubleField>("north").value);
                centerLatitude = (bounds.South + bounds.North) / 2;
                centerLongitude = (bounds.West + bounds.East) / 2;
                InvalidatePreview();
                RefreshMap();
            }
            catch (Exception error) { previewResult.text = error.Message; }
        }

        private void WriteBounds()
        {
            Q<DoubleField>("west").SetValueWithoutNotify(bounds.West);
            Q<DoubleField>("south").SetValueWithoutNotify(bounds.South);
            Q<DoubleField>("east").SetValueWithoutNotify(bounds.East);
            Q<DoubleField>("north").SetValueWithoutNotify(bounds.North);
        }

        private void InvalidatePreview()
        {
            previewGeneration++;
            desired = null;
            downloaded = null;
            downloadedDataRoot = null;
            handoffCities?.Clear();
            if (handoffResult != null) handoffResult.text = "対象が変わりました。取得後にフォルダを表示します。";
            previewResult.text = "対象を調べ直してください。";
            RefreshOverlay();
        }

        private async void Preview()
        {
            var generation = ++previewGeneration;
            var requestedBounds = bounds;
            var types = new[] { "bldg", "tran", "dem" }.Where(type => Q<Toggle>(type).value).ToArray();
            if (types.Length == 0) { previewResult.text = "建築物・道路・地形から選んでください。"; return; }
            previewResult.text = "カタログを確認中…";
            try
            {
                var cities = await PlateauApi.SearchCityGmlAsync(requestedBounds.West, requestedBounds.South,
                    requestedBounds.East, requestedBounds.North, types, ApiBase, CancellationToken.None);
                if (generation != previewGeneration) return;
                var selected = cities.SelectMany(city => types.SelectMany(type => city.FilesFor(type)
                    .Where(gml => !string.IsNullOrEmpty(gml.url))
                    .Where(gml => JapanMeshCode.GetCatalogBounds(gml.code, gml.url).Intersects(requestedBounds, false))
                    .Select(gml => (city, type, gml)))).ToArray();
                if (selected.Length == 0)
                {
                    desired = null;
                    previewResult.text = "この範囲に選択した種類のCityGMLはありません。";
                    RefreshOverlay();
                    return;
                }
                desired = PackDownloader.CreateManifest(placeName, requestedBounds.West, requestedBounds.South,
                    requestedBounds.East, requestedBounds.North, selected, ApiBase);
                var descriptions = selected.GroupBy(item => item.city.cityCode)
                    .Select(group => group.First().city.cityName + " " + group.First().city.year +
                        " / 仕様" + group.First().city.spec).ToArray();
                var lod = selected.Max(item => item.gml.maxLod);
                previewResult.text = string.Join("、", descriptions) + "\n" +
                    selected.Length + "ファイル / カタログ上の最大LOD " + lod +
                    "\nGML: " + FormatBytes(desired.gmlBytes) + "、付属データを含む取得量: 不明" +
                    "\n選択範囲と交差するファイル全体を取得します。";
                RefreshOverlay();
            }
            catch (Exception error)
            {
                if (generation != previewGeneration) return;
                desired = null;
                previewResult.text = "カタログ取得に失敗しました: " + error.Message;
            }
        }

        private static string SearchCacheKey(string endpoint, string query)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(endpoint + "\n" + query));
            return Prefs + "search." + BitConverter.ToString(bytes).Replace("-", "");
        }

        private async void Download()
        {
            if (desired == null) { progressLabel.text = "先に対象を調べてください。"; return; }
            if (operation != null) { progressLabel.text = "別の処理を実行中です。"; return; }
            try
            {
                var requested = desired;
                var generation = previewGeneration;
                var dataRoot = DataRoot;
                var downloadLimit = Q<FloatField>("download-limit").value;
                var expandedLimit = Q<FloatField>("expanded-limit").value;
                if (downloadLimit <= 0 || expandedLimit <= 0) throw new ArgumentException("容量上限は正の値を指定してください。");
                EditorPrefs.SetFloat(Prefs + "downloadLimit", downloadLimit);
                EditorPrefs.SetFloat(Prefs + "expandedLimit", expandedLimit);
                operation = new CancellationTokenSource();
                var limits = new PackLimits
                {
                    MaxDownloadBytes = (long)(downloadLimit * 1024d * 1024 * 1024),
                    MaxExpandedBytes = (long)(expandedLimit * 1024d * 1024 * 1024)
                };
                var completed = await PackDownloader.DownloadAsync(requested, dataRoot, limits,
                    step => progressQueue.Enqueue(step), operation.Token);
                while (progressQueue.TryDequeue(out _)) { }
                if (generation != previewGeneration || desired != requested)
                {
                    progressLabel.text = "旧条件の取得が完了しました。現在の範囲は調べ直してください。";
                    return;
                }
                downloaded = completed;
                downloadedDataRoot = dataRoot;
                progressLabel.text = "取得完了: " + downloaded.files.Length + "ファイル / ZIP " +
                    FormatBytes(downloaded.zipBytes) + " / 展開後 " + FormatBytes(downloaded.expandedBytes);
                ShowHandoffCities();
            }
            catch (OperationCanceledException) { progressLabel.text = "取得を中断しました。再度「取得する」で確認・再開できます。"; }
            catch (Exception error) { progressLabel.text = "取得失敗: " + error.Message; }
            finally { operation?.Dispose(); operation = null; }
        }

        private void ShowHandoffCities()
        {
            handoffCities.Clear();
            if (downloaded == null || downloaded.status != "complete") return;
            var datasetRoot = Path.Combine(PackDownloader.JobPath(downloadedDataRoot, downloaded), "dataset");
            foreach (var city in downloaded.selectedGmls.GroupBy(entry => entry.cityRoot))
            {
                var entry = city.First();
                var folder = Path.GetFullPath(Path.Combine(datasetRoot, city.Key));
                handoffCities.Add(new Label(entry.cityName + " " + entry.year + " / 仕様" + entry.spec));
                var pathLabel = new Label(folder) { tooltip = folder };
                pathLabel.AddToClassList("result");
                handoffCities.Add(pathLabel);
                handoffCities.Add(new Button(() => OpenSdkForCity(folder))
                    { text = "パスをコピーしてSDKを開く" });
            }
            handoffResult.text = "公式SDKで都市ごとに「参照...」からフォルダを選択してください。Macの選択画面では⌘⇧Gでコピーしたパスを入力できます。";
        }

        private void OpenSdkForCity(string folder)
        {
            if (!Directory.Exists(Path.Combine(folder, "udx")))
            {
                handoffResult.text = "都市フォルダが見つかりません。取得結果を確認してください: " + folder;
                return;
            }
            EditorGUIUtility.systemCopyBuffer = folder;
            handoffResult.text = EditorApplication.ExecuteMenuItem("PLATEAU/PLATEAU SDK")
                ? "パスをコピーしました。公式SDKで「都市の追加 → ローカル → 入力フォルダ → 参照...」を選んでください。"
                : "パスをコピーしました。公式SDKを「PLATEAU → PLATEAU SDK」から手動で開いてください。";
        }

        private void DrainProgress()
        {
            if (progressLabel == null) return;
            while (progressQueue.TryDequeue(out var step))
                progressLabel.text = step.Stage == "preparing"
                    ? "Packを準備中: " + step.Bytes + "%"
                    : step.Stage + ": " + FormatBytes(step.Bytes) +
                      (step.TotalBytes > 0 ? " / " + FormatBytes(step.TotalBytes) : "");
        }

        private static string FormatBytes(long value) => value < 0 ? "不明" :
            value >= 1024L * 1024 * 1024 ? (value / (1024d * 1024 * 1024)).ToString("F2", CultureInfo.InvariantCulture) + " GiB" :
            (value / (1024d * 1024)).ToString("F1", CultureInfo.InvariantCulture) + " MiB";

        private void OnPointerDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            pointerActive = true;
            selecting = e.shiftKey;
            pointerStart = map.WorldToLocal(e.position);
            pointerCenterLatitude = centerLatitude;
            pointerCenterLongitude = centerLongitude;
            map.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            if (!pointerActive) return;
            if (selecting)
            {
                ShowScreenBounds(pointerStart, map.WorldToLocal(e.position));
            }
            else
            {
                var start = ToWorld(pointerCenterLatitude, pointerCenterLongitude);
                var currentPosition = map.WorldToLocal(e.position);
                var current = FromWorld(start.x - (currentPosition.x - pointerStart.x),
                    start.y - (currentPosition.y - pointerStart.y));
                centerLatitude = current.latitude;
                centerLongitude = current.longitude;
                RefreshMap();
            }
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            if (!pointerActive) return;
            pointerActive = false;
            map.ReleasePointer(e.pointerId);
            if (selecting)
            {
                var a = ScreenToGeo(pointerStart);
                var end = map.WorldToLocal(e.position);
                var b = ScreenToGeo(end);
                if (Math.Abs(end.x - pointerStart.x) > 5 && Math.Abs(end.y - pointerStart.y) > 5)
                {
                    bounds = new GeoBounds(Math.Min(a.longitude, b.longitude), Math.Min(a.latitude, b.latitude),
                        Math.Max(a.longitude, b.longitude), Math.Max(a.latitude, b.latitude));
                    WriteBounds();
                    InvalidatePreview();
                }
                selecting = false;
                RefreshOverlay();
            }
        }

        private void OnWheel(WheelEvent e)
        {
            if (pointerActive) { e.StopPropagation(); return; }
            if (Mathf.Abs(e.delta.y) <= Mathf.Abs(e.delta.x)) return;
            var step = wheelZoom.Consume(e.delta.y / WheelEvent.scrollDeltaPerTick,
                EditorApplication.timeSinceStartup, zoom, 5, 18);
            if (step == 0) { e.StopPropagation(); return; }
            var point = map.WorldToLocal(e.mousePosition);
            var anchor = ScreenToGeo(point);
            zoom += step;
            var center = ToWorld(anchor.latitude, anchor.longitude);
            var adjusted = FromWorld(center.x - point.x + map.contentRect.width / 2,
                center.y - point.y + map.contentRect.height / 2);
            centerLatitude = adjusted.latitude;
            centerLongitude = adjusted.longitude;
            RefreshMap();
            e.StopPropagation();
        }

        private (double x, double y) ToWorld(double latitude, double longitude)
        {
            var scale = 256d * (1 << zoom);
            var sin = Math.Sin(Math.Max(-85.0511, Math.Min(85.0511, latitude)) * Math.PI / 180);
            return ((longitude + 180) / 360 * scale,
                (0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * scale);
        }

        private (double latitude, double longitude) FromWorld(double x, double y)
        {
            var scale = 256d * (1 << zoom);
            return (Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / scale))) * 180 / Math.PI,
                x / scale * 360 - 180);
        }

        private (double latitude, double longitude) ScreenToGeo(Vector2 point)
        {
            var center = ToWorld(centerLatitude, centerLongitude);
            return FromWorld(center.x + point.x - map.contentRect.width / 2,
                center.y + point.y - map.contentRect.height / 2);
        }

        private Vector2 GeoToScreen(double latitude, double longitude)
        {
            var center = ToWorld(centerLatitude, centerLongitude);
            var point = ToWorld(latitude, longitude);
            return new Vector2((float)(point.x - center.x + map.contentRect.width / 2),
                (float)(point.y - center.y + map.contentRect.height / 2));
        }

        private void RefreshMap()
        {
            if (map == null || tiles == null || map.contentRect.width <= 0) return;
            tiles.Clear();
            var needed = new HashSet<string>();
            var missing = new HashSet<string>();
            var center = ToWorld(centerLatitude, centerLongitude);
            int firstX = (int)Math.Floor((center.x - map.contentRect.width / 2) / 256);
            int lastX = (int)Math.Floor((center.x + map.contentRect.width / 2) / 256);
            int firstY = (int)Math.Floor((center.y - map.contentRect.height / 2) / 256);
            int lastY = (int)Math.Floor((center.y + map.contentRect.height / 2) / 256);
            for (var x = firstX; x <= lastX; x++)
            for (var y = firstY; y <= lastY; y++)
            {
                var tileX = ((x % (1 << zoom)) + (1 << zoom)) % (1 << zoom);
                var key = zoom + "/" + tileX + "/" + y;
                needed.Add(key);
                var visual = new VisualElement { pickingMode = PickingMode.Ignore };
                visual.style.position = Position.Absolute;
                visual.style.left = (float)(x * 256 - center.x + map.contentRect.width / 2);
                visual.style.top = (float)(y * 256 - center.y + map.contentRect.height / 2);
                visual.style.width = 256;
                visual.style.height = 256;
                tiles.Add(visual);
                if (tileTextures.TryGetValue(key, out var cached)) visual.style.backgroundImage = new StyleBackground(cached);
                else if (y >= 0 && y < (1 << zoom)) missing.Add(key);
            }
            foreach (var stale in tileRequests.Keys.Where(key => !needed.Contains(key)).ToArray())
            {
                tileRequests[stale].Cancellation.Cancel();
                tileRequests.Remove(stale);
            }
            neededTileKeys = needed;
            foreach (var key in missing) EnsureTileRequest(key);
            RefreshOverlay();
        }

        private void EnsureTileRequest(string key)
        {
            if (tileRequests.ContainsKey(key)) return;
            var request = new TileRequest();
            tileRequests.Add(key, request);
            _ = LoadTileAsync(key, request);
        }

        private async Task LoadTileAsync(string key, TileRequest request)
        {
            try
            {
                await tileDownloadSlots.WaitAsync(request.Cancellation.Token);
                byte[] bytes;
                try
                {
                    var url = Q<TextField>("tiles-url").value.TrimEnd('/') + "/" + key + ".png";
                    bytes = await PlateauApi.GetTileAsync(url, request.Cancellation.Token);
                }
                finally { tileDownloadSlots.Release(); }
                if (request.Cancellation.IsCancellationRequested || !neededTileKeys.Contains(key)) return;
                var texture = new Texture2D(2, 2);
                if (!texture.LoadImage(bytes)) { DestroyImmediate(texture); return; }
                tileTextures[key] = texture;
                RefreshMap();
            }
            catch (OperationCanceledException) { }
            catch { /* Coordinates and catalog remain usable when map tiles are unavailable. */ }
            finally
            {
                if (tileRequests.TryGetValue(key, out var current) && ReferenceEquals(current, request))
                    tileRequests.Remove(key);
                request.Cancellation.Dispose();
            }
        }

        private void RefreshOverlay()
        {
            if (map == null || meshOverlay == null || boundsOverlay == null) return;
            meshOverlay.Clear();
            boundsOverlay.Clear();
            if (desired != null)
            {
                foreach (var entry in desired.selectedGmls.GroupBy(gml => gml.url).Select(group => group.First()).Take(100))
                    DrawGeoRect(meshOverlay, JapanMeshCode.GetCatalogBounds(entry.code, entry.url),
                        new Color(1f, 0.58f, 0.12f, 0.16f), new Color(1f, 0.58f, 0.12f, 0.8f));
            }
            DrawGeoRect(boundsOverlay, bounds, new Color(0.1f, 0.55f, 1f, 0.12f),
                new Color(0.1f, 0.55f, 1f, 1f));
        }

        private void ShowScreenBounds(Vector2 a, Vector2 b)
        {
            boundsOverlay.Clear();
            DrawScreenRect(boundsOverlay, a, b, new Color(0.1f, 0.55f, 1f, 0.12f),
                new Color(0.1f, 0.55f, 1f, 1f));
        }

        private void DrawGeoRect(VisualElement parent, GeoBounds geo, Color fill, Color stroke) =>
            DrawScreenRect(parent, GeoToScreen(geo.North, geo.West), GeoToScreen(geo.South, geo.East), fill, stroke);

        private static void DrawScreenRect(VisualElement parent, Vector2 a, Vector2 b, Color fill, Color stroke)
        {
            var rect = new VisualElement { pickingMode = PickingMode.Ignore };
            rect.style.position = Position.Absolute;
            rect.style.left = Mathf.Min(a.x, b.x);
            rect.style.top = Mathf.Min(a.y, b.y);
            rect.style.width = Mathf.Abs(a.x - b.x);
            rect.style.height = Mathf.Abs(a.y - b.y);
            rect.style.backgroundColor = fill;
            rect.style.borderTopWidth = rect.style.borderBottomWidth = 2;
            rect.style.borderLeftWidth = rect.style.borderRightWidth = 2;
            rect.style.borderTopColor = rect.style.borderBottomColor = stroke;
            rect.style.borderLeftColor = rect.style.borderRightColor = stroke;
            parent.Add(rect);
        }
    }
}
