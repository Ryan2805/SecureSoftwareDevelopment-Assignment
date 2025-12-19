using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Banking_Application.Services;
using System.Security.Cryptography;

namespace Banking_Application
{
    public class Data_Access_Layer
    {
        private readonly CryptoService crypto;
        private readonly List<Bank_Account> accounts;
        private readonly AuditLogger auditLogger = new AuditLogger();

        // SQLite DB file name (stored in current working directory)
        public static string databaseName = "Banking Database.db";

        // Singleton instance
        private static readonly Data_Access_Layer instance = new Data_Access_Layer();

        // Singleton Design Pattern (For Concurrency Control) - Use getInstance() Method Instead.
        private Data_Access_Layer()
        {
            accounts = new List<Bank_Account>();

            // Application-Level Encryption 
            // Key is generated once and protected at rest using Windows DPAPI
            var keyStore = new KeyStore();
            crypto = new CryptoService(keyStore.GetOrCreateAes256Key());
        }

        public static Data_Access_Layer getInstance()
        {
            return instance;
        }

        private SqliteConnection getDatabaseConnection()
        {

            string databaseConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Data_Access_Layer.databaseName,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            return new SqliteConnection(databaseConnectionString);
        }

        private void initialiseDatabase()
        {
            using (var connection = getDatabaseConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    // Create table if it does not exist
                    command.CommandText =
                    @"
                        CREATE TABLE IF NOT EXISTS Bank_Accounts(    
                            accountNo TEXT PRIMARY KEY,
                            name TEXT NOT NULL,
                            address_line_1 TEXT,
                            address_line_2 TEXT,
                            address_line_3 TEXT,
                            town TEXT NOT NULL,
                            balance REAL NOT NULL,
                            accountType INTEGER NOT NULL,
                            overdraftAmount REAL,
                            interestRate REAL
                        ) WITHOUT ROWID;
                    ";

                    command.ExecuteNonQuery();
                }
            }
        }

