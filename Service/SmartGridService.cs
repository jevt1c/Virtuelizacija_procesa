using Common;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text;

namespace Service
{

    public class TransferStartedEventArgs : EventArgs
    {
        public string SessionId  { get; }
        public string SourceFile { get; }
        public int    ExpectedRows { get; }
        public TransferStartedEventArgs(string id, string src, int exp)
        { SessionId = id; SourceFile = src; ExpectedRows = exp; }
    }

    public class SampleReceivedEventArgs : EventArgs
    {
        public SmartGridSample Sample { get; }
        public int             SampleIndex { get; }
        public SampleReceivedEventArgs(SmartGridSample s, int idx) { Sample = s; SampleIndex = idx; }
    }

    public class TransferCompletedEventArgs : EventArgs
    {
        public string SessionId      { get; }
        public int    TotalReceived  { get; }
        public int    TotalRejected  { get; }
        public TransferCompletedEventArgs(string id, int rx, int rej)
        { SessionId = id; TotalReceived = rx; TotalRejected = rej; }
    }

    public class WarningRaisedEventArgs : EventArgs
    {
        public string WarningType { get; }
        public string Message     { get; }
        public WarningRaisedEventArgs(string type, string msg) { WarningType = type; Message = msg; }
    }

    public class FrequencySpikeEventArgs : EventArgs
    {
        public double DeltaF    { get; }
        public string Direction { get; }
        public DateTime Timestamp { get; }
        public FrequencySpikeEventArgs(double df, string dir, DateTime ts)
        { DeltaF = df; Direction = dir; Timestamp = ts; }
    }

    public class OutOfBandWarningEventArgs : EventArgs
    {
        public double   Value     { get; }
        public double   Mean      { get; }
        public string   Direction { get; }
        public string   Parameter { get; }
        public DateTime Timestamp { get; }
        public OutOfBandWarningEventArgs(double v, double m, string dir, string param, DateTime ts)
        { Value = v; Mean = m; Direction = dir; Parameter = param; Timestamp = ts; }
    }

