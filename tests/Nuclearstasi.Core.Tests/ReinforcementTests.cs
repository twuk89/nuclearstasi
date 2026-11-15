using System.Collections.Generic;
using Nuclearstasi.Core;
using Xunit;

namespace Nuclearstasi.Core.Tests
{
    public class ReinforcementTests
    {
        private static ReinforcementPlanner Planner(Dictionary<string, int> baseline)
        {
            return new ReinforcementPlanner(baseline, new ReinforcementSettings());
        }

        [Theory]
        [InlineData(1.0, 1.0)]
        [InlineData(0.4, 0.064)]
        [InlineData(0.95, 0.857375)]
        [InlineData(0.0, 0.0)]
        public void MultiplierIsShareCubed(double share, double expected)
        {
            Assert.Equal(expected, ReinforcementPlanner.Multiplier(share, 3), 6);
        }

        [Fact]
        public void MultiplierClampsShare()
        {
            Assert.Equal(1.0, ReinforcementPlanner.Multiplier(1.5, 3), 6);
            Assert.Equal(0.0, ReinforcementPlanner.Multiplier(-1, 3), 6);
        }

        [Fact]
        public void FullShareSendsQuarterOfBaseline()
        {
            var wave = Planner(new Dictionary<string, int> { ["tank"] = 8 }).NextWave(1.0);
            Assert.Equal(2, wave["tank"]);
        }

        [Fact]
        public void FractionsCarryOverBetweenWaves()
        {
            var planner = Planner(new Dictionary<string, int> { ["tank"] = 2 });
            Assert.False(planner.NextWave(1.0).ContainsKey("tank"));
            Assert.Equal(1, planner.NextWave(1.0)["tank"]);
        }

        [Fact]
        public void NoShareSendsNothing()
        {
            var wave = Planner(new Dictionary<string, int> { ["tank"] = 100 }).NextWave(0);
            Assert.Empty(wave);
        }

        [Fact]
        public void ZeroBaselineEntriesAreIgnored()
        {
            var wave = Planner(new Dictionary<string, int> { ["tank"] = 0, ["jet"] = 4 }).NextWave(1.0);
            Assert.False(wave.ContainsKey("tank"));
            Assert.Equal(1, wave["jet"]);
        }
    }
}
