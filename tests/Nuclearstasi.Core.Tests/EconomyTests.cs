using Nuclearstasi.Core;
using Xunit;

namespace Nuclearstasi.Core.Tests
{
    public class EconomyTests
    {
        private readonly EconomySettings settings = new EconomySettings();

        [Fact]
        public void SingleSmallAirstripEarnsTricklePlusAirstrip()
        {
            var rebel = new SiteCounts { SmallAirbases = 1 };
            Assert.Equal(5f, Economy.RebelIncome(rebel, settings));
        }

        [Fact]
        public void IncomeSumsEverySiteType()
        {
            var rebel = new SiteCounts { LargeAirbases = 1, Factories = 2, Depots = 1 };
            Assert.Equal(2f + 6f + 8f + 2f, Economy.RebelIncome(rebel, settings));
        }

        [Fact]
        public void NoTerritoryStillEarnsTrickle()
        {
            Assert.Equal(2f, Economy.RebelIncome(new SiteCounts(), settings));
        }

        [Theory]
        [InlineData(9f, 3)]
        [InlineData(5f, 1)]
        [InlineData(12f, 4)]
        public void RegularIncomeMakesVanillaPayoutEqualIncome(float income, int players)
        {
            float regular = Economy.RegularIncomeForPayout(income, players);
            // vanilla DistributeFunds pays regularIncome * players * 0.5
            Assert.Equal(income, regular * players * 0.5f, 0.001f);
        }

        [Fact]
        public void RegularIncomeIsZeroWithNoPlayers()
        {
            Assert.Equal(0f, Economy.RegularIncomeForPayout(10f, 0));
        }

        [Fact]
        public void TerritorySharesUseAllSites()
        {
            var t = new Territory
            {
                Rebel = new SiteCounts { SmallAirbases = 1 },
                Occupier = new SiteCounts { LargeAirbases = 2, Factories = 1 },
            };
            Assert.Equal(4, t.TotalSites);
            Assert.Equal(0.25, t.RebelShare, 6);
            Assert.Equal(0.75, t.OccupierShare, 6);
        }

        [Fact]
        public void EmptyTerritoryHasZeroShares()
        {
            var t = new Territory();
            Assert.Equal(0, t.RebelShare);
            Assert.Equal(0, t.OccupierShare);
        }
    }
}
