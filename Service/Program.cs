using Common;
using System;
using System.ServiceModel;

namespace Service
{
    internal class Program
    {
        static void Main(string[] args)
        {
            SmartGridService serviceInstance = null;
            ServiceHost host = null;

            try
            {
                serviceInstance = new SmartGridService();
                host = new ServiceHost(serviceInstance);

                host.Open();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("╔══════════════════════════════════════════╗");
                Console.WriteLine("║      SMART GRID SERVICE - POKRENUTO      ║");
                Console.WriteLine("╚══════════════════════════════════════════╝");
                Console.ResetColor();
                Console.WriteLine($"\nServis sluša na: net.tcp://localhost:9000/SmartGridService");
                Console.WriteLine("Pritisnite ENTER za zaustavljanje servisa...\n");

                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[GREŠKA] {ex.Message}");
                Console.ResetColor();
                Console.ReadLine();
            }
            finally
            {
                try { host?.Close(); } catch { host?.Abort(); }
                serviceInstance?.Dispose();
            }
        }
    }
}