    public class PowerSpikeEventArgs : EventArgs
    {
        public double   ComputedPower { get; }
        public string   Direction     { get; }
        public DateTime Timestamp     { get; }
        public PowerSpikeEventArgs(double p, string dir, DateTime ts)
        { ComputedPower = p; Direction = dir; Timestamp = ts; }
    }


    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    public class SmartGridService : ISmartGridService, IDisposable
    {

        private readonly double _fThreshold;
        private readonly double _pMaxThreshold;
        private readonly double _outOfBandPct;

        private readonly string _dataPath;
        private readonly List<SmartGridSample> _allRecords = new List<SmartGridSample>();
        private int _anomalyCount = 0;
        private bool _disposed = false;


        private string       _sessionId      = null;
        private int          _sessionReceived = 0;
        private int          _sessionRejected = 0;
        private StreamWriter _sessionWriter  = null;
        private StreamWriter _rejectWriter   = null;
        private FileStream   _sessionFs      = null;
        private FileStream   _rejectFs       = null;


        private double _prevFrequency   = double.NaN;
        private double _freqRunningSum  = 0;
        private double _freqRunningCount = 0;

        public event EventHandler<TransferStartedEventArgs>  OnTransferStarted;
        public event EventHandler<SampleReceivedEventArgs>   OnSampleReceived;
        public event EventHandler<TransferCompletedEventArgs> OnTransferCompleted;
        public event EventHandler<WarningRaisedEventArgs>    OnWarningRaised;
        public event EventHandler<FrequencySpikeEventArgs>   OnFrequencySpike;
        public event EventHandler<OutOfBandWarningEventArgs> OnOutOfBandWarning;
        public event EventHandler<PowerSpikeEventArgs>       OnPowerSpike;


        public SmartGridService()
        {
            _fThreshold    = ParseConfig("F_threshold",    0.5);
            _pMaxThreshold = ParseConfig("P_max_threshold", 10000.0);
            _outOfBandPct  = ParseConfig("OutOfBand_Pct",  0.25);

            _dataPath = ConfigurationManager.AppSettings["dataPath"] ?? "SmartGridData";
            if (!Directory.Exists(_dataPath))
                Directory.CreateDirectory(_dataPath);

            OnTransferStarted  += (s, e) => Log(ConsoleColor.Cyan,
                $"[START SESSION] {e.SessionId} | src={e.SourceFile} | expectedRows={e.ExpectedRows}");

            OnSampleReceived   += (s, e) =>
            {
                if (e.SampleIndex % 100 == 0)
                    Log(ConsoleColor.Gray, $"  prenos u toku... primljeno {e.SampleIndex} uzoraka");
            };

            OnTransferCompleted += (s, e) => Log(ConsoleColor.Green,
                $"[END SESSION] {e.SessionId} | primljeno={e.TotalReceived} odbijeno={e.TotalRejected} | završen prenos");

            OnWarningRaised    += (s, e) => Log(ConsoleColor.Yellow,
                $"[UPOZORENJE:{e.WarningType}] {e.Message}");

            OnFrequencySpike   += (s, e) => Log(ConsoleColor.Yellow,
                $"[FREQUENCY SPIKE] {e.Timestamp:HH:mm:ss} | ΔF={e.DeltaF:F4}Hz ({e.Direction} očekivanog)");

            OnOutOfBandWarning += (s, e) => Log(ConsoleColor.Yellow,
                $"[OUT OF BAND] {e.Timestamp:HH:mm:ss} | {e.Parameter}={e.Value:F4} " +
                $"mean={e.Mean:F4} ({e.Direction} ±{_outOfBandPct*100:F0}%)");

            OnPowerSpike       += (s, e) => Log(ConsoleColor.Red,
                $"[POWER SPIKE] {e.Timestamp:HH:mm:ss} | P={e.ComputedPower:F2}W " +
                $"({e.Direction} praga {_pMaxThreshold}W)");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"  Thresholds: F_threshold={_fThreshold}Hz | " +
                              $"P_max={_pMaxThreshold}W | OutOfBand=±{_outOfBandPct*100:F0}%");
            Console.ResetColor();
        }

