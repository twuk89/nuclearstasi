using Nuclearstasi.Core;
using Xunit;

namespace Nuclearstasi.Core.Tests
{
    public class AggressionTests
    {
        private static AggressionMeter Meter(double initial = 0)
        {
            return new AggressionMeter(new AggressionSettings(), initial);
        }

        [Fact]
        public void StartsQuietAtInitialValue()
        {
            var m = Meter(10);
            Assert.Equal(10, m.Value);
            Assert.Equal(Tier.Quiet, m.Tier);
        }

        [Fact]
        public void CapturingAnAirbaseAddsFifteen()
        {
            var m = Meter();
            m.Add(AggressionEvent.AirbaseCaptured, nearCapital: false);
            Assert.Equal(15, m.Value);
        }

        [Fact]
        public void ActionsNearCapitalAreDoubled()
        {
            var m = Meter();
            m.Add(AggressionEvent.AircraftKilled, nearCapital: true);
            Assert.Equal(4, m.Value);
        }

        [Fact]
        public void ValueIsClampedTo100()
        {
            var m = Meter(95);
            m.Add(AggressionEvent.AirbaseCaptured, nearCapital: false);
            Assert.Equal(100, m.Value);
        }

        [Theory]
        [InlineData(0, Tier.Quiet)]
        [InlineData(24.9, Tier.Quiet)]
        [InlineData(25, Tier.Alert)]
        [InlineData(49.9, Tier.Alert)]
        [InlineData(50, Tier.Hostile)]
        [InlineData(75, Tier.TotalWar)]
        [InlineData(100, Tier.TotalWar)]
        public void TiersFollowThresholds(double value, Tier expected)
        {
            Assert.Equal(expected, Meter().TierFor(value));
        }

        [Fact]
        public void AddReportsTierRise()
        {
            var m = Meter(20);
            TierChange change = m.Add(AggressionEvent.AirbaseCaptured, nearCapital: false);
            Assert.True(change.Changed);
            Assert.True(change.Rose);
            Assert.Equal(Tier.Quiet, change.From);
            Assert.Equal(Tier.Alert, change.To);
        }

        [Fact]
        public void AddWithinTierReportsNoChange()
        {
            var m = Meter(0);
            TierChange change = m.Add(AggressionEvent.GroundUnitKilled, nearCapital: false);
            Assert.False(change.Changed);
        }

        [Fact]
        public void DecaysOnePointPerTwoMinutes()
        {
            var m = Meter(10);
            m.Tick(240, rebelShare: 0);
            Assert.Equal(8, m.Value);
        }

        [Fact]
        public void PartialTicksAccumulate()
        {
            var m = Meter(10);
            m.Tick(100, rebelShare: 0);
            Assert.Equal(10, m.Value);
            m.Tick(30, rebelShare: 0);
            Assert.Equal(9, m.Value);
        }

        [Fact]
        public void FloorRaisesValueAndReportsTierChange()
        {
            var m = Meter(0);
            TierChange change = m.Tick(1, rebelShare: 0.5);
            Assert.Equal(25, m.Value);
            Assert.Equal(Tier.Alert, change.To);
            Assert.True(change.Rose);
        }

        [Fact]
        public void DecayStopsAtFloor()
        {
            var m = Meter(30);
            m.Tick(1200, rebelShare: 0.5);
            Assert.Equal(25, m.Value);
        }

        [Fact]
        public void DecayNeverGoesBelowZero()
        {
            var m = Meter(1);
            m.Tick(1200, rebelShare: 0);
            Assert.Equal(0, m.Value);
        }

        [Fact]
        public void SetClampsAndReportsChange()
        {
            var m = Meter(0);
            TierChange change = m.Set(150);
            Assert.Equal(100, m.Value);
            Assert.Equal(Tier.TotalWar, change.To);
            m.Set(-5);
            Assert.Equal(0, m.Value);
        }

        [Fact]
        public void FloorIsFiftyTimesShare()
        {
            Assert.Equal(10, Meter().Floor(0.2), 6);
        }
    }
}
