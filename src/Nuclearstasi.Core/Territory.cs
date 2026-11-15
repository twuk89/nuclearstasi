namespace Nuclearstasi.Core
{
    public struct SiteCounts
    {
        public int SmallAirbases;
        public int LargeAirbases;
        public int Factories;
        public int Depots;

        public int Total => SmallAirbases + LargeAirbases + Factories + Depots;

        public SiteCounts Plus(SiteCounts other)
        {
            return new SiteCounts
            {
                SmallAirbases = SmallAirbases + other.SmallAirbases,
                LargeAirbases = LargeAirbases + other.LargeAirbases,
                Factories = Factories + other.Factories,
                Depots = Depots + other.Depots,
            };
        }
    }

    public sealed class Territory
    {
        public SiteCounts Rebel;
        public SiteCounts Occupier;
        public SiteCounts Neutral;

        public int TotalSites => Rebel.Total + Occupier.Total + Neutral.Total;

        public double RebelShare => TotalSites == 0 ? 0 : (double)Rebel.Total / TotalSites;

        public double OccupierShare => TotalSites == 0 ? 0 : (double)Occupier.Total / TotalSites;
    }
}
