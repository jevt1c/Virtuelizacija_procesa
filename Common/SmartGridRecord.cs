using System;
using System.Globalization;
using System.Runtime.Serialization;

namespace Common
{
    [DataContract]
    public class SmartGridSample
    {
        private static int _counter = 0;

        private int    _id;
        private DateTime _timestamp;
        private double _voltage;
        private double _current;
        private int    _faultIndicator;
        private double _powerUsage;
        private double _frequency;

        public SmartGridSample(DateTime timestamp, double voltage, double current,
            int faultIndicator, double powerUsage, double frequency)
        {
            _id             = ++_counter;
            _timestamp      = timestamp;
            _voltage        = voltage;
            _current        = current;
            _faultIndicator = faultIndicator;
            _powerUsage     = powerUsage;
            _frequency      = frequency;
        }

        [DataMember] public int      Id             { get => _id;             set => _id = value; }
        [DataMember] public DateTime Timestamp      { get => _timestamp;      set => _timestamp = value; }
        [DataMember] public double   Voltage        { get => _voltage;        set => _voltage = value; }
        [DataMember] public double   Current        { get => _current;        set => _current = value; }
        [DataMember] public int      FaultIndicator { get => _faultIndicator; set => _faultIndicator = value; }
        [DataMember] public double   PowerUsage     { get => _powerUsage;     set => _powerUsage = value; }
        [DataMember] public double   Frequency      { get => _frequency;      set => _frequency = value; }

        // P(t) = V(t) * I(t)
        public double ComputedPower => _voltage * _current;

        public string ToCsvLine()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5}",
                _timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                _voltage, _current, _faultIndicator, _powerUsage, _frequency);
        }

        public static SmartGridSample FromCsvLine(string line)
        {
            var p = line.Split(',');
            if (p.Length < 6)
                throw new FormatException($"Expected 6 columns, got {p.Length}: '{line}'");

            return new SmartGridSample(
                DateTime.Parse(p[0], CultureInfo.InvariantCulture),
                double.Parse(p[1], CultureInfo.InvariantCulture),
                double.Parse(p[2], CultureInfo.InvariantCulture),
                int.Parse(p[3], CultureInfo.InvariantCulture),
                double.Parse(p[4], CultureInfo.InvariantCulture),
                double.Parse(p[5], CultureInfo.InvariantCulture)
            );
        }

        public static string CsvHeader() =>
            "Timestamp,Voltage,Current,FaultIndicator,PowerUsage,Frequency";

        public override string ToString() =>
            $"[{_id}] {_timestamp:yyyy-MM-dd HH:mm:ss} | " +
            $"V:{_voltage:F2}V I:{_current:F2}A f:{_frequency:F3}Hz " +
            $"P_calc:{ComputedPower:F1}W PU:{_powerUsage:F2} Fault:{_faultIndicator}";
    }
}
