namespace Nuclearstasi.Core
{
    public static class Economy
    {
        public static float RebelIncome(SiteCounts rebel, EconomySettings s)
        {
            return s.BaseTrickle
                + rebel.SmallAirbases * s.SmallAirbase
                + rebel.LargeAirbases * s.LargeAirbase
                + rebel.Factories * s.Factory
                + rebel.Depots * s.Depot;
        }

        /// <summary>
        /// Vanilla FactionHQ.DistributeFunds pays (regularIncome + excess) * players * 0.5.
        /// Returns the regularIncome that makes the regular part equal <paramref name="income"/>.
        /// </summary>
        public static float RegularIncomeForPayout(float income, int playerCount)
        {
            if (playerCount <= 0)
            {
                return 0f;
            }
            return income * 2f / playerCount;
        }
    }
}
