using Nuclearstasi.Core;
using Xunit;

namespace Nuclearstasi.Core.Tests
{
    public class MessagesTests
    {
        [Theory]
        [InlineData(Tier.Quiet, "QUIET")]
        [InlineData(Tier.Alert, "ALERT")]
        [InlineData(Tier.Hostile, "HOSTILE")]
        [InlineData(Tier.TotalWar, "TOTAL WAR")]
        public void TierNames(Tier tier, string expected)
        {
            Assert.Equal(expected, Messages.TierName(tier));
        }

        [Theory]
        [InlineData(Tier.Quiet, Tier.Alert, "Occupation forces are now on ALERT")]
        [InlineData(Tier.Alert, Tier.Hostile, "Occupation forces are now HOSTILE")]
        [InlineData(Tier.Hostile, Tier.TotalWar, "The occupation has declared TOTAL WAR")]
        [InlineData(Tier.Quiet, Tier.TotalWar, "The occupation has declared TOTAL WAR")]
        [InlineData(Tier.Hostile, Tier.Alert, "Occupation forces have stood down to ALERT")]
        [InlineData(Tier.Alert, Tier.Quiet, "Occupation forces have stood down to QUIET")]
        public void TierChangeAnnouncements(Tier from, Tier to, string expected)
        {
            Assert.Equal(expected, Messages.TierChanged(new TierChange { From = from, To = to }));
        }

        [Fact]
        public void UnchangedTierHasNoAnnouncement()
        {
            Assert.Null(Messages.TierChanged(new TierChange { From = Tier.Alert, To = Tier.Alert }));
        }

        [Fact]
        public void StatusLine()
        {
            var t = new Territory
            {
                Rebel = new SiteCounts { SmallAirbases = 1, Factories = 2 },
                Occupier = new SiteCounts { LargeAirbases = 5, Depots = 12 },
            };
            string text = Messages.Status(42.4, Tier.Alert, t, 123.6f, 3 * 3600 + 7 * 60 + 5);
            Assert.Equal("Aggression 42 (ALERT) | Territory 3/20 sites (15%) | Rebel funds 124 | Campaign time 3h 07m", text);
        }

        [Fact]
        public void AirbaseCapturedNamesBase()
        {
            Assert.Equal("Rebels have captured Red Hill", Messages.AirbaseCaptured("Red Hill"));
        }
    }
}
