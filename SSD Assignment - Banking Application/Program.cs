using System;
using System.Globalization;
using Banking_Application.Services;

namespace Banking_Application
{
    public class Program
    {
        // Assignment environment
        private const string DomainName = "ITSLIGO.LAN";

        // Group names 
        private const string TellerGroupName = "Bank Teller ";
        private const string AdminGroupName = "Bank Teller Administrator ";

        // Basic limits to reduce input abuse / DoS and maintain data integrity.
        private const int MaxTextLength = 100;
        private const decimal MaxTransactionAmount = 1_000_000m;

        public static void Main(string[] args)
        {
            Data_Access_Layer dal = Data_Access_Layer.getInstance();
            dal.loadBankAccounts();

            // Centralised audit logging 
            var auditLogger = new AuditLogger();
            auditLogger.EnsureEventSource();

            // Active Directory authentication/authorisation service
            var adAuth = new ActiveDirectoryAuthService(DomainName);

            // Require user authentication before allowing access to the banking menu.
            // LOCAL DEVELOPMENT MODE
            // When running locally 
            string tellerUsername = Environment.UserName;
            //on the vm
            //string tellerUsername = AuthenticateTeller(adAuth, auditLogger);

            bool running = true;

            do
            {
                Console.WriteLine("");
                Console.WriteLine("***Banking Application Menu***");
                Console.WriteLine("1. Add Bank Account");
                Console.WriteLine("2. Close Bank Account");
                Console.WriteLine("3. View Account Information");
                Console.WriteLine("4. Make Lodgement");
                Console.WriteLine("5. Make Withdrawal");
                Console.WriteLine("6. Exit");
                Console.WriteLine("CHOOSE OPTION:");
                string option = Console.ReadLine();

                switch (option)
                {
                    case "1":
                        string accountType = "";
                        int loopCount = 0;

                        do
                        {
                            if (loopCount > 0)
                                Console.WriteLine("INVALID OPTION CHOSEN - PLEASE TRY AGAIN");

                            Console.WriteLine("");
                            Console.WriteLine("***Account Types***:");
                            Console.WriteLine("1. Current Account.");
                            Console.WriteLine("2. Savings Account.");
                            Console.WriteLine("CHOOSE OPTION:");
                            accountType = Console.ReadLine();

                            loopCount++;

                        } while (!(accountType.Equals("1") || accountType.Equals("2")));

                        string name = ReadRequiredText("Enter Name: ", MaxTextLength);
                        string addressLine1 = ReadRequiredText("Enter Address Line 1: ", MaxTextLength);

                        Console.WriteLine("Enter Address Line 2: ");
                        string addressLine2 = ReadOptionalText(MaxTextLength);

                        Console.WriteLine("Enter Address Line 3: ");
                        string addressLine3 = ReadOptionalText(MaxTextLength);

                        string town = ReadRequiredText("Enter Town: ", MaxTextLength);

                        double openingBalance = (double)ReadMoney("Enter Opening Balance: ", allowZero: true);

                        Bank_Account ba;

                        if (Convert.ToInt32(accountType) == Account_Type.Current_Account)
                        {
                            double overdraftAmount = (double)ReadMoney("Enter Overdraft Amount: ", allowZero: true);
                            ba = new Current_Account(name, addressLine1, addressLine2, addressLine3, town, openingBalance, overdraftAmount);
                        }
                        else
                        {
                            double interestRate = (double)ReadMoney("Enter Interest Rate: ", allowZero: true);
                            ba = new Savings_Account(name, addressLine1, addressLine2, addressLine3, town, openingBalance, interestRate);
                        }

                        string accNo = dal.addBankAccount(ba);

                        Console.WriteLine("New Account Number Is: " + accNo);

                        auditLogger.LogTransaction(
                            tellerUsername,
                            accNo,
                            ba.name,
                            transactionType: "Account Creation",
                            outcome: "Success"
                        );

                        break;

                    case "2":
                        Console.WriteLine("Enter Account Number: ");
                        accNo = (Console.ReadLine() ?? string.Empty).Trim();

                        ba = dal.findBankAccountByAccNo(accNo);

                        if (ba is null)
                        {
                            Console.WriteLine("Account Does Not Exist");

                            auditLogger.LogTransaction(
                                tellerUsername,
                                accNo,
                                accountHolderName: "Unknown",
                                transactionType: "Account Closure",
                                outcome: "Failure - Account Not Found"
                            );
                        }
                        else
                        {
                            Console.WriteLine(ba.ToString());

                            // deletion requires administrator approval
                            if (!RequireAdministratorApproval(adAuth, auditLogger, out string approvingAdminUsername))
                            {
                                Console.WriteLine("Deletion denied: administrator approval not granted.");

                                auditLogger.LogTransaction(
                                    tellerUsername,
                                    accNo,
                                    ba.name,
                                    transactionType: "Account Closure",
                                    outcome: "Failure - Admin Approval Denied"
                                );

                                break;
                            }

                            string ans = "";

                            do
                            {
                                Console.WriteLine("Proceed With Deletion (Y/N)?");
                                ans = Console.ReadLine();

                                switch (ans)
                                {
                                    case "Y":
                                    case "y":
                                        bool closed = dal.closeBankAccount(accNo);

                                        auditLogger.LogTransaction(
                                            tellerUsername,
                                            accNo,
                                            ba.name,
                                            transactionType: "Account Closure",
                                            outcome: closed ? "Success" : "Failure",
                                            approvingAdminUsername: approvingAdminUsername
                                        );

                                        break;

                                    case "N":
                                    case "n":
                                        auditLogger.LogTransaction(
                                            tellerUsername,
                                            accNo,
                                            ba.name,
                                            transactionType: "Account Closure",
                                            outcome: "Cancelled By User",
                                            approvingAdminUsername: approvingAdminUsername
                                        );
                                        break;

                                    default:
                                        Console.WriteLine("INVALID OPTION CHOSEN - PLEASE TRY AGAIN");
                                        break;
                                }
                            } while (!(ans.Equals("Y") || ans.Equals("y") || ans.Equals("N") || ans.Equals("n")));
                        }

                        break;

                    case "3":
                        Console.WriteLine("Enter Account Number: ");
                        accNo = (Console.ReadLine() ?? string.Empty).Trim();

                        ba = dal.findBankAccountByAccNo(accNo);

                        if (ba is null)
                        {
                            Console.WriteLine("Account Does Not Exist");

                            auditLogger.LogTransaction(
                                tellerUsername,
                                accNo,
                                accountHolderName: "Unknown",
                                transactionType: "Balance / Account Information Query",
                                outcome: "Failure - Account Not Found"
                            );
                        }
                        else
                        {
                            Console.WriteLine(ba.ToString());

                            auditLogger.LogTransaction(
                                tellerUsername,
                                accNo,
                                ba.name,
                                transactionType: "Balance / Account Information Query",
                                outcome: "Success"
                            );
                        }

                        break;

                    case "4": // Lodgement
                        Console.WriteLine("Enter Account Number: ");
                        accNo = (Console.ReadLine() ?? string.Empty).Trim();

                        ba = dal.findBankAccountByAccNo(accNo);

                        if (ba is null)
                        {
                            Console.WriteLine("Account Does Not Exist");

                            auditLogger.LogTransaction(
                                tellerUsername,
                                accNo,
                                accountHolderName: "Unknown",
                                transactionType: "Lodgement",
                                outcome: "Failure - Account Not Found"
                            );
                        }
                        else
                        {
                            decimal amountToLodge = ReadMoney("Enter Amount To Lodge: ", allowZero: false);

                            string reason = null;
                            if (amountToLodge > 10000m)
                            {
                                reason = ReadRequiredText("Reason for Transaction (required for amounts over 10,000):", MaxTextLength);
                            }

                            bool lodged = dal.lodge(accNo, (double)amountToLodge);

                            auditLogger.LogTransaction(
                                tellerUsername,
                                accNo,
                                ba.name,
                                transactionType: "Lodgement",
                                outcome: lodged ? "Success" : "Failure",
                                amount: amountToLodge,
                                reason: reason
                            );
                        }

                        break;

                    case "5": // Withdrawal
                        Console.WriteLine("Enter Account Number: ");
                        accNo = (Console.ReadLine() ?? string.Empty).Trim();

                        ba = dal.findBankAccountByAccNo(accNo);

                        if (ba is null)
                        {
                            Console.WriteLine("Account Does Not Exist");

                            auditLogger.LogTransaction(
                                tellerUsername,
                                accNo,
                                accountHolderName: "Unknown",
                                transactionType: "Withdrawal",
                                outcome: "Failure - Account Not Found"
                            );
                        }
                        else
                        {
                            decimal amountToWithdraw = ReadMoney(
                                "Enter Amount To Withdraw (€" + ba.getAvailableFunds() + " Available): ",
                                allowZero: false);

                            string reason = null;
                            if (amountToWithdraw > 10000m)
                            {
                                reason = ReadRequiredText("Reason for Transaction (required for amounts over 10,000):", MaxTextLength);
                            }

                            bool withdrawalOK = dal.withdraw(accNo, (double)amountToWithdraw);

                            if (withdrawalOK == false)
                            {
                                Console.WriteLine("Insufficient Funds Available.");
                            }

                            auditLogger.LogTransaction(
                                tellerUsername,
                                accNo,
                                ba.name,
                                transactionType: "Withdrawal",
                                outcome: withdrawalOK ? "Success" : "Failure - Insufficient Funds",
                                amount: amountToWithdraw,
                                reason: reason
                            );
                        }

                        break;

                    case "6":
                        running = false;
                        break;

                    default:
                        Console.WriteLine("INVALID OPTION CHOSEN - PLEASE TRY AGAIN");
                        auditLogger.LogTransaction(
                            tellerUsername,
                            accountNo: "N/A",
                            accountHolderName: "N/A",
                            transactionType: "Menu Selection",
                            outcome: "Failure - Invalid Option"
                        );
                        break;
                }

            } while (running != false);
        }

