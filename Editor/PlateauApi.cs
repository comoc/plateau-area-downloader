using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Zabaglione.PlateauAreaDownloader.Editor
{
    [Serializable]
    internal sealed class PhotonResponse
    {
        public PhotonFeature[] features;
    }

    [Serializable]
    internal sealed class PhotonFeature
    {
        public PhotonGeometry geometry;
        public PhotonProperties properties;
    }

    [Serializable]
    internal sealed class PhotonGeometry
    {
        public double[] coordinates;
    }

    [Serializable]
    internal sealed class PhotonProperties
    {
        public string name;
        public string city;
        public string district;
        public string state;
        public string country;
        public string osm_value;
        public string street;
    }

    [Serializable]
    internal sealed class CatalogResponse
    {
        public CatalogCity[] cities;
    }

    [Serializable]
    internal sealed class CatalogCity
    {
        public string cityCode;
        public string cityName;
        public int year;
        public string spec;
        public string url;
        public CatalogFiles files;
        public string[] metadataZipUrls;

        public IEnumerable<CatalogGml> FilesFor(string type)
        {
            if (files == null) return Array.Empty<CatalogGml>();
            switch (type)
            {
                case "bldg": return files.bldg ?? Array.Empty<CatalogGml>();
                case "tran": return files.tran ?? Array.Empty<CatalogGml>();
                case "dem": return files.dem ?? Array.Empty<CatalogGml>();
                default: return Array.Empty<CatalogGml>();
            }
        }
    }

    [Serializable]
    internal sealed class CatalogFiles
    {
        public CatalogGml[] bldg;
        public CatalogGml[] tran;
        public CatalogGml[] dem;
    }

    [Serializable]
    internal sealed class CatalogGml
    {
        public string code;
        public int maxLod;
        public string url;
        public long fileSize;
    }

    [Serializable]
    internal sealed class PackCreated
    {
        public string id;
    }

    [Serializable]
    internal sealed class PackStatus
    {
        public string status;
        public float progress;
        public string error;
    }

    [Serializable]
    internal sealed class PackRequest
    {
        public string[] urls;
    }

    internal static class PlateauApi
    {
        internal const string DefaultApiBase = "https://api.plateauview.mlit.go.jp";
        internal const string DefaultPhotonBase = "https://photon.komoot.io";
        internal const string DefaultGsiTiles = "https://cyberjapandata.gsi.go.jp/xyz/std";

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PLATEAU-Area-Downloader/0.1");
            return client;
        }

        internal static async Task<PhotonFeature[]> SearchPlacesAsync(
            string query, string photonBase, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<PhotonFeature>();
            var url = photonBase.TrimEnd('/') + "/api/?q=" + Uri.EscapeDataString(query.Trim()) +
                      "&limit=8";
            var json = await GetStringAsync(url, token);
            return JsonUtility.FromJson<PhotonResponse>(json)?.features ?? Array.Empty<PhotonFeature>();
        }

        internal static async Task<CatalogCity[]> SearchCityGmlAsync(
            double west, double south, double east, double north,
            IEnumerable<string> types, string apiBase, CancellationToken token)
        {
            var selectedTypes = types.Distinct().ToArray();
            if (selectedTypes.Length == 0) return Array.Empty<CatalogCity>();
            var c = CultureInfo.InvariantCulture;
            var extent = string.Join(",", new[] { west, south, east, north }.Select(v => v.ToString("R", c)));
            var url = apiBase.TrimEnd('/') + "/datacatalog/citygml/r:" + extent +
                      "?types=" + string.Join(",", selectedTypes);
            var json = await GetStringAsync(url, token);
            return JsonUtility.FromJson<CatalogResponse>(json)?.cities ?? Array.Empty<CatalogCity>();
        }

        internal static async Task<string> CreatePackAsync(string[] urls, string apiBase, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, apiBase.TrimEnd('/') + "/citygml/pack");
            request.Content = new StringContent(JsonUtility.ToJson(new PackRequest { urls = urls }),
                Encoding.UTF8, "application/json");
            using var response = await SendWithThrottleAsync(request, token);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("Pack creation failed: " + (int)response.StatusCode + " " + body);
            var id = JsonUtility.FromJson<PackCreated>(body)?.id;
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Pack response omitted id.");
            return id;
        }

        internal static async Task<PackStatus> GetPackStatusAsync(string id, string apiBase, CancellationToken token)
        {
            var url = apiBase.TrimEnd('/') + "/citygml/pack/" + Uri.EscapeDataString(id) + "/status";
            var json = await GetStringAsync(url, token);
            return JsonUtility.FromJson<PackStatus>(json) ?? throw new InvalidOperationException("Invalid pack status.");
        }

        internal static async Task<byte[]> GetTileAsync(string url, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendWithThrottleAsync(request, token);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("HTTP " + (int)response.StatusCode + " for " + url);
            return body;
        }

        private static async Task<HttpResponseMessage> SendWithThrottleAsync(HttpRequestMessage request, CancellationToken token)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var retry = CloneRequest(request);
                var response = await Client.SendAsync(retry, HttpCompletionOption.ResponseContentRead, token);
                if (response.StatusCode != (HttpStatusCode)429 || attempt == 2) return response;
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(3 * (attempt + 1));
                response.Dispose();
                await Task.Delay(delay, token);
            }
            throw new InvalidOperationException("Request retry exhausted.");
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage source)
        {
            var clone = new HttpRequestMessage(source.Method, source.RequestUri);
            if (source.Content != null)
            {
                var bytes = source.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                clone.Content = new ByteArrayContent(bytes);
                foreach (var header in source.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return clone;
        }
    }
}
