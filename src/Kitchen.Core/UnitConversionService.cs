using System.Globalization;

namespace Kitchen.Core;

public sealed class UnitConversionService : IUnitConversionService
{
    public bool TryConvert(decimal quantity, string fromUnit, string toUnit, out decimal converted)
    {
        var from = Units.Canonical(fromUnit); var to = Units.Canonical(toUnit);
        if (from == to) { converted = quantity; return true; }
        converted = (from, to) switch
        {
            (Units.Kilogram, Units.Gram) => quantity * 1000m,
            (Units.Gram, Units.Kilogram) => quantity / 1000m,
            (Units.Litre, Units.Millilitre) => quantity * 1000m,
            (Units.Millilitre, Units.Litre) => quantity / 1000m,
            _ => 0m
        };
        return converted != 0m;
    }
    public string Format(decimal quantity, string unit)
    {
        var canonical = Units.Canonical(unit);
        if (canonical == Units.Gram && quantity >= 1000m) return $"{(quantity / 1000m).ToString("0.##", CultureInfo.GetCultureInfo("pl-PL"))} kg";
        if (canonical == Units.Millilitre && quantity >= 1000m) return $"{(quantity / 1000m).ToString("0.##", CultureInfo.GetCultureInfo("pl-PL"))} l";
        return $"{quantity.ToString("0.##", CultureInfo.GetCultureInfo("pl-PL"))} {canonical}";
    }
}
