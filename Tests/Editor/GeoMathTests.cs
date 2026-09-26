using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Zabaglione.PlateauAreaDownloader.Editor.Tests
{
    public class GeoMathTests
    {
        [Test]
        public void FromCoordinate_ReturnsExpectedJapaneseThirdLevelMeshCodes()
        {
            Assert.That(JapanMeshCode.FromCoordinate(35.6586, 139.7454), Is.EqualTo("53393599"));
            Assert.That(JapanMeshCode.FromCoordinate(34.9949, 135.7850), Is.EqualTo("52353692"));
            Assert.That(JapanMeshCode.FromCoordinate(43.0687, 141.3508), Is.EqualTo("64414288"));
        }

        [Test]
        public void GetBounds_ContainsCoordinateAndHasThirdLevelDimensions()
        {
            GeoBounds bounds = JapanMeshCode.GetBounds("53393599");

            Assert.That(bounds.Contains(35.6586, 139.7454), Is.True);
            Assert.That(bounds.North - bounds.South, Is.EqualTo(1.0 / 120.0).Within(1e-12));
            Assert.That(bounds.East - bounds.West, Is.EqualTo(1.0 / 80.0).Within(1e-12));
        }

        [Test]
        public void CatalogBounds_DistinguishesSecondLevelDemQuarters()
        {
            var southwest = JapanMeshCode.GetCatalogBounds("644142",
                "https://example.test/udx/dem/644142_dem_6697_00_op.gml");
            var northeast = JapanMeshCode.GetCatalogBounds("644142",
                "https://example.test/udx/dem/644142_dem_6697_55_op.gml");
            Assert.That(southwest.East, Is.EqualTo(northeast.West).Within(1e-12));
            Assert.That(southwest.North, Is.EqualTo(northeast.South).Within(1e-12));
            Assert.That(northeast.Contains(43.0687, 141.3508), Is.True);
        }

        [Test]
        public void EnumerateIntersecting_ExcludesAdjacentCellAtDecimalBoundary()
        {
            var bounds = new GeoBounds(141.35, 43.068, 141.351, 43.069);
            CollectionAssert.AreEqual(new[] { "64414288" },
                JapanMeshCode.EnumerateIntersecting(bounds, includeBoundary: false));
        }

        [Test]
        public void EnumerateIntersecting_IncludesCellsTouchingAnExactMeshBoundary()
        {
            GeoBounds cell = JapanMeshCode.GetBounds("53393599");
            GeoBounds sharedEdge = new GeoBounds(
                cell.East,
                cell.South + 0.001,
                cell.East,
                cell.North - 0.001);
            List<string> codes = new List<string>(JapanMeshCode.EnumerateIntersecting(sharedEdge));

            CollectionAssert.AreEquivalent(new[] { "53393599", "53393690" }, codes);
            Assert.That(JapanMeshCode.EnumerateIntersecting(sharedEdge, includeBoundary: false), Is.Empty);
        }

        [Test]
        public void FromCenter_CreatesAOneKilometerSquareAtCenterLatitude()
        {
            const double latitude = 35.6586;
            const double longitude = 139.7454;
            GeoBounds bounds = GeoBounds.FromCenter(latitude, longitude);

            Assert.That((bounds.West + bounds.East) * 0.5, Is.EqualTo(longitude).Within(1e-12));
            Assert.That((bounds.South + bounds.North) * 0.5, Is.EqualTo(latitude).Within(1e-12));

            double radians = latitude * (Math.PI / 180.0);
            const double semiMajorAxisMeters = 6378137.0;
            const double flattening = 1.0 / 298.257223563;
            double eccentricitySquared = flattening * (2.0 - flattening);
            double denominator = Math.Sqrt(1.0 - eccentricitySquared * Math.Sin(radians) * Math.Sin(radians));
            double primeVerticalRadius = semiMajorAxisMeters / denominator;
            double meridionalRadius = semiMajorAxisMeters * (1.0 - eccentricitySquared)
                / (denominator * denominator * denominator);
            double widthMeters = (bounds.East - bounds.West) * (Math.PI / 180.0)
                * primeVerticalRadius * Math.Cos(radians);
            double heightMeters = (bounds.North - bounds.South) * (Math.PI / 180.0) * meridionalRadius;

            Assert.That(widthMeters, Is.EqualTo(1000.0).Within(1e-8));
            Assert.That(heightMeters, Is.EqualTo(1000.0).Within(1e-8));
        }

        [Test]
        public void GeoBounds_RejectsInvalidRanges()
        {
            Assert.Throws<ArgumentException>(() => new GeoBounds(140.0, 36.0, 139.0, 35.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoBounds(-181.0, 35.0, 139.0, 36.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => GeoBounds.FromCenter(35.0, 139.0, 0.0));
        }

        [Test]
        public void WheelZoom_AccumulatesSmallEventsWithoutZoomingForEveryEvent()
        {
            var gate = new WheelZoomGate();
            for (var i = 0; i < 5; i++)
                Assert.That(gate.Consume(-0.5f, i * 0.01, 15, 5, 18), Is.Zero);
            Assert.That(gate.Consume(-0.5f, 0.05, 15, 5, 18), Is.EqualTo(1));
            for (var i = 0; i < 16; i++)
                Assert.That(gate.Consume(-0.5f, 0.06 + i * 0.01, 16, 5, 18), Is.Zero);
        }

        [Test]
        public void WheelZoom_LimitsOneLargeEventAndDiscardsRemainder()
        {
            var gate = new WheelZoomGate();
            Assert.That(gate.Consume(100, 0, 15, 5, 18), Is.EqualTo(-1));
            Assert.That(gate.Consume(0, 0.5, 14, 5, 18), Is.Zero);
            Assert.That(gate.Consume(0.5f, 0.51, 14, 5, 18), Is.Zero);
        }

        [Test]
        public void WheelZoom_RespondsToIndividualWheelTicksAfterIdle()
        {
            var gate = new WheelZoomGate();
            Assert.That(gate.Consume(-1, 0, 15, 5, 18), Is.EqualTo(1));
            Assert.That(gate.Consume(-1, 0.4, 16, 5, 18), Is.EqualTo(1));
            Assert.That(gate.Consume(-1, 0.8, 17, 5, 18), Is.EqualTo(1));
        }

        [Test]
        public void WheelZoom_ResetsOnDirectionChangeAndIdle()
        {
            var gate = new WheelZoomGate();
            Assert.That(gate.Consume(-0.5f, 0, 15, 5, 18), Is.Zero);
            Assert.That(gate.Consume(0.5f, 0.05, 15, 5, 18), Is.Zero);
            Assert.That(gate.Consume(2.5f, 0.10, 15, 5, 18), Is.EqualTo(-1));
            Assert.That(gate.Consume(-0.5f, 0.5, 14, 5, 18), Is.Zero);
            Assert.That(gate.Consume(-0.5f, 0.9, 14, 5, 18), Is.Zero);
        }

        [Test]
        public void WheelZoom_DiscardsInputAtZoomLimits()
        {
            var gate = new WheelZoomGate();
            Assert.That(gate.Consume(-100, 0, 18, 5, 18), Is.Zero);
            Assert.That(gate.Consume(-0.5f, 0.1, 17, 5, 18), Is.Zero);
            Assert.That(gate.Consume(100, 0.5, 5, 5, 18), Is.Zero);
        }
    }
}