        public void loadBankAccounts()
        {
            // Ensure DB exists before attempting to read
            if (!File.Exists(Data_Access_Layer.databaseName))
            {
                initialiseDatabase();
                return;
            }

            // Prevent duplicate entries if loadBankAccounts() is called more than once
            accounts.Clear();

            using (var connection = getDatabaseConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM Bank_Accounts;";

                    using (SqliteDataReader dr = command.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            // Column indices based on schema:
                            // 0 accountNo, 1 name, 2 a1, 3 a2, 4 a3, 5 town, 6 balance, 7 type, 8 overdraft, 9 interest
                            int accountType = dr.GetInt16(7);

                            try
                            {
                                if (accountType == Account_Type.Current_Account)
                                {
                                    var ca = new Current_Account
                                    {
                                        accountNo = dr.GetString(0),

                                        // Decrypt PII fields after reading from database 
                                        name = crypto.DecryptToken(dr.GetString(1)),
                                        address_line_1 = crypto.DecryptToken(dr.IsDBNull(2) ? "" : dr.GetString(2)),
                                        address_line_2 = crypto.DecryptToken(dr.IsDBNull(3) ? "" : dr.GetString(3)),
                                        address_line_3 = crypto.DecryptToken(dr.IsDBNull(4) ? "" : dr.GetString(4)),
                                        town = crypto.DecryptToken(dr.GetString(5)),

                                        balance = dr.GetDouble(6),
                                        overdraftAmount = dr.IsDBNull(8) ? 0 : dr.GetDouble(8)
                                    };

                                    accounts.Add(ca);
                                }
                                else if (accountType == Account_Type.Savings_Account)
                                {
                                    var sa = new Savings_Account
                                    {
                                        accountNo = dr.GetString(0),

                                        // Decrypt PII fields after reading from database 
                                        name = crypto.DecryptToken(dr.GetString(1)),
                                        address_line_1 = crypto.DecryptToken(dr.IsDBNull(2) ? "" : dr.GetString(2)),
                                        address_line_2 = crypto.DecryptToken(dr.IsDBNull(3) ? "" : dr.GetString(3)),
                                        address_line_3 = crypto.DecryptToken(dr.IsDBNull(4) ? "" : dr.GetString(4)),
                                        town = crypto.DecryptToken(dr.GetString(5)),

                                        // Non-PII numeric fields remain plaintext for arithmetic operations
                                        balance = dr.GetDouble(6),
                                        interestRate = dr.IsDBNull(9) ? 0 : dr.GetDouble(9)
                                    };

                                    accounts.Add(sa);
                                }
                                else
                                {
                                    continue;
                                }
                            }
                            catch (CryptographicException)
                            {
                                string accNo = dr.IsDBNull(0) ? "Unknown" : dr.GetString(0);

                                auditLogger.LogTransaction(
                                    tellerUsername: "SYSTEM",
                                    accountNo: accNo,
                                    accountHolderName: "Unknown",
                                    transactionType: "Database Read / Decrypt",
                                    outcome: "Failure - Possible Data Tampering Detected"
                                );

                                continue;
                            }
                            catch
                            {
                                continue;
                            }
                        }
                    }
                }
            }
        }

        public string addBankAccount(Bank_Account ba)
        {
            if (ba == null)
                throw new ArgumentNullException(nameof(ba));

            accounts.Add(ba);

            using (var connection = getDatabaseConnection())
            {
                connection.Open();

                // Encrypt PII fields before writing to database 
                string encName = crypto.EncryptToToken(ba.name);
                string encA1 = crypto.EncryptToToken(ba.address_line_1);
                string encA2 = crypto.EncryptToToken(ba.address_line_2);
                string encA3 = crypto.EncryptToToken(ba.address_line_3);
                string encTown = crypto.EncryptToToken(ba.town);

                using (var command = connection.CreateCommand())
                {
                    // Specify columns and use parameters to prevent SQL injection
                    command.CommandText = @"
                        INSERT INTO Bank_Accounts
                        (accountNo, name, address_line_1, address_line_2, address_line_3, town, balance, accountType, overdraftAmount, interestRate)
                        VALUES
                        (@accountNo, @name, @a1, @a2, @a3, @town, @balance, @accountType, @overdraftAmount, @interestRate);
                    ";

                    // Non-PII fields stored in plaintext to support lookups and numeric operations
                    command.Parameters.AddWithValue("@accountNo", ba.accountNo);
                    command.Parameters.AddWithValue("@balance", ba.balance);

                    // PII fields stored encrypted-at-rest.
                    command.Parameters.AddWithValue("@name", encName);
                    command.Parameters.AddWithValue("@a1", encA1);
                    command.Parameters.AddWithValue("@a2", encA2);
                    command.Parameters.AddWithValue("@a3", encA3);
                    command.Parameters.AddWithValue("@town", encTown);

                    if (ba is Current_Account ca)
                    {
                        command.Parameters.AddWithValue("@accountType", Account_Type.Current_Account);
                        command.Parameters.AddWithValue("@overdraftAmount", ca.overdraftAmount);
                        command.Parameters.AddWithValue("@interestRate", DBNull.Value);
                    }
                    else if (ba is Savings_Account sa)
                    {
                        command.Parameters.AddWithValue("@accountType", Account_Type.Savings_Account);
                        command.Parameters.AddWithValue("@overdraftAmount", DBNull.Value);
                        command.Parameters.AddWithValue("@interestRate", sa.interestRate);
                    }
                    else
                    {
                        // prevent unknown account types from being inserted silently
                        throw new InvalidOperationException("Unknown bank account type.");
                    }

                    command.ExecuteNonQuery();
                }
            }

            return ba.accountNo;
        }

        public Bank_Account findBankAccountByAccNo(string accNo)
        {
            // Keep behaviour identical: search the in-memory cache.
            foreach (Bank_Account ba in accounts)
            {
                if (ba.accountNo != null && ba.accountNo.Equals(accNo))
                {
                    return ba;
                }
            }

            return null;
        }

        public bool closeBankAccount(string accNo)
        {
            Bank_Account toRemove = null;

            foreach (Bank_Account ba in accounts)
            {
                if (ba.accountNo != null && ba.accountNo.Equals(accNo))
                {
                    toRemove = ba;
                    break;
                }
            }

            if (toRemove == null)
                return false;

            accounts.Remove(toRemove);

            using (var connection = getDatabaseConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    // Parameterised DELETE prevents SQL injection.
                    command.CommandText = "DELETE FROM Bank_Accounts WHERE accountNo = @accNo;";
                    command.Parameters.AddWithValue("@accNo", toRemove.accountNo);
                    command.ExecuteNonQuery();
                }
            }

            return true;
        }

        public bool lodge(string accNo, double amountToLodge)
        {
            Bank_Account toLodgeTo = null;

            foreach (Bank_Account ba in accounts)
            {
                if (ba.accountNo != null && ba.accountNo.Equals(accNo))
                {
                    ba.lodge(amountToLodge);
                    toLodgeTo = ba;
                    break;
                }
            }

            if (toLodgeTo == null)
                return false;

            using (var connection = getDatabaseConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    // Parameterised UPDATE prevents SQL injection and keeps SQL readable.
                    command.CommandText = "UPDATE Bank_Accounts SET balance = @balance WHERE accountNo = @accNo;";
                    command.Parameters.AddWithValue("@balance", toLodgeTo.balance);
                    command.Parameters.AddWithValue("@accNo", toLodgeTo.accountNo);
                    command.ExecuteNonQuery();
                }
            }

            return true;
        }

        public bool withdraw(string accNo, double amountToWithdraw)
        {
            Bank_Account toWithdrawFrom = null;
            bool result = false;

            foreach (Bank_Account ba in accounts)
            {
                if (ba.accountNo != null && ba.accountNo.Equals(accNo))
                {
                    result = ba.withdraw(amountToWithdraw);
                    toWithdrawFrom = ba;
                    break;
                }
            }

            if (toWithdrawFrom == null || result == false)
                return false;

            using (var connection = getDatabaseConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    // Parameterised UPDATE prevents SQL injection and keeps SQL readable.
                    command.CommandText = "UPDATE Bank_Accounts SET balance = @balance WHERE accountNo = @accNo;";
                    command.Parameters.AddWithValue("@balance", toWithdrawFrom.balance);
                    command.Parameters.AddWithValue("@accNo", toWithdrawFrom.accountNo);
                    command.ExecuteNonQuery();
                }
            }

            return true;
        }
    }
}
