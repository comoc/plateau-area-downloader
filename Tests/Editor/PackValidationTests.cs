using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Zabaglione.PlateauAreaDownloader.Editor.Tests
{
    public class PackValidationTests
    {
        private const string JobKey = "0123456789abcdef01234567";
        private string root;

        [SetUp]
        public void SetUp()
        {
            var tempRoot = Path.GetTempPath();
            if (Application.platform == RuntimePlatform.OSXEditor &&
                (tempRoot.StartsWith("/var/", StringComparison.Ordinal) ||
                 tempRoot.StartsWith("/tmp/", StringComparison.Ordinal)))
                tempRoot = "/private" + tempRoot;
            root = Path.Combine(tempRoot, "plateau-area-test-" + Guid.NewGuid().ToString("N"));
        }

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
        public async Task DownloadAsync_RejectsAssetsDestinationBeforeNetworkAccess()
        {
            var manifest = new PackManifest
            {
                key = "test", apiBase = "https://example.invalid",
                selectedGmls = new[] { new SelectedGml { url = "https://example.invalid/test.gml" } }
            };
            await AssertDownloadAsyncThrowsAsync<ArgumentException>(() => PackDownloader.DownloadAsync(
                manifest, Path.Combine(Application.dataPath, "CityGml"), new PackLimits(),
                null, CancellationToken.None));
        }

        [Test]
        public async Task DownloadAsync_CachedDatasetRemovesOnlyKnownTemporaryFiles()
        {
            var desired = WriteManifest("complete", true);
            WriteTemporaryFiles(desired);
            var jobPath = PackDownloader.JobPath(root, desired);
            var unknown = Path.Combine(jobPath, "notes.txt");
            File.WriteAllText(unknown, "keep");

            var result = await PackDownloader.DownloadAsync(desired, root, new PackLimits(),
                null, CancellationToken.None);

            Assert.That(result.status, Is.EqualTo("complete"));
            Assert.That(File.Exists(Path.Combine(jobPath, "dataset", "sample_city", "udx", "bldg", "sample.gml")), Is.True);
            AssertTemporaryFilesMissing(jobPath);
            Assert.That(File.Exists(unknown), Is.True);
            Assert.That(PackDownloader.LoadManifest(jobPath).packId, Is.EqualTo("reusable-pack"));
        }

        [Test]
        public async Task DownloadAsync_InterruptedBeforeRecoveryCleansSafeStagingAndKeepsPackId()
        {
            var desired = WriteManifest("pack-created", false);
            WriteTemporaryFiles(desired);
            var jobPath = PackDownloader.JobPath(root, desired);
            var unknown = Path.Combine(jobPath, "notes.txt");
            File.WriteAllText(unknown, "keep");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await AssertDownloadAsyncCatchesAsync<OperationCanceledException>(() =>
                PackDownloader.DownloadAsync(desired, root, new PackLimits(), null, cancellation.Token));

            var saved = PackDownloader.LoadManifest(jobPath);
            Assert.That(saved.status, Is.EqualTo("interrupted"));
            Assert.That(saved.packId, Is.EqualTo("reusable-pack"));
            AssertTemporaryFilesMissing(jobPath);
            Assert.That(File.Exists(unknown), Is.True);
            Assert.That(desired.status, Is.EqualTo("pack-created"));
        }

        [Test]
        public async Task DownloadAsync_CancelDuringCompleteVerificationKeepsCompleteStatus()
        {
            var desired = WriteManifest("complete", true);
            var jobPath = PackDownloader.JobPath(root, desired);
            using var cancellation = new CancellationTokenSource();

            await AssertDownloadAsyncCatchesAsync<OperationCanceledException>(() =>
                PackDownloader.DownloadAsync(desired, root, new PackLimits(), step =>
                {
                    if (step.Stage == "verifying-files") cancellation.Cancel();
                }, cancellation.Token));

            Assert.That(PackDownloader.LoadManifest(jobPath).status, Is.EqualTo("complete"));
            Assert.That(Directory.Exists(Path.Combine(jobPath, "dataset")), Is.True);
            var resumed = await PackDownloader.DownloadAsync(desired, root, new PackLimits(),
                null, CancellationToken.None);
            Assert.That(resumed.status, Is.EqualTo("complete"));
        }

        [Test]
        public async Task DownloadAsync_FailureCleansTemporaryFilesAndKeepsDataset()
        {
            var desired = WriteManifest("pack-created", false);
            var jobPath = PackDownloader.JobPath(root, desired);
            var saved = PackDownloader.LoadManifest(jobPath);
            saved.apiBase = null;
            File.WriteAllText(Path.Combine(jobPath, "manifest.json"), JsonUtility.ToJson(saved));
            var dataset = Path.Combine(jobPath, "dataset");
            Directory.CreateDirectory(dataset);
            File.WriteAllText(Path.Combine(dataset, "user.bin"), "keep");
            WriteTemporaryFiles(desired);

            await AssertDownloadAsyncThrowsAnyAsync(() => PackDownloader.DownloadAsync(
                desired, root, new PackLimits(), null, CancellationToken.None));

            Assert.That(PackDownloader.LoadManifest(jobPath).status, Is.EqualTo("failed"));
            Assert.That(PackDownloader.LoadManifest(jobPath).packId, Is.EqualTo("reusable-pack"));
            AssertTemporaryFilesMissing(jobPath);
            Assert.That(File.ReadAllText(Path.Combine(dataset, "user.bin")), Is.EqualTo("keep"));
        }

        [Test]
        public async Task DownloadAsync_RecoversPreviousDatasetBeforeCleaningCrashRemainders()
        {
            var desired = WriteManifest("complete", true);
            var jobPath = PackDownloader.JobPath(root, desired);
            Directory.Move(Path.Combine(jobPath, "dataset"), Path.Combine(jobPath, "dataset.previous"));
            Directory.CreateDirectory(Path.Combine(jobPath, "staging"));
            File.WriteAllText(Path.Combine(jobPath, "staging", "partial.bin"), "partial");

            var resumed = await PackDownloader.DownloadAsync(desired, root, new PackLimits(),
                null, CancellationToken.None);

            Assert.That(resumed.status, Is.EqualTo("complete"));
            Assert.That(Directory.Exists(Path.Combine(jobPath, "dataset")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(jobPath, "dataset.previous")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(jobPath, "staging")), Is.False);
        }

        [Test]
        public void ReplaceDataset_RollsBackOldDatasetWhenStagingMoveFails()
        {
            var desired = WriteManifest("downloaded", false);
            var jobPath = PackDownloader.JobPath(root, desired);
            var dataset = Path.Combine(jobPath, "dataset");
            var staging = Path.Combine(jobPath, "staging");
            Directory.CreateDirectory(dataset);
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(dataset, "old.bin"), "old");
            File.WriteAllText(Path.Combine(staging, "new.bin"), "new");

            Assert.Throws<IOException>(() => PackDownloader.ReplaceDataset(jobPath,
                () => throw new IOException("simulated staging move failure")));

            Assert.That(File.ReadAllText(Path.Combine(dataset, "old.bin")), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(Path.Combine(staging, "new.bin")), Is.EqualTo("new"));
            Assert.That(Directory.Exists(Path.Combine(jobPath, "dataset.previous")), Is.False);
        }

        [Test]
        public async Task DownloadAsync_RejectsUnownedManifestWithoutDeletingFiles()
        {
            var desired = WriteManifest("pack-created", false);
            var jobPath = PackDownloader.JobPath(root, desired);
            var foreign = PackDownloader.LoadManifest(jobPath);
            foreign.key = "aaaaaaaaaaaaaaaaaaaaaaaa";
            File.WriteAllText(Path.Combine(jobPath, "manifest.json"), JsonUtility.ToJson(foreign));
            File.WriteAllText(Path.Combine(jobPath, "pack.zip"), "keep");

            await AssertDownloadAsyncThrowsAsync<IOException>(() => PackDownloader.DownloadAsync(
                desired, root, new PackLimits(), null, CancellationToken.None));

            Assert.That(File.Exists(Path.Combine(jobPath, "pack.zip")), Is.True);
            Assert.That(PackDownloader.LoadManifest(jobPath).key, Is.EqualTo(foreign.key));
        }

        [Test]
        public async Task DownloadAsync_RejectsMissingManifestWithoutDeletingFiles()
        {
            var desired = WriteManifest("pack-created", false);
            var jobPath = PackDownloader.JobPath(root, desired);
            File.Delete(Path.Combine(jobPath, "manifest.json"));
            File.WriteAllText(Path.Combine(jobPath, "pack.zip.part"), "keep");

            await AssertDownloadAsyncThrowsAsync<IOException>(() => PackDownloader.DownloadAsync(
                desired, root, new PackLimits(), null, CancellationToken.None));

            Assert.That(File.Exists(Path.Combine(jobPath, "pack.zip.part")), Is.True);
        }

        [Test]
        public async Task DownloadAsync_RejectsCorruptManifestWithoutDeletingFiles()
        {
            var desired = WriteManifest("pack-created", false);
            var jobPath = PackDownloader.JobPath(root, desired);
            File.WriteAllText(Path.Combine(jobPath, "manifest.json"), "{broken json");
            File.WriteAllText(Path.Combine(jobPath, "pack.zip"), "keep");

            await AssertDownloadAsyncThrowsAnyAsync(() => PackDownloader.DownloadAsync(
                desired, root, new PackLimits(), null, CancellationToken.None));

            Assert.That(File.Exists(Path.Combine(jobPath, "pack.zip")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(jobPath, "manifest.json")), Is.EqualTo("{broken json"));
        }

        [Test]
        public async Task DownloadAsync_ClonesDesiredAndDropsStalePackIdWhenNoManifestExists()
        {
            var desired = new PackManifest
            {
                key = JobKey, status = "new", packId = "stale-in-memory",
                apiBase = "https://example.invalid",
                selectedGmls = new[] { new SelectedGml { url = "https://example.invalid/sample.gml" } }
            };
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await AssertDownloadAsyncCatchesAsync<OperationCanceledException>(() =>
                PackDownloader.DownloadAsync(desired, root, new PackLimits(), null, cancellation.Token));

            var saved = PackDownloader.LoadManifest(PackDownloader.JobPath(root, desired));
            Assert.That(saved.status, Is.EqualTo("interrupted"));
            Assert.That(string.IsNullOrEmpty(saved.packId), Is.True);
            Assert.That(desired.status, Is.EqualTo("new"));
            Assert.That(desired.packId, Is.EqualTo("stale-in-memory"));
        }

        [Test]
        public async Task DownloadAsync_PreservesAmbiguousDatasetRecoveryState()
        {
            var desired = WriteManifest("downloaded", false);
            var jobPath = PackDownloader.JobPath(root, desired);
            var dataset = Path.Combine(jobPath, "dataset");
            var previous = Path.Combine(jobPath, "dataset.previous");
            Directory.CreateDirectory(dataset);
            Directory.CreateDirectory(previous);
            var staging = Path.Combine(jobPath, "staging");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(dataset, "new.bin"), "new");
            File.WriteAllText(Path.Combine(previous, "old.bin"), "old");
            File.WriteAllText(Path.Combine(staging, "in-progress.bin"), "keep");

            await AssertDownloadAsyncThrowsAsync<IOException>(() => PackDownloader.DownloadAsync(
                desired, root, new PackLimits(), null, CancellationToken.None));

            Assert.That(File.ReadAllText(Path.Combine(dataset, "new.bin")), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(Path.Combine(previous, "old.bin")), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(Path.Combine(staging, "in-progress.bin")), Is.EqualTo("keep"));
        }

        [Test]
        public async Task DownloadAsync_CancelBeforeRecoveryPreservesDatasetPreviousAndStaging()
        {
            var desired = WriteManifest("downloaded", false);
            var jobPath = PackDownloader.JobPath(root, desired);
            var dataset = Path.Combine(jobPath, "dataset");
            var previous = Path.Combine(jobPath, "dataset.previous");
            var staging = Path.Combine(jobPath, "staging");
            Directory.CreateDirectory(dataset);
            Directory.CreateDirectory(previous);
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(dataset, "new.bin"), "new");
            File.WriteAllText(Path.Combine(previous, "old.bin"), "old");
            File.WriteAllText(Path.Combine(staging, "in-progress.bin"), "keep");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await AssertDownloadAsyncCatchesAsync<OperationCanceledException>(() =>
                PackDownloader.DownloadAsync(desired, root, new PackLimits(), null, cancellation.Token));

            Assert.That(File.ReadAllText(Path.Combine(dataset, "new.bin")), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(Path.Combine(previous, "old.bin")), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(Path.Combine(staging, "in-progress.bin")), Is.EqualTo("keep"));
        }

        [Test]
        public async Task DownloadAsync_CancelBeforeRecoveryPreservesStagingReferencedByDatasetLink()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
                Assert.Ignore("Symbolic link creation is not available in this test on Windows.");
            var desired = WriteManifest("pack-created", false);
            var jobPath = PackDownloader.JobPath(root, desired);
            var staging = Path.Combine(jobPath, "staging");
            var dataset = Path.Combine(jobPath, "dataset");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(dataset);
            var target = Path.Combine(staging, "referenced.bin");
            var link = Path.Combine(dataset, "referenced.bin");
            File.WriteAllText(target, "keep");
            CreateSymbolicLink(target, link);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Staging was preserved because its safety check failed"));

            await AssertDownloadAsyncCatchesAsync<OperationCanceledException>(() =>
                PackDownloader.DownloadAsync(desired, root, new PackLimits(), null, cancellation.Token));

            Assert.That(File.ReadAllText(target), Is.EqualTo("keep"));
            Assert.That(File.Exists(link), Is.True);
            Assert.That(PackDownloader.LoadManifest(jobPath).key, Is.EqualTo(JobKey));
        }

        [Test]
        public async Task DownloadAsync_RejectsCachedDatasetLinkIntoPreviousWithoutDeletingTarget()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
                Assert.Ignore("Symbolic link creation is not available in this test on Windows.");
            var desired = WriteManifest("complete", true);
            var jobPath = PackDownloader.JobPath(root, desired);
            var datasetFile = Path.Combine(jobPath, "dataset", "sample_city", "udx", "bldg", "sample.gml");
            var previous = Path.Combine(jobPath, "dataset.previous");
            Directory.CreateDirectory(previous);
            var target = Path.Combine(previous, "sample.gml");
            File.Move(datasetFile, target);
            CreateSymbolicLink(target, datasetFile);

            await AssertDownloadAsyncThrowsAsync<IOException>(() => PackDownloader.DownloadAsync(
                desired, root, new PackLimits(), null, CancellationToken.None));

            Assert.That(File.ReadAllText(target), Is.EqualTo("<root />"));
            Assert.That(File.Exists(datasetFile), Is.True);
            var saved = PackDownloader.LoadManifest(jobPath);
            Assert.That(saved != null, Is.True);
            Assert.That(saved.key, Is.EqualTo(JobKey));
        }

        [Test]
        public async Task DownloadAsync_FinallyRemovesTemporaryFilesCreatedAfterPreflight()
        {
            var desired = WriteManifest("complete", true);
            var jobPath = PackDownloader.JobPath(root, desired);
            using var cancellation = new CancellationTokenSource();
            var created = false;

            await AssertDownloadAsyncCatchesAsync<OperationCanceledException>(() =>
                PackDownloader.DownloadAsync(desired, root, new PackLimits(), step =>
                {
                    if (created || step.Stage != "verifying-files") return;
                    created = true;
                    WriteTemporaryFiles(desired);
                    cancellation.Cancel();
                }, cancellation.Token));

            Assert.That(created, Is.True);
            AssertTemporaryFilesMissing(jobPath);
            Assert.That(PackDownloader.LoadManifest(jobPath).status, Is.EqualTo("complete"));
        }

        [Test]
        public async Task DownloadAsync_LockPreventsConcurrentCleanup()
        {
            var desired = WriteManifest("complete", true);
            var jobPath = PackDownloader.JobPath(root, desired);
            File.WriteAllText(Path.Combine(jobPath, "pack.zip"), "keep");
            using var held = new FileStream(Path.Combine(root, ".plateau-area-downloader.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            await AssertDownloadAsyncThrowsAsync<IOException>(() => PackDownloader.DownloadAsync(
                desired, root, new PackLimits(), null, CancellationToken.None));

            Assert.That(File.Exists(Path.Combine(jobPath, "pack.zip")), Is.True);
        }

        [Test]
        public async Task DownloadAsync_RejectsLinkedTemporaryFileWithoutDeletingItsTarget()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
                Assert.Ignore("Symbolic link creation is not available in this test on Windows.");
            var desired = WriteManifest("pack-created", false);
            var jobPath = PackDownloader.JobPath(root, desired);
            var target = Path.Combine(root, "outside.bin");
            var link = Path.Combine(jobPath, "pack.zip.part");
            File.WriteAllText(target, "keep");
            CreateSymbolicLink(target, link);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Temporary file was preserved"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Temporary file was preserved"));

            await AssertDownloadAsyncThrowsAsync<IOException>(() => PackDownloader.DownloadAsync(
                desired, root, new PackLimits(), null, CancellationToken.None));

            Assert.That(File.ReadAllText(target), Is.EqualTo("keep"));
            Assert.That(File.Exists(link), Is.True);
        }

        [Test]
        public async Task DownloadAsync_CleanupWarningDoesNotHideCancellation()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
                Assert.Ignore("Symbolic link creation is not available in this test on Windows.");
            var desired = WriteManifest("pack-created", false);
            var jobPath = PackDownloader.JobPath(root, desired);
            var target = Path.Combine(root, "outside.bin");
            File.WriteAllText(target, "keep");
            CreateSymbolicLink(target, Path.Combine(jobPath, "manifest.json.tmp"));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Could not save download status"));

            await AssertDownloadAsyncCatchesAsync<OperationCanceledException>(() =>
                PackDownloader.DownloadAsync(desired, root, new PackLimits(), null, cancellation.Token));

            Assert.That(File.ReadAllText(target), Is.EqualTo("keep"));
            Assert.That(PackDownloader.LoadManifest(jobPath).status, Is.EqualTo("pack-created"));
        }

        private PackManifest WriteManifest(string status, bool completeDataset)
        {
            var jobPath = Path.Combine(root, JobKey);
            Directory.CreateDirectory(jobPath);
            var relative = "sample_city/udx/bldg/sample.gml";
            var manifest = new PackManifest
            {
                key = JobKey, status = status, packId = "reusable-pack",
                apiBase = "https://example.invalid",
                selectedGmls = new[] { new SelectedGml
                {
                    cityRoot = "sample_city", type = "bldg",
                    url = "https://example.invalid/udx/bldg/sample.gml"
                } },
                metadataUrls = Array.Empty<string>()
            };
            if (completeDataset)
            {
                var path = Path.Combine(jobPath, "dataset", "sample_city", "udx", "bldg", "sample.gml");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "<root />");
                using var sha = SHA256.Create();
                manifest.files = new[] { new SavedFile
                {
                    path = relative, size = new FileInfo(path).Length,
                    sha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)))
                        .Replace("-", "").ToLowerInvariant()
                } };
            }
            File.WriteAllText(Path.Combine(jobPath, "manifest.json"), JsonUtility.ToJson(manifest, true));
            return manifest;
        }

        private void WriteTemporaryFiles(PackManifest manifest)
        {
            var jobPath = PackDownloader.JobPath(root, manifest);
            File.WriteAllText(Path.Combine(jobPath, "pack.zip"), "zip");
            File.WriteAllText(Path.Combine(jobPath, "pack.zip.part"), "partial");
            Directory.CreateDirectory(Path.Combine(jobPath, "staging"));
            File.WriteAllText(Path.Combine(jobPath, "staging", "partial.bin"), "partial");
        }

        private static void AssertTemporaryFilesMissing(string jobPath)
        {
            Assert.That(File.Exists(Path.Combine(jobPath, "pack.zip")), Is.False);
            Assert.That(File.Exists(Path.Combine(jobPath, "pack.zip.part")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(jobPath, "staging")), Is.False);
        }

        private static async Task AssertDownloadAsyncThrowsAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            Exception actual = null;
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                actual = exception;
            }

            Assert.That(actual, Is.TypeOf<TException>());
        }

        private static async Task AssertDownloadAsyncCatchesAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            Exception actual = null;
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                actual = exception;
            }

            Assert.That(actual, Is.InstanceOf<TException>());
        }

        private static async Task AssertDownloadAsyncThrowsAnyAsync(Func<Task> action)
        {
            Exception actual = null;
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                actual = exception;
            }

            Assert.That(actual, Is.InstanceOf<Exception>());
        }

        private static void CreateSymbolicLink(string target, string link)
        {
            var start = new ProcessStartInfo("/bin/ln")
            {
                Arguments = "-s \"" + target.Replace("\"", "\\\"") + "\" \"" + link.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(start);
            process.WaitForExit();
            if (process.ExitCode != 0) Assert.Ignore("Could not create a symbolic link in the test directory.");
        }
    }
}
