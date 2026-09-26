using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PLATEAU.CityImport.AreaSelector;
using PLATEAU.CityImport.Config;
using PLATEAU.CityImport.Config.PackageImportConfigs;
using PLATEAU.CityImport.Import;
using PLATEAU.CityInfo;
using PLATEAU.Dataset;
using PLATEAU.Native;
using PLATEAU.PolygonMesh;
using UnityEngine;

namespace Zabaglione.PlateauAreaDownloader.Editor
{
    internal sealed class ImportOptions
    {
        internal int MinimumLod;
        internal int MaximumLod = 4;
        internal bool IncludeTexture = true;
        internal bool Collider = true;
        internal bool Attributes = true;
        internal MeshGranularity Granularity = MeshGranularity.PerPrimaryFeatureObject;
        internal PLATEAUInstancedCityModel ExistingOrigin;
    }

    internal sealed class ImportOutcome
    {
        internal int CityCount;
        internal int GmlCount;
        internal int ModelCount;
        internal int MeshCount;
        internal string[] Errors;
        internal bool Interrupted;
    }

    internal static class PlateauImport
    {
        internal static async Task<ImportOutcome> ImportAsync(PackManifest manifest, string dataRoot,
            ImportOptions options, CancellationToken token)
        {
            if (manifest == null || manifest.status != "complete")
                throw new InvalidOperationException("Complete download is required before import.");
            if (manifest.coordinateZone < 1 || manifest.coordinateZone > 19)
                throw new InvalidOperationException("Select a Japanese plane coordinate zone before import.");
            var datasetPath = Path.Combine(PackDownloader.JobPath(dataRoot, manifest), "dataset");
            await Task.Run(() => PackDownloader.ValidateReferences(datasetPath, manifest.selectedGmls, token), token);
            var outcome = new ImportOutcome();
            var errors = new System.Collections.Generic.List<string>();
            var beforeModels = UnityEngine.Object.FindObjectsByType<PLATEAUInstancedCityModel>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            var beforeMeshes = UnityEngine.Object.FindObjectsByType<MeshFilter>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            var grouped = manifest.selectedGmls.GroupBy(entry => entry.cityRoot).ToArray();
            PlateauVector3d sharedOrigin = default;
            bool hasOrigin = false;
            if (options.ExistingOrigin != null)
            {
                var geo = options.ExistingOrigin.GeoReference;
                if (geo.ZoneID != manifest.coordinateZone)
                    throw new InvalidOperationException("Existing model uses another coordinate zone.");
                sharedOrigin = geo.ReferencePoint;
                hasOrigin = true;
            }
            void Capture(string condition, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    errors.Add(condition);
            }
            Application.logMessageReceived += Capture;
            try
            {
                foreach (var city in grouped)
                {
                    token.ThrowIfCancellationRequested();
                    var cityRoot = Path.Combine(datasetPath, city.Key);
                    if (!Directory.Exists(Path.Combine(cityRoot, "udx")))
                        throw new DirectoryNotFoundException(cityRoot);
                    var meshes = PackDownloader.ImportMeshCodes(manifest, city.Key);
                    if (meshes.Length == 0) throw new IOException("No selected third-level meshes for " + city.Key);
                    using var grids = GridCodeList.CreateFromGridCodesStr(meshes);
                    var before = new ConfigBeforeAreaSelect(new DatasetSourceConfigLocal(cityRoot),
                        manifest.coordinateZone);
                    var area = new AreaSelectResult(before, grids, AreaSelectResult.ResultReason.Confirm);
                    var config = CityImportConfig.CreateWithAreaSelectResult(area);
                    if (!hasOrigin)
                    {
                        sharedOrigin = config.ReferencePoint;
                        hasOrigin = true;
                    }
                    config.ReferencePoint = sharedOrigin;
                    foreach (var package in config.PackageImportConfigDict.PackagesToLoad().ToArray())
                        config.GetConfigForPackage(package).ImportPackage = false;
                    foreach (var type in city.Select(entry => entry.type).Distinct())
                    {
                        var package = ToPackage(type);
                        var setting = config.GetConfigForPackage(package);
                        setting.ImportPackage = true;
                        int max = Math.Min(options.MaximumLod, setting.LODRange.AvailableMaxLOD);
                        int min = Math.Min(Math.Max(options.MinimumLod, setting.LODRange.MinLOD), max);
                        setting.LODRange = new LODRange(min, max, setting.LODRange.AvailableMaxLOD);
                        setting.IncludeTexture = options.IncludeTexture;
                        setting.DoSetMeshCollider = options.Collider;
                        setting.DoSetAttrInfo = options.Attributes;
                        setting.MeshGranularity = options.Granularity;
                    }
                    var found = config.SearchMatchingGMLList(token);
                    if (found.Count == 0)
                        throw new IOException("SDK found no GML files for " + city.Key);
                    outcome.GmlCount += found.Count;
                    await CityImporter.ImportAsync(config, null, token);
                    token.ThrowIfCancellationRequested();
                    outcome.CityCount++;
                }
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                outcome.Interrupted = true;
            }
            finally
            {
                Application.logMessageReceived -= Capture;
                outcome.ModelCount = UnityEngine.Object.FindObjectsByType<PLATEAUInstancedCityModel>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length - beforeModels;
                outcome.MeshCount = UnityEngine.Object.FindObjectsByType<MeshFilter>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length - beforeMeshes;
                outcome.Errors = errors.ToArray();
            }
            return outcome;
        }

        private static PredefinedCityModelPackage ToPackage(string type)
        {
            switch (type)
            {
                case "bldg": return PredefinedCityModelPackage.Building;
                case "tran": return PredefinedCityModelPackage.Road;
                case "dem": return PredefinedCityModelPackage.Relief;
                default: throw new ArgumentException("Unsupported package: " + type);
            }
        }
    }
}