        private static string AuthenticateTeller(ActiveDirectoryAuthService adAuth, AuditLogger auditLogger)
        {
            int attempts = 0;

            while (true)
            {
                if (attempts >= 5)
                {
                    Console.WriteLine("Too many failed attempts. Exiting.");
                    Environment.Exit(1);
                }

                Console.WriteLine("Username:");
                string username = Console.ReadLine();

                Console.WriteLine("Password:");
                string password = ReadPassword();

                bool credentialsValid;
                try
                {
                    credentialsValid = adAuth.ValidateCredentials(username, password);
                }
                catch
                {
                    auditLogger.LogLoginAttempt(username, success: false, "Active Directory error");
                    Console.WriteLine("Authentication service unavailable. Please try again.");
                    attempts++;
                    continue;
                }

                if (!credentialsValid)
                {
                    auditLogger.LogLoginAttempt(username, success: false, "Invalid credentials");
                    Console.WriteLine("Invalid username or password.");
                    attempts++;
                    continue;
                }

                bool inTellerGroup = adAuth.IsUserInGroup(username, TellerGroupName);
                if (!inTellerGroup)
                {
                    auditLogger.LogLoginAttempt(username, success: false, "User not in teller group");
                    Console.WriteLine("Access denied: you are not authorised to use this application.");
                    attempts++;
                    continue;
                }

                auditLogger.LogLoginAttempt(username, success: true);
                Console.WriteLine("Login successful.");
                return username;
            }
        }

