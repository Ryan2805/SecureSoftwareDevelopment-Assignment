using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;

namespace Banking_Application.Services
{
   
    public sealed class AuditLogger
    {
       
        private const string EventSourceName = "SSD Banking Application";
        private const string LogName = "Application";

     
        private const string FallbackLogFileName = "audit-fallback.log";

        
        private bool eventLogAvailable = true;

        public void EnsureEventSource()
        {
            try
            {
             
                if (!EventLog.SourceExists(EventSourceName))
                {
                    EventLog.CreateEventSource(EventSourceName, LogName);
                }

                eventLogAvailable = true;
            }
            catch (SecurityException)
            {
              
                eventLogAvailable = false;
                WriteFallback("Event Log unavailable due to security restrictions. Falling back to file logging.");
            }
            catch (UnauthorizedAccessException)
            {
                eventLogAvailable = false;
                WriteFallback("Event Log unavailable due to access restrictions. Falling back to file logging.");
            }
            catch
            {
              
                eventLogAvailable = false;
                WriteFallback("Event Log unavailable due to unexpected error. Falling back to file logging.");
            }
        }

        
        //Logs an application transaction 
    
        public void LogTransaction(
            string tellerUsername,
            string accountNo,
            string accountHolderName,
            string transactionType,
            string outcome,
            decimal? amount = null,
            string reason = null,
            string approvingAdminUsername = null)
        {
            string where = GetMacAddress() ?? GetIpAddress() ?? Environment.MachineName;
            string how = GetApplicationMetadata();

            string message =
                $"WHO(Teller): {tellerUsername}\n" +
                $"WHO(Account): {accountNo} - {accountHolderName}\n" +
                $"WHAT: {transactionType}\n" +
                $"WHERE: {where}\n" +
                $"WHEN: {DateTime.UtcNow:O}\n" +
                (amount.HasValue ? $"AMOUNT: {amount.Value}\n" : string.Empty) +
                (!string.IsNullOrWhiteSpace(reason) ? $"WHY: {reason}\n" : string.Empty) +
                (!string.IsNullOrWhiteSpace(approvingAdminUsername) ? $"ADMIN_APPROVAL: {approvingAdminUsername}\n" : string.Empty) +
                $"HOW: {how}\n" +
                $"OUTCOME: {outcome}";

            WriteEventOrFallback(message, EventLogEntryType.Information);
        }

   
        public void LogLoginAttempt(string username, bool success, string failureReason = null)
        {
            string where = GetMacAddress() ?? GetIpAddress() ?? Environment.MachineName;
            string how = GetApplicationMetadata();

            string message =
                $"WHO: {username}\n" +
                $"WHAT: Login Attempt\n" +
                $"WHERE: {where}\n" +
                $"WHEN: {DateTime.UtcNow:O}\n" +
                $"HOW: {how}\n" +
                $"OUTCOME: {(success ? "Success" : "Failure")}" +
                (!string.IsNullOrWhiteSpace(failureReason) ? $"\nDETAILS: {failureReason}" : string.Empty);

            WriteEventOrFallback(message, success ? EventLogEntryType.Information : EventLogEntryType.Warning);
        }

        private void WriteEventOrFallback(string message, EventLogEntryType type)
        {
            if (eventLogAvailable)
            {
                try
                {
                    EventLog.WriteEntry(EventSourceName, message, type);
                    return;
                }
                catch (SecurityException)
                {
                    eventLogAvailable = false;
                }
                catch (UnauthorizedAccessException)
                {
                    eventLogAvailable = false;
                }
                catch
                {
                    eventLogAvailable = false;
                }
            }

          
            WriteFallback(message);
        }

        private static void WriteFallback(string message)
        {
            try
            {
             
                File.AppendAllText(FallbackLogFileName,
                    $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                
            }
        }

        private static string GetIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a =>
                    a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                return ip?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string GetMacAddress()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    var addr = nic.GetPhysicalAddress();
                    if (addr == null || addr.GetAddressBytes().Length == 0)
                        continue;

                    return string.Join("-", addr.GetAddressBytes().Select(b => b.ToString("X2")));
                }
            }
            catch
            {
            }

            return null;
        }

        private static string GetApplicationMetadata()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string name = asm.GetName().Name ?? "UnknownApp";
            string version = asm.GetName().Version?.ToString() ?? "UnknownVersion";
            string hash = TrySha256OfFile(asm.Location) ?? "UnavailableHash";

            return $"Name={name}; Version={version}; Sha256={hash}";
        }

        private static string TrySha256OfFile(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                    return null;

                using var sha = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                byte[] digest = sha.ComputeHash(stream);

                return Convert.ToHexString(digest);
            }
            catch
            {
                return null;
            }
        }
    }
}
