using System;

namespace Nuclearstasi.Core
{
    public enum AggressionEvent
    {
        AircraftKilled,
        GroundUnitKilled,
        BuildingDestroyed,
        FactoryOrDepotCaptured,
        AirbaseCaptured,
        ConvoyDestroyed,
    }

    public enum Tier
    {
        Quiet = 0,
        Alert = 1,
        Hostile = 2,
        TotalWar = 3,
    }

    public struct TierChange
    {
        public Tier From;
        public Tier To;

        public bool Changed => From != To;

        public bool Rose => To > From;
    }

    public sealed class AggressionMeter
    {
        private const double Min = 0;
        private const double Max = 100;

        private readonly AggressionSettings settings;
        private double decayTimer;

        public AggressionMeter(AggressionSettings settings, double initial)
        {
            this.settings = settings;
            Value = Clamp(initial);
        }

        public double Value { get; private set; }

        public Tier Tier => TierFor(Value);

        public Tier TierFor(double value)
        {
            if (value >= settings.TotalWarAt) return Tier.TotalWar;
            if (value >= settings.HostileAt) return Tier.Hostile;
            if (value >= settings.AlertAt) return Tier.Alert;
            return Tier.Quiet;
        }

        public double Floor(double rebelShare)
        {
            return Clamp(settings.FloorPerRebelShare * rebelShare);
        }

        public TierChange Add(AggressionEvent e, bool nearCapital)
        {
            double amount = AmountFor(e);
            if (nearCapital)
            {
                amount *= settings.CapitalMultiplier;
            }
            return Set(Value + amount);
        }

        public TierChange Tick(double seconds, double rebelShare)
        {
            double next = Value;
            decayTimer += seconds;
            while (decayTimer >= settings.DecayIntervalSeconds)
            {
                decayTimer -= settings.DecayIntervalSeconds;
                next -= settings.DecayAmount;
            }
            next = Math.Max(next, Floor(rebelShare));
            return Set(next);
        }

        public TierChange Set(double value)
        {
            Tier from = Tier;
            Value = Clamp(value);
            return new TierChange { From = from, To = Tier };
        }

        private double AmountFor(AggressionEvent e)
        {
            switch (e)
            {
                case AggressionEvent.AircraftKilled: return settings.AircraftKilled;
                case AggressionEvent.GroundUnitKilled: return settings.GroundUnitKilled;
                case AggressionEvent.BuildingDestroyed: return settings.BuildingDestroyed;
                case AggressionEvent.FactoryOrDepotCaptured: return settings.FactoryOrDepotCaptured;
                case AggressionEvent.AirbaseCaptured: return settings.AirbaseCaptured;
                case AggressionEvent.ConvoyDestroyed: return settings.ConvoyDestroyed;
                default: return 0;
            }
        }

        private static double Clamp(double v)
        {
            return Math.Max(Min, Math.Min(Max, v));
        }
    }
}