        private static bool RequireAdministratorApproval(
            ActiveDirectoryAuthService adAuth,
            AuditLogger auditLogger,
            out string adminUsername)
        {
            adminUsername = null;
            int attempts = 0;

            while (attempts < 3)
            {
                Console.WriteLine("Administrator Username:");
                string username = Console.ReadLine();

                Console.WriteLine("Administrator Password:");
                string password = ReadPassword();

                bool credentialsValid;
                try
                {
                    credentialsValid = adAuth.ValidateCredentials(username, password);
                }
                catch
                {
                    auditLogger.LogLoginAttempt(username, success: false, "Active Directory error during admin approval");
                    attempts++;
                    continue;
                }

                if (!credentialsValid)
                {
                    auditLogger.LogLoginAttempt(username, success: false, "Invalid admin credentials");
                    attempts++;
                    continue;
                }

                bool inAdminGroup = adAuth.IsUserInGroup(username, AdminGroupName);
                if (!inAdminGroup)
                {
                    auditLogger.LogLoginAttempt(username, success: false, "User not in admin group");
                    attempts++;
                    continue;
                }

                // Record that admin approval was granted 
                auditLogger.LogLoginAttempt(username, success: true);
                adminUsername = username;
                return true;
            }

            return false;
        }

        private static string ReadPassword()
        {
            string password = string.Empty;
            ConsoleKeyInfo key;

            do
            {
                key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password = password[..^1];
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    password += key.KeyChar;
                }

            } while (key.Key != ConsoleKey.Enter);

            Console.WriteLine();
            return password;
        }

        private static string ReadRequiredText(string prompt, int maxLen)
        {
            while (true)
            {
                Console.WriteLine(prompt);
                string value = (Console.ReadLine() ?? string.Empty).Trim();

                if (value.Length == 0 || value.Length > maxLen)
                {
                    Console.WriteLine("INVALID INPUT - PLEASE TRY AGAIN");
                    continue;
                }

                return value;
            }
        }

        private static string ReadOptionalText(int maxLen)
        {
            string value = (Console.ReadLine() ?? string.Empty).Trim();
            if (value.Length > maxLen)
                return value.Substring(0, maxLen);

            return value;
        }

        private static decimal ReadMoney(string prompt, bool allowZero)
        {
            while (true)
            {
                Console.WriteLine(prompt);
                string input = (Console.ReadLine() ?? string.Empty).Trim();

                if (!decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
                {
                    if (!decimal.TryParse(input, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
                    {
                        Console.WriteLine("INVALID AMOUNT ENTERED - PLEASE TRY AGAIN");
                        continue;
                    }
                }

                if (value < 0 || (!allowZero && value == 0))
                {
                    Console.WriteLine("INVALID AMOUNT ENTERED - PLEASE TRY AGAIN");
                    continue;
                }

                if (value > MaxTransactionAmount)
                {
                    Console.WriteLine("AMOUNT EXCEEDS ALLOWED LIMIT - PLEASE TRY AGAIN");
                    continue;
                }

                return value;
            }
        }
    }
}