        public SmartGridResponse StartSession(SessionMeta meta)
        {
            try
            {
                if (_sessionId != null)
                    return Nack("Sesija je već aktivna. Pozovite EndSession() pre nove sesije.", SessionStatus.IN_PROGRESS);

                _sessionId       = meta?.SessionId ?? Guid.NewGuid().ToString("N").Substring(0, 8);
                _sessionReceived = 0;
                _sessionRejected = 0;
                _prevFrequency   = double.NaN;
                _freqRunningSum  = 0;
                _freqRunningCount = 0;


                string sessionFolder = Path.Combine(_dataPath, _sessionId);
                if (!Directory.Exists(sessionFolder))
                    Directory.CreateDirectory(sessionFolder);

                string measurePath = Path.Combine(sessionFolder, "measurements_session.csv");
                _sessionFs     = new FileStream(measurePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                _sessionWriter = new StreamWriter(_sessionFs, Encoding.UTF8);
                _sessionWriter.WriteLine(SmartGridSample.CsvHeader());
                _sessionWriter.Flush();

                string rejectPath = Path.Combine(sessionFolder, "rejects.csv");
                _rejectFs     = new FileStream(rejectPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                _rejectWriter = new StreamWriter(_rejectFs, Encoding.UTF8);
                _rejectWriter.WriteLine("Reason,RawLine");
                _rejectWriter.Flush();

                OnTransferStarted?.Invoke(this,
                    new TransferStartedEventArgs(_sessionId, meta?.SourceFile ?? "", meta?.ExpectedRows ?? 0));

                return Ack($"Sesija {_sessionId} pokrenuta.", SessionStatus.IN_PROGRESS);
            }
            catch (Exception ex)
            {
                CloseSessionWriters();
                _sessionId = null;
                throw new FaultException<ValidationFault>(
                    new ValidationFault(ex.Message), ex.Message);
            }
        }

        public SmartGridResponse PushSample(SmartGridSample sample)
        {
            if (_sessionId == null)
                return Nack("Nema aktivne sesije. Pozovite StartSession() pre slanja.", SessionStatus.COMPLETED);


            string validationError = ValidateSample(sample);
            if (validationError != null)
            {
                _sessionRejected++;
                try { _rejectWriter?.WriteLine($"\"{validationError}\",\"{sample?.ToString() ?? "null"}\""); _rejectWriter?.Flush(); }
                catch { }
                throw new FaultException<ValidationFault>(
                    new ValidationFault(validationError, "sample"), validationError);
            }

            _sessionReceived++;
            _allRecords.Add(sample);
            _sessionWriter.WriteLine(sample.ToCsvLine());
            _sessionWriter.Flush();

            OnSampleReceived?.Invoke(this, new SampleReceivedEventArgs(sample, _sessionReceived));


            //ΔF = f(t) − f(t−Δt)
            AnalyzeFrequency(sample);

            //P(t) = V(t) * I(t)
            AnalyzePower(sample);

            return Ack($"Uzorak {_sessionReceived} prihvaćen.", SessionStatus.IN_PROGRESS);
        }

        public SmartGridResponse EndSession()
        {
            if (_sessionId == null)
                return Nack("Nema aktivne sesije.", SessionStatus.COMPLETED);

            int rx  = _sessionReceived;
            int rej = _sessionRejected;
            string sid = _sessionId;

            CloseSessionWriters();
            _sessionId = null;

            OnTransferCompleted?.Invoke(this, new TransferCompletedEventArgs(sid, rx, rej));

            return Ack($"Sesija {sid} završena. Primljeno={rx}, Odbijeno={rej}.", SessionStatus.COMPLETED);
        }


        public List<SmartGridSample> GetAllRecords()
        {
            try { return new List<SmartGridSample>(_allRecords); }
            catch (Exception ex)
            { throw new FaultException<SmartGridException>(new SmartGridException(ex.Message), ex.Message); }
        }

        public GridSummary GetSummary()
        {
            try
            {
                if (_allRecords.Count == 0) return new GridSummary();
                return new GridSummary
                {
                    TotalRecords  = _allRecords.Count,
                    AvgVoltage    = _allRecords.Average(r => r.Voltage),
                    AvgFrequency  = _allRecords.Average(r => r.Frequency),
                    AvgPower      = _allRecords.Average(r => r.ComputedPower),
                    TotalFaults   = _allRecords.Count(r => r.FaultIndicator != 0),
                    TotalAnomalies = _anomalyCount
                };
            }
            catch (Exception ex)
            { throw new FaultException<SmartGridException>(new SmartGridException(ex.Message), ex.Message); }
        }

        [OperationBehavior(AutoDisposeParameters = true)]
        public FileManipulationResults GetFiles(FileManipulationOptions options)
        {
            try
            {
                if (!Directory.Exists(_dataPath))
                    return new FileManipulationResults(ResultType.Warning, "Nema podataka na serveru.");

                var keyword  = options?.KeyWord ?? "";
                var allFiles = Directory.GetFiles(_dataPath, "*.csv", SearchOption.AllDirectories);
                var matched  = allFiles.Where(f =>
                    string.IsNullOrEmpty(keyword) || Path.GetFileName(f).Contains(keyword)).ToArray();

                if (matched.Length == 0)
                    return new FileManipulationResults(ResultType.Warning,
                        $"Nema fajlova koji odgovaraju ključnoj reči '{keyword}'.");

                var col = new Dictionary<string, MemoryStream>();
                foreach (var file in matched)
                {
                    var ms = new MemoryStream();
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                        fs.CopyTo(ms);
                    ms.Position = 0;
                    col[file.Replace(_dataPath, "").TrimStart(Path.DirectorySeparatorChar)] = ms;
                }
                return new FileManipulationResults(ResultType.Success,
                    $"Pronađeno {col.Count} fajl(ova).", col);
            }
            catch (Exception ex)
            { throw new FaultException<SmartGridException>(new SmartGridException(ex.Message), ex.Message); }
        }

        private void AnalyzeFrequency(SmartGridSample s)
        {
            _freqRunningSum += s.Frequency;
            _freqRunningCount++;
            double fmean = _freqRunningSum / _freqRunningCount;

            if (!double.IsNaN(_prevFrequency))
            {
                double deltaF = s.Frequency - _prevFrequency;
                if (Math.Abs(deltaF) > _fThreshold)
                {
                    _anomalyCount++;
                    string dir = deltaF > 0 ? "iznad" : "ispod";
                    OnFrequencySpike?.Invoke(this,
                        new FrequencySpikeEventArgs(deltaF, dir, s.Timestamp));
                }
            }
            _prevFrequency = s.Frequency;

            if (_freqRunningCount > 1)
            {
                double lo = (1.0 - _outOfBandPct) * fmean;
                double hi = (1.0 + _outOfBandPct) * fmean;
                if (s.Frequency < lo || s.Frequency > hi)
                {
                    _anomalyCount++;
                    string dir = s.Frequency < lo ? "ispod" : "iznad";
                    OnOutOfBandWarning?.Invoke(this,
                        new OutOfBandWarningEventArgs(s.Frequency, fmean, dir, "Frequency", s.Timestamp));
                    OnWarningRaised?.Invoke(this,
                        new WarningRaisedEventArgs("OutOfBandFrequency",
                            $"f={s.Frequency:F3}Hz {dir} očekivane vrednosti (mean={fmean:F3}Hz)"));
                }
            }
        }

        private void AnalyzePower(SmartGridSample s)
        {
            double p = s.ComputedPower;
            if (p > _pMaxThreshold)
            {
                _anomalyCount++;
                string dir = "iznad";
                OnPowerSpike?.Invoke(this,
                    new PowerSpikeEventArgs(p, dir, s.Timestamp));
                OnWarningRaised?.Invoke(this,
                    new WarningRaisedEventArgs("PowerSpike",
                        $"P={p:F2}W > prag {_pMaxThreshold}W (V={s.Voltage:F2}V, I={s.Current:F2}A)"));
            }
        }


        private string ValidateSample(SmartGridSample s)
        {
            if (s == null)           return "Uzorak je null.";
            if (s.Frequency <= 0)    return $"Frequency mora biti > 0, dobijeno: {s.Frequency}";
            if (s.Voltage < 0)       return $"Voltage ne može biti negativan: {s.Voltage}";
            if (s.Current < 0)       return $"Current ne može biti negativan: {s.Current}";
            if (s.Timestamp == default) return "Timestamp nije postavljen.";
            return null;   // valid
        }


        private void CloseSessionWriters()
        {
            try { _sessionWriter?.Flush(); _sessionWriter?.Dispose(); } catch { }
            try { _sessionFs?.Dispose(); } catch { }
            try { _rejectWriter?.Flush(); _rejectWriter?.Dispose(); } catch { }
            try { _rejectFs?.Dispose(); } catch { }
            _sessionWriter = null;
            _sessionFs     = null;
            _rejectWriter  = null;
            _rejectFs      = null;
        }

        private SmartGridResponse Ack(string msg, SessionStatus status) =>
            new SmartGridResponse { Ack = AckStatus.ACK, Status = status,
                Message = msg, ReceivedSamples = _sessionReceived };

        private SmartGridResponse Nack(string msg, SessionStatus status) =>
            new SmartGridResponse { Ack = AckStatus.NACK, Status = status,
                Message = msg, ReceivedSamples = _sessionReceived };

        private static double ParseConfig(string key, double def)
        {
            var val = ConfigurationManager.AppSettings[key];
            return double.TryParse(val, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : def;
        }

        private void Log(ConsoleColor color, string msg)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(msg);
            Console.ResetColor();
            try
            {
                File.AppendAllText(Path.Combine(_dataPath, "service.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
            }
            catch { }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    CloseSessionWriters();
                    _allRecords.Clear();
                }
                _disposed = true;
            }
        }

        ~SmartGridService() { Dispose(false); }
    }
}
