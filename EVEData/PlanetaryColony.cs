using System;
using System.Collections.Generic;
using System.Linq;

namespace SMT.EVEData
{
    public enum PinType { CommandCenter, Extractor, Factory, Storage, Launchpad, Unknown }

    public class PinContent
    {
        public long TypeId { get; set; }
        public long Amount { get; set; }
    }

    public class ColonyPin
    {
        public long PinId { get; set; }
        public long TypeId { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public PinType PinType { get; set; }
        public DateTime? ExpiryTime { get; set; }          // extractor expiry
        public string ProductTypeName { get; set; } = string.Empty;  // what it extracts/produces

        // Contents: what's stored in this pin right now
        public List<PinContent> Contents { get; set; } = new List<PinContent>();

        // Capacity in m³ (0 = no storage)
        public double CapacityM3 { get; set; }

        // Current volume used (sum of contents * item volume)
        public double UsedM3 { get; set; }

        // Fill ratio 0.0 - 1.0
        public double FillRatio => CapacityM3 > 0 ? Math.Min(UsedM3 / CapacityM3, 1.0) : 0.0;

        // Is full or nearly full (>= 90%)
        public bool IsOverflowing => CapacityM3 > 0 && FillRatio >= 0.90;
    }

    public class PlanetaryColony
    {
        public long PlanetId { get; set; }
        public long SolarSystemId { get; set; }
        public string SolarSystemName { get; set; } = string.Empty;
        public string PlanetType { get; set; } = string.Empty;  // temperate/barren/gas/ice/lava/oceanic/plasma/storm
        public int NumPins { get; set; }
        public int UpgradeLevel { get; set; }
        public DateTime? LastUpdate { get; set; }
        public string CharacterName { get; set; } = string.Empty;
        public List<ColonyPin> Pins { get; set; } = new List<ColonyPin>();

        // Computed: earliest extractor expiry among all pins
        public DateTime? EarliestExpiry => Pins
            .Where(p => p.ExpiryTime.HasValue)
            .Select(p => p.ExpiryTime!.Value)
            .DefaultIfEmpty(DateTime.MinValue)
            .Min() == DateTime.MinValue ? null :
            Pins.Where(p => p.ExpiryTime.HasValue).Min(p => p.ExpiryTime!.Value);

        // Expiry status: "expired" / "warning" (<4h) / "ok" / "none" (no extractor)
        public string ExpiryStatus
        {
            get
            {
                if (!EarliestExpiry.HasValue) return "none";
                var diff = EarliestExpiry.Value - DateTime.UtcNow;
                if (diff.TotalSeconds <= 0) return "expired";
                if (diff.TotalHours < 4) return "warning";
                return "ok";
            }
        }

        // Human readable expiry string
        public string ExpiryText
        {
            get
            {
                if (!EarliestExpiry.HasValue) return "No extractors";
                var diff = EarliestExpiry.Value - DateTime.UtcNow;
                if (diff.TotalSeconds <= 0) return $"Expired {Math.Abs((int)diff.TotalHours)}h ago";
                if (diff.TotalDays >= 1) return $"Expires in {(int)diff.TotalDays}d {diff.Hours}h";
                if (diff.TotalHours >= 1) return $"Expires in {(int)diff.TotalHours}h {diff.Minutes}m";
                return $"Expires in {diff.Minutes}m";
            }
        }

        public string ExpiryColor
        {
            get
            {
                return ExpiryStatus switch
                {
                    "expired" => "#FF4444",
                    "warning" => "#FFAA00",
                    "ok" => "#44CC44",
                    _ => "#888888"
                };
            }
        }

        // Planet type emoji/icon prefix
        public string PlanetIcon => PlanetType?.ToLower() switch
        {
            "temperate" => "🌍",
            "barren" => "🪨",
            "gas" => "🌀",
            "ice" => "❄️",
            "lava" => "🌋",
            "oceanic" => "🌊",
            "plasma" => "⚡",
            "storm" => "🌪️",
            _ => "🪐"
        };

        // True if ANY storage pin (Storage/Launchpad/CommandCenter) is >= 90% full
        public bool HasStorageAlert => Pins.Any(p => p.IsOverflowing);

        // Summary text for storage status
        public string StorageText
        {
            get
            {
                var storagePins = Pins.Where(p => p.CapacityM3 > 0).ToList();
                if (!storagePins.Any()) return string.Empty;

                var overflowing = storagePins.Where(p => p.IsOverflowing).ToList();
                if (overflowing.Any())
                    return $"⚠ Storage {overflowing.Count}/{storagePins.Count} full";

                // Show highest fill ratio
                var maxFill = storagePins.Max(p => p.FillRatio);
                if (maxFill > 0)
                    return $"Storage {(maxFill * 100):F0}% max";
                return string.Empty;
            }
        }

        public string StorageColor => HasStorageAlert ? "#FF4444" : "#888888";

        // What the extractors are harvesting (distinct product names)
        public string ExtractedResources
        {
            get
            {
                var products = Pins
                    .Where(p => p.PinType == PinType.Extractor && !string.IsNullOrEmpty(p.ProductTypeName))
                    .Select(p => p.ProductTypeName)
                    .Distinct()
                    .ToList();
                return products.Any() ? string.Join(", ", products) : string.Empty;
            }
        }

        public string StorageFillText
        {
            get
            {
                var storagePins = Pins.Where(p => p.CapacityM3 > 0).ToList();
                if (!storagePins.Any()) return string.Empty;
                var maxFill = storagePins.Max(p => p.FillRatio);
                var warning = storagePins.Any(p => p.IsOverflowing) ? "⚠ " : "";
                return $"{warning}Storage: {(maxFill * 100):F0}%";
            }
        }

        public string StorageFillColor
        {
            get
            {
                var storagePins = Pins.Where(p => p.CapacityM3 > 0).ToList();
                if (!storagePins.Any()) return "#555555";
                var maxFill = storagePins.Max(p => p.FillRatio);
                if (maxFill >= 0.90) return "#FF4444";
                if (maxFill >= 0.70) return "#FFAA00";
                return "#55AA55";
            }
        }
    }
}
