using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Zabaglione.PlateauAreaDownloader.Editor.Tests
{
    public class PackValidationTests
    {
        private string root;

        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "plateau-area-test-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void ExtractSafe_RejectsParentTraversal()
        {
            Directory.CreateDirectory(root);
            var zip = Path.Combine(root, "bad.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open()))
                writer.Write("unsafe");

            Assert.Throws<IOException>(() => PackDownloader.ExtractSafe(zip,
                Path.Combine(root, "dataset"), 1024, null, CancellationToken.None));
            Assert.That(File.Exists(Path.Combine(root, "outside.txt")), Is.False);
        }

        [Test]
        public void ExtractThenValidate_ReportsWorkAfterExpansionCompletes()
        {
            Directory.CreateDirectory(root);
            var zip = Path.Combine(root, "sample.zip");
            var city = "sample_city_2025_citygml_1_op";
            var gmlName = "53393599_bldg_test.gml";
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry(city + "/udx/bldg/" + gmlName).Open()))
                    writer.Write("<root><entry codeSpace=\"../../codelists/values.xml\" /></root>");
                using (var writer = new StreamWriter(archive.CreateEntry(city + "/codelists/values.xml").Open()))
                    writer.Write("<values />");
            }

            var stages = new List<PackProgress>();
            var dataset = Path.Combine(root, "dataset");
            var files = PackDownloader.ExtractSafe(zip, dataset, 1024 * 1024, stages.Add,
                CancellationToken.None);
            Assert.That(files.Length, Is.EqualTo(2));
            var expanded = stages[stages.Count - 1];
            Assert.That(expanded.Stage, Is.EqualTo("extracting"));
            Assert.That(expanded.Bytes, Is.EqualTo(expanded.TotalBytes));

            PackDownloader.ValidateReferences(dataset, new[] { new SelectedGml
            {
                cityRoot = city, type = "bldg",
                url = "https://example.invalid/udx/bldg/" + gmlName
            } }, CancellationToken.None, stages.Add);
            var validated = stages[stages.Count - 1];
            Assert.That(validated.Stage, Is.EqualTo("validating-references"));
            Assert.That(validated.FilesCompleted, Is.EqualTo(1));
            Assert.That(validated.ReferencesChecked, Is.EqualTo(1));
        }

        [Test]
        public void ValidateReferences_RejectsMissingCodeListAndTexture()
        {
            var city = "sample_city_2025_citygml_1_op";
            var file = "53393599_bldg_test.gml";
            var path = Path.Combine(root, city, "udx", "bldg", file);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "<root xmlns:gml=\"http://www.opengis.net/gml\"><gml:description codeSpace=\"../../codelists/missing.xml\"/>" +
                "<imageURI>../textures/missing.png</imageURI></root>");
            var selected = new[] { new SelectedGml
            {
                cityRoot = city,
                type = "bldg",
                url = "https://example.invalid/udx/bldg/" + file
            } };
            var error = Assert.Throws<IOException>(() => PackDownloader.ValidateReferences(root,
                selected, CancellationToken.None));
            StringAssert.Contains("Missing local references (2)", error.Message);
        }

        [Test]
        public void VerifySavedFiles_RejectsSameSizeCorruption()
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "data.bin");
            File.WriteAllText(path, "good");
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            var files = new[] { new SavedFile { path = "data.bin", size = 4, sha256 = hash } };
            Assert.That(PackDownloader.VerifySavedFiles(root, files), Is.True);
            File.WriteAllText(path, "evil");
            Assert.That(PackDownloader.VerifySavedFiles(root, files), Is.False);
        }

        [Test]
        public void CatalogResponse_ParsesLiveResponseShapes()
        {
            const string json = "{\"cities\":[{\"cityCode\":\"26100\",\"cityName\":\"京都市\",\"year\":2025," +
                "\"spec\":\"5.0\",\"files\":{\"bldg\":[{\"code\":\"52354602\",\"maxLod\":2," +
                "\"fileSize\":59603577,\"url\":\"https://example.invalid/sample.gml\"}]}," +
                "\"metadataZipUrls\":[\"https://example.invalid/metadata.zip\"]}]}";
            var response = JsonUtility.FromJson<CatalogResponse>(json);
            Assert.That(response.cities[0].files.bldg.Length, Is.EqualTo(1));
            Assert.That(response.cities[0].files.bldg[0].code, Is.EqualTo("52354602"));
            Assert.That(response.cities[0].files.bldg[0].fileSize, Is.EqualTo(59603577));
            Assert.That(response.cities[0].metadataZipUrls[0], Does.EndWith("metadata.zip"));
        }

        [Test]
        public void DownloadAsync_RejectsAssetsDestinationBeforeNetworkAccess()
        {
            var manifest = new PackManifest
            {
                key = "test", apiBase = "https://example.invalid",
                selectedGmls = new[] { new SelectedGml { url = "https://example.invalid/test.gml" } }
            };
            Assert.ThrowsAsync<ArgumentException>(async () => await PackDownloader.DownloadAsync(
                manifest, Path.Combine(Application.dataPath, "CityGml"), new PackLimits(),
                null, CancellationToken.None));
        }
    }
}
