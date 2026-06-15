using Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Client
{
    public static class CsvDataGenerator
    {
        private const int MaxRows = 106;

        public static List<SmartGridSample> ReadFromCsv(string filePath, out int skippedCount)
        {
            skippedCount = 0;
            var result = new List<SmartGridSample>();

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[GREŠKA] Fajl ne postoji: {filePath}");
                return result;
            }

            string logPath = Path.Combine(
                Path.GetDirectoryName(filePath) ?? ".", "parse_errors.log");

            using (var logWriter = new StreamWriter(logPath, append: false, Encoding.UTF8))
            using (var reader = new StreamReader(filePath, Encoding.UTF8))
            {
                logWriter.WriteLine($"Parse errors log – {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                logWriter.WriteLine($"Source: {filePath}");
                logWriter.WriteLine($"Max rows: {MaxRows}");
                logWriter.WriteLine(new string('-', 60));

                string line;
                int lineNum = 0;
                int loaded = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    lineNum++;

                    if (lineNum == 1) continue;

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (loaded >= MaxRows)
                    {
                        skippedCount++;
                        logWriter.WriteLine($"Line {lineNum}: [VIŠAK] Red preskočen – dostignuto je {MaxRows} validnih redova | raw='{line}'");
                        continue;
                    }

                    try
                    {
                        var sample = ParseLine(line, lineNum);
                        result.Add(sample);
                        loaded++;
                    }
                    catch (Exception ex)
                    {
                        skippedCount++;
                        logWriter.WriteLine($"Line {lineNum}: [NEVALIDAN] {ex.Message} | raw='{line}'");
                    }
                }

                logWriter.WriteLine(new string('-', 60));
                logWriter.WriteLine($"Učitano validnih: {loaded} | Preskočeno: {skippedCount}");
            }
            return result;
        }

        private static SmartGridSample ParseLine(string line, int lineNum)
        {
            var p = line.Split(',');

            if (p.Length >= 6)
            {
                return new SmartGridSample(
                    ParseTimestamp(p[0], lineNum),
                    ParseDouble(p[1], "Voltage", lineNum),
                    ParseDouble(p[2], "Current", lineNum),
                    ParseInt(p[5], "FaultIndicator", lineNum),
                    ParseDouble(p[3], "PowerUsage", lineNum),
                    ParseDouble(p[4], "Frequency", lineNum)
                );
            }

            throw new FormatException($"Expected ≥6 columns, got {p.Length}");
        }

        private static DateTime ParseTimestamp(string s, int line)
        {
            s = s.Trim().Trim('"');
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt)) return dt;
            throw new FormatException($"Timestamp neispravan: '{s}' (line {line})");
        }

        private static double ParseDouble(string s, string field, int line)
        {
            s = s.Trim();
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d;
            throw new FormatException($"Polje '{field}' nije broj: '{s}' (line {line})");
        }

        private static int ParseInt(string s, string field, int line)
        {
            s = s.Trim();
            if (int.TryParse(s, out var i)) return i;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return (int)d;
            throw new FormatException($"Polje '{field}' nije ceo broj: '{s}' (line {line})");
        }

        public static List<SmartGridSample> GenerateSampleRecords(int count = 100)
        {
            var rng = new Random();
            var result = new List<SmartGridSample>(count);
            var start = DateTime.Now.AddSeconds(-count);

            double prevFreq = 50.0;

            for (int i = 0; i < count; i++)
            {
                double freqDelta = (rng.NextDouble() - 0.5) * 1.2;
                double freq = Math.Max(45, prevFreq + freqDelta);
                double volt = 220 + (rng.NextDouble() - 0.5) * 40;
                double curr = 20 + (rng.NextDouble() - 0.5) * 30;
                double pow = Math.Abs(volt * curr * (0.8 + rng.NextDouble() * 0.4));
                int fault = rng.Next(100) < 5 ? rng.Next(1, 4) : 0;

                result.Add(new SmartGridSample(
                    start.AddSeconds(i),
                    Math.Round(volt, 3),
                    Math.Round(curr, 3),
                    fault,
                    Math.Round(pow, 3),
                    Math.Round(freq, 4)
                ));
                prevFreq = freq;
            }
            return result;
        }

        public static void SaveMemoryStreamToFile(string directory, string fileName, MemoryStream ms)
        {
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, fileName);
            ms.Position = 0;
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                ms.CopyTo(fs);
        }
    }
}
