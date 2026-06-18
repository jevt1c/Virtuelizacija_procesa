using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using Common;

namespace Client
{
    internal class Program
    {
        private static ISmartGridService _proxy;
        private static ChannelFactory<ISmartGridService> _factory;

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            PrintBanner();

            try
            {
                _factory = new ChannelFactory<ISmartGridService>("SmartGridEndpoint");
                _proxy = _factory.CreateChannel();

                bool running = true;
                while (running)
                {
                    PrintMenu();
                    string choice = Console.ReadLine()?.Trim();
                    Console.WriteLine();

                    switch (choice)
                    {
                        case "1": SendFromCsvFile(); break;
                        case "2": SendGeneratedData(); break;
                        case "3": ShowAllRecords(); break;
                        case "4": ShowSummary(); break;
                        case "5": DownloadFiles(); break;
                        case "0": running = false; break;
                        default:
                            PrintColor(ConsoleColor.Red, "Nepoznata opcija.");
                            break;
                    }

                    if (running)
                    {
                        Console.WriteLine("\nPritisnite Enter za nastavak...");
                        Console.ReadLine();
                    }
                }
            }
            catch (EndpointNotFoundException)
            {
                PrintColor(ConsoleColor.Red,
                    "[GREŠKA] Servis nije pronađen. Proverite da li je Service.exe pokrenut.");
            }
            catch (Exception ex)
            {
                PrintColor(ConsoleColor.Red, $"[GREŠKA] {ex.Message}");
            }
            finally
            {
                try { _factory?.Close(); }
                catch { _factory?.Abort(); }
                Console.WriteLine("\nKlijent zatvoren. Pritisnite Enter za izlaz...");
                Console.ReadLine();
            }
        }

        static void SendFromCsvFile()
        {
            Console.Write("Putanja do CSV fajla: ");
            string path = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                PrintColor(ConsoleColor.Red, "Fajl nije pronađen.");
                return;
            }

            Console.WriteLine($"\nUčitavam dataset: {path}");
            int skipped;
            var records = CsvDataGenerator.ReadFromCsv(path, out skipped);

            if (records.Count == 0)
            {
                PrintColor(ConsoleColor.Yellow, "Nema validnih zapisa u fajlu.");
                return;
            }

            Console.WriteLine($"  Učitano: {records.Count} | Preskočeno (nevalidno): {skipped}");
            if (skipped > 0)
                Console.WriteLine("  Nevalidni redovi su zapisani u parse_errors.log pored fajla.");

            SendSessionSequential(records, Path.GetFileName(path));
        }


        static void SendGeneratedData()
        {
            Console.Write("Broj uzoraka za generisanje (npr. 200): ");
            if (!int.TryParse(Console.ReadLine(), out int count) || count <= 0)
                count = 200;

            Console.WriteLine($"\nGenerišem {count} sintetičkih uzoraka...");
            var records = CsvDataGenerator.GenerateSampleRecords(count);
            SendSessionSequential(records, "synthetic_data");
        }


        static void SendSessionSequential(List<SmartGridSample> records, string sourceFile)
        {
            string sessionId = $"SES-{DateTime.Now:HHmmss}";

            try
            {

                Console.WriteLine($"\nPokretanje sesije [{sessionId}]...");
                var meta = new SessionMeta
                {
                    SessionId = sessionId,
                    SourceFile = sourceFile,
                    ExpectedRows = records.Count
                };
                var startResp = _proxy.StartSession(meta);
                PrintResponse(startResp);

                if (startResp.Ack == AckStatus.NACK)
                {
                    PrintColor(ConsoleColor.Red, "Servis odbio sesiju. Proverite da li je prethodna sesija završena.");
                    return;
                }

                Console.WriteLine($"\nSlanje {records.Count} uzoraka sekvencijalno...");
                int sent = 0;
                int rejected = 0;
                //sekvencijalno slanje
                foreach (var sample in records)
                {
                    try
                    {
                        var pushResp = _proxy.PushSample(sample);

                        if (pushResp.Ack == AckStatus.ACK)
                        {
                            sent++;
                            if (sent % 50 == 0 || sent == records.Count)
                                Console.Write($"\r  prenos u toku... {sent}/{records.Count}    ");
                        }
                        else
                        {
                            rejected++;
                            PrintColor(ConsoleColor.Yellow, $"\n  [NACK] {pushResp.Message}");
                        }
                    }
                    catch (FaultException<ValidationFault> fe)
                    {
                        rejected++;
                        Console.Write($"\r  [ODBIJEN] {fe.Detail.Message}                 ");
                    }
                    catch (FaultException<DataFormatFault> fe)
                    {
                        rejected++;
                        Console.Write($"\r  [FORMAT ERR] {fe.Detail.Message}              ");
                    }
                }
                Console.WriteLine();

                Console.WriteLine("\nZavršavanje sesije...");
                var endResp = _proxy.EndSession();
                PrintResponse(endResp);

                PrintColor(ConsoleColor.Green,
                    $"\nRezultat: Poslato={sent} | Odbijeno={rejected} | Ukupno={records.Count}");
            }
            catch (FaultException<ValidationFault> fe)
            {
                PrintColor(ConsoleColor.Red, $"[VALIDATION FAULT] {fe.Detail.Message}");
                TryEndSession();
            }
            catch (FaultException<DataFormatFault> fe)
            {
                PrintColor(ConsoleColor.Red, $"[DATA FORMAT FAULT] {fe.Detail.Message}");
                TryEndSession();
            }
            catch (Exception ex)
            {
                PrintColor(ConsoleColor.Red, $"[GREŠKA] {ex.Message}");
                TryEndSession();
            }
        }

        static void ShowAllRecords()
        {
            try
            {
                var records = _proxy.GetAllRecords();

                if (records == null || records.Count == 0)
                {
                    Console.WriteLine("Nema zapisa na servisu.");
                    return;
                }

                Console.WriteLine($"\n{'─',70}");
                Console.WriteLine($"  Ukupno zapisa: {records.Count}");
                Console.WriteLine($"{'─',70}");

                int start = Math.Max(0, records.Count - 20);
                if (start > 0)
                    Console.WriteLine($"  (prikazujem poslednjih 20 od {records.Count})");

                for (int i = start; i < records.Count; i++)
                    Console.WriteLine("  " + records[i]);

                Console.WriteLine($"{'─',70}");

                Console.Write("\nSačuvati lokalno? (da/ne): ");
                if (Console.ReadLine()?.Trim().ToLower() == "da")
                    SaveRecordsLocally(records);
            }
            catch (FaultException<SmartGridException> fe)
            { PrintColor(ConsoleColor.Red, $"[SERVIS GREŠKA] {fe.Detail.Message}"); }
        }

        static void ShowSummary()
        {
            try
            {
                var s = _proxy.GetSummary();
                Console.WriteLine();
                PrintColor(ConsoleColor.Cyan,
                    "╔══════════════════════════════════════════════╗\n" +
                    "║          SMART GRID – REZIME                 ║\n" +
                    "╠══════════════════════════════════════════════╣\n" +
                   $"║  Ukupno zapisa:      {s.TotalRecords,23} ║\n" +
                   $"║  Prosečan napon:     {s.AvgVoltage,21:F3} V ║\n" +
                   $"║  Prosečna frekv.:   {s.AvgFrequency,21:F4} Hz ║\n" +
                   $"║  Prosečna snaga:    {s.AvgPower,21:F2} W ║\n" +
                   $"║  Ukupno kvarova:    {s.TotalFaults,23} ║\n" +
                   $"║  Ukupno anomalija:  {s.TotalAnomalies,23} ║\n" +
                    "╚══════════════════════════════════════════════╝");
            }
            catch (FaultException<SmartGridException> fe)
            { PrintColor(ConsoleColor.Red, $"[SERVIS GREŠKA] {fe.Detail.Message}"); }
        }


        static void DownloadFiles()
        {
            try
            {
                Console.Write("Ključna reč (Enter = svi): ");
                string kw = Console.ReadLine()?.Trim() ?? "";

                using (var ms = new MemoryStream())
                using (var opts = new FileManipulationOptions(ms, kw))
                {
                    var result = _proxy.GetFiles(opts);
                    PrintResultType(result.ResultType, result.ResultMessage);

                    if (result.ResultType == ResultType.Success &&
                        result.MemoryStreamCollection?.Count > 0)
                    {
                        Console.Write($"\nSačuvati {result.MemoryStreamCollection.Count} fajl(ova) lokalno? (da/ne): ");
                        if (Console.ReadLine()?.Trim().ToLower() == "da")
                        {
                            string outDir = "PrimljeniFajlovi";
                            foreach (var kvp in result.MemoryStreamCollection)
                            {
                                string subDir = Path.Combine(outDir,
                                    Path.GetDirectoryName(kvp.Key) ?? "");
                                CsvDataGenerator.SaveMemoryStreamToFile(
                                    subDir, Path.GetFileName(kvp.Key), kvp.Value);
                            }
                            PrintColor(ConsoleColor.Green,
                                $"Sačuvano u: {Path.GetFullPath(outDir)}");
                        }
                        result.Dispose();
                    }
                }
            }
            catch (FaultException<SmartGridException> fe)
            { PrintColor(ConsoleColor.Red, $"[SERVIS GREŠKA] {fe.Detail.Message}"); }
        }


        static void SaveRecordsLocally(List<SmartGridSample> records)
        {
            string dir = "LokalniZapisi";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"zapisi_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine(SmartGridSample.CsvHeader());
                foreach (var r in records)
                    writer.WriteLine(r.ToCsvLine());
            }
            PrintColor(ConsoleColor.Green, $"Sačuvano: {Path.GetFullPath(path)}");
        }

        static void TryEndSession()
        {
            try
            {
                Console.WriteLine("Pokušaj EndSession() nakon greške...");
                _proxy.EndSession();
            }
            catch { /* best effort */ }
        }

        static void PrintResponse(SmartGridResponse r)
        {
            var color = r.Ack == AckStatus.ACK ? ConsoleColor.Green : ConsoleColor.Red;
            PrintColor(color, $"  [{r.Ack}|{r.Status}] {r.Message}");
        }

        static void PrintResultType(ResultType rt, string msg)
        {
            var color = rt == ResultType.Success ? ConsoleColor.Green
                      : rt == ResultType.Warning ? ConsoleColor.Yellow
                      : ConsoleColor.Red;
            PrintColor(color, $"[{rt.ToString().ToUpper()}] {msg}");
        }

        static void PrintColor(ConsoleColor c, string msg)
        {
            Console.ForegroundColor = c;
            Console.WriteLine(msg);
            Console.ResetColor();
        }

        static void PrintMenu()
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("┌───────────────────────────────────────────────────┐");
            Console.WriteLine("│               SMART GRID KLIJENT                  │");
            Console.WriteLine("├───────────────────────────────────────────────────┤");
            Console.WriteLine("│  1. Pošalji CSV dataset sa diska (sekvencijalno)  │");
            Console.WriteLine("│  2. Pošalji sintetičke podatke                    │");
            Console.WriteLine("│  3. Prikaži sve zapise sa servisa                 │");
            Console.WriteLine("│  4. Prikaži rezime / statistiku                   │");
            Console.WriteLine("│  5. Preuzmi fajlove sa servisa                    │");
            Console.WriteLine("│  0. Izlaz                                         │");
            Console.WriteLine("└───────────────────────────────────────────────────┘");
            Console.ResetColor();
            Console.Write("Izbor: ");
        }

        static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔═══════════════════════════════════════════════════╗");
            Console.WriteLine("║    VIRTUELIZACIJA PROCESA – Smart Grid v2.0       ║");
            Console.WriteLine("║    Klijentska aplikacija                          ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }
    }
}
